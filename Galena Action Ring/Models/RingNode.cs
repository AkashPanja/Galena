using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Galena_Action_Ring;

public enum ActionType
{
    None,
    LaunchApp,
    OpenUrl,
    VolumeUp,
    VolumeDown,
    MuteToggle,
    BrightnessUp,
    BrightnessDown,
    MediaPlayPause,
    MediaNext,
    MediaPrevious,
    TextExpansion,
    Folder,
    CloseOsd
}

public class RingNode
{
    public string Glyph { get; set; } = "\uE774";
    public string Label { get; set; } = "Action";
    public ActionType ActionType { get; set; } = ActionType.None;
    public string ActionData { get; set; } = "";

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RingNode>? Children { get; set; }
}
