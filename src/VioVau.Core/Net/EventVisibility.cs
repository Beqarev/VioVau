namespace VioVau.Core.Net;

/// <summary>Who may see an event: public events go to everyone, private ones only to the machine that owns the player.</summary>
public static class EventVisibility
{
    public static bool IsVisibleTo(IGameEvent e, Func<PlayerId, bool> ownsPlayer) =>
        e is not IPrivateEvent p || ownsPlayer(p.Owner);

    /// <summary>The events a particular machine may receive, in order.</summary>
    public static IReadOnlyList<IGameEvent> For(IReadOnlyList<IGameEvent> events, Func<PlayerId, bool> ownsPlayer) =>
        events.Where(e => IsVisibleTo(e, ownsPlayer)).ToList();
}
