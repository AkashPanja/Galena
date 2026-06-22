using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Galena_Action_Ring;

public class MaterialIconInfo
{
    public string Name { get; set; } = "";
    public string Glyph { get; set; } = "";
    public string DisplayName { get; set; } = "";
}

public static class MaterialIcons
{
    public static readonly string FontFamilyName = "Assets/MaterialSymbols.ttf#Material Symbols Outlined";

    private static List<MaterialIconInfo>? _allIcons;

    public static List<MaterialIconInfo> AllIcons
    {
        get
        {
            if (_allIcons == null)
                LoadAllIcons();
            return _allIcons ?? new List<MaterialIconInfo>();
        }
    }

    public static List<MaterialIconInfo> CommonIcons { get; } = new()
    {
        new() { Name = "volume_up", Glyph = "\uE050", DisplayName = "Volume Up" },
        new() { Name = "volume_down", Glyph = "\uE04D", DisplayName = "Volume Down" },
        new() { Name = "volume_off", Glyph = "\uE04F", DisplayName = "Volume Off" },
        new() { Name = "volume_mute", Glyph = "\uE04E", DisplayName = "Volume Mute" },
        new() { Name = "brightness_high", Glyph = "\uE1AC", DisplayName = "Brightness High" },
        new() { Name = "brightness_low", Glyph = "\uE1AD", DisplayName = "Brightness Low" },
        new() { Name = "brightness_medium", Glyph = "\uE1AE", DisplayName = "Brightness Medium" },
        new() { Name = "play_arrow", Glyph = "\uE037", DisplayName = "Play" },
        new() { Name = "pause", Glyph = "\uE034", DisplayName = "Pause" },
        new() { Name = "skip_next", Glyph = "\uE044", DisplayName = "Skip Next" },
        new() { Name = "skip_previous", Glyph = "\uE045", DisplayName = "Skip Previous" },
        new() { Name = "fast_forward", Glyph = "\uE01F", DisplayName = "Fast Forward" },
        new() { Name = "fast_rewind", Glyph = "\uE020", DisplayName = "Fast Rewind" },
        new() { Name = "play_circle", Glyph = "\uE1C4", DisplayName = "Play Circle" },
        new() { Name = "playlist_play", Glyph = "\uE05F", DisplayName = "Playlist Play" },
        new() { Name = "playlist_add", Glyph = "\uE03B", DisplayName = "Playlist Add" },
        new() { Name = "queue_music", Glyph = "\uE03D", DisplayName = "Queue Music" },
        new() { Name = "music_note", Glyph = "\uE405", DisplayName = "Music Note" },
        new() { Name = "album", Glyph = "\uE019", DisplayName = "Album" },
        new() { Name = "equalizer", Glyph = "\uE01D", DisplayName = "Equalizer" },
        new() { Name = "repeat", Glyph = "\uE040", DisplayName = "Repeat" },
        new() { Name = "shuffle", Glyph = "\uE043", DisplayName = "Shuffle" },
        new() { Name = "stop", Glyph = "\uE047", DisplayName = "Stop" },
        new() { Name = "mic", Glyph = "\uE31D", DisplayName = "Microphone" },
        new() { Name = "headphones", Glyph = "\uF01F", DisplayName = "Headphones" },
        new() { Name = "radio", Glyph = "\uE03E", DisplayName = "Radio" },
        new() { Name = "home", Glyph = "\uE9B2", DisplayName = "Home" },
        new() { Name = "search", Glyph = "\uEF7A", DisplayName = "Search" },
        new() { Name = "settings", Glyph = "\uE8B8", DisplayName = "Settings" },
        new() { Name = "language", Glyph = "\uEA07", DisplayName = "Language" },
        new() { Name = "chat", Glyph = "\uE0C9", DisplayName = "Chat" },
        new() { Name = "calculate", Glyph = "\uEA5F", DisplayName = "Calculate" },
        new() { Name = "dark_mode", Glyph = "\uE51C", DisplayName = "Dark Mode" },
        new() { Name = "light_mode", Glyph = "\uE518", DisplayName = "Light Mode" },
        new() { Name = "nightlight", Glyph = "\uF03D", DisplayName = "Night Light" },
        new() { Name = "launch", Glyph = "\uE89E", DisplayName = "Launch" },
        new() { Name = "link", Glyph = "\uE250", DisplayName = "Link" },
        new() { Name = "refresh", Glyph = "\uE5D5", DisplayName = "Refresh" },
        new() { Name = "power", Glyph = "\uE63C", DisplayName = "Power" },
        new() { Name = "close", Glyph = "\uE5CD", DisplayName = "Close" },
        new() { Name = "menu", Glyph = "\uE5D2", DisplayName = "Menu" },
        new() { Name = "more_vert", Glyph = "\uE5D4", DisplayName = "More" },
        new() { Name = "arrow_back", Glyph = "\uE5C4", DisplayName = "Arrow Back" },
        new() { Name = "arrow_forward", Glyph = "\uE5C8", DisplayName = "Arrow Forward" },
        new() { Name = "check", Glyph = "\uE5CA", DisplayName = "Check" },
        new() { Name = "check_circle", Glyph = "\uF0BE", DisplayName = "Check Circle" },
        new() { Name = "error", Glyph = "\uF8B6", DisplayName = "Error" },
        new() { Name = "info", Glyph = "\uE88E", DisplayName = "Info" },
        new() { Name = "warning", Glyph = "\uE002", DisplayName = "Warning" },
        new() { Name = "add", Glyph = "\uE145", DisplayName = "Add" },
        new() { Name = "delete", Glyph = "\uE92E", DisplayName = "Delete" },
        new() { Name = "edit", Glyph = "\uF097", DisplayName = "Edit" },
        new() { Name = "save", Glyph = "\uE161", DisplayName = "Save" },
        new() { Name = "lock", Glyph = "\uE897", DisplayName = "Lock" },
        new() { Name = "star", Glyph = "\uF09A", DisplayName = "Star" },
        new() { Name = "favorite", Glyph = "\uE87E", DisplayName = "Favorite" },
        new() { Name = "notifications", Glyph = "\uE7F4", DisplayName = "Notifications" },
        new() { Name = "bluetooth", Glyph = "\uE1A7", DisplayName = "Bluetooth" },
        new() { Name = "wifi", Glyph = "\uE63E", DisplayName = "WiFi" },
        new() { Name = "battery_full", Glyph = "\uE1A5", DisplayName = "Battery Full" },
        new() { Name = "phone", Glyph = "\uE0CD", DisplayName = "Phone" },
        new() { Name = "email", Glyph = "\uE159", DisplayName = "Email" },
        new() { Name = "person", Glyph = "\uF20B", DisplayName = "Person" },
        new() { Name = "group", Glyph = "\uEA21", DisplayName = "Group" },
        new() { Name = "public", Glyph = "\uE80B", DisplayName = "Public" },
        new() { Name = "map", Glyph = "\uE55B", DisplayName = "Map" },
        new() { Name = "pin_drop", Glyph = "\uE55E", DisplayName = "Pin Drop" },
        new() { Name = "photo_camera", Glyph = "\uE412", DisplayName = "Camera" },
        new() { Name = "palette", Glyph = "\uE40A", DisplayName = "Palette" },
        new() { Name = "code", Glyph = "\uE86F", DisplayName = "Code" },
        new() { Name = "terminal", Glyph = "\uEB8E", DisplayName = "Terminal" },
        new() { Name = "computer", Glyph = "\uE31E", DisplayName = "Computer" },
        new() { Name = "phone_android", Glyph = "\uE324", DisplayName = "Android Phone" },
        new() { Name = "tablet", Glyph = "\uE33F", DisplayName = "Tablet" },
        new() { Name = "watch", Glyph = "\uE334", DisplayName = "Watch" },
        new() { Name = "memory", Glyph = "\uE322", DisplayName = "Memory" },
        new() { Name = "gamepad", Glyph = "\uE30F", DisplayName = "Gamepad" },
        new() { Name = "keyboard", Glyph = "\uE312", DisplayName = "Keyboard" },
        new() { Name = "mouse", Glyph = "\uE323", DisplayName = "Mouse" },
        new() { Name = "folder", Glyph = "\uE2C7", DisplayName = "Folder" },
        new() { Name = "folder_open", Glyph = "\uE2C8", DisplayName = "Folder Open" },
        new() { Name = "description", Glyph = "\uE873", DisplayName = "Document" },
        new() { Name = "image", Glyph = "\uE3F4", DisplayName = "Image" },
        new() { Name = "movie", Glyph = "\uE404", DisplayName = "Movie" },
        new() { Name = "music_video", Glyph = "\uE063", DisplayName = "Music Video" },
        new() { Name = "smart_display", Glyph = "\uF06A", DisplayName = "Smart Display" },
        new() { Name = "light", Glyph = "\uE518", DisplayName = "Light" },
        new() { Name = "flash_on", Glyph = "\uE3E7", DisplayName = "Flash On" },
        new() { Name = "flash_off", Glyph = "\uE3E6", DisplayName = "Flash Off" },
        new() { Name = "screen_lock", Glyph = "\uE7C2", DisplayName = "Screen Lock" },
        new() { Name = "cast", Glyph = "\uE307", DisplayName = "Cast" },
        new() { Name = "download", Glyph = "\uF090", DisplayName = "Download" },
        new() { Name = "upload", Glyph = "\uF09B", DisplayName = "Upload" },
        new() { Name = "open_in_new", Glyph = "\uE89E", DisplayName = "Open In New" },
        new() { Name = "qr_code", Glyph = "\uEF6B", DisplayName = "QR Code" },
        new() { Name = "share", Glyph = "\uE80D", DisplayName = "Share" },
        new() { Name = "print", Glyph = "\uE8AD", DisplayName = "Print" },
    };

    private static void LoadAllIcons()
    {
        try
        {
            var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "MaterialSymbols.codepoints");
            if (!System.IO.File.Exists(path))
            {
                _allIcons = new List<MaterialIconInfo>(CommonIcons);
                return;
            }

            var icons = new List<MaterialIconInfo>();
            var lines = System.IO.File.ReadAllLines(path);
            foreach (var line in lines)
            {
                var parts = line.Trim().Split(' ');
                if (parts.Length < 2) continue;
                var name = parts[0];
                var codepoint = parts[1];
                try
                {
                    var codeVal = System.Convert.ToInt32(codepoint, 16);
                    var glyph = char.ConvertFromUtf32(codeVal);
                    var displayName = string.Join(" ", name.Split('_')
                        .Select(w => w.Length > 0 ? char.ToUpper(w[0]) + w.Substring(1) : w));
                    icons.Add(new MaterialIconInfo { Name = name, Glyph = glyph, DisplayName = displayName });
                }
                catch { }
            }

            _allIcons = icons;
        }
        catch
        {
            _allIcons = new List<MaterialIconInfo>(CommonIcons);
        }
    }
}
