using Godot;
using VioVau.Core;
using VioVau.Core.Cards;
using VioVau.Core.Dungeon;
using VioVau.Core.Enemies;
using VioVau.Game.Dev;
using VioVau.Game.Entities;
using VioVau.Game.Input;
using VioVau.Game.Networking;
using VioVau.Game.UI;
using VioVau.Game.World;

namespace VioVau.Game.Session;

/// <summary>Enemy nodes: spawning, client-side state sync, and the IEnemyWorld the host AI uses.</summary>
public partial class GameSession
{
    // ── Enemies ─────────────────────────────────────────────────────────────

    private void SpawnEnemy(EnemySpawned spawned)
    {
        if (_enemies.ContainsKey(spawned.Enemy)) return;
        var position = WorldLayout.LootPosition(spawned.Room, spawned.At);
        var target = spawned.Target is { } t ? _characters.GetValueOrDefault(t) : null;
        var node = new EnemyNode().Init(spawned.Enemy, spawned.Kind, position, _view.Config.EnemySet.Ai(spawned.Kind),
            _net.IsHost ? this : null, target);
        _enemyLayer.AddChild(node);
        _enemies[spawned.Enemy] = node;

        if (spawned.Kind == EnemyKind.Warden)
        {
            Audio.Instance.SetWardenPresent(true);
            _banner.Show("THE WARDEN AWAKENS", Palette.White, 3.5);
        }
        if (spawned.Target is { } strikeTarget)
            GD.Print($"[Game {_elapsed:0.0}s] {spawned.Kind} {spawned.Enemy} sent after {strikeTarget} in {spawned.Room}");
    }

    private void OnEnemyStates(int[] ids, Vector2[] positions, int[] states)
    {
        for (int i = 0; i < ids.Length && i < positions.Length && i < states.Length; i++)
            if (_enemies.TryGetValue(new EnemyId(ids[i]), out var enemy)) enemy.ApplyRemoteState(positions[i], states[i]);
    }

    // IEnemyWorld (host only)
    public DungeonMap Map => _view.Dungeon;
    public IEnumerable<PlayerCharacter> ActivePlayers => _characters.Values.Where(c => c.IsActive);
    public PlayerCharacter? WardenPrey => _net.State?.Loudest() is { } prey ? _characters.GetValueOrDefault(prey.Id) : null;

    public void ReportHit(EnemyNode enemy, PlayerCharacter target)
    {
        var at = target.AuthoritativePosition;
        _net.ApplyCommand(new HitPlayer(target.Id, enemy.Id, WorldLayout.ToRoomPoint(WorldLayout.RoomAt(0, at), at)));
    }
}
