using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TouchControl.Bridge;

/// <summary>
/// Wire format shared with the Android companion app: one JSON object per line, UTF-8, over TCP.
/// "t" selects the message type. Field names are short because the state message goes out ten times a second.
/// </summary>
public static class Protocol
{
    public const int Version = 1;
}

/// <summary>Incoming message. All optional fields are read after dispatching on <see cref="T"/>.</summary>
public sealed class ClientMessage
{
    [JsonPropertyName("t")] public string T { get; set; } = string.Empty;

    // hello
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("ver")] public int Ver { get; set; }

    // move: stick vector, x right, y down (screen space), |v| <= 1.  cam: pixel deltas.
    [JsonPropertyName("x")] public float X { get; set; }
    [JsonPropertyName("y")] public float Y { get; set; }
    [JsonPropertyName("dx")] public float Dx { get; set; }
    [JsonPropertyName("dy")] public float Dy { get; set; }

    // slot: hotbar h (0-based) / slot s (0-based)
    [JsonPropertyName("h")] public int H { get; set; }
    [JsonPropertyName("s")] public int S { get; set; }

    // key: virtual-key code + down/up
    [JsonPropertyName("vk")] public int Vk { get; set; }
    [JsonPropertyName("down")] public bool Down { get; set; }

    // ga / mc / icon: row or icon id
    [JsonPropertyName("id")] public uint Id { get; set; }

    // sub: hotbars and general-action ids to include in state messages
    [JsonPropertyName("bars")] public List<int>? Bars { get; set; }
    [JsonPropertyName("ga")] public List<uint>? Ga { get; set; }
}

public sealed class HelloReply
{
    [JsonPropertyName("t")] public string T { get; } = "hello";
    [JsonPropertyName("plugin")] public string Plugin { get; } = "TouchControl";
    [JsonPropertyName("ver")] public int Ver { get; } = Protocol.Version;
}

public sealed class CatalogEntry
{
    [JsonPropertyName("id")] public uint Id { get; set; }
    [JsonPropertyName("n")] public string N { get; set; } = string.Empty;
    [JsonPropertyName("i")] public uint I { get; set; }
}

/// <summary>Names and icon ids for General Actions and Main Commands, so the app can offer them in its button editor.</summary>
public sealed class CatalogMessage
{
    [JsonPropertyName("t")] public string T { get; } = "catalog";
    [JsonPropertyName("ga")] public List<CatalogEntry> Ga { get; set; } = [];
    [JsonPropertyName("mc")] public List<CatalogEntry> Mc { get; set; } = [];
}

public sealed class SlotState
{
    [JsonPropertyName("i")] public uint I { get; set; }        // icon id, 0 when empty
    [JsonPropertyName("e")] public bool E { get; set; }        // empty
    [JsonPropertyName("cd")] public float Cd { get; set; }     // remaining cooldown seconds
    [JsonPropertyName("tot")] public float Tot { get; set; }   // total cooldown seconds
    [JsonPropertyName("k")] public string K { get; set; } = string.Empty; // keybind hint
}

public sealed class BarState
{
    [JsonPropertyName("h")] public int H { get; set; }
    [JsonPropertyName("slots")] public List<SlotState> Slots { get; set; } = [];
}

public sealed class StateMessage
{
    [JsonPropertyName("t")] public string T { get; } = "state";
    [JsonPropertyName("in")] public bool In { get; set; }            // logged in
    [JsonPropertyName("mounted")] public bool Mounted { get; set; }
    [JsonPropertyName("combat")] public bool Combat { get; set; }
    [JsonPropertyName("bars")] public List<BarState> Bars { get; set; } = [];
    [JsonPropertyName("ga")] public List<SlotState> Ga { get; set; } = [];   // cooldowns for general actions the app asked about
    [JsonPropertyName("gaIds")] public List<uint> GaIds { get; set; } = [];
}

public sealed class IconMessage
{
    [JsonPropertyName("t")] public string T { get; } = "icon";
    [JsonPropertyName("id")] public uint Id { get; set; }
    [JsonPropertyName("png")] public string? Png { get; set; }   // base64, null when the icon could not be rendered
}
