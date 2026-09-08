using Godot;
using Steamworks;
using System;
using System.Linq;
using System.Text;

// Which transport this lobby instance drives. Set by MainMenu (button or --steam
// cmdline) into LobbyDebug.NextMode before the scene is loaded.
public enum LobbyMode { Lan, Steam }

// Single debug lobby UI for both transports. It is a thin view over Lobby's C# events:
// it never touches the network directly beyond creating the transport, asking Lobby to
// host/join, and dialing the host. All roster/mesh logic lives in Lobby; all wire
// transport lives in ENetNetwork / SteamNetwork.
//
// Everything transport-specific is confined to five spots, each a single switch on
// _mode: BuildIdentity, CreateNetwork, ApplyModeChrome, JoinHost, HandleCmdline.
public partial class LobbyDebug : Control
{
    // Set by the caller (MainMenu) before ChangeSceneToFile; read once in _Ready.
    public static LobbyMode NextMode = LobbyMode.Lan;

    private const int MaxPlayers = 6;

    private LobbyMode _mode;
    private ulong _selfId;
    private string _selfName;
    private int _listenPort;
    private Network _net;
    private bool _started;
    private int _autoTick;

    // Cached nodes.
    private Button _backButton;
    private Button _hostButton;
    private Button _joinButton;
    private Button _startButton;
    private Label _titleLabel;
    private LineEdit _joinInput;   // IP (LAN) or host SteamID (Steam)
    private LineEdit _portInput;   // LAN only; hidden in Steam mode
    private Label _stateValue;
    private Label _peerValue;
    private Label _lobbyIdValue;
    private Label _hostValue;
    private Label _playersValue;
    private RichTextLabel _logText;
    private VBoxContainer _playerRows;
    private RichTextLabel _chatLog;
    private LineEdit _chatInput;
    private Button _sendButton;
    private PackedScene _rowScene;
    private OptionButton _levelSelect;

    public override void _Ready()
    {
        _mode = NextMode;
        BuildIdentity();

        _backButton = GetNode<Button>("%BackButton");
        _hostButton = GetNode<Button>("%HostButton");
        _joinButton = GetNode<Button>("%JoinButton");
        _startButton = GetNode<Button>("%StartGameButton");
        _titleLabel = GetNode<Label>("%TitleLabel");
        _joinInput = GetNode<LineEdit>("%JoinInput");
        _portInput = GetNode<LineEdit>("%PortInput");
        _stateValue = GetNode<Label>("%StateValue");
        _peerValue = GetNode<Label>("%PeerValue");
        _lobbyIdValue = GetNode<Label>("%LobbyIdValue");
        _hostValue = GetNode<Label>("%HostValue");
        _playersValue = GetNode<Label>("%PlayersValue");
        _levelSelect = GetNode<OptionButton>("%LevelOption");

        _logText = GetNode<RichTextLabel>("Margin/RootVBox/Body/LeftPanel/LeftContent/EventLogSection/LogPanel/LogText");
        _playerRows = GetNode<VBoxContainer>("Margin/RootVBox/Body/CenterColumn/PlayerSection/PlayerListPanel/PlayerScroll/PlayerRows");
        _chatLog = GetNode<RichTextLabel>("Margin/RootVBox/Body/CenterColumn/ChatSection/ChatPanel/ChatVBox/ChatOutputPanel/ChatLog");
        _chatInput = GetNode<LineEdit>("Margin/RootVBox/Body/CenterColumn/ChatSection/ChatPanel/ChatVBox/ChatInputRow/ChatInput");
        _sendButton = GetNode<Button>("Margin/RootVBox/Body/CenterColumn/ChatSection/ChatPanel/ChatVBox/ChatInputRow/SendButton");
        _rowScene = GD.Load<PackedScene>("res://gmp/ui/player_row.tscn");

        ApplyModeChrome();

        _backButton.Pressed += OnBackPressed;
        _hostButton.Pressed += OnHostPressed;
        _joinButton.Pressed += OnJoinPressed;
        _sendButton.Pressed += OnSendChat;
        _startButton.Pressed += OnStartGamePressed;
        _levelSelect.ItemSelected += _levelSelect_ItemSelected;
        _chatInput.TextSubmitted += _ => OnSendChat();

        // Subscribe to Lobby (an autoload) events.
        Lobby.PeerConnectedEvent += OnPeerConnected;
        Lobby.PeerDisconnectedEvent += OnPeerDisconnected;
        Lobby.ConnectedToHostEvent += OnConnectedToHost;
        Lobby.DisconnectedFromHostEvent += OnDisconnectedFromHost;
        Lobby.LobbyMembersChangedEvent += RefreshRoster;
        Lobby.LobbyGameInfoChangedEvent += RefreshGameInfo;
        Lobby.LobbyDoneLoadingEvent += DoneLoading;

        // Chat (LOBBY_Chat) is not surfaced by Lobby's LobbyMessageEvent — it is read
        // straight off the transport, wired in StartHost/StartJoin once _net exists.

        _peerValue.Text = ShortId(_selfId);
        _stateValue.Text = "Disconnected";

        foreach (var item in GameResources.LevelsList)
        {
            _levelSelect.AddItem(item.levelName);
        }
        _levelSelect_ItemSelected(1);
        Log($"lobby ready ({_mode}) as {_selfName} ({ShortId(_selfId)})");



        CallDeferred(nameof(HandleCmdline));
    }

