using System.Collections.Generic;

/// <summary>The newest claim on a synced object. Every peer keeps the same one once it has heard all the claims.</summary>
public struct ClaimRecord
{
    /// <summary>When the claim was sent (<see cref="GameWorld.netTime"/>).</summary>
    public ulong time;
    /// <summary>The peer that claimed.</summary>
    public ulong peer;
    /// <summary>The claim has ended and the object is back with the host.</summary>
    public bool released;
}

/// <summary>
/// GameWorld: claims, i.e. who simulates (is the authority of) a synced object. Claims are sent to every peer and the
/// newest one wins: each peer keeps the claim with the latest send time (ties go to the higher peer id), so all of
/// them end up agreeing whatever order the claims arrive in. A claim takes effect on the claiming peer at once.
/// <para>
/// A claim can be a lease (<c>keepSeconds</c>): it lasts until that long after the claimer last called
/// <see cref="Claim"/>, then the claimer releases it and the object goes back to the host, the default authority.
/// Keeping a lease alive costs nothing on the network; only the claim and the release are sent. A release carries the
/// time of the claim it ends, so a newer claim by someone else always beats it.
/// </para>
/// </summary>
public partial class GameWorld
{
    /// <summary>The newest claim on each object, by object id. No entry: nobody has claimed it.</summary>
    static readonly Dictionary<ulong, ClaimRecord> claims = new();

    /// <summary>This peer's leases: when each runs out (<see cref="Godot.Time.GetTicksMsec"/>), by object id.</summary>
    static readonly Dictionary<ulong, ulong> leaseEnds = new();

    static readonly List<ulong> endedLeases = new();

    /// <summary>
    /// Makes this peer <paramref name="gmpo"/>'s authority, for everyone. With <paramref name="keepSeconds"/> above 0
    /// it's a lease: call again to keep it, and it's released that long after the last call. A claim this peer already
    /// holds is only kept (nothing is sent).
    /// </summary>
    public static void Claim(GMPObject gmpo, double keepSeconds = 0)
    {
        if (keepSeconds > 0)
        {
            leaseEnds[gmpo.id] = Godot.Time.GetTicksMsec() + (ulong)(keepSeconds * 1000);
        }
        else
        {
            leaseEnds.Remove(gmpo.id);
        }
        if (claims.TryGetValue(gmpo.id, out ClaimRecord claim) && claim.peer == Lobby.selfPeerID && !claim.released)
        {
            return;
        }
        RPCManager.RPC(instance, nameof(_Claim), [gmpo.id]);
    }

    /// <summary>True if another peer holds a claim on <paramref name="gmpo"/> that hasn't ended.</summary>
    public static bool IsClaimedByOther(GMPObject gmpo)
    {
        return claims.TryGetValue(gmpo.id, out ClaimRecord claim) && claim.peer != Lobby.selfPeerID && !claim.released;
    }

    [RPC]
    private void _Claim(ulong id)
    {
        ApplyClaim(id, new ClaimRecord { time = RPCManager.sendTime, peer = RPCManager.sender, released = false });
    }

    [RPC]
    private void _Release(ulong id, ulong claimTime)
    {
        ApplyClaim(id, new ClaimRecord { time = claimTime, peer = RPCManager.sender, released = true });
    }

    // Keeps the claim if it's newer than the one held, and moves the object's authority to match. A claim on an object
    // this peer hasn't spawned yet is kept too, and applied when it spawns (see WithEarlyClaim).
    static void ApplyClaim(ulong id, ClaimRecord claim)
    {
        if (claims.TryGetValue(id, out ClaimRecord current) && !IsNewer(claim, current))
        {
            return;
        }
        claims[id] = claim;
        if (syncedObjs.TryGetValue(id, out GMPObject gmpo))
        {
            SetAuthority(gmpo, AuthorityFromClaim(claim));
        }
    }

    static bool IsNewer(ClaimRecord a, ClaimRecord b)
    {
        if (a.time != b.time)
        {
            return a.time > b.time;
        }
        if (a.peer != b.peer)
        {
            return a.peer > b.peer;
        }
        // The release of a claim beats the claim itself.
        return a.released && !b.released;
    }

    static ulong AuthorityFromClaim(ClaimRecord claim)
    {
        if (claim.released)
        {
            return Lobby.hostID;
        }
        return claim.peer;
    }

    static void SetAuthority(GMPObject gmpo, ulong authority)
    {
        if (gmpo.authority == authority)
        {
            return;
        }
        gmpo.authority = authority;
        // The new authority's clock may be behind the old one's: don't hold its updates against the old ones'.
        lastStateTime.Remove(gmpo.id);
        gmpo.OnAuthorityChanged();
    }

    /// <summary>A spawning object's init data, with the authority of a claim that reached this peer before the spawn did.</summary>
    static GMPOInitData WithEarlyClaim(GMPOInitData init)
    {
        if (claims.TryGetValue(init.id, out ClaimRecord claim))
        {
            init.authority = AuthorityFromClaim(claim);
        }
        return init;
    }

    /// <summary>Each physics tick: releases this peer's leases that have run out.</summary>
    static void CheckLeases()
    {
        ulong now = Godot.Time.GetTicksMsec();
        endedLeases.Clear();
        foreach ((ulong id, ulong end) in leaseEnds)
        {
            if (now >= end)
            {
                endedLeases.Add(id);
            }
        }
        foreach (ulong id in endedLeases)
        {
            leaseEnds.Remove(id);
            // Only if the newest claim is still this peer's: if someone claimed since, it's theirs now.
            if (claims.TryGetValue(id, out ClaimRecord claim) && claim.peer == Lobby.selfPeerID && !claim.released)
            {
                RPCManager.RPC(instance, nameof(_Release), [id, claim.time]);
            }
        }
    }

    /// <summary>
    /// Ends <paramref name="peer"/>'s claims and hands everything it simulated to the host. Every peer runs this when
    /// it loses its link to a peer that left, so they all agree without sending anything.
    /// </summary>
    static void RehomeFrom(ulong peer)
    {
        foreach (GMPObject gmpo in syncedObjs.Values)
        {
            if (claims.TryGetValue(gmpo.id, out ClaimRecord claim) && claim.peer == peer && !claim.released)
            {
                claim.released = true;
                claims[gmpo.id] = claim;
            }
            if (gmpo.authority == peer)
            {
                SetAuthority(gmpo, Lobby.hostID);
            }
        }
    }

    /// <summary>Forgets a despawned object's claim and lease.</summary>
    static void ForgetClaim(ulong id)
    {
        claims.Remove(id);
        leaseEnds.Remove(id);
    }
}
