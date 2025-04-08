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

class SubprocessCLIOptions
{
    [Option("subprocess", Required = false)]
    public bool SubProcess { get; set; }

    [Option("wasm-debug", Required = false)]
    public bool WasmDebug { get; set; }

    [Option('p', "in-pipe-handle", Required = false)]
    public string? InPipeHandle { get; set; }

    [Option('p', "out-pipe-handle", Required = false)]
    public string? OutPipeHandle { get; set; }
}