using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Galena_Action_Ring;

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
            Nodes = new List<RingNode>
            {
                new() { Glyph = "\uE774", Label = "Chrome",  ActionType = ActionType.LaunchApp,  ActionData = "chrome" },
                new() { Glyph = "\uE8B9", Label = "VLC",     ActionType = ActionType.LaunchApp,  ActionData = "vlc" },
                new() { Glyph = "\uE995", Label = "Vol+",    ActionType = ActionType.VolumeUp },
                new() { Glyph = "\uE994", Label = "Vol-",    ActionType = ActionType.VolumeDown },
                new() { Glyph = "\uE74F", Label = "Mute",    ActionType = ActionType.MuteToggle },
                new() { Glyph = "\uE706", Label = "Bright+", ActionType = ActionType.BrightnessUp },
                new() { Glyph = "\uE708", Label = "Bright-", ActionType = ActionType.BrightnessDown },
                new() { Glyph = "\uE768", Label = "Play",    ActionType = ActionType.MediaPlayPause },
            }
        };
    }
}
