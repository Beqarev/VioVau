namespace VioVau.Core.Enemies;

/// <summary>Core's record of an enemy: what it is and whether it lives. Where it is and what it does is the AI's business.</summary>
public sealed class EnemyState
{
    public EnemyId Id { get; }
    public EnemyKind Kind { get; }
    public bool Alive { get; internal set; } = true;

    /// <summary>The Warden cannot be killed.</summary>
    public bool Killable => Kind != EnemyKind.Warden;

    internal EnemyState(EnemyId id, EnemyKind kind)
    {
        Id = id;
        Kind = kind;
    }
}
