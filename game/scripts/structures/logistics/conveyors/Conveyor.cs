using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public partial class Conveyor : Structure, Interactable
{
    [Export]
    public Godot.Collections.Array<GMPOBox3DBody> beltSurfaces;
}