using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
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

public class RingNode : INotifyPropertyChanged
{
    private string _glyph = "\uE774";
    private string _label = "Action";
    private ActionType _actionType = ActionType.None;
    private string _actionData = "";

    public string Glyph
    {
        get => _glyph;
        set { _glyph = value; OnPropertyChanged(); }
    }

    public string Label
    {
        get => _label;
        set { _label = value; OnPropertyChanged(); }
    }

    public ActionType ActionType
    {
        get => _actionType;
        set { _actionType = value; OnPropertyChanged(); }
    }

    public string ActionData
    {
        get => _actionData;
        set { _actionData = value; OnPropertyChanged(); }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RingNode>? Children { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
