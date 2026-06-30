using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GalenaActionRing;

public class ProfileService
{
    private static readonly string ProfilesDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Galena", "profiles");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    private static void EnsureDir()
    {
        Directory.CreateDirectory(ProfilesDir);
    }

    public static string[] ListProfiles()
    {
        EnsureDir();
        var files = Directory.GetFiles(ProfilesDir, "*.json");
        for (int i = 0; i < files.Length; i++)
            files[i] = Path.GetFileNameWithoutExtension(files[i]);
        return files;
    }

    public static RingProfile? LoadProfile(string name)
    {
        EnsureDir();
        var path = Path.Combine(ProfilesDir, $"{name}.json");
        if (!File.Exists(path)) return null;
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<RingProfile>(json);
    }

    public static void SaveProfile(RingProfile profile)
    {
        EnsureDir();
        var path = Path.Combine(ProfilesDir, $"{profile.Name}.json");
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(path, json);
    }

    public static bool DeleteProfile(string name)
    {
        var path = Path.Combine(ProfilesDir, $"{name}.json");
        if (!File.Exists(path)) return false;
        File.Delete(path);
        return true;
    }

    public static RingProfile CreateDefault()
    {
        return new RingProfile
        {
            Name = "Default",
            Radius = 120,
            PrimaryColor = "#FF000000",
            SecondaryColor = "#80FFFFFF",
            Nodes = new List<RingNode>
            {
                new() { Glyph = "\uE050", Label = "Volume", ActionType = ActionType.VolumeControl, Category = ActionCategory.Group },
                new() { Glyph = "\uE1AC", Label = "Brightness", ActionType = ActionType.BrightnessControl, Category = ActionCategory.Group },
                new() { Glyph = "\uEA5F", Label = "Calc", ActionType = ActionType.LaunchApp, ActionData = "calc" },
                new() { Glyph = "\uEF7A", Label = "Chrome", ActionType = ActionType.OpenUrl, ActionData = "https://www.google.com" },
                new() { Glyph = "\uE710", Label = "Mute",    ActionType = ActionType.MuteToggle },
                new() { Glyph = "\uF6B5", Label = "Playback Control", ActionType = ActionType.Folder, Category = ActionCategory.Folder,
                        Children = new List<RingNode>
                        {
                            new() { Glyph = "\uEAC9", Label = "Seek",   ActionType = ActionType.MediaSeekForward },
                            new() { Glyph = "\uE044", Label = "Next",   ActionType = ActionType.MediaNext },
                            new() { Glyph = "\uEF6A", Label = "Play",   ActionType = ActionType.MediaPlayPause },
                            new() { Glyph = "\uE045", Label = "Prev",   ActionType = ActionType.MediaPrevious },
                        }},
                new() { Glyph = "\uF03D", Label = "Night",   ActionType = ActionType.ToggleNightLight },
                new() { Glyph = "\uE0C9", Label = "ChatGPT", ActionType = ActionType.OpenUrl, ActionData = "https://chatgpt.com" },
            }
        };
    }
}