    private void _levelSelect_ItemSelected(long index)
    {
        Lobby.gameInfo.levelIdx = (int)index;
        Lobby.SendGameInfoUpdate();
    }

    public override void _ExitTree()
    {
        Lobby.PeerConnectedEvent -= OnPeerConnected;
        Lobby.PeerDisconnectedEvent -= OnPeerDisconnected;
        Lobby.ConnectedToHostEvent -= OnConnectedToHost;
        Lobby.DisconnectedFromHostEvent -= OnDisconnectedFromHost;
        Lobby.LobbyMembersChangedEvent -= RefreshRoster;
        Lobby.LobbyGameInfoChangedEvent -= RefreshGameInfo;
        Lobby.LobbyDoneLoadingEvent -= DoneLoading;
        if (_net != null)
            _net.MessageReceivedEvent -= OnLobbyMessage;
    }

    // ---- transport seam (the only places _mode matters) ----------------------

    private void BuildIdentity()
    {
        if (_mode == LobbyMode.Steam)
        {
            _selfId = Global.steamid;
            _selfName = Global.bIsSteamConnected ? SteamFriends.GetPersonaName() : "";
            if (string.IsNullOrEmpty(_selfName))
                _selfName = $"Peer-{_selfId % 10000:0000}";
        }
        else
        {
            _selfId = NewPeerId();
            _selfName = $"Peer-{_selfId % 10000:0000}";
        }
    }

    private Network CreateNetwork(int listenPort) => _mode switch
    {
        LobbyMode.Steam => new SteamNetwork(),
        _ => new ENetNetwork(_selfId, listenPort),
    };

    // Set the transport-specific labels/visibility on the shared scene.
    private void ApplyModeChrome()
    {
        if (_mode == LobbyMode.Steam)
        {
            _titleLabel.Text = "Lobby Debug (Steam)";
            _joinButton.Text = "Join ID:";
            _joinInput.Text = "";
            _joinInput.PlaceholderText = "HOST STEAMID";
            _portInput.Visible = false;
        }
        else
        {
            _titleLabel.Text = "Lobby Debug (LAN)";
            _joinButton.Text = "Join to:";
            _joinInput.PlaceholderText = "IP ADDRESS";
            _portInput.Visible = true;
        }
    }

    // Stand up the joiner transport and dial the host.
    //   LAN:   target is an IP,          hostPort is the host's listen port, and
    //          listenPort is our own bind port.
    //   Steam: target is a host SteamID; hostPort and listenPort are ignored.
    private void JoinHost(string target, int hostPort, int listenPort)
    {
        _listenPort = _mode == LobbyMode.Steam ? 0 : listenPort;
        StartJoin();
        if (_mode == LobbyMode.Steam)
        {
            ulong.TryParse(target, out ulong hostId);
            _net.Connect(hostId);
            Log($"joining host SteamID {hostId}");
        }
        else
        {
            // The raw endpoint dial is LAN-specific and belongs to the transport.
            ((ENetNetwork)_net).ConnectByEndpoint(target, hostPort);
            Log($"joining {target}:{hostPort} (my listen port {_listenPort})");
        }
    }

    // ---- button handlers ----------------------------------------------------

    private void OnHostPressed()
    {
        if (_started) return;
        int port = _mode == LobbyMode.Steam ? 0 : ParsePort(_portInput.Text, 2272);
        StartHost(port);
    }

