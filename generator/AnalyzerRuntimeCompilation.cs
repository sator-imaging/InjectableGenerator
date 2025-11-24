using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Text;

namespace InjectableGenerator.Generator
{
    internal sealed class AnalyzerRuntimeCompilation : IDisposable
    {
        private readonly GeneratorExecutionContext _context;
        private Assembly? _runtimeAssembly;
        private IReadOnlyList<GeneratorInvoker> _runtimeGenerators = Array.Empty<GeneratorInvoker>();
        private IReadOnlyList<GeneratorAttributeInfo> _attributedGenerators = Array.Empty<GeneratorAttributeInfo>();
        private string? _compilationError;

        public AnalyzerRuntimeCompilation(GeneratorExecutionContext context)
        {
            _context = context;
        }

        public void Dispose()
        {
            if (_runtimeAssembly is not null)
            {
                var loadContext = AssemblyLoadContext.GetLoadContext(_runtimeAssembly);
                (loadContext as IDisposable)?.Dispose();
            }
        }

        public string? Compile(IReadOnlyList<GeneratorAttributeInfo> attributedGenerators)
        {
            _attributedGenerators = attributedGenerators;
            if (attributedGenerators.Count == 0)
            {
                return null;
            }

            var parseOptions = _context.ParseOptions as CSharpParseOptions;
            var generatorSyntaxTrees =
                // NOTE: injected generators run in ANALYZER environment so need to compile whole assembly in analyzer...
                //       -->  even through using full qualified type name cannot eliminate target assembly dependency.
                _context.Compilation.SyntaxTrees
                .Where(t => !t.FilePath.EndsWith(".g.cs", StringComparison.Ordinal) && !t.FilePath.EndsWith(".generated.cs", StringComparison.Ordinal));
            //attributedGenerators
            //.SelectMany(g => g.TypeSymbol.DeclaringSyntaxReferences.Select(r => r.SyntaxTree))
            //;//.Distinct();

            // Filter references to include only core runtime assemblies
            // This prevents issues when the target project uses a different framework version
            var coreReferences = _context.Compilation.References
                // TODO: Filtering references doesn't solve the problem occurred when
                //       target framework is greater than net9.0.
                // .Where(r =>
                // {
                //     var display = r.Display ?? "";
                //     // Include only essential runtime assemblies
                //     return display.Contains("System.Runtime.dll") ||
                //            display.Contains("System.Private.CoreLib.dll") ||
                //            display.Contains("netstandard.dll") ||
                //            display.Contains("mscorlib.dll");
                // })
                // .ToList();
                ;

            var attributeSyntaxTree = CSharpSyntaxTree.ParseText(SR.AttributeSource, parseOptions, encoding: Encoding.UTF8);

            var compilation = CSharpCompilation.Create(
                assemblyName: $"InjectableGenerator_{Guid.NewGuid()}",
                syntaxTrees: generatorSyntaxTrees.Append(attributeSyntaxTree),
                references: coreReferences,
                options: _context.Compilation.Options as CSharpCompilationOptions);

            if (!TryEmitCompilation(compilation, out var image, out var diagnostics, out var exception))
            {
                _compilationError = string.Join("\n", diagnostics.Select(d => d.ToString()));
                if (exception is not null)
                {
                    _compilationError += "\n" + exception;
                }

                return _compilationError;
            }

            using var peStream = new MemoryStream(image, writable: false);
            var loadContext = new InjectableGeneratorLoadContext(_context.Compilation);
            _runtimeAssembly = loadContext.LoadFromStream(peStream);

            _runtimeGenerators = ResolveGeneratorInvokers(_runtimeAssembly, attributedGenerators);
            if (_runtimeGenerators.Count == 0)
            {
                return null;
            }

            // trace log is always required
            foreach (var generator in _runtimeGenerators)
            {
                SR.ReportDebug(_context, generator.AttributeLocation, $"Resolved runtime generator '{generator.Type.Name}'");
            }

            return null;
        }

