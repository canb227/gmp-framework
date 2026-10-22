using System.Collections.Generic;
using Godot;
using PolyType;

/// <summary>Everything a <see cref="DevSpawner"/>'s menu can change, sent as one record and saved as one.</summary>
[GenerateShape]
public partial record struct DevSpawnerSettings
{
    public bool spawning;
    public Dictionary<string, float> itemWeights;
    public double spawnInterval;
    public int itemsPerSpawn;
    public float launchSpeed;
    public float launchSpreadDegrees;
    public int maxOfItem;
    public int maxTotalItems;
    public int maxNearby;
    public float nearbyRadius;
}

/// <summary>
/// A dev tool: an <see cref="ItemSpawner"/> whose items, rate, burst, launch and limits are set in game from an
/// ImGui menu (<see cref="OpenConfigMenu"/>). Any peer can change any spawner: the menu sends the whole
/// <see cref="DevSpawnerSettings"/> to every peer, each keeps the newest it has heard, and saves (and so late
/// joins) keep it.
/// </summary>
public partial class DevSpawner : ItemSpawner
{
    /// <summary>When the settings now applied were sent (<see cref="GameWorld.netTime"/>), so an older edit arriving late is ignored.</summary>
    ulong settingsSentAt;

    /// <summary>Opens the config menu for the local player. Call on the peer whose player should see it.</summary>
    public void OpenConfigMenu()
    {
        UIManager.OpenScreen(new DevSpawnerMenu(this));
    }

    /// <summary>Opens the config menu for the interacting player (interact only runs on that player's own peer).</summary>
    public override void onInteract(ulong playerID)
    {
        if (GameWorld.syncedObjs.TryGetValue(playerID, out GMPObject obj) && obj is FactoryPlayer { isLocal: true })
        {
            OpenConfigMenu();
        }
    }

    /// <summary>What it spawns now (or that it's off), and how to change it.</summary>
    protected override void UpdateHoverText()
    {
        string status;
        if (!spawning)
        {
            status = "Off";
        }
        else if (itemWeights.Count == 0)
        {
            status = "On, but no items chosen";
        }
        else
        {
            status = $"Spawning {itemsPerSpawn} every {spawnInterval:0.##} s: {string.Join(", ", itemWeights.Keys)}";
        }
        hoverText = status + "\nPress F to configure";
    }

    /// <summary>The settings it runs with now (a copy: changing it changes nothing until <see cref="SendSettings"/>).</summary>
    public DevSpawnerSettings CurrentSettings()
    {
        Dictionary<string, float> weights = new();
        foreach (var (itemID, weight) in itemWeights)
        {
            weights[itemID] = weight;
        }
        return new DevSpawnerSettings
        {
            spawning = spawning,
            itemWeights = weights,
            spawnInterval = spawnInterval,
            itemsPerSpawn = itemsPerSpawn,
            launchSpeed = launchSpeed,
            launchSpreadDegrees = launchSpreadDegrees,
            maxOfItem = maxOfItem,
            maxTotalItems = maxTotalItems,
            maxNearby = maxNearby,
            nearbyRadius = nearbyRadius,
        };
    }

    /// <summary>Sends <paramref name="settings"/> to every peer (this one included) to run with.</summary>
    public void SendSettings(DevSpawnerSettings settings)
    {
        RPCManager.RPC(this, nameof(SetSettings), [settings]);
    }

    [RPC]
    void SetSettings(DevSpawnerSettings settings)
    {
        // Two peers editing at once: everyone ends up on whichever was sent last.
        if (RPCManager.sendTime < settingsSentAt)
        {
            return;
        }
        settingsSentAt = RPCManager.sendTime;
        ApplySettings(settings);
    }

    void ApplySettings(DevSpawnerSettings settings)
    {
        spawning = settings.spawning;
        itemWeights.Clear();
        if (settings.itemWeights != null)
        {
            foreach (var (itemID, weight) in settings.itemWeights)
            {
                itemWeights[itemID] = weight;
            }
        }
        spawnInterval = Mathf.Max(0.01, settings.spawnInterval);
        itemsPerSpawn = Mathf.Max(1, settings.itemsPerSpawn);
        launchSpeed = settings.launchSpeed;
        launchSpreadDegrees = settings.launchSpreadDegrees;
        maxOfItem = settings.maxOfItem;
        maxTotalItems = settings.maxTotalItems;
        maxNearby = settings.maxNearby;
        nearbyRadius = settings.nearbyRadius;
        UpdateHoverText();
    }

    /// <summary>Asks the authority to spawn one burst right away, on top of its timer (limits still apply).</summary>
    public void SpawnNow()
    {
        RPCManager.RPCTo(authority, this, nameof(RequestSpawnNow), []);
    }

    [RPC]
    void RequestSpawnNow()
    {
        if (runsMachineLogic && streamOutputs.Count > 0)
        {
            SpawnBurst();
        }
    }

    // ---- saving ----

    protected override byte[] SaveSpawnerState()
    {
        return GMPObject.serializer.Serialize(CurrentSettings());
    }

    protected override void LoadSpawnerState(byte[] state)
    {
        ApplySettings(GMPObject.serializer.Deserialize<DevSpawnerSettings>(state));
    }
}
