using Godot;
using ImGuiGodot;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using Vec2 = System.Numerics.Vector2;

/// <summary>
/// "debugui items" window: browse every item definition under res://game/definitions (grouped by folder,
/// filterable), with an icon + spinning model preview, the definition's fields, and buttons to give it to the
/// local player's inventory or spawn it in front of the camera. Driven from Console._Process.
/// </summary>
public static class ItemsDebug
{
    public static bool displayItemsDebugInfo = false;

    const string DefinitionsRoot = "res://game/definitions";

    // (category folder, itemID), sorted; built on first open and on Refresh.
    private static List<(string category, string itemID)> entries;
    private static string filter = "";
    private static string selectedID;
    private static int count = 1;

    // Model preview: an own-world SubViewport holding mesh-only copies of the item's scene, spun on a pivot.
    private const int previewSize = 220;
    private static SubViewport preview;
    private static Node3D previewPivot;
    private static Camera3D previewCamera;
    private static string previewID;

    public static void Process(Node owner, double delta)
    {
        if (preview != null)
        {
            preview.RenderTargetUpdateMode = displayItemsDebugInfo ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        }
        if (!displayItemsDebugInfo)
        {
            return;
        }
        entries ??= ScanDefinitions();
        EnsurePreview(owner);
        previewPivot.RotateY((float)delta * 0.8f);
        Render(owner);
    }

    // ---- definitions -----------------------------------------------------------

    private static List<(string, string)> ScanDefinitions()
    {
        List<(string, string)> found = new();
        ScanDir(DefinitionsRoot, found);
        // Only keep files that really are ItemInfo definitions. Fetch also caches them strongly (see ItemInfo).
        return found.Where(e => ItemInfo.Fetch(e.Item2) != null).OrderBy(e => e.Item1).ThenBy(e => e.Item2).ToList();
    }

    private static void ScanDir(string path, List<(string, string)> found)
    {
        using DirAccess dir = DirAccess.Open(path);
        if (dir == null)
        {
            return;
        }
        string category = path == DefinitionsRoot ? "(root)" : path.Substring(DefinitionsRoot.Length + 1);
        foreach (string sub in dir.GetDirectories())
        {
            ScanDir($"{path}/{sub}", found);
        }
        foreach (string file in dir.GetFiles())
        {
            // Exported builds list "x.tres.remap" instead of "x.tres".
            string name = file.TrimSuffix(".remap");
            if (name.EndsWith(".tres"))
            {
                found.Add((category, name.TrimSuffix(".tres")));
            }
        }
    }

    // ---- window ----------------------------------------------------------------

    private static void Render(Node owner)
    {
        ImGui.SetNextWindowPos(new Vec2(460, 10), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vec2(720, 460), ImGuiCond.FirstUseEver);
        ImGui.Begin("debugui items", ref displayItemsDebugInfo);

        ImGui.SetNextItemWidth(220);
        ImGui.InputTextWithHint("##filter", "filter (id / name)", ref filter, 64);
        ImGui.SameLine();
        if (ImGui.Button("Refresh"))
        {
            entries = ScanDefinitions();
        }
        ImGui.SameLine();
        ImGui.TextDisabled($"{entries.Count} definitions");

        ImGui.BeginChild("##list", new Vec2(260, 0), ImGuiChildFlags.Borders);
        RenderList();
        ImGui.EndChild();

        ImGui.SameLine();
        ImGui.BeginChild("##details", Vec2.Zero);
        RenderDetails(owner);
        ImGui.EndChild();

        ImGui.End();
    }

