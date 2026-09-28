using Godot;
using Limbo.Console.Sharp;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Console command <c>blueprints &lt;set&gt;</c>: gives the local player a stack of every blueprint in one of
/// <see cref="BlueprintSets.Sets"/> (conveyors, advanced, magnetic, chutes, chutes_advanced, launchers, sorting,
/// fields, processing). There are more structure blueprints than inventory slots, so they are handed out a set
/// at a time instead of all at the start. Added once per session by <see cref="GameBootstrap.Init"/>.
/// </summary>
public partial class BlueprintCommands : Node
{
    const int StackCount = 5;

    public static void Ensure()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root.HasNode(nameof(BlueprintCommands))) return;
        tree.Root.CallDeferred(Node.MethodName.AddChild, new BlueprintCommands { Name = nameof(BlueprintCommands) });
    }

    public override void _Ready()
    {
        LimboConsole.RegisterCommand(new Callable(this, "blueprints"));
        LimboConsole.AddArgumentAutocompleteSource("blueprints", 0, new Callable(this, "GetBlueprintSets"));
    }

    public void blueprints(string set)
    {
        if (!BlueprintSets.Sets.TryGetValue(set ?? "", out string[] items))
        {
            LimboConsole.Error("Name a blueprint set: " + string.Join(", ", BlueprintSets.Sets.Keys));
            return;
        }
        FactoryPlayer local = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
        if (local == null)
        {
            LimboConsole.Error("No local player yet: start a game first.");
            return;
        }
        List<string> full = new();
        foreach (string item in items)
        {
            if (local.inventory.AddItem(item, StackCount) > 0) full.Add(item);
        }
        local.UpdateEquippedItem();
        if (full.Count > 0) LimboConsole.Warn("Inventory full, not given: " + string.Join(", ", full));
        else LimboConsole.Info($"Gave {items.Length} blueprints ({set}).");
    }

    public Godot.Collections.Array GetBlueprintSets()
    {
        Godot.Collections.Array sets = new();
        foreach (string key in BlueprintSets.Sets.Keys) sets.Add(key);
        return sets;
    }
}
