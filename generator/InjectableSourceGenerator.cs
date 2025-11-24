using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace InjectableGenerator.Generator
{
    [Generator]
    public sealed class InjectableSourceGenerator : ISourceGenerator
    {
        public void Initialize(GeneratorInitializationContext context)
        {
        }

        public void Execute(GeneratorExecutionContext context)
        {
            context.AddSource("InjectableGeneratorAttribute.g.cs", SR.AttributeSource);

            IReadOnlyList<GeneratorAttributeInfo> attributedGenerators = Array.Empty<GeneratorAttributeInfo>();
            try
            {
                attributedGenerators = GetAttributedGeneratorSymbols(context);
                if (attributedGenerators.Count == 0)
                {
                    return;
                }
                var attributeLocations = attributedGenerators.Select(x => x.AttributeLocation).ToList();

                // Check target framework version
                var tfm = GetTargetFrameworkMoniker(context.Compilation);
                SR.ReportDebug(context, attributeLocations, $"Target framework: {tfm ?? "unknown"}");

                if (IsUnsupportedFramework(context.Compilation))
                {
                    foreach (var info in attributedGenerators)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(SR.UnsupportedFrameworkDescriptor, info.AttributeLocation, tfm ?? "unknown"));
                    }

                    return;
                }

                // Check for duplicate generators
                var groupedGenerators = attributedGenerators
                    .GroupBy(g => g.TypeSymbol, SymbolEqualityComparer.Default)
                    ;//.ToList();

                var hasDuplicates = false;
                foreach (var group in groupedGenerators)
                {
                    if (group.Count() > 1)
                    {
                        hasDuplicates = true;
                        foreach (var info in group)
                        {
                            context.ReportDiagnostic(Diagnostic.Create(SR.DuplicateGeneratorDescriptor, info.AttributeLocation, info.TypeSymbol.Name));
                        }
                    }
                }
                if (hasDuplicates)
                {
                    return;
                }

                // trace log is always required
                SR.ReportDebug(context, attributeLocations, "Compiling generator assembly...");

                using var runtime = new AnalyzerRuntimeCompilation(context);

                var compilationError = runtime.Compile(attributedGenerators);
                if (compilationError is not null)
                {
                    foreach (var info in attributedGenerators)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(SR.CompilationFailedDescriptor, info.AttributeLocation, compilationError));
                    }

                    var errorSource = $@"/* Runtime compilation failed.
{compilationError}
*/";
                    context.AddSource($"++Error++{nameof(AnalyzerRuntimeCompilation)}.g.cs", errorSource);
                    return;
                }

                // trace log is always required
                SR.ReportDebug(context, attributeLocations, "Processing generators...");

                foreach (var syntaxTree in context.Compilation.SyntaxTrees)
                {
                    var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);
                    var root = syntaxTree.GetRoot(context.CancellationToken);
                    foreach (var declaration in root.DescendantNodes().OfType<TypeDeclarationSyntax>())
                    {
                        if (semanticModel.GetDeclaredSymbol(declaration, context.CancellationToken) is not INamedTypeSymbol typeSymbol)
                        {
                            // trace log is always required
                            SR.ReportDebug(context, attributeLocations, $"Ignored type: '{declaration?.Identifier}'");

                            continue;
                        }

                        var isPartial = declaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword));
                        var isRecord = declaration is RecordDeclarationSyntax;
                        var location = declaration.Identifier.GetLocation();

                        // trace log is always required
                        SR.ReportDebug(context, attributeLocations, $"Generating for '{typeSymbol.Name}'");

                        runtime.Invoke(typeSymbol, isPartial, isRecord, location);
                    }
                }
            }
            catch (Exception ex)
            {
                if (attributedGenerators.Count == 0)
                {
                    try
                    {
                        attributedGenerators = GetAttributedGeneratorSymbols(context);
                    }
                    catch
                    {
                        // ignore
                    }
                }

                foreach (var info in attributedGenerators)
                {
                    context.ReportDiagnostic(Diagnostic.Create(SR.UnhandledExceptionDescriptor, info.AttributeLocation, ex.ToString()));
                }

                var source = $@"/*
Generator execution failed: InjectableSourceGenerator
{ex}
*/";
                context.AddSource($"++Error++{nameof(InjectableSourceGenerator)}.g.cs", source);
            }
        }

        private static IReadOnlyList<GeneratorAttributeInfo> GetAttributedGeneratorSymbols(GeneratorExecutionContext context)
        {
            var symbols = new List<GeneratorAttributeInfo>();

            foreach (var syntaxTree in context.Compilation.SyntaxTrees)
            {
                var semanticModel = context.Compilation.GetSemanticModel(syntaxTree);
                var root = syntaxTree.GetRoot(context.CancellationToken);

                foreach (var attributeList in root.DescendantNodes().OfType<AttributeListSyntax>())
                {
                    if (attributeList.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) != true)
                    {
                        continue;
                    }

                    foreach (var attribute in attributeList.Attributes)
                    {
                        if (!IsInjectableGeneratorAttribute(attribute))
                        {
                            continue;
                        }

                        if (TryResolveGeneratorTypeSymbol(attribute, semanticModel, context.CancellationToken) is INamedTypeSymbol typeSymbol)
                        {
                            if (typeSymbol.IsGenericType)
                            {
                                context.ReportDiagnostic(Diagnostic.Create(SR.GenericGeneratorDescriptor, attribute.GetLocation(), typeSymbol.ToDisplayString()));
                                continue;
                            }

                            symbols.Add(new GeneratorAttributeInfo(typeSymbol, attribute.GetLocation()));
                        }
                    }
                }
            }

            return symbols.Count == 0 ? Array.Empty<GeneratorAttributeInfo>() : symbols;
        }


        private static bool IsInjectableGeneratorAttribute(AttributeSyntax attribute)
        {
            var nameText = attribute.Name.ToString();
            if (nameText.StartsWith("global::", StringComparison.Ordinal))
            {
                nameText = nameText.Substring("global::".Length);
            }

            if (nameText.EndsWith("Attribute", StringComparison.Ordinal))
            {
                nameText = nameText.Substring(0, nameText.Length - "Attribute".Length);
            }

            return nameText == "InjectableGenerator" || nameText == "InjectableGenerator.InjectableGenerator";
        }

        private static INamedTypeSymbol? TryResolveGeneratorTypeSymbol(AttributeSyntax attribute, SemanticModel model, System.Threading.CancellationToken cancellationToken)
        {
            if (attribute.ArgumentList is null || attribute.ArgumentList.Arguments.Count == 0)
            {
                return null;
            }

            var argument = attribute.ArgumentList.Arguments[0];
            if (argument.Expression is not TypeOfExpressionSyntax typeOfExpression)
            {
                return null;
            }

            var typeInfo = model.GetTypeInfo(typeOfExpression.Type, cancellationToken);
            return typeInfo.Type as INamedTypeSymbol;
        }

        private static bool IsUnsupportedFramework(Compilation compilation)
        {
            var tfm = GetTargetFrameworkMoniker(compilation);
            if (tfm == null)
                return false;

            // Check for .NET (Core) versions > 8.0
            if (tfm.StartsWith("net") && !tfm.StartsWith("netstandard") && !tfm.StartsWith("netcoreapp"))
            {
                // Extract version number (e.g., "net9.0" -> 9.0, "net10.0" -> 10.0, "net8.0" -> 8.0)
                var versionPart = tfm.Substring(3);
                if (double.TryParse(versionPart, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var version))
                {
                    return version > 8.0;
                }
            }

            return false;
        }

        private static string? GetTargetFrameworkMoniker(Compilation compilation)
        {
            var targetFrameworkAttribute = compilation.Assembly.GetAttributes()
                .FirstOrDefault(a => a.AttributeClass?.Name == "TargetFrameworkAttribute");

            if (targetFrameworkAttribute?.ConstructorArguments.Length > 0)
            {
                var frameworkName = targetFrameworkAttribute.ConstructorArguments[0].Value?.ToString();
                if (frameworkName != null)
                {
                    // Extract TFM from framework name (e.g., ".NETCoreApp,Version=v9.0" -> "net9.0")
                    if (frameworkName.Contains("Version=v"))
                    {
                        var versionStart = frameworkName.IndexOf("Version=v") + 9;
                        var version = frameworkName.Substring(versionStart);

                        if (frameworkName.Contains(".NETCoreApp") || frameworkName.Contains(".NET,"))
                        {
                            return "net" + version;
                        }
                        else if (frameworkName.Contains(".NETStandard"))
                        {
                            return "netstandard" + version;
                        }
                    }
                }
            }

            return null;
        }

        private static IEnumerable<INamedTypeSymbol> GetAllTypes(Compilation compilation)
        {
            var types = new List<INamedTypeSymbol>();
            var stack = new Stack<INamespaceSymbol>();
            stack.Push(compilation.GlobalNamespace);

            while (stack.Count > 0)
            {
                var ns = stack.Pop();
                foreach (var type in ns.GetTypeMembers())
                {
                    types.Add(type);
                }

                foreach (var childNs in ns.GetNamespaceMembers())
                {
                    stack.Push(childNs);
                }
            }
            return types;
        }
    }
}
