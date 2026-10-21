/// <summary>
/// Something the player can use with interact (F) while looking at it: a structure, a dropped tool, a button. Runs on the
/// interacting player's own peer only, so anything it changes for everyone goes through an RPC (usually
/// <see cref="RPCManager.RequestFromAuthority"/>).
/// </summary>
public interface Interactable
{
    /// <summary>The player with GMPObject id <paramref name="player"/> interacted with this.</summary>
    public void onInteract(ulong player)
    {
    }
}