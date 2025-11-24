#if DEBUG

using InjectableGenerator;
using Sample;

[assembly: InjectableGenerator(typeof(Test))]

namespace Sample
{
    internal class Test
    {
    }
}

#endif
