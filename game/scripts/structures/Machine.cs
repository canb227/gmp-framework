using Godot;
using PolyType;

[GenerateShape]
public partial record struct MachineSave
{
    public bool isOperating;
    public float processTimeRemaining;
    /// <summary>Whatever the machine itself adds (<see cref="Machine.SaveMachineState"/>), or null.</summary>
    public byte[] extra;
}

/// <summary>
/// A structure that works in cycles. Only its authority runs it: when idle it asks the machine to start a cycle
/// (<see cref="TryStartProcess"/>, which sets <see cref="processTimeRemaining"/>), and once that has counted down
/// it finishes it (<see cref="FinishProcess"/>). Every peer is told when a cycle starts and stops
/// (<see cref="isOperating"/>, <see cref="OnOperatingChanged"/>) for sounds and visuals.
/// Most machines take items in and put items out: see <see cref="RecipeMachine"/>.
/// </summary>
public abstract partial class Machine : Structure
{
    /// <summary>How fast cycles count down: 2 is twice as fast.</summary>
    [Export] public float processingSpeed = 1f;
    [Export] public AnimationPlayer animationPlayer;

    /// <summary>Every peer: whether a cycle is running.</summary>
    public bool isOperating { get; private set; }
    /// <summary>Authority only: seconds left in the current cycle at a processingSpeed of 1.</summary>
    public float processTimeRemaining = 0f;

    /// <summary>
    /// Authority only, while idle: start a cycle if there's something to do (take the inputs, set
    /// <see cref="processTimeRemaining"/> to the cycle's base time) and return true.
    /// </summary>
    protected abstract bool TryStartProcess();

    /// <summary>Authority only: the current cycle is done (spawn the outputs).</summary>
    protected abstract void FinishProcess();

    /// <summary>Every peer, when <see cref="isOperating"/> changes: sounds, animation, particles.</summary>
    protected virtual void OnOperatingChanged() { }

    public override void _PhysicsProcess(double delta)
    {
        if (!runsMachineLogic) return;

        if (isOperating)
        {
            processTimeRemaining -= (float)delta * processingSpeed;
            if (processTimeRemaining <= 0)
            {
                FinishProcess();
                RPCManager.RPC(this, nameof(SetOperating), [false]);
            }
        }
        else if (TryStartProcess())
        {
            RPCManager.RPC(this, nameof(SetOperating), [true]);
        }
    }

    [RPC(requireAuthority = true)]
    void SetOperating(bool operating)
    {
        isOperating = operating;
        OnOperatingChanged();
    }

    /// <summary>Extra state a machine saves on top of its cycle (e.g. the item it's holding), or null.</summary>
    protected virtual byte[] SaveMachineState() { return null; }

    /// <summary>Reads back what <see cref="SaveMachineState"/> returned (never called with null). Must be silent.</summary>
    protected virtual void LoadMachineState(byte[] state) { }

    public override byte[] SaveState()
    {
        return GMPObject.serializer.Serialize(new MachineSave
        {
            isOperating = isOperating,
            processTimeRemaining = processTimeRemaining,
            extra = SaveMachineState(),
        });
    }

    public override void LoadState(byte[] state)
    {
        MachineSave s = GMPObject.serializer.Deserialize<MachineSave>(state);
        isOperating = s.isOperating;
        processTimeRemaining = s.processTimeRemaining;
        if (s.extra != null) LoadMachineState(s.extra);
    }
}
