using Godot;

/// <summary>
/// A pressable level prop: each accepted press spawns one item from each target spawner
/// (<see cref="ObjectSpawner.SpawnOnce"/>, which only the host carries out). Presses are decided by the lobby host
/// (level props aren't GMPObjects): simultaneous presses within <see cref="pressCooldown"/> count once,
/// and every peer applies the same accepted presses.
/// </summary>
public partial class BasicButton : Node3D, Interactable
{
    [Export] public string displayName;
    [Export] public AnimationPlayer animator;
    [Export] public Godot.Collections.Array<ObjectSpawner> targetObjectSpawner = new();
    /// <summary>Seconds after an accepted press during which further presses are ignored.</summary>
    [Export] public double pressCooldown = 0.5;
    /// <summary>Number of presses that took effect (used by the headless multiplayer test).</summary>
    public int acceptedPresses;
    private ulong lastAcceptedPressMs; // host only

    public void onInteract(ulong playerID) => OnPressed();

    /// <summary>Asks the host to accept a press.</summary>
    public void OnPressed()
    {
        RPCManager.RPCTo(Lobby.hostID, this, nameof(_RequestPress), []);
    }

    // Runs on the host. First press wins; presses inside the cooldown are dropped.
    [RPC]
    private void _RequestPress()
    {
        ulong now = Time.GetTicksMsec();
        if (lastAcceptedPressMs != 0 && now - lastAcceptedPressMs < pressCooldown * 1000)
        {
            return;
        }
        lastAcceptedPressMs = now;
        RPCManager.RPC(this, nameof(_ApplyPress), [RPCManager.sender]);
    }

    [RPC(requireAuthority = true)]
    private void _ApplyPress(ulong presser)
    {
        acceptedPresses++;
        animator.Play("button_press");
        foreach (ObjectSpawner spawner in targetObjectSpawner)
        {
            spawner.SpawnOnce();
        }
    }
}