    private void OnJoinPressed()
    {
        if (_started) return;

        if (_mode == LobbyMode.Steam)
        {
            string id = _joinInput.Text.Trim();
            if (!ulong.TryParse(id, out ulong hostId) || hostId == 0)
            {
                Log("enter a valid host SteamID to join");
                return;
            }
            JoinHost(id, 0, 0);
        }
        else
        {
            string ip = string.IsNullOrWhiteSpace(_joinInput.Text) ? "127.0.0.1" : _joinInput.Text;
            // No "my port" field in the scene: pick a distinct high port so several
            // instances can coexist on localhost.
            JoinHost(ip, ParsePort(_portInput.Text, 2272), 9000 + (int)(GD.Randi() % 1000));
        }
    }

    private void OnStartGamePressed()
    {
        Lobby.SendStartGame();
    }

    // Return to the main menu, resetting all lobby/network state so the next lobby
    // starts clean. Unsubscribing from Lobby's events happens in _ExitTree.
    private void OnBackPressed()
    {
        Lobby.LeaveLobby();
        GetTree().ChangeSceneToFile("res://gmp/ui/main_menu.tscn");
    }

    // ---- host / join --------------------------------------------------------

    private void StartHost(int port)
    {
        _listenPort = port;
        _net = CreateNetwork(port);
        Error err = Lobby.StartHost(_net, _selfId, _selfName);
        _net.MessageReceivedEvent += OnLobbyMessage;
        _started = true;
        _hostButton.Disabled = true;
        _joinButton.Disabled = true;
        _stateValue.Text = err == Error.Ok ? "Hosting" : $"Error: {err}";
        _hostValue.Text = ShortId(_selfId) + " (you)";
        _lobbyIdValue.Text = ShortId(_selfId);
        Log($"hosting ({_mode}) as {_selfName}" + (_mode == LobbyMode.Steam ? "" : $" on port {port}"));
        RefreshRoster();
    }

    private void StartJoin()
    {
        _net = CreateNetwork(_listenPort);
        Lobby.StartJoin(_net, _selfId, _selfName);
        _net.MessageReceivedEvent += OnLobbyMessage;
        _started = true;
        _hostButton.Disabled = true;
        _joinButton.Disabled = true;
        _stateValue.Text = "Connecting…";
        RefreshRoster();
    }

    private void OnSendChat()
    {
        string text = _chatInput.Text;
        if (string.IsNullOrWhiteSpace(text) || !_started) return;
        _chatInput.Text = "";
        byte[] payload = Encoding.UTF8.GetBytes(text);
        // Includes self: the transport loops our own line back through OnLobbyMessage
        // (ENetNetwork.Send / SteamNetwork.Send self-branch), so no local echo here.
        Error err = Lobby.SendToAllAndSelf(Channel.LOBBY_Chat, payload);
        Log($"sent chat to {Lobby.members.Count} peer(s): \"{text}\" ({err})");
    }

    // ---- Lobby event handlers ---------------------------------------------

    private void OnPeerConnected(ulong peerID)
    {
        Log($"peer connected: {ShortId(peerID)}");
        _stateValue.Text = "Connected";
    }

    private void OnPeerDisconnected(ulong peerID) => Log($"peer disconnected: {ShortId(peerID)}");

    private void OnConnectedToHost(ulong hostID)
    {
        _stateValue.Text = "Connected";
        _hostValue.Text = ShortId(hostID);
        _lobbyIdValue.Text = ShortId(hostID);
        Log($"connected to host {ShortId(hostID)}");
    }

    private void OnDisconnectedFromHost(ulong hostID)
    {
        _stateValue.Text = "Disconnected";
        Log($"lost host {ShortId(hostID)}");
    }

    private void OnLobbyMessage(ulong from, Channel ch, byte[] msg)
    {
        if (ch != Channel.LOBBY_Chat)
            return;
        string text = Encoding.UTF8.GetString(msg);
        string name = Lobby.members.TryGetValue(from, out var m) && !string.IsNullOrEmpty(m.Name)
            ? m.Name : ShortId(from);
        AppendChat(name, text, "#e0e4ec");
        Log($"recv chat from {name}: \"{text}\"");
    }

    private void RefreshGameInfo()
    {
        // GameInfo (level/mode/max players) changed on the wire. Nothing to render yet.
        _levelSelect.Select(Lobby.gameInfo.levelIdx);
    }

    // Every peer has finished loading the game world — drop the lobby UI.
    private void DoneLoading()
    {
        Hide();
    }

    // ---- rendering --------------------------------------------------------

