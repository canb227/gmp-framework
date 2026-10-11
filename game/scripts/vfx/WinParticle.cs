using Godot;

public partial class WinParticle : GpuParticles3D
{
    public override void _Ready()
    {
        ProgressManager.QuestCompleted += OnQuestCompleted;
    }

    private void OnQuestCompleted(string obj)
    {
        Restart();
    }
}
