using System.Collections.Generic;
using Godot;

/// <summary>
/// Melts down scrap: once the items in its inputs are worth <see cref="scrapNeeded"/> scrap between them it takes them
/// all, cooks for <see cref="cookTime"/> and puts out a scrap ingot at each of its first two outputs.
/// </summary>
public partial class BasicSmelter : IOMachine
{
    [Export] int scrapNeeded = 10;
    [Export] float cookTime = 10.0f;

    protected override bool TryStartProcess()
    {
        int found = 0;
        foreach (ItemRecord input in inputItems)
        {
            if (input.currencyValue.ContainsKey(Currency.Scrap))
            {
                found += input.currencyValue[Currency.Scrap];
            }
        }

        if (found >= scrapNeeded)
        {
            // Taken out of the list first: despawning an item takes it out of inputItems (IOMachine.OnItemRemoved).
            List<ItemRecord> consumed = new(inputItems);
            inputItems.Clear();
            foreach (ItemRecord input in consumed)
            {
                ItemWorld.Despawn(input.id, DespawnReason.Consumed);
            }
            processTimeRemaining = cookTime;
            RPCManager.RPC(this, nameof(PlayAnimation), ["process"]);
            return true;
        }

        return false;
    }

    protected override void FinishProcess()
    {
        RPCManager.RPC(this, nameof(PlayAnimation), ["finish_process"]);
        ItemInfo.SpawnInWorld("scrap_ingot", OutputPosition(0));
        ItemInfo.SpawnInWorld("scrap_ingot", OutputPosition(1));
    }

    [RPC(requireAuthority = true)]
    protected virtual void PlayAnimation(string animationName)
    {
        animationPlayer.Play(animationName);
    }
}
