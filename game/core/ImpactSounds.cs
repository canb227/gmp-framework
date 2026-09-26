using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// What an impact sounds like. Surfaces are the material <see cref="ItemTags"/> (METAL ... GLASS); a hit between
/// an item and another surface plays one or more layers, each a material's sound bank at some gain, chosen by
/// the pair (item's surface, other surface) like <see cref="TagInteractions"/> rules. Unlisted pairs play the
/// struck surface loudest with the item's own sound under it. Impact speed picks the light or heavy variants
/// (see <see cref="ImpactAudio"/> for volume and pitch).
/// <para>
/// Sounds are loaded by name from <see cref="Folder"/>: <c>&lt;material&gt;_&lt;light|heavy&gt;_&lt;n&gt;.wav</c>.
/// </para>
/// </summary>
public static class ImpactSounds
{
    public const string Folder = "res://game/assets/audio/impacts/";
    const int Variants = 4;

    /// <summary>Surface of anything without a material tag (level geometry, untagged props).</summary>
    public const ItemTags DefaultSurface = ItemTags.CONCRETE;
    /// <summary>Approach speed (m/s) of the quietest impact; matches the world's hit threshold.</summary>
    public const float MinSpeed = 1f;
    /// <summary>Approach speed (m/s) at which impacts play at full volume.</summary>
    public const float FullSpeed = 8f;
    /// <summary>Approach speed (m/s) from which the heavy variants play.</summary>
    public const float HeavySpeed = 4f;
    /// <summary>Gain of a full-speed impact (every sound's level at baseline).</summary>
    public const float BaselineVolume = 0.5f;
    /// <summary>Loudness of an impact at <see cref="MinSpeed"/>, as a fraction of <see cref="BaselineVolume"/>.</summary>
    public const float MinSpeedVolume = 0.25f;
    /// <summary>Shape of the speed-to-loudness ramp: above 1 keeps gentle bumps quiet and saves the volume for hard hits.</summary>
    public const float SpeedCurve = 2f;

    /// <summary>Linear gain for an impact at <paramref name="speed"/> m/s.</summary>
    public static float Loudness(float speed)
    {
        float t = Mathf.Clamp((speed - MinSpeed) / (FullSpeed - MinSpeed), 0f, 1f);
        return BaselineVolume * Mathf.Lerp(MinSpeedVolume, 1f, Mathf.Pow(t, SpeedCurve));
    }

    public static bool IsMaterial(ItemTags tag) => tag >= ItemTags.METAL && tag <= ItemTags.GLASS;

    /// <summary>The first material tag in <paramref name="tags"/>, or null.</summary>
    public static ItemTags? MaterialOf(IEnumerable<ItemTags> tags)
    {
        if (tags == null) return null;
        foreach (ItemTags tag in tags)
        {
            if (IsMaterial(tag)) return tag;
        }
        return null;
    }

    static readonly ItemTags[] materials = [ItemTags.METAL, ItemTags.ROCK, ItemTags.RUBBER, ItemTags.CONCRETE, ItemTags.PLASTIC, ItemTags.GLASS];

    /// <summary>(item surface, other surface) -> layers. Filled in by the static constructor; edit there to tune.</summary>
    static readonly Dictionary<(ItemTags self, ItemTags other), (ItemTags material, float gain)[]> rules = new();

    static ImpactSounds()
    {
        foreach (ItemTags m in materials)
        {
            // Landing on a belt: the rubber swallows it, a little of the item still comes through.
            rules[(m, ItemTags.RUBBER)] = [(ItemTags.RUBBER, 1f), (m, 0.3f)];
            // Glass rings over whatever it meets.
            rules[(m, ItemTags.GLASS)] = [(ItemTags.GLASS, 1f), (m, 0.5f)];
            rules[(ItemTags.GLASS, m)] = [(ItemTags.GLASS, 1f), (m, 0.6f)];
        }
        // A soft item barely marks what it hits.
        foreach (ItemTags m in materials)
        {
            rules.TryAdd((ItemTags.RUBBER, m), [(m, 0.5f), (ItemTags.RUBBER, 1f)]);
        }
        // Metal on metal is all clang.
        rules[(ItemTags.METAL, ItemTags.METAL)] = [(ItemTags.METAL, 1f)];
    }

    /// <summary>The layers to play when an item of surface <paramref name="self"/> hits <paramref name="other"/>.</summary>
    public static (ItemTags material, float gain)[] Layers(ItemTags self, ItemTags other)
    {
        if (rules.TryGetValue((self, other), out var layers)) return layers;
        if (self == other) return [(self, 1f)];
        return [(other, 1f), (self, 0.6f)];
    }

    // Strong cache: loaded once, kept for the session.
    static readonly Dictionary<string, AudioStream> streams = new();

    /// <summary>A random variant from <paramref name="material"/>'s light or heavy bank (null if the file is missing).</summary>
    public static AudioStream Pick(ItemTags material, bool heavy)
    {
        string path = $"{Folder}{material.ToString().ToLowerInvariant()}_{(heavy ? "heavy" : "light")}_{Random.Shared.Next(Variants)}.wav";
        if (!streams.TryGetValue(path, out AudioStream stream))
        {
            stream = ResourceLoader.Exists(path) ? ResourceLoader.Load<AudioStream>(path) : null;
            streams[path] = stream;
        }
        return stream;
    }
}
