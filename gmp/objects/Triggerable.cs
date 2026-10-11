/// <summary>
/// Something an <see cref="Activator"/> (button, lever...) switches. Called on every peer, in the same order, once the
/// host accepts an activation, so visual changes need no syncing of their own; anything that creates or destroys
/// objects must only do so on the host (<see cref="Lobby.isHost"/>).
/// <para>
/// A toggle activator passes its new state. A momentary one (a push button) pulses: <c>true</c>, then straight away
/// <c>false</c>.
/// </para>
/// </summary>
public interface Triggerable
{
    void OnTrigger(bool active);
}
