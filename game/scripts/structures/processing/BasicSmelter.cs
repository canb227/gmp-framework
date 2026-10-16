using Godot;


/// <summary>Smelts what reaches its inputs by its <see cref="RecipeMachine.recipes"/> (all the work is in <see cref="RecipeMachine"/>).</summary>
public partial class BasicSmelter : RecipeMachine
{
    [Export] int scrapNeeded = 10;
    [Export] float cookTime = 10.0f;
    protected override bool TryStartProcess()
    {
        int found = 0;
        foreach (PhysicalFactoryItem input in inputItems)
        {
            if (input.currencyValue.ContainsKey(Currency.Scrap)) 
            {
                found += input.currencyValue[Currency.Scrap];
            }
        }

        if(found >= scrapNeeded)
        {
            foreach (PhysicalFactoryItem input in inputItems)
            {
                GameWorld.DespawnObject(input.id, DespawnReason.Consumed);
            }
            inputItems.Clear();
            processTimeRemaining = cookTime;
        RPCManager.RPC(this, "PlayAnimation", ["process"]);
            return true;
        }
        
        return false;
    }

    protected override void FinishProcess()
    {
        RPCManager.RPC(this, "PlayAnimation", ["finish_process"]);
        ItemInfo.SpawnInWorld("scrap_ingot", OutputPosition(0));
        ItemInfo.SpawnInWorld("scrap_ingot", OutputPosition(1));
    }

}
