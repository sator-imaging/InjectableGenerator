using Microsoft.CodeAnalysis;
using System;
using System.Reflection;

namespace InjectableGenerator.Generator
{
    internal sealed class GeneratorInvoker
    {
        public GeneratorInvoker(Type type, MethodInfo method, Location attributeLocation)
        {
            Type = type;
            Method = method;
            AttributeLocation = attributeLocation;
        }

        public Type Type { get; }

        public MethodInfo Method { get; }

        public Location AttributeLocation { get; }
    }

    internal sealed class GeneratorAttributeInfo
    {
        public GeneratorAttributeInfo(INamedTypeSymbol typeSymbol, Location attributeLocation)
        {
            TypeSymbol = typeSymbol;
            AttributeLocation = attributeLocation;
        }

        public INamedTypeSymbol TypeSymbol { get; }

        public Location AttributeLocation { get; }
    }
}
