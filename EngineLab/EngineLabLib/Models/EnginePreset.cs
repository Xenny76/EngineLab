using System.Text.Json.Serialization;

namespace EngineLabLib.Models;

public sealed class EnginePreset
{
    public string Id { get; set; } = Guid.NewGuid().ToString(); // unique ID
    public string Name { get; set; } = "Untitled preset";
    public string? Description { get; set; }

    // Where this JSON lives and how it should be loaded
    public bool IsBuiltIn { get; set; }     // true = embedded/Resources, false = user file
    public string JsonFileName { get; set; } = "";  // e.g. "B6ZE_Default.json" or "user_123.json"

    public string? BasedOnPresetId { get; set; }     // optional: which preset it was derived from

    // 🔹 Convenience for XAML: only user presets can be deleted
    [JsonIgnore]
    public bool IsUserPreset => !IsBuiltIn;
}