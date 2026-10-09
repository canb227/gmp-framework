using Godot;

/// <summary>
/// Base for node <see cref="Activator"/>s (buttons, levers): declares the activator's exported settings and state,
/// and makes the hooks virtual so subclasses can override them. Attach it to the static Box3DBody the player aims at.
/// Activators never stream state; everything goes through the Activator RPCs.
/// </summary>
public partial class GMPOActivator : GMPOBox3DBody, Activator
{
    /// <summary>Stays on or off between activations (a lever) rather than pulsing (a push button).</summary>
    [Export] public bool toggle { get; set; }
    /// <summary>Seconds after an activation (accepted or refused) during which further interacts are ignored.</summary>
    [Export] public double cooldown { get; set; } = 0.5;
    /// <summary>Nodes switched by this activator; each must implement <see cref="Triggerable"/>.</summary>
    [Export] public Godot.Collections.Array<Node> targets { get; set; } = new();

    /// <summary>Setting it refreshes <see cref="GMPOBox3DBody.hoverText"/>.</summary>
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
    public ulong lastActivationMs { get; set; }
    public int acceptedActivations { get; set; }

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

    /// <summary>Sets <see cref="GMPOBox3DBody.hoverText"/> for the current state.</summary>
    protected virtual void UpdateHoverText()
    {
        hoverText = toggle ? (active ? "Press F to turn off." : "Press F to turn on.") : "Press F to activate.";
    }

    // Same defaults as Activator's, redeclared as virtual so subclasses can override them.
    public virtual bool TryAccept(ulong presser) => true;
    public virtual void OnRejected(ulong presser) { }
    public virtual void OnActivated() { }
}
