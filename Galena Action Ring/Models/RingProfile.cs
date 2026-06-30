using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GalenaActionRing;

public class RingProfile
{
    public string Name { get; set; } = "Default";
    public string? ProcessName { get; set; }
    public int Radius { get; set; } = 120;
    public List<RingNode> Nodes { get; set; } = new();
    public string PrimaryColor { get; set; } = "#FF000000";
    public string SecondaryColor { get; set; } = "#80FFFFFF";

    [JsonIgnore]
    public int NodeCount => Nodes.Count;

    public RingProfile DeepCopy()
    {
        return new RingProfile
        {
            Name = Name,
            ProcessName = ProcessName,
            Radius = Radius,
            Nodes = Nodes.ConvertAll(n => n.DeepCopy()),
            PrimaryColor = PrimaryColor,
            SecondaryColor = SecondaryColor,
        };
    }
}
