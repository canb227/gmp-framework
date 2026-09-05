using Godot;
using System;
using System.Linq;
using System.Text;

// Drives the LobbyLANDebug scene. It is a thin view over Lobby's C# events: it never
// touches the network directly beyond creating the LAN transport and asking Lobby to
// host/join. All roster/mesh logic lives in Lobby; all wire transport lives in
// ENetNetwork.
public partial class LobbyLanDebug : Control
{
    private const int MaxPlayers = 6;

    private ulong _selfId;
    private string _selfName;
    private int _listenPort;
    private ENetNetwork _net;
    private bool _started;
    private int _autoTick;

    // Cached nodes.
    private Button _backButton;
    private Button _hostButton;
    private Button _joinButton;
    private Button _startButton;
    private LineEdit _ipInput;
    private LineEdit _portInput;
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

    public override void _Ready()
    {
        _selfId = NewPeerId();
        _selfName = $"Peer-{_selfId % 10000:0000}";

        _backButton = GetNode<Button>("%BackButton");
        _hostButton = GetNode<Button>("%HostButton");
        _joinButton = GetNode<Button>("%JoinButton");
        _startButton = GetNode<Button>("%StartGameButton");
        _ipInput = GetNode<LineEdit>("%IpInput");
        _portInput = GetNode<LineEdit>("%PortInput");
        _stateValue = GetNode<Label>("%StateValue");
        _peerValue = GetNode<Label>("%PeerValue");
        _lobbyIdValue = GetNode<Label>("%LobbyIdValue");
        _hostValue = GetNode<Label>("%HostValue");
        _playersValue = GetNode<Label>("%PlayersValue");

        _logText = GetNode<RichTextLabel>("Margin/RootVBox/Body/LeftPanel/LeftContent/EventLogSection/LogPanel/LogText");
        _playerRows = GetNode<VBoxContainer>("Margin/RootVBox/Body/CenterColumn/PlayerSection/PlayerListPanel/PlayerScroll/PlayerRows");
        _chatLog = GetNode<RichTextLabel>("Margin/RootVBox/Body/CenterColumn/ChatSection/ChatPanel/ChatVBox/ChatOutputPanel/ChatLog");
        _chatInput = GetNode<LineEdit>("Margin/RootVBox/Body/CenterColumn/ChatSection/ChatPanel/ChatVBox/ChatInputRow/ChatInput");
        _sendButton = GetNode<Button>("Margin/RootVBox/Body/CenterColumn/ChatSection/ChatPanel/ChatVBox/ChatInputRow/SendButton");
        _rowScene = GD.Load<PackedScene>("res://gmp/ui/player_row.tscn");

        _backButton.Pressed += OnBackPressed;
        _hostButton.Pressed += OnHostPressed;
        _joinButton.Pressed += OnJoinPressed;
        _sendButton.Pressed += OnSendChat;
        _startButton.Pressed += _startButton_Pressed;
        _chatInput.TextSubmitted += _ => OnSendChat();

        // Subscribe to Lobby (an autoload) events.

        Lobby.PeerConnectedEvent += OnPeerConnected;
        Lobby.PeerDisconnectedEvent += OnPeerDisconnected;
        Lobby.ConnectedToHostEvent += OnConnectedToHost;
        Lobby.DisconnectedFromHostEvent += OnDisconnectedFromHost;
        Lobby.LobbyMembersChangedEvent += RefreshRoster;
        Lobby.LobbyGameInfoChangedEvent += RefreshGameInfo;
        Lobby.LobbyDoneLoadingEvent += DoneLoading;

        _peerValue.Text = ShortId(_selfId);
        _stateValue.Text = "Disconnected";
        Logging.Log($"Lobby initialized as {_selfName} (peer {_selfId})", "Lobby");

        CallDeferred(nameof(HandleCmdline));
    }

    private void DoneLoading()
    {
        //Logging.Log("All players have loaded the game. Lobby dropping UI and showing game world", "Lobby");
        //throw new NotImplementedException();
        this.Hide();
    }

    private void RefreshGameInfo()
    {
       // throw new NotImplementedException();
    }

    private void _startButton_Pressed()
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

    public override void _ExitTree()
    {
        Lobby.PeerConnectedEvent -= OnPeerConnected;
        Lobby.PeerDisconnectedEvent -= OnPeerDisconnected;
        Lobby.ConnectedToHostEvent -= OnConnectedToHost;
        Lobby.DisconnectedFromHostEvent -= OnDisconnectedFromHost;
        Lobby.LobbyMembersChangedEvent -= RefreshRoster;
        Lobby.LobbyMessageEvent -= OnLobbyMessage;
    }

    // ---- button handlers -------------------------------------------------------

    private void OnHostPressed()
    {
        if (_started) return;
        int port = ParsePort(_portInput.Text, 2272);
        _listenPort = port;
        StartHost(port);
    }

