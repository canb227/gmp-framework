using Godot;
using System;

/// <summary>
/// The save/load file browser: Godot's <see cref="FileDialog"/>, opened in this user's save folder
/// (<see cref="GameWorld.SaveDir"/>) and showing only save files. Saving suggests a timestamped name and asks before
/// overwriting; either way the player can browse elsewhere. Used by the pause menu (save) and the lobby (load).
/// </summary>
public static class SaveFileDialog
{
    /// <summary>Opens the browser over <paramref name="owner"/>; <paramref name="chosen"/> gets the picked file's path. Cancelling does nothing.</summary>
    public static void Open(Node owner, bool saving, Action<string> chosen)
    {
        DirAccess.MakeDirRecursiveAbsolute(GameWorld.SaveDir);
        FileDialog dialog = new()
        {
            Title = saving ? "Save Game" : "Load Game",
            FileMode = saving ? FileDialog.FileModeEnum.SaveFile : FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Filesystem,
            CurrentDir = ProjectSettings.GlobalizePath(GameWorld.SaveDir),
            Filters = ["*" + GameWorld.SaveExtension + " ; Saved games"],
        };
        if (saving)
        {
            dialog.CurrentFile = GameWorld.DefaultSaveName();
        }
        dialog.FileSelected += path => chosen(path);
        // Freed however it closes, picked or cancelled.
        dialog.VisibilityChanged += () =>
        {
            if (!dialog.Visible) dialog.QueueFree();
        };
        owner.AddChild(dialog);
        dialog.PopupCenteredRatio(0.6f);
    }
}
