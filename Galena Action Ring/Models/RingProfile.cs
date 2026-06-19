using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Galena_Action_Ring;

public class RingProfile
{
    public string Name { get; set; } = "Default";
    public int Radius { get; set; } = 120;
    public List<RingNode> Nodes { get; set; } = new();

    [JsonIgnore]
    public int NodeCount => Nodes.Count;
}
