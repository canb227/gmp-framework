using Godot;
using Steamworks;
using System.Text;

// Steam variant of the LAN debug lobby UI. Structurally identical to LobbyLanDebug and
// driven by the same Lobby events — the ONLY differences are the transport (SteamNetwork
// instead of ENetNetwork) and that peers are addressed by SteamID: the "join" field
// takes a host SteamID rather than an ip:port. Self's peerID is our own SteamID.
public partial class LobbySteamDebug : Control
{
    private const int MaxPlayers = 6;

    private ulong _selfId;
    private string _selfName;
    private SteamNetwork _net;
    private bool _started;
    private int _autoTick;

    private Button _backButton;
    private Button _hostButton;
    private Button _joinButton;
    private LineEdit _idInput;      // host SteamID to join
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
        _selfId = Global.steamid;
        _selfName = Global.bIsSteamConnected ? SteamFriends.GetPersonaName() : $"Peer-{_selfId % 10000:0000}";
        if (string.IsNullOrEmpty(_selfName)) _selfName = $"Peer-{_selfId % 10000:0000}";

        _backButton = GetNode<Button>("%BackButton");
        _hostButton = GetNode<Button>("%HostButton");
        _joinButton = GetNode<Button>("%JoinButton");
        _idInput = GetNode<LineEdit>("%IpInput");
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
        _chatInput.TextSubmitted += _ => OnSendChat();


        Lobby.PeerConnectedEvent += OnPeerConnected;
        Lobby.PeerDisconnectedEvent += OnPeerDisconnected;
        Lobby.ConnectedToHostEvent += OnConnectedToHost;
        Lobby.DisconnectedFromHostEvent += OnDisconnectedFromHost;
        Lobby.LobbyMembersChangedEvent += RefreshRoster;
        Lobby.LobbyMessageEvent += OnLobbyMessage;

        _peerValue.Text = ShortId(_selfId);
        _stateValue.Text = "Disconnected";
        Log($"ready as {_selfName} (SteamID {_selfId})");

        CallDeferred(nameof(HandleCmdline));
    }

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
        StartHost();
    }

    private void OnJoinPressed()
    {
        if (_started) return;
        if (!ulong.TryParse(_idInput.Text.Trim(), out ulong hostId) || hostId == 0)
        {
            Log("enter a valid host SteamID to join");
            return;
        }
        StartJoin(hostId);
    }

    private void StartHost()
    {
        _net = new SteamNetwork();
        //AddChild(_net);
        Error err = Lobby.StartHost(_net, _selfId, _selfName);
        _started = true;
        LockControls();
        _stateValue.Text = err == Error.Ok ? "Hosting" : $"Error: {err}";
        _hostValue.Text = ShortId(_selfId) + " (you)";
        _lobbyIdValue.Text = ShortId(_selfId);
        Log($"hosting Steam lobby (SteamID {_selfId})");
        RefreshRoster();
    }

    private void StartJoin(ulong hostId)
    {
        _net = new SteamNetwork();
        //AddChild(_net);
        Lobby.StartJoin(_net, _selfId, _selfName);
        // A SteamID is directly dialable — no endpoint shim, unlike LAN.
        _net.Connect(hostId);
        _started = true;
        LockControls();
        _stateValue.Text = "Connecting…";
        Log($"joining host SteamID {hostId}");
        RefreshRoster();
    }

    private void LockControls()
    {
        _hostButton.Disabled = true;
        _joinButton.Disabled = true;
    }

    private void OnSendChat()
    {
        string text = _chatInput.Text;
        if (string.IsNullOrWhiteSpace(text) || !_started) return;
        _chatInput.Text = "";
        byte[] payload = Encoding.UTF8.GetBytes(text);
        Error err = Lobby.SendToAllExceptSelf(Channel.LOBBY_Chat, payload);
        AppendChat(_selfName + " (you)", text, "#4dc7d1");
        Log($"sent chat to {Lobby.members.Count} peer(s): \"{text}\" ({err})");
    }

    // ---- Lobby event handlers --------------------------------------------------

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
        if (ch != Channel.LOBBY_Chat) return;
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
        if (_playerRows == null) return;
        foreach (Node child in _playerRows.GetChildren())
            child.QueueFree();

        foreach (var m in Lobby.members.Values)
        {
            Node row = _rowScene.Instantiate();
            var nameLabel = row.GetNodeOrNull<Label>("PlayerRow1Box/NameLabel");
            var idLabel = row.GetNodeOrNull<Label>("PlayerRow1Box/PeerIDLabel");
            var badge = row.GetNodeOrNull<Control>("PlayerRow1Box/Badge");
            var avatarLabel = row.GetNodeOrNull<Label>("PlayerRow1Box/Avatar/AvatarCenter/AvatarLabel");

            string display = !string.IsNullOrEmpty(m.Name) ? m.Name : $"Peer {ShortId(m.PeerID)}";
            if (m.PeerID == Lobby.selfPeerID) display += " (you)";
            if (nameLabel != null) nameLabel.Text = display;
            if (idLabel != null) idLabel.Text = ShortId(m.PeerID);
            if (avatarLabel != null) avatarLabel.Text = display.Length > 0 ? display.Substring(0, 1).ToUpper() : "?";
            if (badge != null) badge.Visible = m.IsHost;

            _playerRows.AddChild(row);
        }
    }

    private void AppendChat(string who, string text, string color) =>
        _chatLog?.AppendText($"\n[color={color}]{who}:[/color] {text}");

    private void Log(string line)
    {
        _logText?.AppendText($"\n[color=#8c93a3][{Time.GetTimeStringFromSystem()}][/color] {line}");
        Logging.Log(line, "LobbySteamDebug");
    }

    // ---- command-line driving (single-instance host smoke test) ----------------
    //   -- --host            (host a Steam lobby)
    //   -- --join <steamid>  (join a host by SteamID)
    private void HandleCmdline()
    {
        string[] args = OS.GetCmdlineUserArgs();
        string name = null, join = null;
        bool host = false, auto = false;
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--name": name = Next(args, ref i); break;
                case "--host": host = true; break;
                case "--join": join = Next(args, ref i); break;
                case "--auto": auto = true; break;
            }
        }

        if (name != null) _selfName = name;

        if (host)
            StartHost();
        else if (join != null && ulong.TryParse(join, out ulong hostId))
        {
            _idInput.Text = join;
            StartJoin(hostId);
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
        if (!_started || Lobby.members.Count == 0) return;
        _autoTick++;
        byte[] payload = Encoding.UTF8.GetBytes($"auto #{_autoTick} from {_selfName}");
        Lobby.SendToAllExceptSelf(Channel.LOBBY_Chat, payload);
    }

    // ---- helpers ---------------------------------------------------------------

    private static string ShortId(ulong id) => (id % 100000).ToString("00000");

    private static string Next(string[] args, ref int i) => (i + 1 < args.Length) ? args[++i] : null;
}
