using Godot;
using Limbo.Console.Sharp;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

/// <summary>
/// Lets a test driver (a script outside the game) run console commands in this instance and read what they printed.
/// Only exists when the game is launched with "-- --testctl &lt;folder&gt;"; <see cref="Console"/> adds it.
/// <para>
/// The driver appends lines "&lt;id&gt; &lt;console command&gt;" to &lt;folder&gt;/in.txt. Each is run through LimboConsole and
/// whatever the console printed while it ran is written to &lt;folder&gt;/resp_&lt;id&gt;.txt. Everything Godot prints
/// (logs, errors, exceptions) also goes to &lt;folder&gt;/log.txt. The instance quits by itself after
/// <see cref="IdleQuitSeconds"/> without a command, so a forgotten test copy doesn't stay open.
/// </para>
/// </summary>
public partial class TestControl : Node
{
    /// <summary>The folder the driver and this instance talk through, set from --testctl.</summary>
    public static string folder;

    const double PollSeconds = 0.1;
    const double IdleQuitSeconds = 600;

    long readPos;
    double pollTimer;
    double idleTimer;
    RichTextLabel consoleOutput;

    static readonly Regex bbcode = new Regex(@"\[/?(color|b|i|u|s|url|font_size|bgcolor|fgcolor|code|center|right|indent)[^\]]*\]");

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool SetForegroundWindow(IntPtr window);

    public TestControl(string testFolder)
    {
        folder = testFolder;
    }

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        Directory.CreateDirectory(folder);
        OS.AddLogger(new TestLogger(Path.Combine(folder, "log.txt")));

        // Commands are run non-silently so their output can be read back, which also puts them in the console
        // history; keep that history out of the player's saved one.
        Node limbo = GetNode("/root/LimboConsole");
        GodotObject options = limbo.Get("_options").AsGodotObject();
        options.Set("persist_history", false);
        consoleOutput = limbo.Get("_output").As<RichTextLabel>();

        string name = "test";
        string[] args = OS.GetCmdlineUserArgs();
        int nameFlag = Array.IndexOf(args, "--name");
        if (nameFlag >= 0 && nameFlag + 1 < args.Length)
            name = args[nameFlag + 1];
        DisplayServer.WindowSetTitle($"{name} (test)");

        // Stay out of the way of whoever is using the computer: the window can't take focus from now on (clicks don't
        // activate it), and focus goes back to the window that had it before this copy started ("--return-focus
        // <window handle>", passed by the launcher). Godot takes focus while starting up, before this runs.
        DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.NoFocus, true);
        int focusFlag = Array.IndexOf(args, "--return-focus");
        if (OS.GetName() == "Windows" && focusFlag >= 0 && focusFlag + 1 < args.Length && long.TryParse(args[focusFlag + 1], out long window))
        {
            SetForegroundWindow(new IntPtr(window));
        }
        GD.Print($"[TestControl] listening on {folder}");
    }

    public override void _Process(double delta)
    {
        idleTimer += delta;
        if (idleTimer > IdleQuitSeconds)
        {
            GD.Print("[TestControl] no commands for a while, quitting");
            GetTree().Quit();
            return;
        }

        pollTimer += delta;
        if (pollTimer < PollSeconds)
            return;
        pollTimer = 0;

        foreach (string line in ReadNewLines())
        {
            idleTimer = 0;
            RunLine(line);
        }
    }

    /// <summary>Returns the complete lines added to in.txt since the last read (a line still being written waits).</summary>
    string[] ReadNewLines()
    {
        string inPath = Path.Combine(folder, "in.txt");
        if (!File.Exists(inPath))
            return [];

        string text;
        using (var stream = new FileStream(inPath, FileMode.Open, System.IO.FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            if (stream.Length <= readPos)
                return [];
            stream.Seek(readPos, SeekOrigin.Begin);
            byte[] bytes = new byte[stream.Length - readPos];
            int count = stream.Read(bytes, 0, bytes.Length);
            int lastNewline = Array.LastIndexOf(bytes, (byte)'\n', count - 1);
            if (lastNewline < 0)
                return [];
            readPos += lastNewline + 1;
            text = Encoding.UTF8.GetString(bytes, 0, lastNewline + 1);
        }
        return text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    void RunLine(string line)
    {
        int space = line.IndexOf(' ');
        if (space < 0)
            return;
        string id = line.Substring(0, space);
        string command = line.Substring(space + 1);

        int before = consoleOutput.Text.Length;
        LimboConsole.ExecuteCommand(command);
        string after = consoleOutput.Text;
        string printed;
        if (after.Length >= before)
            printed = after.Substring(before);
        else
            printed = after; // the command cleared the console
        printed = bbcode.Replace(printed, "");

        // Written under another name and renamed, so the driver never reads a half-written reply.
        string tempPath = Path.Combine(folder, $"resp_{id}.tmp");
        File.WriteAllText(tempPath, printed);
        File.Move(tempPath, Path.Combine(folder, $"resp_{id}.txt"), true);
    }

    /// <summary>Writes everything Godot prints, including errors and exceptions, to the test folder's log.txt.</summary>
    partial class TestLogger : Logger
    {
        readonly StreamWriter writer;
        readonly object writeLock = new object();

        public TestLogger(string path)
        {
            writer = new StreamWriter(new FileStream(path, FileMode.Create, System.IO.FileAccess.Write, FileShare.ReadWrite));
            writer.AutoFlush = true;
        }

        public override void _LogMessage(string message, bool error)
        {
            lock (writeLock)
            {
                writer.Write(message);
            }
        }

        public override void _LogError(string function, string file, int line, string code, string rationale,
            bool editorNotify, int errorType, Godot.Collections.Array<ScriptBacktrace> scriptBacktraces)
        {
            string text = string.IsNullOrEmpty(rationale) ? code : rationale;
            lock (writeLock)
            {
                writer.WriteLine($"{(ErrorType)errorType}: {text}\n   at {function} ({file}:{line})");
            }
        }
    }
}
