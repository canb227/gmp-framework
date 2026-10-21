using Godot;
using System;
using System.Collections.Generic;

public partial class CoreColumn : MeshInstance3D
{
    Material mat;
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        mat = GetActiveMaterial(2) as StandardMaterial3D;
        AudioManager.Instance.PlayOneShot("event:/Machines/Central_Pillar", 1.0f, GlobalPosition, new Dictionary<string, float>{});
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        if (mat == null)
            return;

        float t = (Mathf.Sin(Time.GetTicksMsec() / 1000.0f) + 1.0f) * 0.5f;
        float emission = Mathf.Lerp(1.0f, 3.0f, t);

        mat.Set("emission_energy_multiplier", emission);
    }
}
