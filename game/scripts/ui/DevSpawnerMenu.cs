using System.Collections.Generic;
using Godot;
using ImGuiNET;
using Vec2 = System.Numerics.Vector2;

/// <summary>
/// The <see cref="DevSpawner"/> config menu: an ImGui window, opened as an invisible <see cref="UIScreen"/> so that
/// while it's up the mouse is free, the player stands still and Escape closes it. It edits a copy of the spawner's
/// settings; Apply sends them to every peer, Revert goes back to what the spawner runs with now.
/// </summary>
public partial class DevSpawnerMenu : UIScreen
{
    readonly DevSpawner spawner;
    DevSpawnerSettings settings;
    /// <summary>Whether <see cref="settings"/> has edits not yet applied.</summary>
    bool changed;
    List<(string category, string itemID)> entries;
    string filter = "";
    bool open = true;

    // Godot needs a parameterless constructor for every node script.
    public DevSpawnerMenu() { }

    public DevSpawnerMenu(DevSpawner spawner)
    {
        this.spawner = spawner;
    }

    public override void OnOpened()
    {
        // Covers nothing and takes no clicks: ImGui draws and handles the window itself.
        MouseFilter = MouseFilterEnum.Ignore;
        settings = spawner.CurrentSettings();
        entries = ItemsDebug.ScanDefinitions();
    }

    public override void _Process(double delta)
    {
        // The spawner was picked up or despawned while the menu was open.
        if (!IsInstanceValid(spawner) || !open)
        {
            UIManager.CloseScreen(this);
            return;
        }

        ImGui.SetNextWindowPos(new Vec2(460, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vec2(520, 640), ImGuiCond.FirstUseEver);
        ImGui.Begin($"Dev spawner {spawner.id}###devspawner{spawner.id}", ref open);

        RenderButtons();
        ImGui.Separator();
        RenderSpawning();
        ImGui.Separator();
        RenderLimits();
        ImGui.Separator();
        RenderItems();

        ImGui.End();
    }

    void RenderButtons()
    {
        if (ImGui.Button("Apply"))
        {
            spawner.SendSettings(settings);
            changed = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Revert"))
        {
            settings = spawner.CurrentSettings();
            changed = false;
        }
        ImGui.SameLine();
        if (ImGui.Button("Spawn burst now"))
        {
            spawner.SpawnNow();
        }
        ImGui.SameLine();
        if (ImGui.Button("Close"))
        {
            open = false;
        }
        if (changed)
        {
            ImGui.TextColored(new System.Numerics.Vector4(1f, 0.7f, 0.2f, 1f), "Unapplied changes");
        }
        else
        {
            ImGui.TextDisabled("Running these settings");
        }
        ImGui.TextDisabled($"Authority: {spawner.authority} | items in world: {ItemWorld.totalCount}");
    }

    void RenderSpawning()
    {
        changed |= ImGui.Checkbox("Spawning", ref settings.spawning);
        ImGui.SetNextItemWidth(160);
        changed |= ImGui.InputDouble("Interval (s)", ref settings.spawnInterval, 0.1, 1.0, "%.2f");
        ImGui.SetNextItemWidth(160);
        changed |= ImGui.SliderInt("Items per spawn", ref settings.itemsPerSpawn, 1, 50);
        if (settings.spawnInterval > 0)
        {
            ImGui.TextDisabled($"= {settings.itemsPerSpawn / settings.spawnInterval:0.##} items/s");
        }
        ImGui.SetNextItemWidth(160);
        changed |= ImGui.SliderFloat("Launch speed (m/s)", ref settings.launchSpeed, 0f, 50f);
        ImGui.SetNextItemWidth(160);
        changed |= ImGui.SliderFloat("Launch spread (deg)", ref settings.launchSpreadDegrees, 0f, 180f);
    }

    void RenderLimits()
    {
        ImGui.TextDisabled("Limits (0: none). It holds while one is reached.");
        ImGui.SetNextItemWidth(160);
        changed |= ImGui.InputInt("Max of the item", ref settings.maxOfItem, 10);
        ImGui.SetNextItemWidth(160);
        changed |= ImGui.InputInt("Max items in world", ref settings.maxTotalItems, 100);
        ImGui.SetNextItemWidth(160);
        changed |= ImGui.InputInt("Max nearby", ref settings.maxNearby, 10);
        ImGui.SetNextItemWidth(160);
        changed |= ImGui.SliderFloat("Nearby radius (m)", ref settings.nearbyRadius, 0.5f, 20f);
    }

    /// <summary>The items it spawns (with their weights) first, then every other definition matching the filter.</summary>
    void RenderItems()
    {
        ImGui.Text($"Items ({settings.itemWeights.Count} chosen)");
        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##filter", "filter (id / name)", ref filter, 64);

        ImGui.BeginChild("##items", Vec2.Zero, ImGuiChildFlags.Borders);

        // A copy of the keys, as unticking one removes it from the dictionary.
        foreach (string itemID in new List<string>(settings.itemWeights.Keys))
        {
            bool chosen = true;
            if (ImGui.Checkbox($"##pick{itemID}", ref chosen))
            {
                settings.itemWeights.Remove(itemID);
                changed = true;
                continue;
            }
            ImGui.SameLine();
            float weight = settings.itemWeights[itemID];
            ImGui.SetNextItemWidth(80);
            if (ImGui.DragFloat($"##weight{itemID}", ref weight, 0.05f, 0f, 100f, "%.2f"))
            {
                settings.itemWeights[itemID] = weight;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.Text(itemID);
        }
        if (settings.itemWeights.Count > 0)
        {
            ImGui.Separator();
        }

        string lowerFilter = filter.ToLowerInvariant();
        foreach (var (category, itemID) in entries)
        {
            if (settings.itemWeights.ContainsKey(itemID))
            {
                continue;
            }
            if (lowerFilter.Length > 0 && !itemID.ToLowerInvariant().Contains(lowerFilter))
            {
                continue;
            }
            bool chosen = false;
            if (ImGui.Checkbox($"{itemID}##pick{itemID}", ref chosen))
            {
                settings.itemWeights[itemID] = 1f;
                changed = true;
            }
            ImGui.SameLine();
            ImGui.TextDisabled(category);
        }

        ImGui.EndChild();
    }
}
