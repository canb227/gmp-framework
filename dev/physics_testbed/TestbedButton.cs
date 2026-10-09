using Godot;

/// <summary>
/// Physics-testbed push button. Panel buttons open their <see cref="station"/>'s panel for the local player only;
/// master buttons ask <see cref="master"/> (the host) to act on every segment.
/// </summary>
public partial class TestbedButton : GMPOBox3DBody, Interactable
{
    public enum ButtonAction { ItemPanel, BeltPanel, AllSpawnersOn, AllSpawnersOff, DeleteAllScrap }

    [Export] public ButtonAction action;
    [Export] public PhysicsTuningStation station;
    [Export] public PhysicsTestbedMaster master;

    public TestbedButton()
    {
        priority = -1; // never sends state updates
    }

    public override void _Ready()
    {
        hoverName = action switch
        {
            ButtonAction.ItemPanel => $"{station?.title} item tuning",
            ButtonAction.BeltPanel => $"{station?.title} belt tuning",
            ButtonAction.AllSpawnersOn => "All spawners on",
            ButtonAction.AllSpawnersOff => "All spawners off",
            _ => "Delete all scrap",
        };
        hoverText = action is ButtonAction.ItemPanel or ButtonAction.BeltPanel
            ? "Press F to open the panel." : "Press F to activate.";
    }

    public override byte[] GenerateStateUpdate() => null;

    public void onInteract(ulong playerID)
    {
        switch (action)
        {
            case ButtonAction.ItemPanel: station?.TogglePanel(PhysicsTuningStation.Group.Items); break;
            case ButtonAction.BeltPanel: station?.TogglePanel(PhysicsTuningStation.Group.Belts); break;
            case ButtonAction.AllSpawnersOn: master?.RequestAllSpawners(true); break;
            case ButtonAction.AllSpawnersOff: master?.RequestAllSpawners(false); break;
            case ButtonAction.DeleteAllScrap: master?.RequestDeleteAll(); break;
        }
    }
}
