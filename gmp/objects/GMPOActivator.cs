using Godot;
using PolyType;

/// <summary>What a save keeps of a <see cref="GMPOActivator"/>: that a toggle was left on (saved only then).</summary>
[GenerateShape]
public partial record struct ActivatorSave
{
    public bool active;
}

/// <summary>
/// Something the player interacts with (a button, a lever) to switch its <see cref="targets"/> (<see cref="Triggerable"/>).
/// Attach it to the static GMPBody the player aims at. Activations are decided by the object's authority (the host, for
/// anything in a level or a structure): it ignores interacts within <see cref="cooldown"/>, lets <see cref="TryAccept"/>
/// refuse (e.g. an unpaid price), and broadcasts the result, so every peer applies the same activations.
/// <para>
/// A <see cref="toggle"/> activator flips <see cref="active"/> on each activation and passes the new state on; a
/// momentary one pulses its targets on then off. Activators never stream state; everything goes through the RPCs here.
/// </para>
/// </summary>
public partial class GMPOActivator : GMPOBody, Interactable
{
    /// <summary>Stays on or off between activations (a lever) rather than pulsing (a push button).</summary>
    [Export] public bool toggle { get; set; }
    /// <summary>Seconds after an activation (accepted or refused) during which further interacts are ignored.</summary>
    [Export] public double cooldown { get; set; } = 0.5;
    /// <summary>Nodes switched by this activator; each must implement <see cref="Triggerable"/>.</summary>
    [Export] public Godot.Collections.Array<Node> targets { get; set; } = new();

    /// <summary>A toggle activator's current state (always false for a momentary one). Setting it refreshes <see cref="GMPOBody.hoverText"/>.</summary>
    public bool active
    {
        get => _active;
        set
        {
            _active = value;
            UpdateHoverText();
        }
    }
    bool _active;

    /// <summary>Authority only: when the last activation was decided (<see cref="Time.GetTicksMsec"/>).</summary>
    ulong lastActivationMs;

    public GMPOActivator()
    {
        priority = -1; // never sends state updates
    }

    public override void _Ready()
    {
        foreach (Node target in targets)
        {
            if (target is not Triggerable)
            {
                Logging.Warn($"{GetPath()}: target {target?.Name} isn't Triggerable and will be ignored", "Activator");
            }
        }
        UpdateHoverText();
    }

    // Deliberately don't call base: an activator never moves, so it stays static on every peer instead of following as Kinematic.
    public override void AfterInit() { }
    public override void OnAuthorityChanged() { }

    public override byte[] GenerateStateUpdate() => null;

    // Only this activator's own state: its targets save theirs, so nothing is re-triggered (that could spawn mid-restore).
    // Nor is OnActivated called: a load restores the state, it doesn't press anything. Subclasses that show their
    // state (a lever's handle) override LoadState to snap to it.
    public override byte[] SaveState()
    {
        if (toggle && active)
        {
            return GMPObject.serializer.Serialize(new ActivatorSave { active = true });
        }
        return null;
    }

    public override void LoadState(byte[] state)
    {
        active = GMPObject.serializer.Deserialize<ActivatorSave>(state).active;
    }

    /// <summary>Sets <see cref="GMPOBody.hoverText"/> for the current state.</summary>
    protected virtual void UpdateHoverText()
    {
        if (!toggle)
        {
            hoverText = "Press F to activate.";
        }
        else if (active)
        {
            hoverText = "Press F to turn off.";
        }
        else
        {
            hoverText = "Press F to turn on.";
        }
    }

    /// <summary>Authority only: decides whether an activation goes through, taking any payment. All are accepted by default.</summary>
    public virtual bool TryAccept(ulong presser) => true;

    /// <summary>Runs on every peer when the authority refuses an activation.</summary>
    public virtual void OnRejected(ulong presser) { }

    /// <summary>Runs on every peer when an activation takes effect, after the targets: animate the control here.</summary>
    public virtual void OnActivated() { }

    public void onInteract(ulong player)
    {
        RPCManager.RequestFromAuthority(this, nameof(_RequestActivation), []);
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
    private void _Reject(ulong presser)
    {
        OnRejected(presser);
    }

    // Carries the new state rather than "flip", so every peer ends up on the authority's state.
    [RPC(requireAuthority = true)]
    private void _Apply(bool on)
    {
        active = on;
        foreach (Node target in targets)
        {
            // Skip targets that are gone (e.g. a repaired broken structure), whose wrappers outlive the node.
            if (!IsInstanceValid(target) || target.IsQueuedForDeletion() || target is not Triggerable t) continue;
            t.OnTrigger(toggle ? on : true);
            if (!toggle) t.OnTrigger(false);
        }
        OnActivated();
    }
}
