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

/// <summary>Building the world (rooms, loot, characters, lights) and the HUD from the RunView.</summary>
public partial class GameSession
{
    // ── Setup ───────────────────────────────────────────────────────────────

    private void BuildWorld()
    {
        _world = new Node2D { Name = "World" };
        AddChild(_world);

        // Darkness: the vault is lit only by lanterns, crystals and treasure; it gets darker as the Threat rises.
        _ambient = new CanvasModulate { Color = Lights.Ambient(0, _view.Config.Threat.TierThresholds.Count) };
        _world.AddChild(_ambient);

        foreach (var room in _view.Dungeon.Floors[0].Rooms)
        {
            var node = new RoomNode().Init(room, _view.IsRevealed(room.Id));
            _world.AddChild(node);
            _rooms[room.Id] = node;
        }

        var loot = new Node2D { Name = "Loot" };
        _lootLayer = loot;
        _world.AddChild(loot);
        foreach (var item in _view.Dungeon.Loot)
        {
            var card = item.Card is { } id ? _view.Config.CardSet.Get(id) : null;
            var node = new LootNode().Init(item, fancy: _view.Dungeon.GetRoom(item.Room).Type == RoomType.Vault, card);
            loot.AddChild(node);
            _loot[item.Id] = node;
        }

        _enemyLayer = new Node2D { Name = "Enemies" };
        _world.AddChild(_enemyLayer);

        var players = new Node2D { Name = "Players" };
        _world.AddChild(players);

        var spawnCenter = WorldLayout.RoomCenter(_view.Dungeon.Entrance);
        for (int i = 0; i < _view.Players.Count; i++)
        {
            var info = _view.Players[i];
            var entry = _net.Roster.FirstOrDefault(r => r.PlayerId == info.Id.Value);
            var device = entry is not null && entry.PeerId == _net.LocalPeerId ? LocalPlayers.Get(entry.LocalSlot) : null;

            PlayerCharacter character = null!;
            if (DevOptions.Autopilot && device is not null && (entry!.LocalSlot == 0 || DevOptions.AutopilotAll))
                device = new AutopilotDevice(_view, info.Id, () => character.GlobalPosition,
                    id => _characters.TryGetValue(id, out var c) && c.IsInside ? c.GlobalPosition : null,
                    at => _enemies.Values.Select(e => (Vector2?)e.GlobalPosition).OrderBy(p => p!.Value.DistanceTo(at)).FirstOrDefault());

            character = new PlayerCharacter().Init(info.Id, info.DisplayName, i, device, spawnCenter + SpawnOffsets[i % SpawnOffsets.Length]);
            character.InteractPressed += OnLocalInteractPressed;
            character.CardPressed += (c, slot) => _net.RequestPlayCard(c.Id, slot);
            players.AddChild(character);
            _characters[info.Id] = character;
        }

        // Enemies spawned at run start arrived before this scene existed; the view remembers them.
        foreach (var spawned in _view.Enemies.Values) SpawnEnemy(spawned);
    }

    private void BuildUi()
    {
        var screensLayer = new CanvasLayer { Name = "Screens", Layer = 1 };
        AddChild(screensLayer);
        _screens = new SplitScreen().Init(GetViewport().World2D);
        screensLayer.AddChild(_screens);

        var hud = new CanvasLayer { Name = "Hud", Layer = 2 };
        AddChild(hud);
        hud.AddChild(new Minimap().Init(_view, MinimapMarkers));
        hud.AddChild(new ThreatHud().Init(_view));
        _banner = new TierBanner();
        hud.AddChild(_banner);
        _hands = new HandHud().Init(_view, LocalHands);
        hud.AddChild(_hands);
        _collapseHud = new CollapseHud().Init(_view);
        hud.AddChild(_collapseHud);
        _scoreboard = new Scoreboard().Init(_view, _net.IsHost);
        hud.AddChild(_scoreboard);

        _info = new Label { Position = new Vector2(196, 1) }; // top centre; the bottom-left belongs to the hand
        _info.AddThemeFontOverride("font", UiFonts.Regular);
        _info.AddThemeFontSizeOverride("font_size", 8);
        _info.AddThemeColorOverride("font_color", Palette.Dim);
        hud.AddChild(_info);

        _pause = new PauseMenu();
        AddChild(_pause);

        bool warden = _view.Enemies.Values.Any(e => e.Kind == EnemyKind.Warden);
        Audio.Instance.PlayExplore(_view.ThreatTier, warden);
        if (_view.CollapseSeconds is not null) Audio.Instance.PlayCollapse();
    }

    private IEnumerable<Minimap.Marker> MinimapMarkers() =>
        _characters.Values
            .Where(c => c.IsInside)
            .Select(c => new Minimap.Marker(WorldLayout.RoomAt(0, c.GlobalPosition), c.ColorIndex))
            .Where(m => _view.Dungeon.Contains(m.Room));
}
