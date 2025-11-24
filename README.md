# InjectableGenerator

A C# source generator that enables runtime code generation by compiling and invoking user-defined generators at compile time. This allows you to inject custom code generation logic into your build process without creating a full Roslyn analyzer package.

## ✨ Features

- **Runtime Generator Invocation**: Define generator classes in your project and invoke them at compile time
- **Type-Safe API**: Strongly-typed generator method signature with support for diagnostics and source output
- **Code Fix Provider**: Automatic code fix to add the required `Generate` method to your generator class
- **Comprehensive Diagnostics**: Detailed error reporting with specific diagnostic codes (INJECT001-INJECT007)
- **Framework Support**: Works with .NET Standard 2.0+ and .NET Core/Framework up to .NET 8.0

## 📦 Installation

Add the project reference to your `.csproj` file:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/InjectableGenerator.csproj">
    <OutputItemType>Analyzer</OutputItemType>
    <ReferenceOutputAssembly>false</ReferenceOutputAssembly>
  </ProjectReference>
</ItemGroup>
```

## 🚀 Usage

### 1. Define Your Generator

Create a class with a static `Generate` method that matches the required signature:

```csharp
using InjectableGenerator;
using System;

namespace MyProject
{
    internal class MyGenerator
    {
        public static bool Generate(
            Type type,
            bool isPartial,
            bool isRecord,
            out string? info,
            out string? warning,
            out string? error,
            out string? source)
        {
            info = warning = error = source = null;

            // Return false to skip generation for this type
            if (type.Name != "MyTargetType")
            {
                return false;
            }

            // Optionally report diagnostics
            info = "Generating code for " + type.Name;
            warning = "This is a warning message";
            // error = "This would be an error";

            // Generate source code
            source = $@"
public static class {type.Name}Extensions
{{
    public static string GetMessage(this {type.FullName} instance)
    {{
        return ""Hello from generated code!"";
    }}
}}";

            return true; // Indicates generation was requested
        }
    }
}
```

### 2. Register Your Generator

Add an assembly-level attribute to register your generator:

```csharp
[assembly: InjectableGenerator(typeof(MyGenerator))]
```

### 3. Use Generated Code

The generator will be invoked for every type declaration in your compilation:

```csharp
var instance = new MyTargetType();
// var message = instance.GetMessage(); // Extension method from generated code
// Console.WriteLine(message); // "Hello from generated code!"
```

> [!IMPORTANT]
> Generated code cannot be used directly in the same assembly where the generator is defined. You must reference the project containing the generator from another project to use the generated code, or use reflection if testing within the same assembly.

## 📝 Generator Method Signature

The `Generate` method must match this exact signature:

```csharp
public static bool Generate(
    Type type,           // The runtime Type being processed
    bool isPartial,      // Whether the type is declared as partial
    bool isRecord,       // Whether the type is a record
    out string? info,    // Optional info diagnostic message
    out string? warning, // Optional warning diagnostic message
    out string? error,   // Optional error diagnostic message
    out string? source)  // Generated source code
```

**Return Value:**
- `true`: Generation was requested for this type (source code will be added even if null/empty)
- `false`: Skip generation for this type (no source will be added)

**Parameters:**
- `type`: The runtime `Type` object for the type declaration being processed
- `isPartial`: `true` if the type has the `partial` modifier
- `isRecord`: `true` if the type is a `record` declaration
- `info`, `warning`, `error`: Set these to report diagnostics at the type's location
- `source`: Set this to the generated C# source code

## 🔍 Diagnostic Codes

| Code | Severity | Description |
|------|----------|-------------|
| INJECT001 | Error | Generator method threw an exception during execution |
| INJECT002 | Error | Failed to compile the generator assembly |
| INJECT003 | Error | Generator class is missing the required `Generate` method |
| INJECT004 | Error | Generator class cannot be generic |
| INJECT005 | Error | Unhandled exception in InjectableGenerator infrastructure |
| INJECT006 | Error | Failed to resolve generator type in compiled assembly |
| INJECT007 | Error | Target framework is not supported (only .NET 8.0 and earlier) |
| INJECT008 | Error | Generator is referenced by multiple InjectableGenerator attributes |
| INJECT901 | Info | Info message from your generator (via `info` parameter) |
| INJECT902 | Warning | Warning message from your generator (via `warning` parameter) |
| INJECT903 | Error | Error message from your generator (via `error` parameter) |

## 🛠️ Code Fix Provider

If you get an **INJECT003** error (missing `Generate` method), the code fix provider can automatically add the method stub to your generator class:

1. Place your cursor on the error
2. Press `Ctrl+.` (Quick Actions)
3. Select "Add Generate method"

This will add a properly formatted method with `throw new NotImplementedException();` as the body.

## ⚙️ How It Works

1. **Attribute Generation**: The source generator first emits the `InjectableGeneratorAttribute` class
2. **Discovery**: Scans for `[assembly: InjectableGenerator(typeof(...))]` attributes
3. **Compilation**: Compiles the generator class(es) into a runtime assembly using Roslyn
4. **Type Processing**: For each type declaration in your project:
   - Resolves the runtime `Type` object
   - Invokes the generator's `Generate` method
   - Adds the generated source code to the compilation
5. **Diagnostics**: Reports any errors, warnings, or info messages from the generator

The compilation happens in an isolated `AssemblyLoadContext` to avoid conflicts with the analyzer runtime.

## ⚠️ Limitations

- **Framework Support**: Only .NET 8.0 and earlier are supported. .NET 9.0+ will produce INJECT007 errors
- **Generic Generators**: Generator classes cannot be generic types (INJECT004)
- **Reflection Required**: Generators use reflection at compile time, so they need access to runtime type information
- **Roslyn Version**: Built against Roslyn 3.8.0 for compatibility with Unity 2021.2+

## 💡 Important Considerations

### Analyzer Environment Execution

**Your generator runs in the analyzer environment, not your target assembly's environment.** This has important implications:

1. **The `Type` parameter is a mirror type**: The `Type` object passed to your `Generate` method exists in the compiled analyzer assembly, not your target assembly. It has the same structure but is a different runtime type.

2. **Type reflection works**: You can use reflection on the `Type` parameter (e.g., `type.GetProperties()`, `type.GetMethods()`) because the analyzer assembly contains a compiled mirror of your types.

### Generated Types and Members in Same Assembly

**Note**: Generated types and members by InjectableGenerator cannot be used in the same assembly where they are generated. This is a limitation of how C# source generators work - the generated code is added to the compilation but isn't available for IntelliSense or usage within the same assembly.

## 📂 Sample Project

See the `sample/` directory for a complete working example:

- [`SampleGenerator.cs`](sample/SampleGenerator.cs): Example generator implementation
- [`Program.cs`](sample/Program.cs): Usage of generated code

## 🔨 Building

```bash
# Build the generator
dotnet build src/InjectableGenerator.csproj

# Build and run the sample
dotnet run --project sample/InjectableGenerator.Sample.csproj
```

## 📄 License

See [LICENSE](LICENSE.md) for details.
