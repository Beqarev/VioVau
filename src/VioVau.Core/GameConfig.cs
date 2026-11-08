using VioVau.Core.Cards;
using VioVau.Core.Dungeon;
using VioVau.Core.Enemies;
using VioVau.Core.Threat;

namespace VioVau.Core;

/// <summary>All tunable data for a run. Loaded by the host from game/data and sent to clients in <see cref="RunStarted"/>.</summary>
public sealed record GameConfig(DungeonConfig Dungeon, ThreatConfig Threat, CardsConfig? Cards = null, EnemiesConfig? Enemies = null)
{
    public static GameConfig Default { get; } = new(DungeonConfig.Default, ThreatConfig.Default);

    /// <summary>Cards, or an empty set if none were configured.</summary>
    public CardsConfig CardSet => Cards ?? CardsConfig.Default;

    /// <summary>Enemy and health rules, or none (no danger) if not configured.</summary>
    public EnemiesConfig EnemySet => Enemies ?? EnemiesConfig.Default;

    public void Validate()
    {
        Dungeon.Validate();
        Threat.Validate();
        CardSet.Validate();
        EnemySet.Validate();
    }
}
