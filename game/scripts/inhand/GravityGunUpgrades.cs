/// <summary>One gravity gun upgrade: bought once per player, for <see cref="cost"/> of <see cref="GravityGunUpgrades.currency"/>.</summary>
/// <param name="id">What saves and the network call it.</param>
public record GravityGunUpgrade(string id, string name, string description, int cost);

/// <summary>
/// Every gravity gun upgrade. Each player buys their own (<see cref="FactoryPlayer.gunUpgrades"/>), paid for with
/// <see cref="currency"/> from the shared banks (<see cref="Shop.TryBuyGunUpgrade"/>); quests can pay that currency out
/// as a reward. What an upgrade does is written where it applies, by checking <see cref="FactoryPlayer.HasGunUpgrade"/>
/// (e.g. <see cref="GravityGun.ForceScale"/>).
/// <para>A new upgrade: a field here, added to <see cref="all"/>, and a check where it applies.</para>
/// </summary>
public static class GravityGunUpgrades
{
    /// <summary>What every upgrade is paid in.</summary>
    public const Currency currency = Currency.GravityCores;

    public static readonly GravityGunUpgrade DoubleForce = new("double_force", "Double Force", "Launches, punts and blasts push twice as hard.", 5);

    public static readonly GravityGunUpgrade[] all = [DoubleForce];

    /// <summary>The upgrade with this id, or null.</summary>
    public static GravityGunUpgrade Find(string id)
    {
        foreach (GravityGunUpgrade upgrade in all)
        {
            if (upgrade.id == id) return upgrade;
        }
        return null;
    }
}
