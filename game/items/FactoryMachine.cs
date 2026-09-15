using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public partial class FactoryMachine : PhysicalFactoryItem
{
    [Export]
    public CompressedTexture2D icon { get; set; }
}
