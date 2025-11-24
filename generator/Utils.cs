using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace InjectableGenerator.Generator
{
    internal static class Utils
    {
        public static void AppendNamespace(INamespaceSymbol? symbol, StringBuilder builder)
        {
            if (symbol is null || symbol.IsGlobalNamespace)
            {
                return;
            }

            AppendNamespace(symbol.ContainingNamespace, builder);
            if (builder.Length > 0)
            {
                builder.Append('.');
            }

            builder.Append(symbol.Name);
        }

        public static string GetMetadataTypeName(INamedTypeSymbol symbol)
        {
            var builder = new StringBuilder();
            AppendNamespace(symbol.ContainingNamespace, builder);

            var containingTypes = new Stack<INamedTypeSymbol>();
            var current = symbol;
            while (current is not null)
            {
                containingTypes.Push(current);
                current = current.ContainingType;
            }

            if (builder.Length > 0 && containingTypes.Count > 0)
            {
                builder.Append('.');
            }

            if (containingTypes.Count > 0)
            {
                var outer = containingTypes.Pop();
                builder.Append(outer.MetadataName);

                while (containingTypes.Count > 0)
                {
                    builder.Append('+');
                    builder.Append(containingTypes.Pop().MetadataName);
                }
            }

            return builder.ToString();
        }

        public static string CreateHintName(Type generatorType, INamedTypeSymbol typeSymbol)
        {
            var generatorName = Sanitize(generatorType.FullName ?? generatorType.Name);
            var typeName = Sanitize(GetMetadataTypeName(typeSymbol));
            return $"{typeName} - {generatorName}.g.cs";
        }

        public static string CreateHintName(string generatorTypeName, INamedTypeSymbol typeSymbol)
        {
            var generatorName = Sanitize(generatorTypeName);
            var typeName = Sanitize(GetMetadataTypeName(typeSymbol));
            return $"{typeName}__{generatorName}.g.cs";
        }

        public static string Sanitize(string value)
        {
            var builder = new StringBuilder(value.Length);
            foreach (var ch in value)
            {
                if (ch is '`')
                {
                    builder.Append('T');
                }
                else if (ch is '<' or '>')
                {
                    builder.Append('+');
                }
                else if (ch is '.')
                {
                    builder.Append(ch);
                }
                else if (!char.IsLetterOrDigit(ch))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(ch);
                }
            }

            return builder.ToString();
        }


        /*  CLR full qualified name  ================================================================ */

        public static string GetClrFullQualifiedNameWithoutAssemblyVersion(this ITypeSymbol type, Compilation compilation)
        {
            switch (type)
            {
                case INamedTypeSymbol named:

                    if (named.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)
                    {
                        return $"System.Nullable[{GetClrFullQualifiedNameWithoutAssemblyVersion(named.TypeArguments[0], compilation)}]";
                    }

                    if (named.NullableAnnotation == NullableAnnotation.Annotated && named.IsReferenceType)
                    {
                        return GetNestedTypeName(named, compilation);
                    }

                    if (named.IsTupleType)
                    {
                        var args = string.Join(",", named.TupleElements.Select(e => GetClrFullQualifiedNameWithoutAssemblyVersion(e.Type, compilation)));
                        return $"System.ValueTuple[{args}]";
                    }

                    if (named.IsGenericType)
                    {
                        var args = string.Join(",", named.TypeArguments.Select(x => GetClrFullQualifiedNameWithoutAssemblyVersion(x, compilation)));
                        return $"{GetNestedTypeName(named, compilation)}[{args}]";
                    }

                    return GetNestedTypeName(named, compilation);

                case IArrayTypeSymbol arr:
                    return $"{GetClrFullQualifiedNameWithoutAssemblyVersion(arr.ElementType, compilation)}[{new string(',', arr.Rank - 1)}]";

                default:
                    return GetNestedTypeName(type, compilation);
            }
        }

        private static string GetNestedTypeName(ITypeSymbol type, Compilation compilation)
        {
            string name;

            if (type is INamedTypeSymbol named && named.ContainingType != null)
            {
                name = GetNestedTypeName(named.ContainingType, compilation) + "+" + named.Name;
            }
            else if (!string.IsNullOrEmpty(type.ContainingNamespace?.ToString()))
            {
                name = type.ContainingNamespace + "." + type.Name;
            }
            else
            {
                name = type.Name;
            }

            // omit assembly name if it's declared in target assembly
            if (type.ContainingAssembly != null &&
                !SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, compilation.Assembly))
            {
                name += ", " + type.ContainingAssembly.Name;
            }

            return name;
        }
    }
}
