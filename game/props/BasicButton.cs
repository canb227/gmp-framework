using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A pressable level prop that toggles its target spawners. Presses are decided by the lobby host
/// (level props aren't GMPObjects): simultaneous presses within <see cref="pressCooldown"/> count once,
/// and every peer applies the same accepted presses.
/// </summary>
public partial class BasicButton : Node3D
{
    [Export] public string displayName;
    [Export] public AnimationPlayer animator;
    [Export] public Godot.Collections.Array<ObjectSpawner> targetObjectSpawner;
    /// <summary>Seconds after an accepted press during which further presses are ignored.</summary>
    [Export] public double pressCooldown = 0.5;
    private List<ObjectSpawner> targetSpawnerList;
    /// <summary>Number of presses that took effect (used by the headless multiplayer test).</summary>
    public int acceptedPresses;
    private ulong lastAcceptedPressMs; // host only
    
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        targetSpawnerList = targetObjectSpawner.ToList();
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }

    /// <summary>Called by the pressing player; asks the host to accept the press.</summary>
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
        foreach (ObjectSpawner spawner in targetSpawnerList)
        {
            spawner.spawning = !spawner.spawning;
        }
    }
}
