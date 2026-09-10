using Godot;
using System;
using System.Collections.Generic;
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
    /// List of Log Prefixes (categories) to silence. Add logging categories to this to silence them - can also use the relevant console commands while the game is running to add or remove values
    /// </summary>
    //public static List<string> DefaultSilencedPrefixes = ["FirstTimeSetup", "LoggingMeta", "NetworkRelay", "NetworkSession", "GameSessionWire", "NetworkWire", "NetworkBandwidthTracker"];
    public static List<string> DefaultSilencedPrefixes = [];
    public static Dictionary<string, (bool silenced, int timesPrinted, int timesSilenced)> categories = new();

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
        foreach (string s in DefaultSilencedPrefixes)
        {
            categories[s] = (true, 0, 0);
        }
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
        bool release = false; //TODO turn on/off all logging
        if(release) return;
        if (!IsStarted) return;
        if (categories.TryGetValue(prefix,out var category))
        {
            if (category.silenced)
            {
                categories[prefix] = (category.silenced, category.timesPrinted, ++category.timesSilenced);
                return;
            }
            else
            {
                categories[prefix] = (category.silenced, ++category.timesPrinted, category.timesSilenced);
            }
        }
        else
        {
            categories[prefix] = (false, 1, 0);
        }

        string ts = "";
        if (timestamp) ts = $"[{Time.GetTimeStringFromSystem()}]";

        string customPrefix = "";
        if (prefix != "" && prefix != null) customPrefix = $"[{prefix}]";

        string trace = "";
        if (codeTrace) trace = $" [from {caller} in {callerFile.Substring(callerFile.IndexOf("scripts"))} at line {line}]";

        string finalMessage = customPrefix + ts + message + trace;

        LimboConsole.Info(finalMessage);
        GD.Print(finalMessage);
        if (writeToFile)
        {
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

    /// <summary>
    /// Prints the message to both the ingame console and to the default Godot console using warning formatting.
    /// </summary>
    /// <param name="message">message to log</param>
    /// <param name="prefix">A custom prefix. Leave blank to remove prefix.</param>
    /// <param name="timestamp">If true a system timestamp is added to the message.</param>
    public static void Warn(string message, string prefix, bool timestamp = true, bool codeTrace = false, [CallerLineNumber] int line = 0, [CallerMemberName] string callerMethod = "", [CallerFilePath] string callerFile = "")
    {
        if (!IsStarted) return;
        if (categories.TryGetValue(prefix, out var category))
        {
            if (category.silenced)
            {
                categories[prefix] = (category.silenced, category.timesPrinted, ++category.timesSilenced);
                return;
            }
            else
            {
                categories[prefix] = (category.silenced, ++category.timesPrinted, category.timesSilenced);
            }
        }
        else
        {
            categories[prefix] = (false, 1, 0);
        }

        string ts = "";
        if (timestamp) ts = $"[{Time.GetTimeStringFromSystem()}]";

        string customPrefix = "";
        if (prefix != "" && prefix != null) customPrefix = $"[{prefix}]";

        string trace = "";
        if (codeTrace) trace = $" [from {callerMethod} in {callerFile.Substring(callerFile.IndexOf("scripts"))} at line {line}]";

        string finalMessage = customPrefix + ts + message + trace;

        LimboConsole.Warn(finalMessage);
        GD.Print(finalMessage);
        GD.PushWarning(finalMessage);
        if (writeToFile)
        {
            if (logFile.StoreLine(finalMessage))
            {
                logFile.Flush(); //Flush the buffer to disk per line in case we crash.  This probably incurs a non-trivial performance hit if you are logging a lot.
            }
            else
            {
                GD.PrintErr("ALERT! LOG TO FILE ERROR! Log file may be missing options!");
            }
        }
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
        if (categories.TryGetValue(prefix, out var category))
        {
            categories[prefix] = (category.silenced, category.timesPrinted++, category.timesSilenced);
        }
        else
        {
            categories[prefix] = (false, 1, 0);
        }

        string ts = "";
        if (timestamp) ts = $"[{Time.GetTimeStringFromSystem()}]";

        string customPrefix = "";
        if (prefix != "" && prefix != null) customPrefix = $"[{prefix}]";

        string trace = "";
        if (codeTrace) trace = $" [from {callerMethod} in {callerFile.Substring(callerFile.IndexOf("scripts"))} at line {line}]";

        string finalMessage = customPrefix + ts + message + trace;

        GD.Print(finalMessage);
        LimboConsole.Error(finalMessage);
        GD.PushError(finalMessage);
        if (writeToFile)
        {
            if (logFile.StoreLine(finalMessage))
            {
                logFile.Flush(); //Flush the buffer to disk per line in case we crash.  This probably incurs a non-trivial performance hit if you are logging a lot.
            }
            else
            {
                GD.PrintErr("ALERT! LOG TO FILE ERROR! Log file may be missing options!");
            }
        }
    }

    public static void UnSilenceAllPrefixes()
    {
        foreach (var entry in categories)
        {
            categories[entry.Key] = (false,entry.Value.timesPrinted, entry.Value.timesSilenced);
        }
    }

    public static void ResetSilencedPrefixesToDefault()
    {
        foreach (var entry in categories)
        {
            if (DefaultSilencedPrefixes.Contains(entry.Key))
            {
                categories[entry.Key] = (true, entry.Value.timesPrinted, entry.Value.timesSilenced);
            }
            else
            {
                categories[entry.Key] = (false, entry.Value.timesPrinted, entry.Value.timesSilenced);
            }

        }
    }

    public static void SilencePrefix(string category)
    {
        categories[category] = (true, categories[category].timesPrinted, categories[category].timesSilenced);
    }

    public static void UnSilencePrefix(string category)
    {
        categories[category] = (false, categories[category].timesPrinted, categories[category].timesSilenced);
    }
}
