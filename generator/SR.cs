using Microsoft.CodeAnalysis;
using System.Collections.Generic;
using System.Diagnostics;

#pragma warning disable RS2008 // Enable analyzer release tracking
#pragma warning disable RS1032 // Define diagnostic message correctly

namespace InjectableGenerator.Generator
{
    public static class SR
    {
        public const string AttributeMetadataName = "InjectableGenerator.InjectableGeneratorAttribute";
        public const string AttributeSource =
@"using System;

namespace InjectableGenerator
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true, Inherited = false)]
    internal sealed class InjectableGeneratorAttribute : Attribute
    {
        public InjectableGeneratorAttribute(Type generatorType)
        {
            GeneratorType = generatorType;
        }

        public Type GeneratorType { get; }
    }
}
";

        const string PREFIX = "INJECT";
        const string CATEGORY = "InjectableGenerator";

        public static readonly DiagnosticDescriptor ExecutionFailedDescriptor = new(
            id: PREFIX + "001",
            title: "Failed to execute injectable generator",
            messageFormat: "Generator '{0}' threw an exception\n{1}",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor CompilationFailedDescriptor = new(
            id: PREFIX + "002",
            title: "Failed to emit compilation",
            messageFormat: "Injectable generator infrastructure could not emit the target compilation\n{0}",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor MissingGeneratorMethodDescriptor = new(
            id: PREFIX + "003",
            title: "Invalid injectable generator",
            messageFormat: "Generator '{0}' must define the required static Generate method",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor GenericGeneratorDescriptor = new(
            id: PREFIX + "004",
            title: "Generic injectable generator",
            messageFormat: "Generator '{0}' cannot be generic",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnhandledExceptionDescriptor = new(
            id: PREFIX + "005",
            title: "Unhandled exception in InjectableGenerator",
            messageFormat: "InjectableGenerator encountered an unhandled exception\n{0}",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ResolutionFailedDescriptor = new(
            id: PREFIX + "006",
            title: "Failed to resolve injectable generator",
            messageFormat: "Failed to resolve runtime type '{0}' in compiled assembly in analyzer environment. This may occur if the generator uses types not available in the source generator runtime environment.",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor UnsupportedFrameworkDescriptor = new(
            id: PREFIX + "007",
            title: "Unsupported target framework",
            messageFormat: "Target framework '{0}' is not supported. InjectableGenerator only supports .NET 8.0 and earlier.",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor DuplicateGeneratorDescriptor = new(
            id: PREFIX + "008",
            title: "Duplicate injectable generator reference",
            messageFormat: "Generator '{0}' is referenced by multiple InjectableGenerator attributes",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);


        public static readonly DiagnosticDescriptor InfoDescriptor = new(
            id: PREFIX + "901",
            title: "Info diagnostic from Injected generator",
            messageFormat: "{0}",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor WarningDescriptor = new(
            id: PREFIX + "902",
            title: "Warning diagnostic from Injected generator",
            messageFormat: "{0}",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public static readonly DiagnosticDescriptor ErrorDescriptor = new(
            id: PREFIX + "903",
            title: "Error diagnostic from Injected generator",
            messageFormat: "{0}",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true);


        public static readonly DiagnosticDescriptor DebugDescriptor = new(
            id: PREFIX + "xDEBUG",
            title: "Injectable generator debug",
            messageFormat: "{0}",
            category: CATEGORY,
            defaultSeverity: DiagnosticSeverity.Info,
            isEnabledByDefault: true);

        [Conditional("DEBUG")]
        public static void ReportDebug(GeneratorExecutionContext context, Location location, string message)
        {
            context.ReportDiagnostic(Diagnostic.Create(DebugDescriptor, location, message));
        }

        [Conditional("DEBUG")]
        public static void ReportDebug(GeneratorExecutionContext context, IEnumerable<Location> locations, string message)
        {
            foreach (var location in locations)
            {
                context.ReportDiagnostic(Diagnostic.Create(DebugDescriptor, location, message));
            }
        }
    }
}
