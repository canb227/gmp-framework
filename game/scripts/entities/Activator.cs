using Godot;

/// <summary>
/// A GMPObject the player interacts with (buttons, levers) to switch its <see cref="targets"/>
/// (<see cref="Triggerable"/>). Activations are decided by the object's authority (the host, for anything in a level
/// or a structure): it ignores interacts within <see cref="cooldown"/>, lets <see cref="TryAccept"/> refuse (e.g. an
/// unpaid price), and broadcasts the result, so every peer applies the same activations.
/// <para>
/// A <see cref="toggle"/> activator flips <see cref="active"/> on each activation and passes the new state on; a
/// momentary one pulses its targets on then off.
/// </para>
/// <para>
/// The logic lives here as default methods (RPCManager finds [RPC] methods on interfaces too); implementers only
/// supply the state below. <see cref="GMPOActivator"/> does that for plain node activators. To override a hook in a
/// class hierarchy, declare it virtual in the class that implements this interface.
/// </para>
/// </summary>
public interface Activator : GMPObject, Interactable
{
    /// <summary>Stays on or off between activations (a lever) rather than pulsing (a push button).</summary>
    bool toggle { get; }
    /// <summary>Seconds after an activation (accepted or refused) during which further interacts are ignored.</summary>
    double cooldown { get; }
    /// <summary>Nodes switched by this activator; each must implement <see cref="Triggerable"/>.</summary>
    Godot.Collections.Array<Node> targets { get; }

    /// <summary>A toggle activator's current state (always false for a momentary one).</summary>
    bool active { get; set; }
    /// <summary>Authority only: when the last activation was decided.</summary>
    ulong lastActivationMs { get; set; }
    /// <summary>Number of activations that took effect (used by the headless multiplayer test).</summary>
    int acceptedActivations { get; set; }

    /// <summary>Authority only: decides whether an activation goes through, taking any payment. All are accepted by default.</summary>
    bool TryAccept(ulong presser) => true;

    /// <summary>Runs on every peer when the authority refuses an activation.</summary>
    void OnRejected(ulong presser) { }

    /// <summary>Runs on every peer when an activation takes effect, after the targets: animate the control here.</summary>
    void OnActivated() { }

    void Interactable.onInteract(ulong player) => RPCManager.RequestFromAuthority(this, nameof(_RequestActivation), []);

    /// <summary>Authority only: puts a toggle activator in <paramref name="on"/> for everyone (no-op if it's already there).</summary>
    void SetFromAuthority(bool on)
    {
        if (authority == Lobby.selfPeerID && toggle && on != active)
        {
            RPCManager.RPC(id, nameof(_Apply), [on]);
        }
    }

    // Runs on the authority. First interact wins; interacts inside the cooldown are dropped.
    [RPC]
    private void _RequestActivation()
    {
        ulong now = Time.GetTicksMsec();
        if (lastActivationMs != 0 && now - lastActivationMs < cooldown * 1000)
        {
            return;
        }
        lastActivationMs = now;
        if (!TryAccept(RPCManager.sender))
        {
            RPCManager.RPC(id, nameof(_Reject), [RPCManager.sender]);
            return;
        }
        RPCManager.RPC(id, nameof(_Apply), [toggle && !active]);
    }

    [RPC(requireAuthority = true)]
    private void _Reject(ulong presser) => OnRejected(presser);

    // Carries the new state rather than "flip", so every peer ends up on the authority's state.
    [RPC(requireAuthority = true)]
    private void _Apply(bool on)
    {
        acceptedActivations++;
        active = on;
        foreach (Node target in targets)
        {
            if (target is not Triggerable t) continue;
            t.OnTrigger(toggle ? on : true);
            if (!toggle) t.OnTrigger(false);
        }
        OnActivated();
    }
}
