using Godot;
using System.Runtime.CompilerServices;
using Limbo.Console.Sharp;

/// <summary>
/// The game's log: each line goes to the in-game console (LimboConsole) and to Godot's output, which Godot also writes
/// to user://logs/godot.log. Lines read "[prefix][time]message"; the prefix is usually the class logging.
/// </summary>
public static class Logging
{
    /// <summary>True once <see cref="Start"/> has run; lines logged before then are dropped.</summary>
    public static bool IsStarted = false;

    /// <summary>Starts the log (Global does it at startup, once the console exists).</summary>
    public static void Start()
    {
        IsStarted = true;
        Logging.Log("Logger started!", "Logging");
    }

    /// <summary>Logs an ordinary line.</summary>
    /// <param name="message">What to log.</param>
    /// <param name="prefix">Shown in brackets before the line, usually the class logging; empty or null for none.</param>
    /// <param name="timestamp">Adds the system time after the prefix.</param>
    /// <param name="codeTrace">Adds the calling method, file and line (filled in by the compiler).</param>
    public static void Log(string message, string prefix, bool timestamp = true, bool codeTrace = false, [CallerLineNumber] int line = 0, [CallerMemberName] string caller = "", [CallerFilePath] string callerFile = "")
    {
        if (!IsStarted) return;
        string finalMessage = Format(message, prefix, timestamp, codeTrace, line, caller, callerFile);
        LimboConsole.Info(finalMessage);
        GD.Print(finalMessage);
    }

    /// <summary>Logs a warning: also shown in the editor's debugger as one. Parameters as for <see cref="Log"/>.</summary>
    public static void Warn(string message, string prefix, bool timestamp = true, bool codeTrace = false, [CallerLineNumber] int line = 0, [CallerMemberName] string callerMethod = "", [CallerFilePath] string callerFile = "")
    {
        if (!IsStarted) return;
        string finalMessage = Format(message, prefix, timestamp, codeTrace, line, callerMethod, callerFile);
        LimboConsole.Warn(finalMessage);
        GD.Print(finalMessage);
        GD.PushWarning(finalMessage);
    }

    /// <summary>Logs an error: also shown in the editor's debugger as one. Parameters as for <see cref="Log"/>.</summary>
    public static void Error(string message, string prefix, bool timestamp = true, bool codeTrace = false, [CallerLineNumber] int line = 0, [CallerMemberName] string callerMethod = "", [CallerFilePath] string callerFile = "")
    {
        if (!IsStarted) return;
        string finalMessage = Format(message, prefix, timestamp, codeTrace, line, callerMethod, callerFile);
        GD.Print(finalMessage);
        LimboConsole.Error(finalMessage);
        GD.PushError(finalMessage);
    }

    private static string Format(string message, string prefix, bool timestamp, bool codeTrace, int line, string caller, string callerFile)
    {
        string ts = "";
        if (timestamp) ts = $"[{Time.GetTimeStringFromSystem()}]";

        string customPrefix = "";
        if (prefix != "" && prefix != null) customPrefix = $"[{prefix}]";

        string trace = "";
        if (codeTrace) trace = $" [from {caller} in {callerFile.Substring(callerFile.IndexOf("scripts"))} at line {line}]";

        return customPrefix + ts + message + trace;
    }
}
