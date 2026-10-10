using Godot;
using System;
using System.Runtime.CompilerServices;
using Limbo;
using Limbo.Console.Sharp;

/// <summary>
/// Static logger class to keep things organized.
/// </summary>
public static class Logging
{
    //Todo: Logging to file should be a launch option probably
    /// <summary>
    /// compile time constant! Set to true if the logger should start a new logfile at game launch and write to it
    /// </summary>
    public const bool bSaveLogsToFile = false;

    /// <summary>
    /// Is true if Logger is functioning.
    /// </summary>
    public static bool IsStarted = false;
    private static FileAccess logFile = null;
    private static bool writeToFile = false;

    /// <summary>
    /// Starts up the logging engine. Logging functions don't work until after this called.
    /// </summary>
    public static void Start()
    {
        IsStarted = true;
        Logging.Log("Logger started!", "Logging");
    }

    /// <summary>
    /// Starts logging to file
    /// </summary>
    public static void StartLoggingToFile()
    {
        Logging.Log("SaveLogsToFile is enabled, writing logs to disk. (This will reduce performance!)", "[Logging]");
        string logName = $"[{DateTime.Now.Year}-{DateTime.Now.Month}-{DateTime.Now.Day}]--[{DateTime.Now.Hour};{DateTime.Now.Minute};{DateTime.Now.Second}].txt";
        string fileName = $"user://logs/{Global.steamid.ToString()}/{logName}";
        Logging.Log($"Starting log file at: {fileName}", "LoggingMeta");
        Logging.logFile = FileAccess.Open(fileName, FileAccess.ModeFlags.WriteRead);
        if (Logging.logFile == null)
        {
            GD.PushError($"Error creating log file: {FileAccess.GetOpenError().ToString()}");
        }
        Logging.Log($"Log file created succesfully.", "LoggingMeta");
        writeToFile = true;
    }

    /// <summary>
    /// Prints the message to both the ingame console and to the default Godot console using default formatting. Also logs to file if <see cref="bSaveLogsToFile"/>
    /// </summary>
    /// <param name="message">message to log</param>
    /// <param name="prefix">A custom prefix. Leave blank to remove prefix.</param>
    /// <param name="timestamp">If true a system timestamp is added to the message.</param>
    public static void Log(string message, string prefix, bool timestamp = true, bool codeTrace = false, [CallerLineNumber] int line = 0, [CallerMemberName] string caller = "", [CallerFilePath] string callerFile = "")
    {
        if (!IsStarted) return;
        string finalMessage = Format(message, prefix, timestamp, codeTrace, line, caller, callerFile);
        LimboConsole.Info(finalMessage);
        GD.Print(finalMessage);
        WriteToFile(finalMessage);
    }

    /// <summary>
    /// Prints the message to both the ingame console and to the default Godot console using warning formatting.
    /// </summary>
    /// <param name="message">message to log</param>
    /// <param name="prefix">A custom prefix. Leave blank to remove prefix.</param>
    /// <param name="timestamp">If true a system timestamp is added to the message.</param>
    public static void Warn(string message, string prefix, bool timestamp = true, bool codeTrace = false, [CallerLineNumber] int line = 0, [CallerMemberName] string callerMethod = "", [CallerFilePath] string callerFile = "")
    {
        if (!IsStarted) return;
        string finalMessage = Format(message, prefix, timestamp, codeTrace, line, callerMethod, callerFile);
        LimboConsole.Warn(finalMessage);
        GD.Print(finalMessage);
        GD.PushWarning(finalMessage);
        WriteToFile(finalMessage);
    }

    /// <summary>
    /// Prints the message to both the ingame console and to the default Godot console using error formatting.
    /// </summary>
    /// <param name="message">message to log</param>
    /// <param name="prefix">A custom prefix. Leave blank to remove prefix.</param>
    /// <param name="timestamp">If true a system timestamp is added to the message.</param>
    public static void Error(string message, string prefix, bool timestamp = true, bool codeTrace = false, [CallerLineNumber] int line = 0, [CallerMemberName] string callerMethod = "", [CallerFilePath] string callerFile = "")
    {
        if (!IsStarted) return;
        string finalMessage = Format(message, prefix, timestamp, codeTrace, line, callerMethod, callerFile);
        GD.Print(finalMessage);
        LimboConsole.Error(finalMessage);
        GD.PushError(finalMessage);
        WriteToFile(finalMessage);
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

    private static void WriteToFile(string finalMessage)
    {
        if (!writeToFile) return;
        if (logFile.StoreLine(finalMessage))
        {
            logFile.Flush(); //Flush the buffer to disk per line in case we crash. This probably incurs a non-trivial performance hit if you are logging a lot.
        }
        else
        {
            GD.PrintErr("ALERT! LOG TO FILE ERROR! Log file may be missing options!");
        }
    }
}