        public void Invoke(INamedTypeSymbol typeSymbol, bool isPartial, bool isRecord, Location location)
        {
            if (_compilationError is not null)
            {
                foreach (var info in _attributedGenerators)
                {
                    AddErrorSource(info.TypeSymbol, typeSymbol, _compilationError);
                }
                return;
            }

            if (//_runtimeAssembly is null ||
                _runtimeGenerators.Count == 0)
            {
                return;
            }

            if (!TryResolveRuntimeType(_runtimeAssembly, typeSymbol, out var runtimeType))
            {
                foreach (var generator in _runtimeGenerators)
                {
                    _context.ReportDiagnostic(Diagnostic.Create(
                        SR.ResolutionFailedDescriptor,
                        location,
                        $"Failed to resolve runtime type '{typeSymbol.ToDisplayString()}' for generator '{generator.Type.Name}'. The type may use features not available in the analyzer runtime environment."));
                }
                return;
            }

            foreach (var generator in _runtimeGenerators)
            {
                SR.ReportDebug(_context, generator.AttributeLocation, $"Invoking generator '{generator.Type.Name}' for '{typeSymbol.Name}'");
                InvokeGenerator(generator, runtimeType!, typeSymbol, isPartial, isRecord, location);
            }
        }

        private void InvokeGenerator(
            GeneratorInvoker generator,
            Type runtimeType,
            INamedTypeSymbol typeSymbol,
            bool isPartial,
            bool isRecord,
            Location location)
        {
            var arguments = new object?[] { runtimeType, isPartial, isRecord, null, null, null, null };
            bool generationRequested;

            try
            {
                generationRequested = generator.Method.Invoke(null, arguments) as bool? ?? false;
            }
            catch (TargetInvocationException ex)
            {
                var message = ex.InnerException?.ToString() ?? ex.ToString();
                _context.ReportDiagnostic(Diagnostic.Create(SR.ExecutionFailedDescriptor, generator.AttributeLocation, generator.Type.FullName ?? generator.Type.Name, message));
                AddErrorSource(generator.Type, typeSymbol, message);
                return;
            }
            catch (Exception ex)
            {
                var message = ex.ToString();
                _context.ReportDiagnostic(Diagnostic.Create(SR.ExecutionFailedDescriptor, generator.AttributeLocation, generator.Type.FullName ?? generator.Type.Name, message));
                AddErrorSource(generator.Type, typeSymbol, message);
                return;
            }

            if (arguments[3] is string info)
            {
                _context.ReportDiagnostic(Diagnostic.Create(SR.InfoDescriptor, location, info));
            }

            if (arguments[4] is string warning)
            {
                _context.ReportDiagnostic(Diagnostic.Create(SR.WarningDescriptor, location, warning));
            }

            if (arguments[5] is string error)
            {
                _context.ReportDiagnostic(Diagnostic.Create(SR.ErrorDescriptor, location, error));
            }

            var source = arguments[6] as string;

            if (!generationRequested)
            {
                SR.ReportDebug(_context, generator.AttributeLocation, $"Generator '{generator.Type.Name}' returned false");
            }
            else
            {
                SR.ReportDebug(_context, generator.AttributeLocation, $"Generator '{generator.Type.Name}' returned true");

                var header = $@"// <auto-generated>{nameof(InjectableGenerator)}</auto-generated>
// Generator: {generator.Type.Name}
// Target: {typeSymbol.Name}
";

                if (string.IsNullOrWhiteSpace(source))
                {
                    source = $@"{header}
// The generator returned true but provided no source code.";
                }
                else if (source is not null)
                {
                    source = $@"{header}
{source}";
                }

                if (source is not null)
                {
                    SR.ReportDebug(_context, generator.AttributeLocation, $"File is generated for '{typeSymbol.Name}'");

                    var hintName = Utils.CreateHintName(generator.Type, typeSymbol);
                    _context.AddSource(hintName, source);
                }
            }
        }

        private static bool TryEmitCompilation(Compilation compilation, out byte[] image, out System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics, out Exception? exception)
        {
            using var peStream = new MemoryStream();
            EmitResult result;
            try
            {
                result = compilation.Emit(peStream);
                exception = null;
            }
            catch (Exception ex)
            {
                image = Array.Empty<byte>();
                diagnostics = System.Collections.Immutable.ImmutableArray<Diagnostic>.Empty;
                exception = ex;
                return false;
            }

            if (!result.Success)
            {
                image = Array.Empty<byte>();
                diagnostics = result.Diagnostics;
                return false;
            }

            image = peStream.ToArray();
            diagnostics = System.Collections.Immutable.ImmutableArray<Diagnostic>.Empty;
            return true;
        }

