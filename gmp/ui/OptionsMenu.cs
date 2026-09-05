using Godot;

// Minimal controller: the options screen's only wired control is Back, which returns
// to the main menu.
public partial class OptionsMenu : Control
{
    public override void _Ready()
    {
        GetNode<Button>("CenterContainer/MainPanel/Content/Footer/BackButton").Pressed +=
            () => GetTree().ChangeSceneToFile("res://gmp/ui/main_menu.tscn");
    }
}
