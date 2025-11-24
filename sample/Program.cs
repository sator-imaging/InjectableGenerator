//#define __expected_error
#if __expected_error

using System;

namespace Sample
{
    internal class Program
    {
        public static void Main()
        {
            var test = new Test();

            // NOTE: cannot use generated method in same assembly.
            var message = test.GetInjectedMessage();
            Console.WriteLine(message);
        }
    }
}

#endif
