using Godot;
using System;

public partial class WinParticle : GpuParticles3D
{
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        ProgressManager.QuestCompleted += OnQuestCompleted;
    }

    private void OnQuestCompleted(string obj)
    {
        Restart();
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }
}
