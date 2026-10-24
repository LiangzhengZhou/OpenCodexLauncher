using System;
using System.Linq;

// Offline test process. It never speaks to a model or the network.
class MockAppServer
{
    static int Main(string[] args)
    {
        foreach (var arg in args)
        {
            if (arg.StartsWith("--mock-exit=", StringComparison.Ordinal))
                return Int32.Parse(arg.Substring("--mock-exit=".Length));
        }
        if (args.Contains("--mock-partial"))
        {
            Console.Out.Write("{\"id\":1");
            Console.Out.Flush();
            return 0;
        }
        return 0;
    }
}
