using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//Literally every single piece of data that you need to know about a player at the time the game starts MUST go in here.
[GenerateShape]
public partial class PlayerInfo
{
    public ulong PeerID;
    public string Name;
    public bool IsHost;
    public bool donePreloading;
    public bool doneLoading;

}

//suspicious class watches from a distance - it sees everything
[GenerateShapeFor<PlayerInfo[]>]
partial class Witness;