    private void OnJoinPressed()
    {
        if (_started) return;
        string ip = string.IsNullOrWhiteSpace(_ipInput.Text) ? "127.0.0.1" : _ipInput.Text;
        int hostPort = ParsePort(_portInput.Text, 2272);
        // No "my port" field in the scene: pick a distinct high port so several
        // instances can coexist on localhost.
        _listenPort = 9000 + (int)(GD.Randi() % 1000);
        StartJoin(ip, hostPort);
    }

    private void StartHost(int port)
    {
        _net = new ENetNetwork(_selfId, port);
        //AddChild(_net);
        Lobby.StartHost(_net, _selfId, _selfName);
        Lobby.network.MessageReceivedEvent += OnLobbyMessage;
        _started = true;
        _hostButton.Disabled = true;
        _joinButton.Disabled = true;
        _stateValue.Text = "Hosting";
        _hostValue.Text = ShortId(_selfId) + " (you)";
        _lobbyIdValue.Text = ShortId(_selfId);
        Logging.Log($"hosting on port {port}", "Lobby");
        RefreshRoster();
    }

    private void StartJoin(string ip, int hostPort)
    {
        _net = new ENetNetwork(_selfId, _listenPort);
        //AddChild(_net);
        Lobby.StartJoin(_net, _selfId, _selfName);
        Lobby.network.MessageReceivedEvent += OnLobbyMessage;
        // The raw endpoint dial of the host is LAN-specific and belongs to the
        // transport, not the Lobby — so we do it here against the concrete ENet net.
        _net.ConnectByEndpoint(ip, hostPort);
        _started = true;
        _hostButton.Disabled = true;
        _joinButton.Disabled = true;
        _stateValue.Text = "Connecting…";
        Logging.Log($"joining {ip}:{hostPort} (my listen port {_listenPort})", "Lobby");
        RefreshRoster();
    }

    private void OnSendChat()
    {
        string text = _chatInput.Text;
        if (string.IsNullOrWhiteSpace(text) || !_started) return;
        _chatInput.Text = "";
        byte[] payload = Encoding.UTF8.GetBytes(text);
        Error err = Lobby.SendToAllAndSelf(Channel.LOBBY_Chat, payload);
        //AppendChat(_selfName + " (you)", text, "#4dc7d1");
        //Log($"sent chat to {Lobby.members.Count} peer(s): \"{text}\" ({err})");
    }

    // ---- Lobby event handlers --------------------------------------------------

    private void OnPeerConnected(ulong peerID)
    {
        Logging.Log($"peer connected: {ShortId(peerID)}", "Lobby");
        _stateValue.Text = "Connected";
    }

    private void OnPeerDisconnected(ulong peerID)
    {
        Logging.Log($"peer disconnected: {ShortId(peerID)}", "Lobby");
    }

    private void OnConnectedToHost(ulong hostID)
    {
        _stateValue.Text = "Connected";
        _hostValue.Text = ShortId(hostID);
        _lobbyIdValue.Text = ShortId(hostID);
        Logging.Log($"connected to host {ShortId(hostID)}", "Lobby");
    }

    private void OnDisconnectedFromHost(ulong hostID)
    {
        _stateValue.Text = "Disconnected";
        Logging.Log($"lost host {ShortId(hostID)}", "Lobby");
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

    // ---- rendering -------------------------------------------------------------

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
        string stamped = $"[color=#8c93a3][{Time.GetTimeStringFromSystem()}][/color] {line}";
        _logText?.AppendText("\n" + stamped);
        Logging.Log(line, "LobbyLanDebug");
    }

    // ---- command-line driving for the 4-instance verification ------------------
    //   -- --name A --host 2272 --auto
    //   -- --name B --join 127.0.0.1:2272 --port 9101 --auto
    private void HandleCmdline()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string name = null, host = null, join = null, port = null;
        bool auto = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name": name = Next(args, ref i); break;
                case "--host": host = Next(args, ref i); break;
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

        if (host != null && int.TryParse(host, out int hp))
        {
            _portInput.Text = host;
            _listenPort = hp;
            StartHost(hp);
        }
        else if (join != null)
        {
            string[] parts = join.Split(':');
            _listenPort = int.TryParse(port, out int mp) ? mp : 9000 + (int)(GD.Randi() % 1000);
            if (parts.Length == 2 && int.TryParse(parts[1], out int jp))
            {
                _ipInput.Text = parts[0];
                _portInput.Text = parts[1];
                StartJoin(parts[0], jp);
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

    // ---- helpers ---------------------------------------------------------------

    private static ulong NewPeerId()
    {
        ulong id = ((ulong)GD.Randi() << 20) | (GD.Randi() & 0xFFFFF);
        return id == 0 ? 1 : id;
    }

    private static string ShortId(ulong id) => id.ToString();

    private static int ParsePort(string s, int fallback) => int.TryParse(s, out int p) && p > 0 ? p : fallback;

    private static string Next(string[] args, ref int i) => (i + 1 < args.Length) ? args[++i] : null;
}