    private static void RenderList()
    {
        string lowerFilter = filter.ToLowerInvariant();
        foreach (var group in entries.GroupBy(e => e.category))
        {
            var matches = group.Where(e => Matches(e.itemID, lowerFilter)).ToList();
            if (matches.Count == 0)
            {
                continue;
            }
            // Open every category while filtering so hits are visible.
            if (lowerFilter.Length > 0)
            {
                ImGui.SetNextItemOpen(true);
            }
            if (ImGui.TreeNode($"{group.Key} ({matches.Count})"))
            {
                foreach (var (_, itemID) in matches)
                {
                    if (ImGui.Selectable(itemID, itemID == selectedID))
                    {
                        selectedID = itemID;
                    }
                }
                ImGui.TreePop();
            }
        }
    }

    private static bool Matches(string itemID, string lowerFilter)
    {
        if (lowerFilter.Length == 0 || itemID.ToLowerInvariant().Contains(lowerFilter))
        {
            return true;
        }
        return ItemInfo.Fetch(itemID)?.displayName?.ToLowerInvariant().Contains(lowerFilter) ?? false;
    }

    private static void RenderDetails(Node owner)
    {
        ItemInfo item = ItemInfo.Fetch(selectedID);
        if (item == null)
        {
            ImGui.TextDisabled("Select an item.");
            return;
        }
        if (previewID != selectedID)
        {
            BuildPreview(item);
        }

        Widgets.SubViewport(preview);
        if (item.icon != null)
        {
            ImGui.SameLine();
            Widgets.Image(item.icon, new Vec2(64, 64));
        }

        ImGui.Text(item.displayName ?? "(no display name)");
        ImGui.TextDisabled($"{item.itemID} | {item.GetType().Name} | stack {item.maxStackSize}");
        ImGui.TextWrapped(item.description ?? "");
        ImGui.Separator();
        ImGui.TextDisabled($"Definition: {item.ResourcePath}");
        ImGui.TextDisabled($"Dropped: {item.droppedScene?.ResourcePath ?? "(default box)"}");
        ImGui.TextDisabled($"In hand: {item.inHandScene?.ResourcePath ?? "-"}");
        if (item is BlueprintItem blueprint)
        {
            ImGui.TextDisabled($"Structure: {blueprint.structureScene?.ResourcePath ?? "-"}");
        }
        ImGui.Separator();

        ImGui.SetNextItemWidth(120);
        ImGui.SliderInt("Count", ref count, 1, Math.Max(10, item.maxStackSize * 2));
        FactoryPlayer local = LocalPlayer();
        ImGui.BeginDisabled(local == null);
        if (ImGui.Button("Give to inventory"))
        {
            int left = local.inventory.AddItem(item.itemID, count);
            local.UpdateEquippedItem();
            if (left > 0)
            {
                Logging.Log($"inventory full, {left} x {item.itemID} not given", "ItemsDebug");
            }
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!GameWorld.instance.IsInsideTree() || owner.GetViewport().GetCamera3D() == null);
        if (ImGui.Button("Spawn in world"))
        {
            SpawnInFront(owner, item.itemID);
        }
        ImGui.EndDisabled();
        if (local == null)
        {
            ImGui.TextDisabled("No local player: start a game to give items.");
        }
    }

