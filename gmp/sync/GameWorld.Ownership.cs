using System;
using System.Collections.Generic;

/// <summary>
/// GameWorld: claiming synced objects. A claim makes the requesting peer the object's authority and
/// "holder"; it is decided by the object's current authority, so simultaneous claims resolve to the
/// same single winner on every peer. Authority stays with the last holder after release.
/// <para>
/// Limitation: claims of one object are assumed to be well separated in time. A hand-over from A to B
/// and a later one from B to C are sent by different peers, so a third peer could in principle see
/// them out of order; it rejects the second (sender is not yet the authority there).
/// </para>
/// </summary>
public partial class GameWorld
{
    /// <summary>Current holder of each claimed object (absent = not held). Identical on every peer.</summary>
    public static readonly Dictionary<ulong, ulong> heldBy = new();

    /// <summary>Raised on every peer when a claim is granted: (objectId, newAuthority).</summary>
    public static event Action<ulong, ulong> ClaimGrantedEvent;

    /// <summary>
    /// Asks <paramref name="gmpo"/>'s current authority to make this peer its authority and holder.
    /// Resolves asynchronously (immediately if this peer is already the authority); watch
    /// <see cref="ClaimGrantedEvent"/> for the outcome. Denied silently if someone else holds it.
    /// </summary>
    public static void Claim(GMPObject gmpo)
    {
        RPCManager.RPCTo(gmpo.authority, instance, nameof(_RequestClaim), [gmpo.id]);
    }

    /// <summary>Releases an object this peer holds so others can claim it. Authority stays with this peer.</summary>
    public static void Release(GMPObject gmpo)
    {
        if (heldBy.TryGetValue(gmpo.id, out ulong holder) && holder == Lobby.selfPeerID)
        {
            RPCManager.RPC(instance, nameof(_ApplyRelease), [gmpo.id]);
        }
    }

    // Runs on the object's current authority. The first request wins: later ones find the object
    // held by someone else, or find that this peer is no longer the authority.
    [RPC]
    private void _RequestClaim(ulong id)
    {
        if (!syncedObjs.TryGetValue(id, out GMPObject gmpo) || gmpo.authority != Lobby.selfPeerID)
        {
            return;
        }
        if (heldBy.TryGetValue(id, out ulong holder) && holder != RPCManager.sender)
        {
            return;
        }
        RPCManager.RPC(instance, nameof(_ApplyClaim), [id, RPCManager.sender]);
    }

    [RPC]
    private void _ApplyClaim(ulong id, ulong newAuthority)
    {
        if (!syncedObjs.TryGetValue(id, out GMPObject gmpo))
        {
            return;
        }
        // Only the current authority may hand the object over.
        if (RPCManager.sender != gmpo.authority)
        {
            Logging.Warn($"Claim of {id} for {newAuthority} rejected: sent by {RPCManager.sender}, authority is {gmpo.authority}", "GameWorld");
            return;
        }
        heldBy[id] = newAuthority;
        if (gmpo.authority != newAuthority)
        {
            gmpo.authority = newAuthority;
            gmpo.OnAuthorityChanged();
        }
        ClaimGrantedEvent?.Invoke(id, newAuthority);
    }

    [RPC]
    private void _ApplyRelease(ulong id)
    {
        if (heldBy.TryGetValue(id, out ulong holder) && holder == RPCManager.sender)
        {
            heldBy.Remove(id);
        }
    }
}
