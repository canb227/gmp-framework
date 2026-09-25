using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[GlobalClass]
public partial class InHandBuilding : Tool
{



    public override void _Ready()
    {
        Logging.Log("u just equipped me", "InHandBuilding");
    }

    public override void _ExitTree()
    {
        Logging.Log("u just put me away", "InHandBuilding");
    }
}