    private static FactoryPlayer LocalPlayer() =>
        GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);

    // Same placement as FactoryPlayer.DropFromActiveSlot: 2 m in front of the camera, a little scatter.
    private static void SpawnInFront(Node owner, string itemID)
    {
        Camera3D camera = LocalPlayer()?.camera ?? owner.GetViewport().GetCamera3D();
        Vector3 pos = camera.GlobalPosition - camera.GlobalTransform.Basis.Z * 2f;
        Vector3 rot = new(0, camera.GlobalRotation.Y, 0);
        for (int i = 0; i < count; i++)
        {
            Vector3 offset = new((Random.Shared.NextSingle() - 0.5f) * 0.5f, i * 0.3f, (Random.Shared.NextSingle() - 0.5f) * 0.5f);
            ItemInfo.SpawnInWorld(itemID, pos + offset, rot);
        }
    }

    // ---- model preview ---------------------------------------------------------

    private static void EnsurePreview(Node owner)
    {
        if (preview != null && GodotObject.IsInstanceValid(preview))
        {
            return;
        }
        preview = new SubViewport
        {
            Name = "ItemsDebugPreview",
            OwnWorld3D = true,
            TransparentBg = true,
            Size = new Vector2I(previewSize, previewSize),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
        };
        Godot.Environment env = new()
        {
            BackgroundMode = Godot.Environment.BGMode.ClearColor,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.6f, 0.6f, 0.65f),
            AmbientLightEnergy = 0.6f,
        };
        previewCamera = new Camera3D { Fov = 40, Environment = env };
        DirectionalLight3D sun = new() { RotationDegrees = new Vector3(-45, 30, 0), LightEnergy = 1.2f };
        previewPivot = new Node3D();
        preview.AddChild(previewCamera);
        preview.AddChild(sun);
        preview.AddChild(previewPivot);
        owner.AddChild(preview);
        previewID = null;
    }

    private static void BuildPreview(ItemInfo item)
    {
        previewID = item.itemID;
        foreach (Node child in previewPivot.GetChildren())
        {
            child.QueueFree();
        }

        PackedScene scene = (item as BlueprintItem)?.structureScene ?? item.droppedScene;
        if (scene == null)
        {
            // No scene (DefaultDroppedBox items): a plain box stands in.
            previewPivot.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = Vector3.One * 0.5f } });
            FrameCamera(new Aabb(-Vector3.One * 0.25f, Vector3.One * 0.5f));
            return;
        }

        // Copy only the meshes out of an un-entered instance, so none of the item's scripts, bodies or
        // GMPObject registration run (GameWorld.SpawnScene instantiates-and-frees the same way).
        Node root = scene.Instantiate();
        Aabb bounds = default;
        bool first = true;
        foreach (MeshInstance3D source in root.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
        {
            if (source.Mesh == null || !source.Visible)
            {
                continue;
            }
            Transform3D xform = RelativeTransform(source, root);
            MeshInstance3D copy = new() { Mesh = source.Mesh, Transform = xform, MaterialOverride = source.MaterialOverride };
            for (int s = 0; s < source.GetSurfaceOverrideMaterialCount(); s++)
            {
                copy.SetSurfaceOverrideMaterial(s, source.GetSurfaceOverrideMaterial(s));
            }
            previewPivot.AddChild(copy);
            Aabb meshBounds = xform * source.Mesh.GetAabb();
            bounds = first ? meshBounds : bounds.Merge(meshBounds);
            first = false;
        }
        root.Free();
        FrameCamera(first ? new Aabb(-Vector3.One * 0.25f, Vector3.One * 0.5f) : bounds);
    }

    // Transform of node relative to root, walking parents by hand (the instance isn't in the tree).
    private static Transform3D RelativeTransform(Node3D node, Node root)
    {
        Transform3D xform = node.Transform;
        for (Node parent = node.GetParent(); parent != null && parent != root; parent = parent.GetParent())
        {
            if (parent is Node3D parent3D)
            {
                xform = parent3D.Transform * xform;
            }
        }
        return xform;
    }

    private static void FrameCamera(Aabb bounds)
    {
        // Centre the model on the pivot so it spins in place, then back the camera off to fit its bounding sphere.
        Vector3 center = bounds.GetCenter();
        foreach (Node3D child in previewPivot.GetChildren().OfType<Node3D>())
        {
            child.Position -= center;
        }
        previewPivot.Rotation = Vector3.Zero;
        float radius = Math.Max(bounds.Size.Length() * 0.5f, 0.05f);
        float distance = radius / Mathf.Sin(Mathf.DegToRad(previewCamera.Fov * 0.5f)) * 1.1f;
        Vector3 dir = new Vector3(0, 0.5f, 1).Normalized();
        previewCamera.Position = dir * distance;
        previewCamera.LookAt(Vector3.Zero);
        previewCamera.Near = Math.Max(0.01f, distance - radius * 2);
        previewCamera.Far = distance + radius * 2;
    }
}
