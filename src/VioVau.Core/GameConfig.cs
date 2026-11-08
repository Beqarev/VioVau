using VioVau.Core.Dungeon;
using VioVau.Core.Threat;

namespace VioVau.Core;

/// <summary>All tunable data for a run. Loaded by the host from game/data and sent to clients in <see cref="RunStarted"/>.</summary>
public sealed record GameConfig(DungeonConfig Dungeon, ThreatConfig Threat)
{
    public static GameConfig Default { get; } = new(DungeonConfig.Default, ThreatConfig.Default);

    public void Validate()
    {
        Dungeon.Validate();
        Threat.Validate();
    }
}
