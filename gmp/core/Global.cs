using Godot;
using Steamworks;
using System;
using System.Runtime.InteropServices;

public partial class Global : Node
{

    public static Global instance;
    public static ulong steamid = 0;
    public static bool bIsSteamConnected = false;
    public const int APP_ID = 480;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;
        instance = this;
        switch (OS.GetName())
        {
            case "Windows":
                break;
            case "Linux":
                RegisterSteamApiResolver();
                break;
            default:
                break;
        }

        SteamInit();
        if (steamid==1)
        {
            //steam connection failed - rn we bail but eventually adding steamid==1 checks to places will allow for offline mode
            GetTree().Quit();
        }

        Logging.Start(); //Also start logging here instead of Main so its ready super early to log stuff

        //This makes sure the file structure in the godot user data folder is setup correctly
        Logging.Log($" mkdir user://saves                    | {DirAccess.MakeDirAbsolute("user://saves").ToString()}", "FirstTimeSetup");
        Logging.Log($" mkdir user://config                  | {DirAccess.MakeDirAbsolute("user://config").ToString()}", "FirstTimeSetup");
        Logging.Log($" mkdir user://logs                      | {DirAccess.MakeDirAbsolute("user://logs").ToString()}", "FirstTimeSetup");
        Logging.Log($" mkdir user://saves/{Global.steamid}   | {DirAccess.MakeDirAbsolute("user://saves/" + Global.steamid).ToString()}", "FirstTimeSetup");
        Logging.Log($" mkdir user://config/{Global.steamid} | {DirAccess.MakeDirAbsolute("user://config/" + Global.steamid).ToString()}", "FirstTimeSetup");
        Logging.Log($" mkdir user://logs/{Global.steamid}     | {DirAccess.MakeDirAbsolute("user://logs/" + Global.steamid).ToString()}", "FirstTimeSetup");

        if (Logging.bSaveLogsToFile)
        {
            Logging.StartLoggingToFile();
        }

        Logging.Log("Connection to Steam successful.", "SteamAPI");
        Logging.Log($"Steam ID: {steamid}", "SteamAPI");
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }

    public bool SteamInit()
    {
        GD.Print("Initializing Steam API...");

        try
        {
            //SteamAPI call that checks if steam is running in the background. If it is not, it starts steam then starts the game once steam is started.
            //We close the game so we don't end up with two instances open.

            if (SteamAPI.RestartAppIfNecessary((AppId_t)APP_ID)) //ALWAYS RETURNS FALSE IF app_id.txt IS PRESENT IN ROOT FOLDER
            {
                GD.PushError("Steam is not running. Starting Steam then relaunching game");
                GetTree().Quit();
            }

            if (SteamAPI.Init())
            {
                //Not technically needed, but gets our steam relay connection started up as soon as possible - there can be up to a three second delay from first networking API call
                SteamNetworkingUtils.InitRelayNetworkAccess();

                steamid = SteamUser.GetSteamID().m_SteamID;
                bIsSteamConnected = true;
            }
            else
            {
                GD.PushError("Steam not initialized");
                steamid = 1;
                return false;
            }
        }
        catch (System.DllNotFoundException)
        {
            //nothing works if the steam dll for our OS isn't present. We only support Windows at the moment.

            GD.PushError("steam_api64.dll not found. steam_api64.dll is expected in the game root folder.");
            OS.Alert("steam_api64.dll not found. steam_api64.dll is expected in the game root folder.");
            throw;
        }


        return true;
    }

    private void RegisterSteamApiResolver()
    {
        NativeLibrary.SetDllImportResolver(typeof(SteamAPI).Assembly, (libraryName, assembly, searchPath) =>
        {
            if (libraryName == "steam_api" || libraryName == "libsteam_api")
            {
                var path = ProjectSettings.GlobalizePath("res://lib/linux/libsteam_api.so");
                return NativeLibrary.Load(path);
            }

            return IntPtr.Zero;
        });
    }
}
