using System.Diagnostics.CodeAnalysis;

namespace DreamboxVM;

class Program
{
    [DynamicDependency(DynamicallyAccessedMemberTypes.All, typeof(CLIOptions))]
    static void Main()
    {
        var app = new DreamboxApp();
        app.Run();
    }
}
