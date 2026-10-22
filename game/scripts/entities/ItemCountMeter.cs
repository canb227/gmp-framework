using Godot;
using PolyType;
using System;

/// <summary>What an <see cref="ItemCountMeter"/> streams: the host's count.</summary>
[GenerateShape]
public partial record struct ItemCountMeterState
{
    public int count;
}

/// <summary>What a save keeps of an <see cref="ItemCountMeter"/>: that it was over max (saved only then).</summary>
[GenerateShape]
public partial record struct ItemCountMeterSave
{
    public bool over;
}

/// <summary>
/// A level fixture showing how many loose items in the world have <see cref="tag"/> (<see cref="ItemWorld.CountWithTag"/>),
/// as a bar filling up to <see cref="max"/>. When the count goes over max it switches its <see cref="targets"/>
/// (<see cref="Triggerable"/>) on, and off again once the count comes back down, the same on every peer.
/// <para>
/// The host counts (the counts only exist there) and streams the count, so its authority must be the host: place it in a
/// level, which the host spawns. Crossings are decided by the host and broadcast, like an activator's activations.
/// </para>
/// <para>
/// Scene: the root is a static <c>GMPBody</c>; <see cref="barPivot"/> sits at the bottom of the bar and is scaled on Y
/// to the fill, and the bar's mesh has a local-to-scene material whose colour shows over max.
/// </para>
/// </summary>
public partial class ItemCountMeter : GMPOBody
{
    /// <summary>The tag counted (one tag).</summary>
    [Export] public SurfaceTags tag { get; set; } = SurfaceTags.METAL;
    /// <summary>The count the bar is full at; going over it fires the targets.</summary>
    [Export] public int max { get; set; } = 50;
    /// <summary>Nodes switched on while over max and off when back under; each must implement <see cref="Triggerable"/>.</summary>
    [Export] public Godot.Collections.Array<Node> targets { get; set; } = new();

    [Export] public Node3D barPivot;
    [Export] public MeshInstance3D bar;
    [Export] public Label3D countLabel;
    [Export] public Label3D tagLabel;
    [Export] public Color barColor = new Color(0.2f, 0.8f, 0.35f);
    [Export] public Color overColor = new Color(0.95f, 0.15f, 0.1f);

    /// <summary>On every peer when the meter goes over max (true) or comes back to max or under (false), after the targets.</summary>
    public event Action<bool> OverMaxChanged;

    /// <summary>The count shown: the host's own, or the latest it streamed.</summary>
    public int count { get; private set; }
    /// <summary>Whether it is over max, as the host last decided (every peer).</summary>
    public bool over { get; private set; }

    int shownCount = -1;
    bool shownOver;
    StandardMaterial3D barMaterial;

    public ItemCountMeter()
    {
        priority = 1;
        hoverName = "Item Meter";
    }

    public override void _Ready()
    {
        foreach (Node target in targets)
        {
            if (target is not Triggerable)
            {
                Logging.Warn($"{GetPath()}: target {target?.Name} isn't Triggerable and will be ignored", "ItemCountMeter");
            }
        }
        if (tagLabel != null)
        {
            tagLabel.Text = tag.ToString();
        }
        if (bar != null)
        {
            // Its own, so each meter colours its bar alone.
            barMaterial = new StandardMaterial3D();
            barMaterial.EmissionEnabled = true;
            barMaterial.EmissionEnergyMultiplier = 0.4f;
            bar.MaterialOverride = barMaterial;
        }
        UpdateDisplay();
    }

    // It never moves: stay static on every peer instead of following as Kinematic.
    public override void AfterInit() { }
    public override void OnAuthorityChanged() { }

    public override void _PhysicsProcess(double delta)
    {
        if (id == 0 || !Lobby.isHost)
        {
            return;
        }
        count = ItemWorld.CountWithTag(tag);
        bool nowOver = IsOver(count);
        if (nowOver != over)
        {
            RPCManager.RPC(id, nameof(_SetOver), [nowOver]);
        }
        UpdateDisplay();
    }

    /// <summary>Host only: whether <paramref name="newCount"/> puts the meter over max, given whether it is over now (<see cref="over"/>).</summary>
    bool IsOver(int newCount)
    {
        // TODO(human)
        return false;
    }

    [RPC(requireAuthority = true)]
    private void _SetOver(bool on)
    {
        over = on;
        foreach (Node target in targets)
        {
            if (!IsInstanceValid(target) || target.IsQueuedForDeletion() || target is not Triggerable t) continue;
            t.OnTrigger(on);
        }
        OverMaxChanged?.Invoke(on);
        UpdateDisplay();
    }

    public override byte[] GenerateStateUpdate()
    {
        return GMPObject.serializer.Serialize(new ItemCountMeterState { count = count });
    }

    public override void ApplyStateUpdate(byte[] update)
    {
        count = GMPObject.serializer.Deserialize<ItemCountMeterState>(update).count;
        UpdateDisplay();
    }

    // Only its own state: its targets save theirs, so nothing is re-triggered by a load.
    public override byte[] SaveState()
    {
        if (over)
        {
            return GMPObject.serializer.Serialize(new ItemCountMeterSave { over = true });
        }
        return null;
    }

    public override void LoadState(byte[] state)
    {
        over = GMPObject.serializer.Deserialize<ItemCountMeterSave>(state).over;
        UpdateDisplay();
    }

    /// <summary>Puts the count on the bar and the label; does nothing when neither has changed.</summary>
    void UpdateDisplay()
    {
        if (count == shownCount && over == shownOver)
        {
            return;
        }
        shownCount = count;
        shownOver = over;

        float fill = 1f;
        if (max > 0)
        {
            fill = Mathf.Clamp((float)count / max, 0f, 1f);
        }
        if (barPivot != null)
        {
            // A zero scale would make a degenerate basis: hide the bar instead.
            barPivot.Visible = fill > 0f;
            barPivot.Scale = new Vector3(1, Mathf.Max(fill, 0.001f), 1);
        }
        if (barMaterial != null)
        {
            if (over)
            {
                barMaterial.AlbedoColor = overColor;
                barMaterial.Emission = overColor;
            }
            else
            {
                barMaterial.AlbedoColor = barColor;
                barMaterial.Emission = barColor;
            }
        }
        if (countLabel != null)
        {
            countLabel.Text = $"{count} / {max}";
        }
    }
}
