#if DEBUG

using InjectableGenerator;
using Sample;
using System;

// TODO: code fix is implemented but not work as expected. Fix it later.
[assembly: InjectableGenerator(typeof(SampleGenerator))]

namespace Sample
{
    internal class SampleGenerator
    {
        internal static bool Generate(Type type, bool isPartial, bool isRecord,
            out string? info, out string? warning, out string? error, out string? source)
        {
            // INJECT001
            //throw new System.Exception();

            info = warning = error = source = null;

            if (type != typeof(Test))
            {
                info = "Message from injected generator! (info)";
                warning = "Message from injected generator! (warn)";
                error = "Message from injected generator! (error)";
                return false;
            }

            source =
$@"public static class {type.Name}Extensions
{{
    public static string GetInjectedMessage(this {type.FullName} instance)
    {{
        return ""Hello from injected code!"";
    }}
}}
";
            return true;
        }
    }
}

#endif
