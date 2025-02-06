using System.Text.Json;
using System.Text.Json.Serialization;
using DreamboxVM.VM;
using SDL3;

namespace DreamboxVM;

public enum DreamboxVideoMode
{
    Default,
    VGA,
    Composite,
    SVideo,
}

struct GamepadSettings(string? deviceName)
{
    [JsonPropertyName("deviceName")] public string? DeviceName { get; set; } = deviceName;
    [JsonPropertyName("kb")] public KeyboardConfig Kb { get; set; } = new ();
    [JsonPropertyName("gp")] public GamepadConfig Gp { get; set; } = new ();
}

class DreamboxConfig
{
    [JsonPropertyName("lang")] public string Lang { get; set; } = "en";
    [JsonPropertyName("audioVolume")] public float AudioVolume { get; set; } = 1.0f;
    [JsonPropertyName("videoMode")] public DreamboxVideoMode VideoMode { get; set; } = DreamboxVideoMode.Default;
    [JsonPropertyName("displayClock24Hr")] public bool DisplayClock24Hr { get; set; } = false;
    [JsonPropertyName("disableFrameskips")] public bool DisableFrameskips { get; set; } = false;
    [JsonPropertyName("fullscreen")] public bool Fullscreen { get; set; } = false;
    [JsonPropertyName("hideMenu")] public bool HideMenu { get; set; } = false;
    [JsonPropertyName("recentGames")] public List<string> RecentGames { get; set; } = [];
    [JsonPropertyName("gamepads")] public GamepadSettings[] Gamepads { get; set; } = [ new ("Keyboard"), new (null), new (null), new (null) ];

    public void AddGame(string path)
    {
        RecentGames.Remove(path);
        RecentGames.Add(path);
    }

    public static DreamboxConfig LoadPrefs()
    {
        string configPath = PathUtils.GetPath("config.json");
        DreamboxConfig config;

        if (File.Exists(configPath))
        {
            config = JsonSerializer.Deserialize(File.ReadAllText(configPath), SourceGenerationContext.Default.DreamboxConfig)!;
            Console.WriteLine("Config loaded: " + configPath);
        }
        else
        {
            config = new DreamboxConfig();
        }

        return config;
    }

    public static void SavePrefs(DreamboxConfig config)
    {
        string configPath = PathUtils.GetPath("config.json");
        Console.WriteLine("Saving config to " + configPath);
        
        string configJson = JsonSerializer.Serialize(config, SourceGenerationContext.Default.DreamboxConfig);
        File.WriteAllText(configPath, configJson);
    }
}

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(DreamboxConfig))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}