        private IReadOnlyList<GeneratorInvoker> ResolveGeneratorInvokers(
            Assembly runtimeAssembly,
            IReadOnlyList<GeneratorAttributeInfo> attributedGenerators)
        {
            var invokers = new List<GeneratorInvoker>();
            var seen = new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default);
            foreach (var info in attributedGenerators)
            {
                var symbol = info.TypeSymbol;
                if (!seen.Add(symbol))
                {
                    continue;
                }

                if (!TryResolveRuntimeType(runtimeAssembly, symbol, out var type))
                {
                    _context.ReportDiagnostic(Diagnostic.Create(SR.ResolutionFailedDescriptor, info.AttributeLocation, symbol.ToDisplayString()));
                    continue;
                }

                var method = GetGenerateMethod(type!);
                if (method is null || method.ReturnType != typeof(bool))
                {
                    _context.ReportDiagnostic(Diagnostic.Create(SR.MissingGeneratorMethodDescriptor, info.AttributeLocation, info.TypeSymbol.ToDisplayString()));
                    continue;
                }

                invokers.Add(new GeneratorInvoker(type!, method, info.AttributeLocation));
            }

            return invokers;
        }

        private static MethodInfo? GetGenerateMethod(Type type)
        {
            var parameters = new[]
            {
                typeof(Type),
                typeof(bool),
                typeof(bool),
                typeof(string).MakeByRefType(),
                typeof(string).MakeByRefType(),
                typeof(string).MakeByRefType(),
                typeof(string).MakeByRefType()
            };

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            var method = type.GetMethod("Generate", flags, binder: null, types: parameters, modifiers: null);
            if (method is null)
            {
                return null;
            }

            if (method.ReturnType != typeof(bool))
            {
                return null;
            }

            return method;
        }

        private static bool TryResolveRuntimeType(Assembly? assembly, INamedTypeSymbol symbol, out Type? type)
        {
            if (assembly == null)
            {
                type = null;
                return false;
            }

            var metadataName = Utils.GetMetadataTypeName(symbol);
            type = assembly.GetType(metadataName, throwOnError: false, ignoreCase: false);
            return type is not null;
        }

        private void AddErrorSource(Type generatorType, INamedTypeSymbol typeSymbol, string error)
        {
            var generatorName = generatorType.FullName ?? generatorType.Name;
            var hintName = Utils.CreateHintName(generatorType, typeSymbol);
            var source = $@"/*
Generator execution failed: {generatorName}
{error}
*/";
            _context.AddSource(hintName, source);
        }

        private void AddErrorSource(INamedTypeSymbol generatorType, INamedTypeSymbol typeSymbol, string error)
        {
            var generatorName = generatorType.ToDisplayString();
            var hintName = Utils.CreateHintName(generatorName, typeSymbol);
            var source = $@"/*
Generator execution failed: {generatorName}
{error}
*/";
            _context.AddSource(hintName, source);
        }

        private class InjectableGeneratorLoadContext : AssemblyLoadContext, IDisposable
        {
            private readonly Compilation _compilation;

            public InjectableGeneratorLoadContext(Compilation compilation)
            {
                _compilation = compilation;
            }

            public void Dispose()
            {
                try
                {
                    // Use reflection to call Unload() if available (supported in .NET Core 3.0+ and Unity 2021.2+)
                    var unloadMethod = this.GetType().BaseType.GetMethod("Unload", BindingFlags.Public | BindingFlags.Instance);
                    unloadMethod?.Invoke(this, null);
                }
                catch
                {
                    // Ignore unload failures
                }
            }

            protected override Assembly? Load(AssemblyName assemblyName)
            {
                foreach (var reference in _compilation.References)
                {
                    if (reference is PortableExecutableReference peReference && peReference.FilePath is not null)
                    {
                        try
                        {
                            var fileName = Path.GetFileNameWithoutExtension(peReference.FilePath);
                            if (string.Equals(fileName, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
                            {
                                return LoadFromAssemblyPath(peReference.FilePath);
                            }
                        }
                        catch
                        {
                            // Ignore load failures
                        }
                    }
                }
                return null;
            }
        }
    }
}
