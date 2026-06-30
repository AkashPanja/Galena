using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace GalenaActionRing;

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
    MediaSeekForward,
    MediaSeekBackward,
    TextExpansion,
    Folder,
    CloseOsd,
    ToggleNightLight,
    VolumeControl,
    BrightnessControl
}

public enum ActionCategory
{
    Individual,
    Group,
    Folder
}

public class RingNode : INotifyPropertyChanged
{
    private string _glyph = "\uE8B8";
    private string _label = "Action";
    private ActionType _actionType = ActionType.None;
    private string _actionData = "";
    private ActionCategory _category = ActionCategory.Individual;

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

    public ActionCategory Category
    {
        get => _category;
        set { _category = value; OnPropertyChanged(); }
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<RingNode>? Children { get; set; }

    public RingNode DeepCopy()
    {
        var copy = new RingNode
        {
            Glyph = _glyph,
            Label = _label,
            ActionType = _actionType,
            ActionData = _actionData,
            Category = _category,
            Children = Children?.ConvertAll(c => c.DeepCopy()),
        };
        return copy;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
