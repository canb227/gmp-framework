using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public partial interface IOMachine
{


    
    public bool isOperating();

    public float getSpeedMultiplier();

    public void setSpeedMultiplier(float multiplier);


}

