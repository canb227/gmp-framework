using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public partial class BrokenStructure : GMPOBox3DBody, Triggerable
{

    public Structure repairedStructure;

    public void OnTrigger(bool active)
    {
        throw new NotImplementedException();
    }
}