    private void RefreshRoster()
    {
        _playersValue.Text = $"{Lobby.MemberCount} / {MaxPlayers}";
        if (_playerRows == null)
            return;
        foreach (Node child in _playerRows.GetChildren())
            child.QueueFree();

        foreach (var m in Lobby.members.Values.OrderBy(m => m.Name))
        {
            Node row = _rowScene.Instantiate();
            var nameLabel = row.GetNodeOrNull<Label>("PlayerRow1Box/NameLabel");
            var idLabel = row.GetNodeOrNull<Label>("PlayerRow1Box/PeerIDLabel");
            var badge = row.GetNodeOrNull<Control>("PlayerRow1Box/Badge");
            var badgeLabel = row.GetNodeOrNull<Label>("PlayerRow1Box/Badge/BadgeLabel");
            var avatarLabel = row.GetNodeOrNull<Label>("PlayerRow1Box/Avatar/AvatarCenter/AvatarLabel");

            string display = !string.IsNullOrEmpty(m.Name) ? m.Name : $"Peer {ShortId(m.PeerID)}";
            if (m.PeerID == Lobby.selfPeerID) display += " (you)";
            if (nameLabel != null) nameLabel.Text = display;
            if (idLabel != null) idLabel.Text = ShortId(m.PeerID);
            if (avatarLabel != null) avatarLabel.Text = display.Length > 0 ? display.Substring(0, 1).ToUpper() : "?";
            if (badge != null) badge.Visible = m.IsHost;
            if (badgeLabel != null) badgeLabel.Text = "HOST";

            _playerRows.AddChild(row);
        }
    }

    private void AppendChat(string who, string text, string color)
    {
        _chatLog?.AppendText($"\n[color={color}]{who}:[/color] {text}");
    }

    private void Log(string line)
    {
        _logText?.AppendText($"\n[color=#8c93a3][{Time.GetTimeStringFromSystem()}][/color] {line}");
        Logging.Log(line, "LobbyDebug");
    }

    // ---- command-line driving --------------------------------------------
    //   LAN:   -- --name A --host 2272 --auto
    //          -- --name B --join 127.0.0.1:2272 --port 9101 --auto
    //   Steam: -- --steam --name A --host
    //          -- --steam --name B --join <steamid>
    private void HandleCmdline()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string name = null, host = null, join = null, port = null;
        bool hostFlag = false, auto = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name": name = Next(args, ref i); break;
                case "--host":
                    if (_mode == LobbyMode.Steam) hostFlag = true;
                    else host = Next(args, ref i);
                    break;
                case "--join": join = Next(args, ref i); break;
                case "--port": port = Next(args, ref i); break;
                case "--auto": auto = true; break;
            }
        }

        if (name != null)
        {
            _selfName = name;
            _peerValue.Text = ShortId(_selfId);
        }

        if (_mode == LobbyMode.Steam)
        {
            if (hostFlag)
                StartHost(0);
            else if (join != null && ulong.TryParse(join, out _))
            {
                _joinInput.Text = join;
                JoinHost(join, 0, 0);
            }
        }
        else
        {
            if (host != null && int.TryParse(host, out int hp))
            {
                _portInput.Text = host;
                StartHost(hp);
            }
            else if (join != null)
            {
                string[] parts = join.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[1], out int jp))
                {
                    _joinInput.Text = parts[0];
                    _portInput.Text = parts[1];
                    int listen = int.TryParse(port, out int mp) ? mp : 9000 + (int)(GD.Randi() % 1000);
                    JoinHost(parts[0], jp, listen);
                }
            }
        }

        if (auto)
        {
            var timer = new Timer { WaitTime = 2.0, Autostart = true };
            timer.Timeout += AutoChat;
            AddChild(timer);
        }
    }

    private void AutoChat()
    {
        if (!_started || Lobby.members.Count == 0)
            return;
        _autoTick++;
        byte[] payload = Encoding.UTF8.GetBytes($"auto #{_autoTick} from {_selfName}");
        Lobby.SendToAllExceptSelf(Channel.LOBBY_Chat, payload);
        Log($"auto-sent #{_autoTick} to {Lobby.members.Count} peer(s)");
    }

    // ---- helpers --------------------------------------------------------

    private static ulong NewPeerId()
    {
        ulong id = ((ulong)GD.Randi() << 20) | (GD.Randi() & 0xFFFFF);
        return id == 0 ? 1 : id;
    }

    private static string ShortId(ulong id) => (id % 100000).ToString("00000");

    private static int ParsePort(string s, int fallback) => int.TryParse(s, out int p) && p > 0 ? p : fallback;

    private static string Next(string[] args, ref int i) => (i + 1 < args.Length) ? args[++i] : null;
}
