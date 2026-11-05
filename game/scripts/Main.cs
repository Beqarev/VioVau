using Godot;
using VioVau.Core;

namespace VioVau.Game;

/// <summary>
/// M0 smoke scene: proves the Godot project builds against VioVau.Core and can drive a RunState.
/// Replaced by the real boot flow (menu → hub) in M1.
/// </summary>
public partial class Main : Node2D
{
    private RunState _run = null!;
    private Label _status = null!;

    public override void _Ready()
    {
        _status = GetNode<Label>("Status");

        _run = new RunState(seed: 12345);
        _run.Apply(new JoinPlayer(new PlayerId(1), "Player 1"));
        var start = _run.Apply(new StartRun());

        GD.Print($"[VioVau] Core linked. StartRun accepted={start.Accepted}, players={_run.Players.Count}");
    }

    public override void _Process(double delta)
    {
        _run.Tick(TimeSpan.FromSeconds(delta));
        _status.Text =
            $"VIOLET VAULTS  —  M0\n\n" +
            $"Core linked: yes\n" +
            $"Phase: {_run.Phase}\n" +
            $"Players: {string.Join(", ", _run.Players.Select(p => p.DisplayName))}\n" +
            $"Run time: {_run.Elapsed:mm\\:ss}";
    }
}
