using CommandLine;

namespace DreamboxVM;

class CLIOptions
{
    [Option("wasm-debug", Required = false, HelpText = "Enable WASM debugging via GDB")]
    public bool WasmDebug { get; set; }

    [Option('b', "skip-bios", Required = false, HelpText = "Skip the BIOS animation and load straight into game discs")]
    public bool SkipBios { get; set; }

    [Option('s', "startcd", Required = false, HelpText = "Load the given ISO on startup")]
    public string? StartCD { get; set; }
}