using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;

namespace DreamboxVM;

class Program
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CLIOptions))]
    static void Main(string[] args)
    {
    #if ENABLE_SEPARATE_PROCESS
        if (args.Contains("--subprocess"))
        {
            var app = new DreamboxVMSubprocess();
            app.Run();
        }
        else
        {
            var app = new DreamboxVMHost();
            app.Run();
        }
    #else
        var app = new DreamboxApp();
        app.Run();
    #endif
    }
}
