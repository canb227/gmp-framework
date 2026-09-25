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

    // --test mode fields
    private bool _testMode;
    private int _expectPeers;
    private double _testTimeout = 30.0;
    private double _testElapsed;
    private bool _testDone;
    private bool _testPassed;
    private double _testPassedAt;
    private const double TestGracePeriod = 3.0;
    private int _peakMembers;
    private readonly System.Collections.Generic.HashSet<ulong> _chatReceivedFrom = new();
    private bool _testGame;               // --test-game: after the lobby test passes, host starts the game
    private double _testGameSeconds = 20.5; // how long to run in-game before evaluating (the sprint step ends at 20.3)
    private bool _gameStartSent;
    private bool _gameLoaded;
    private double _gameLoadedAt;
    // --test-game scripted multiplayer scenario (times are seconds after load):
    //   0.5  host spawns the claim-test cube (fixed id so every peer can find it)
    //   2.0  every peer presses the museum button at once  -> arbitration must accept exactly one
    //   3.0  every NON-host peer claims the cube at once   -> exactly one non-host winner everywhere
    //   4.0  host alone presses the button                 -> button ends 2 presses; each spawned one ore (host)
    //   8.0  first joiner pulls the spawn lever; 10.0 host pushes it back -> ore streams out meanwhile, then stops
    //   5.0  every peer builds a test block in the same cell -> exactly one structure everywhere, losers refunded
    //   6.0  snapshot structure / occupied-cell counts; check that a 2x1x1 aimed at the block's -X face
    //        snaps to the two cells west of it
    //   6.5  host deconstructs it                          -> grid empty everywhere, blueprint back to host
    //   7.0  host tries to build a block straddling a level wall -> refused for level-geometry overlap, refunded
    //   7.3  host builds a block resting on the floor      -> allowed (flush contact isn't an overlap); stays built
    //   1.0  host spawns a grinder; 3.0 host drops a test cube into its hopper -> a test sphere comes out the front
    //   all  the museum's preplaced conveyor line carries its cube up, round and into a void box -> despawned everywhere
    //   8.5  host scrolls its hotbar every frame for 3.5 s with forced GCs  -> no crash (regression)
    //   2.0  host tries to pick up the museum's iron ore -> refused (ore can't enter inventories)
    //   all  the jumping chunk hops on its own; the cold chunk lands on the hot one -> both report the touch
    //  12.3  first joiner spawns 3 chunks in front of the host; 13 host holds its magnet rod
    //  15.5  every peer: the chunks float in the host's ball, host is their authority and holder
    //  16.0  host lets go -> chunks released everywhere
    //  16.5  host grabs a featherweight chunk; 17.0-17.5 at rest it must sit still at the grab point (the old
    //        grab made light items oscillate wildly); from 17.5 the host turns, and from 18.0 it must track the point
    //  15.5  (host) the HUD shows "Magnet Rod" as the held item's name
    //  19.6  host walks forward holding sprint until 20.3 -> reaches sprint speed
    private const ulong TestCubeId = 0x7E57C0BE;
    private const string TestCubeScene = "res://game/dev/items/test_1x1x1cube.tscn";
    private const string SeedItemId = "blueprint_test_block";
    private static readonly Vector3I TestBuildCell = new(40, 10, 40); // in the air, clear of the level
    private string _buildSnapshot = "none";
    private bool _snapOk;
    private bool _embeddedAttempted;
    private static readonly Vector3I TestGrinderCell = new(-14, 0, -12);
    private const string DemoCubeName = "ConveyorDemoCube";
    private bool _demoCubeSeen;
    private int _grinderSubStep;
    private int _resourceSubStep;
    private int _leverSubStep;
    private int _altSubStep;
    private string _altSnapshot = "none";
    private bool _ghostAltOk;
    private int _buttonSpawned = -1, _leverSpawned = -1;
    private Vector3 _jumperStart;
    private float _jumperMaxMove;
    private bool _jumperSeenAsleep, _jumperSeenAwake;
    private string _magnetSnapshot = "none";
    private float _grabRestError = -1f, _grabRestSpeed;
    private string _hudHeldName = "none";
    private float _sprintMaxSpeed;
    private float _grabMaxError = -1f, _grabMaxSpeed, _grabMaxPointSpeed;
    private Vector3 _lastGrabPoint;
    private const ulong GrabTestId = 0x7E570010;
    private static readonly ulong[] MagnetTestIds = [0x7E570001, 0x7E570002, 0x7E570003];
    private int _scenarioStep;

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
        _levelSelect.Select(0);
        _levelSelect_ItemSelected(0);
        ProcessMode = ProcessModeEnum.Always;
        Log($"lobby ready ({_mode}) as {_selfName} ({ShortId(_selfId)})");

        CallDeferred(nameof(HandleCmdline));
    }

    private void _levelSelect_ItemSelected(long index)
    {
        Lobby.gameInfo.levelIdx = (int)index;
        Lobby.SendGameInfoUpdate();
    }

    public override void _Process(double delta)
    {
        if (!_testMode || _testDone)
            return;
        _testElapsed += delta;

        if (Lobby.MemberCount > _peakMembers)
            _peakMembers = Lobby.MemberCount;

        bool peersOk = _peakMembers >= _expectPeers;
        bool chatOk = _chatReceivedFrom.Count >= _expectPeers - 1;
        bool sentChat = _autoTick >= 1;

        if (peersOk && chatOk && sentChat)
        {
            if (!_testPassed)
            {
                _testPassed = true;
                _testPassedAt = _testElapsed;
                Log($"TEST conditions met — holding {TestGracePeriod}s for peers to converge");
            }
            if (_testElapsed - _testPassedAt >= TestGracePeriod)
            {
                if (!_testGame)
                {
                    _testDone = true;
                    Log($"TEST PASS — peak {_peakMembers} peers, chat from {_chatReceivedFrom.Count} remote peer(s), sent {_autoTick} msg(s)");
                    GD.Print($"TEST_RESULT:PASS peers={_peakMembers} chat_from={_chatReceivedFrom.Count} sent={_autoTick}");
                    GetTree().Quit(0);
                    return;
                }
                if (Lobby.isHost && !_gameStartSent)
                {
                    _gameStartSent = true;
                    Log("TEST host starting game");
                    Lobby.SendStartGame();
                }
            }
        }

        if (_testGame && _gameLoaded)
            RunGameScenario(_testElapsed - _gameLoadedAt);

        // --test-game: evaluate once the world has been running for _testGameSeconds.
        if (_testGame && _gameLoaded && _testElapsed - _gameLoadedAt >= _testGameSeconds)
        {
            _testDone = true;
            var players = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().ToList();
            // Every copy of every player (local and remote) must hold the bootstrap seed item.
            // Blueprints are conserved: one was built by the winner and returned to the host on deconstruct,
            // refunds went back to the losers and the wall block, and one is still built on the floor, so every
            // copy of the inventories adds up to the seed minus one.
            int seedTotal = players.Sum(p => p.inventory.slots.Where(sl => sl.itemID == SeedItemId).Sum(sl => sl.Count));
            bool invOk = players.Count > 0 && seedTotal == players.Count * GameBootstrap.StartingBlueprintCount - 1;
            string buildState = $"{_buildSnapshot}->{TestBlockCounts()}";
            // Host only: the block built into a wall must have been refused for its overlap.
            bool collisionOk = !Lobby.isHost || (_embeddedAttempted && BuildGrid.collisionRefusals == 1);
            bool buildOk = buildState == "1:1->1:1" && _snapOk && collisionOk;
            ulong claimAuthority = GameWorld.syncedObjs.TryGetValue(TestCubeId, out GMPObject cube) ? cube.authority : 0;
            bool claimOk = claimAuthority != 0 && claimAuthority != Lobby.hostID && Lobby.members.ContainsKey(claimAuthority);
            BasicButton button = FindTestButton();
            ObjectSpawner spawner = button?.targetObjectSpawner.FirstOrDefault();
            Lever lever = FindTestLever();
            // Every peer agrees on 2 presses and 2 lever flips (pulled, then pushed back: spawner off). Host only: each
            // press spawned exactly one item, and the lever spawned a stream (0.5 s apart) for its ~2 s, then stopped.
            string buttonState = button == null || lever == null ? "missing"
                : $"{button.acceptedPresses}:lever{lever.acceptedToggles}:{lever.pulled}:{spawner?.spawning}";
            int leverStream = _leverSpawned - _buttonSpawned;
            // Host only, reported but not required: how much ore rode the spawner room's line into its void. Big ore
            // (a rolling sphere) can stall on the slope or bounce off the start belt, which is left for later.
            int roomVoided = GameWorld.syncedObjs.Values.OfType<ItemVoid>().FirstOrDefault(v => v.Name == "SpawnerVoid")?.despawnedCount ?? -1;
            bool buttonOk = buttonState == "2:lever2:False:False"
                && (!Lobby.isHost || (_buttonSpawned == 2 && leverStream >= 3 && leverStream <= 6 && spawner.spawnedCount == _leverSpawned));
            bool demoCubeGone = !GameWorld.syncedObjs.Values.Any(o => (o as Node)?.Name == DemoCubeName);
            string machineState = $"demo:{_demoCubeSeen}->{demoCubeGone} grinder:{CountGrinderOutputs()}";
            // Host only (the museum machines' authority): the spawner has been feeding the line a cube a second
            // and those cubes are reaching the void (the preplaced cube plus spawned ones).
            var cubeSpawner = GameWorld.syncedObjs.Values.OfType<ItemSpawner>().FirstOrDefault();
            var sink = GameWorld.syncedObjs.Values.OfType<ItemVoid>().FirstOrDefault(v => v.Name == "CatchVoid");
            // ...and the line's grinder is turning that ore into two ground chunks each.
            var lineGrinder = GameWorld.syncedObjs.Values.OfType<Grinder>().FirstOrDefault(g => g.Name == "LineGrinder");
            string spawnerState = $"spawned:{cubeSpawner?.spawnedCount} ground:{lineGrinder?.consumedCount}->{lineGrinder?.producedCount} voided:{sink?.despawnedCount}";
            bool spawnerOk = !Lobby.isHost || (cubeSpawner?.spawnedCount >= 10 && sink?.despawnedCount >= 3
                && lineGrinder?.consumedCount >= 3 && lineGrinder.producedCount >= 2 * lineGrinder.consumedCount - 3);
            bool machinesOk = machineState == "demo:True->True grinder:1" && spawnerOk;
            // Resources and tools. Ore and the jumping/tagged chunks belong to the level, so the host runs them.
            bool oreKept = GameWorld.syncedObjs.Values.OfType<PhysicalFactoryItem>().Any(i => i.Name == "IronOre");
            // Host only: the resting museum ore is asleep at its sleeping priority; the jumper went both ways.
            var restingOre = GameWorld.syncedObjs.Values.OfType<PhysicalFactoryItem>().FirstOrDefault(i => i.Name == "IronOre");
            bool sleepOk = !Lobby.isHost || (restingOre != null && restingOre.asleep && restingOre.priority == restingOre.sleepingPriority
                && _jumperSeenAsleep && _jumperSeenAwake);
            bool released = MagnetTestIds.All(id => !GameWorld.heldBy.ContainsKey(id));
            string toolState = $"ore_kept:{oreKept} magnet:{_magnetSnapshot}->released:{released}";
            // Host only: at rest the featherweight sat within 5 cm of the grab point, nearly still; while the host
            // turned it trailed the point by under 0.5 m and never went more than 20% faster than the point itself
            // (sustained overshoot or oscillation shows up as extra speed).
            bool grabOk = !Lobby.isHost || (_grabRestError >= 0 && _grabRestError < 0.05f && _grabRestSpeed < 0.2f
                && _grabMaxError >= 0 && _grabMaxError < 0.5f && _grabMaxPointSpeed > 1f && _grabMaxSpeed < _grabMaxPointSpeed * 1.2f);
            // Host only: HUD named the held magnet rod; sprinting reached (nearly) sprint speed.
            FactoryPlayer self = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
            bool uiOk = !Lobby.isHost || (_hudHeldName == "Magnet Rod" && self != null && _sprintMaxSpeed > self.sprintSpeed * 0.9f);
            // Every peer: only its own player's HUD is drawn (each player scene carries a HUD, and they overlap on screen).
            string visibleHuds = string.Join("+", players.Where(p => p.hud.IsVisibleInTree()).Select(p => p.isLocal ? "self" : $"peer{p.controllingPeerID % 1000}"));
            uiOk &= visibleHuds == "self";
            bool toolsOk = toolState == "ore_kept:True magnet:3/3->released:True" && grabOk && uiOk && sleepOk
                && (!Lobby.isHost || (_jumperMaxMove > 0.5f && TagInteractions.temperatureContacts >= 2));
            machinesOk &= toolsOk;
            // Every peer: the museum's hand-placed conveyor lines (the demo line and the spawner room's line) stand
            // exactly on the grid, and each of their structures registered every cell of its footprint in BuildGrid.
            var demoStructures = GameWorld.syncedObjs.Values.OfType<Structure>()
                .Where(st => st.GetParent()?.Name.ToString() is "ConveyorDemo" or "SpawnerConveyorLine").ToList();
            int demoAligned = demoStructures.Count(st => st.isGridAligned);
            int demoRegistered = demoStructures.Count(st => BuildGrid.FootprintCells(st.anchor, st.cellOffsets, st.quarterTurns).All(c => BuildGrid.GetStructureAt(c) == st));
            bool demoGridOk = demoStructures.Count == 15 && demoAligned == 15 && demoRegistered == 15;
            machinesOk &= demoGridOk;
            // Every conveyor's placement preview gets a non-empty direction arrow; other structures get none.
            string arrows = string.Join(",", new[] { "conveyors/Conveyor", "conveyors/ConveyorSlope", "conveyors/ConveyorSlopeDown", "conveyors/ConveyorTurnLeft", "conveyors/ConveyorTurnRight", "Grinder" }.Select(n =>
            {
                var st = GD.Load<PackedScene>($"res://game/machines/structures/{n}.tscn").Instantiate<Structure>();
                MeshInstance3D arrow = FlowArrowMesh.Create(st, null);
                int tris = arrow?.Mesh is ArrayMesh m && m.GetSurfaceCount() > 0 ? m.SurfaceGetArrayLen(0) / 3 : 0;
                arrow?.Free();
                st.Free();
                return tris.ToString();
            }));
            bool arrowsOk = arrows.Split(',').Take(5).All(n => int.Parse(n) > 0) && arrows.EndsWith(",0");
            machinesOk &= arrowsOk;
            // Every peer: one left turn and one downhill slope were built from the alternate forms, then the turn was
            // deconstructed. Host only: the preview switched forms, and the turn gave back the shared turn blueprint.
            string altState = $"{_altSnapshot}->{AlternateFormCounts()}";
            int turnBlueprints = self?.inventory.slots.Where(sl => sl.itemID == "blueprint_conveyor_turn").Sum(sl => sl.Count) ?? -1;
            bool altOk = altState == "1:1->0:1" && (!Lobby.isHost || (_ghostAltOk && turnBlueprints == GameBootstrap.StartingBlueprintCount));
            machinesOk &= altOk;
            // Character movement queries hit sensors, so players must mask out the placement ghosts' layer.
            bool ghostMaskOk = players.All(p => (p.Get("collision_mask").AsInt64() & GameWorld.QueryHiddenLayer) == 0);
            bool ok = players.Count == _expectPeers && GameWorld.inboundTickCount > 0 && invOk && claimOk && buttonOk && buildOk && machinesOk && ghostMaskOk;
            string summary = $"players={players.Count}/{_expectPeers} synced={GameWorld.syncedObjs.Count} inbound_ticks={GameWorld.inboundTickCount} inv_ok={invOk} consensus_claim={claimAuthority} consensus_button={buttonState} spawner={_buttonSpawned}/{leverStream}/{spawner?.spawnedCount}->voided:{roomVoided} consensus_build={buildState} snap_ok={_snapOk} collision_ok={collisionOk} consensus_machines={machineState.Replace(' ', '_')} ghost_mask_ok={ghostMaskOk} {spawnerState.Replace(' ', '_')} consensus_tools={toolState.Replace(' ', '_')} jumper_moved={_jumperMaxMove:F2} temp_contacts={TagInteractions.temperatureContacts} grab_rest={_grabRestError:F2}m/{_grabRestSpeed:F2}mps grab_track={_grabMaxError:F2}m/{_grabMaxSpeed:F2}of{_grabMaxPointSpeed:F2}mps hud_held={_hudHeldName.Replace(' ', '_')} visible_huds={visibleHuds} sprint={_sprintMaxSpeed:F2} arrow_tris={arrows} consensus_alt={altState} ghost_alt={_ghostAltOk} turn_bp={turnBlueprints} demo_grid={demoStructures.Count}/aligned:{demoAligned}/registered:{demoRegistered} sleep_ok={sleepOk}(ore:{restingOre?.asleep}/{restingOre?.priority} jumper:{_jumperSeenAsleep}/{_jumperSeenAwake})";
            Log($"TEST GAME {(ok ? "PASS" : "FAIL")} — {summary}");
            GD.Print($"TEST_RESULT:{(ok ? "PASS" : "FAIL")} {summary}");
            GetTree().Quit(ok ? 0 : 1);
            return;
        }

        if (_testElapsed >= _testTimeout)
        {
            _testDone = true;
            Log($"TEST FAIL — timeout after {_testTimeout}s (peak_peers={_peakMembers}/{_expectPeers}, chat_from={_chatReceivedFrom.Count}/{_expectPeers - 1}, sent={_autoTick}, game_loaded={_gameLoaded})");
            GD.Print($"TEST_RESULT:FAIL peak_peers={_peakMembers}/{_expectPeers} chat_from={_chatReceivedFrom.Count}/{_expectPeers - 1} sent={_autoTick} game_loaded={_gameLoaded} elapsed={_testElapsed:F1}s");
            GetTree().Quit(1);
        }
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

        if (_testMode && from != _selfId)
            _chatReceivedFrom.Add(from);
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
        if (_testGame && !_gameLoaded)
        {
            _gameLoaded = true;
            _gameLoadedAt = _testElapsed;
            Log("TEST game loaded — running in-game checks");
        }
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
    //   Test:  -- --lan --name A --host 2272 --test --expect-peers 3 --test-timeout 30
    //          -- --lan --name A --host 2272 --test-game [--test-game-seconds 8] --expect-peers 3
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
                case "--test": _testMode = true; auto = true; break;
                case "--expect-peers":
                    string ep = Next(args, ref i);
                    if (ep != null) int.TryParse(ep, out _expectPeers);
                    break;
                case "--test-timeout":
                    string tt = Next(args, ref i);
                    if (tt != null) double.TryParse(tt, out _testTimeout);
                    break;
                case "--test-game": _testMode = true; _testGame = true; auto = true; break;
                case "--test-game-seconds":
                    string gs = Next(args, ref i);
                    if (gs != null) double.TryParse(gs, out _testGameSeconds);
                    break;
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

        if (_testMode)
            Log($"test mode: expect {_expectPeers} peers, timeout {_testTimeout}s");

        if (auto)
        {
            var timer = new Timer { WaitTime = 2.0, Autostart = true };
            timer.Timeout += AutoChat;
            AddChild(timer);
        }
    }

    // A cell that straddles the face of a level-geometry wall, found by probing sideways between the museum's
    // spawner-display walls. (Floors can be triangle meshes, which are hollow, so a block buried under one
    // overlaps nothing.)
    private static bool TryFindLevelWallCell(out Vector3I cell)
    {
        foreach (Vector3 dir in new[] { Vector3.Forward, Vector3.Back })
        {
            Vector3 from = new Vector3(16, 1.5f, 0);
            var ray = GameWorld.Raycast(from, from + dir * 60f);
            if (!ray["hit"].AsBool()) continue;
            Node hit = ray["collider"].AsGodotObject() as Node;
            bool isLevel = true;
            for (Node n = hit; n != null; n = n.GetParent())
                if (n is GMPObject) { isLevel = false; break; }
            // The face must lie well inside a cell, or no cell straddles it.
            float along = ray["position"].AsVector3().Dot(dir.Abs()) / BuildGrid.CellSize;
            float fromBoundary = Mathf.Abs(along - Mathf.Round(along)) * BuildGrid.CellSize;
            cell = BuildGrid.WorldToCell(ray["position"].AsVector3() + dir * 0.05f);
            if (isLevel && fromBoundary > 0.2f && BuildGrid.GetStructureAt(cell) == null) return true;
        }
        cell = default;
        return false;
    }

    // "structures:cells" for the test blocks the scenario builds (the level and the test grinder have
    // structures too): how many exist, and how many grid cells are registered to them.
    private static string TestBlockCounts()
    {
        var blocks = GameWorld.syncedObjs.Values.OfType<Structure>().Where(s => s.blueprintItemID == SeedItemId).ToList();
        int cells = blocks.Sum(s => BuildGrid.FootprintCells(s.anchor, s.cellOffsets, s.quarterTurns).Count(c => BuildGrid.GetStructureAt(c) == s));
        return $"{blocks.Count}:{cells}";
    }

    // Test spheres lying within reach of the test grinder's output.
    private static int CountGrinderOutputs()
    {
        Vector3 output = BuildGrid.CellToWorld(TestGrinderCell) + new Vector3(0, -0.3f, -1.6f);
        return GameWorld.syncedObjs.Values.OfType<PhysicalFactoryItem>()
            .Count(i => i.itemID == "test_inert_sphere" && i.GlobalPosition.DistanceTo(output) < 3f);
    }

    private void RunMachineScenario(double t)
    {
        if (!_demoCubeSeen && GameWorld.syncedObjs.Values.Any(o => (o as Node)?.Name == DemoCubeName))
            _demoCubeSeen = true;
        if (!Lobby.isHost) return;
        if (_grinderSubStep == 0 && t >= 1.0)
        {
            _grinderSubStep++;
            var state = new StructureState { anchor = TestGrinderCell, quarterTurns = 0 };
            GameWorld.SpawnScene("res://game/machines/structures/Grinder.tscn", BuildGrid.CellToWorld(TestGrinderCell), default,
                default, GMPObject.serializer.Serialize(state));
        }
        else if (_grinderSubStep == 1 && t >= 3.0)
        {
            _grinderSubStep++;
            GameWorld.SpawnScene(TestCubeScene, BuildGrid.CellToWorld(TestGrinderCell + Vector3I.Up) + Vector3.Up * 0.3f);
        }
    }

    // 5.0 host notes the button's spawns; 8.0 the first joiner pulls the spawn lever; 10.0 the host pushes it back;
    // 11.0 host notes the total, which must not grow afterwards.
    private void RunLeverScenario(double t)
    {
        ObjectSpawner spawner = FindTestButton()?.targetObjectSpawner.FirstOrDefault();
        if (_leverSubStep == 0 && t >= 5.0)
        {
            _leverSubStep++;
            _buttonSpawned = spawner?.spawnedCount ?? -1;
        }
        else if (_leverSubStep == 1 && t >= 8.0)
        {
            _leverSubStep++;
            bool firstJoiner = !Lobby.isHost && Lobby.selfPeerID == Lobby.members.Keys.Where(k => k != Lobby.hostID).Min();
            if (firstJoiner) FindTestLever()?.OnPressed();
        }
        else if (_leverSubStep == 2 && t >= 10.0)
        {
            _leverSubStep++;
            if (Lobby.isHost) FindTestLever()?.OnPressed();
        }
        else if (_leverSubStep == 3 && t >= 11.0)
        {
            _leverSubStep++;
            _leverSpawned = spawner?.spawnedCount ?? -1;
        }
    }

    // Alternate structure forms (the host's hotbar is otherwise idle from 7.5 to 8.4 s):
    // 7.6 host builds the turn blueprint's alternate (left turn); 7.9 the slope's alternate (downhill);
    // 8.2 host's ghost switches forms and its preview arrow follows; 9.0 every peer counts them; 9.2 host deconstructs the turn.
    private void RunAlternateFormScenario(double t)
    {
        FactoryPlayer local = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
        if (_altSubStep == 0 && t >= 7.6)
        {
            _altSubStep++;
            if (Lobby.isHost && local != null) BuildAlternate(local, "blueprint_conveyor_turn");
        }
        else if (_altSubStep == 1 && t >= 7.9)
        {
            _altSubStep++;
            if (Lobby.isHost && local != null) BuildAlternate(local, "blueprint_conveyor_slope");
        }
        else if (_altSubStep == 2 && t >= 8.2)
        {
            _altSubStep++;
            if (Lobby.isHost && local != null && HoldBlueprint(local, "blueprint_conveyor_slope"))
            {
                local.UpdateEquippedItem();
                if (local.heldItem is BlueprintGhost ghost)
                {
                    ghost.SetAlternate(true);
                    bool down = ghost.showingAlternate && ghost.previewStructure?.flowArrow == FlowArrow.StraightDown;
                    ghost.SetAlternate(false);
                    _ghostAltOk = down && ghost.previewStructure?.flowArrow == FlowArrow.Straight;
                }
            }
        }
        else if (_altSubStep == 3 && t >= 9.0)
        {
            _altSubStep++;
            _altSnapshot = AlternateFormCounts();
        }
        else if (_altSubStep == 4 && t >= 9.2)
        {
            _altSubStep++;
            Structure leftTurn = GameWorld.syncedObjs.Values.OfType<Structure>().FirstOrDefault(st => st.SceneFilePath.EndsWith("/ConveyorTurnLeft.tscn"));
            if (Lobby.isHost && local != null && leftTurn != null) BuildGrid.RequestDeconstruct(local, leftTurn);
        }
    }

    // Holds a blueprint and builds its alternate form on free floor.
    private static void BuildAlternate(FactoryPlayer local, string blueprintID)
    {
        if (!HoldBlueprint(local, blueprintID) || ItemInfo.Fetch(blueprintID) is not BlueprintItem blueprint) return;
        Structure shape = blueprint.StructureScene(true).Instantiate<Structure>();
        Vector3I[] offsets = shape.cellOffsets.ToArray();
        shape.Free();
        if (TryFindOpenFloorCell(out Vector3I cell, offsets))
            BuildGrid.RequestPlace(local, blueprint, cell, 0, true);
    }

    // "left turns:downhill slopes" among the synced structures (the museum has neither).
    private static string AlternateFormCounts()
    {
        var structures = GameWorld.syncedObjs.Values.OfType<Structure>().ToList();
        return $"{structures.Count(st => st.SceneFilePath.EndsWith("/ConveyorTurnLeft.tscn"))}:{structures.Count(st => st.SceneFilePath.EndsWith("/ConveyorSlopeDown.tscn"))}";
    }

    private void RunResourceScenario(double t)
    {
        var items = GameWorld.syncedObjs.Values.OfType<PhysicalFactoryItem>();
        FactoryPlayer local = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
        FactoryPlayer hostPlayer = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.controllingPeerID == Lobby.hostID);

        if (items.FirstOrDefault(i => i.Name == "JumpingChunk") is PhysicalFactoryItem jumper)
        {
            if (_jumperStart == Vector3.Zero) _jumperStart = jumper.GlobalPosition;
            _jumperMaxMove = Mathf.Max(_jumperMaxMove, jumper.GlobalPosition.DistanceTo(_jumperStart));
            // Sleep-based sync priority: between hops it should settle, drop its priority, and restore it on the next hop.
            if (jumper.authority == Lobby.selfPeerID && t >= 2.0)
            {
                if (jumper.asleep && jumper.priority == jumper.sleepingPriority) _jumperSeenAsleep = true;
                if (!jumper.asleep && jumper.priority > jumper.sleepingPriority) _jumperSeenAwake = true;
            }
        }

        if (_resourceSubStep == 0 && t >= 2.0)
        {
            _resourceSubStep++;
            if (Lobby.isHost && local != null && items.FirstOrDefault(i => i.Name == "IronOre") is PhysicalFactoryItem ore)
                ore.RequestPickup(local);
        }
        else if (_resourceSubStep == 1 && t >= 12.3)
        {
            _resourceSubStep++;
            bool firstJoiner = !Lobby.isHost && Lobby.selfPeerID == Lobby.members.Keys.Where(k => k != Lobby.hostID).Min();
            if (firstJoiner && hostPlayer != null)
            {
                Vector3 ball = hostPlayer.camera.GlobalPosition - hostPlayer.camera.GlobalTransform.Basis.Z * 3f;
                for (int i = 0; i < MagnetTestIds.Length; i++)
                {
                    Vector3 offset = new((i - 1) * 1.0f, 0.5f, 0.5f);
                    GameWorld.SpawnScene("res://game/items/world/resources/copper_ore_ground.tscn", ball + offset, default,
                        new GMPOInitData(MagnetTestIds[i], 0, 0, 0, false));
                }
            }
        }
        else if (_resourceSubStep == 2 && t >= 13.0)
        {
            _resourceSubStep++;
            if (Lobby.isHost && local != null)
            {
                int slot = System.Array.FindIndex(local.inventory.slots, sl => sl.itemID == "magnet_rod");
                local.inventory.ActiveHotbarSlot = slot;
                local.UpdateEquippedItem();
                if (local.FindChildren("*", "", true, false).OfType<MagnetRod>().FirstOrDefault() is MagnetRod rod) rod.forceActive = true;
            }
        }
        else if (_resourceSubStep == 3 && t >= 15.5)
        {
            _resourceSubStep++;
            float floor = hostPlayer != null ? hostPlayer.GlobalPosition.Y : 0f;
            int floating = MagnetTestIds.Count(id => GameWorld.syncedObjs.TryGetValue(id, out GMPObject o)
                && o.authority == Lobby.hostID
                && GameWorld.heldBy.TryGetValue(id, out ulong holder) && holder == Lobby.hostID
                && (o as Node3D).GlobalPosition.Y > floor + 0.5f);
            _magnetSnapshot = $"{floating}/{MagnetTestIds.Length}";
            if (Lobby.isHost && local != null) _hudHeldName = local.hud.heldItemText;
            if (floating != MagnetTestIds.Length)
            {
                foreach (ulong id in MagnetTestIds)
                    if (GameWorld.syncedObjs.TryGetValue(id, out GMPObject o))
                        Log($"TEST magnet chunk {id:X}: authority={o.authority} held={(GameWorld.heldBy.TryGetValue(id, out ulong h) ? h : 0)} y={(o as Node3D).GlobalPosition.Y:F2} floor={floor:F2}");
            }
        }
        else if (_resourceSubStep == 4 && t >= 16.0)
        {
            _resourceSubStep++;
            if (Lobby.isHost && local != null && local.FindChildren("*", "", true, false).OfType<MagnetRod>().FirstOrDefault() is MagnetRod rod)
                rod.forceActive = false;
        }
        else if (_resourceSubStep == 5 && t >= 16.3)
        {
            _resourceSubStep++;
            if (Lobby.isHost && local != null)
            {
                // Empty hand, then a featherweight chunk right at the grab point.
                local.inventory.ActiveHotbarSlot = -1;
                local.UpdateEquippedItem();
                GameWorld.SpawnScene("res://game/dev/items/test_chunk_light.tscn", local.grabPoint, default, new GMPOInitData(GrabTestId, 0, 0, 0, false));
            }
        }
        else if (_resourceSubStep == 6 && t >= 16.5)
        {
            _resourceSubStep++;
            if (Lobby.isHost && local != null && GameWorld.syncedObjs.TryGetValue(GrabTestId, out GMPObject chunk))
            {
                local.forceGrabHeld = true;
                local.RequestGrab((PhysicalFactoryItem)chunk);
            }
        }
        else if (_resourceSubStep == 7 && t >= 19.5)
        {
            _resourceSubStep++;
            if (Lobby.isHost && local != null)
            {
                local.forceGrabHeld = false;
                local.ReleaseGrab();
            }
        }
        // Sprint: walk forward with sprint held, recording the horizontal speed reached.
        if (Lobby.isHost && local != null && t >= 19.6 && t < 20.3)
        {
            Input.ActionPress("forward");
            Input.ActionPress("sprint");
            _sprintMaxSpeed = Mathf.Max(_sprintMaxSpeed, new Vector2(local.cachedVel.X, local.cachedVel.Z).Length());
        }
        else if (Lobby.isHost && t >= 20.3 && Input.IsActionPressed("sprint"))
        {
            Input.ActionRelease("forward");
            Input.ActionRelease("sprint");
        }

        // Half a second after grabbing, sample the chunk at rest; from 17.5 turn on the spot (swinging the grab
        // point at a few m/s) and, once past the start-up transient, sample how closely it tracks.
        if (Lobby.isHost && _resourceSubStep == 7 && t >= 17.0 && local?.grabbedItem is PhysicalFactoryItem held && held.id == GrabTestId)
        {
            float error = held.GlobalPosition.DistanceTo(local.grabPoint);
            float speed = held.Call("get_linear_velocity").AsVector3().Length();
            if (t < 17.5)
            {
                _grabRestError = Mathf.Max(_grabRestError, error);
                _grabRestSpeed = Mathf.Max(_grabRestSpeed, speed);
            }
            else
            {
                float dt = (float)GetProcessDeltaTime();
                local.RotateY(1.5f * dt);
                if (t >= 18.0)
                {
                    _grabMaxPointSpeed = Mathf.Max(_grabMaxPointSpeed, local.grabPoint.DistanceTo(_lastGrabPoint) / dt);
                    _grabMaxError = Mathf.Max(_grabMaxError, error);
                    _grabMaxSpeed = Mathf.Max(_grabMaxSpeed, speed);
                }
                _lastGrabPoint = local.grabPoint;
            }
        }
    }

    private void RunGameScenario(double t)
    {
        RunMachineScenario(t);
        RunResourceScenario(t);
        RunLeverScenario(t);
        RunAlternateFormScenario(t);
        // 8.5-12: host scrolls its hotbar every frame with a forced GC each time; this crashed when item
        // definitions weren't held (GC finalizer disposing one while ItemInfo.Fetch reloaded it).
        if (Lobby.isHost && t >= 8.5 && t < 12.0)
        {
            FactoryPlayer local = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
            if (local != null)
            {
                local.inventory.ActiveHotbarSlot = (local.inventory.ActiveHotbarSlot + 1) % 8;
                local.UpdateEquippedItem();
                local.inventory.InventoryUpdated();
                System.GC.Collect();
            }
        }
        if (_scenarioStep == 0 && t >= 0.5)
        {
            _scenarioStep++;
            if (Lobby.isHost)
                GameWorld.SpawnScene(TestCubeScene, new Vector3(0, 3, 0), default, new GMPOInitData(TestCubeId, 0, 0, 0, false));
        }
        else if (_scenarioStep == 1 && t >= 2.0)
        {
            _scenarioStep++;
            FindTestButton()?.OnPressed();
        }
        else if (_scenarioStep == 2 && t >= 3.0)
        {
            _scenarioStep++;
            if (!Lobby.isHost && GameWorld.syncedObjs.TryGetValue(TestCubeId, out GMPObject cube))
                GameWorld.Claim(cube as PhysicalFactoryItem);
        }
        else if (_scenarioStep == 3 && t >= 4.0)
        {
            _scenarioStep++;
            if (Lobby.isHost)
                FindTestButton()?.OnPressed();
        }
        else if (_scenarioStep == 4 && t >= 5.0)
        {
            _scenarioStep++;
            FactoryPlayer local = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
            if (local != null && HoldSeedBlueprint(local) && ItemInfo.Fetch(SeedItemId) is BlueprintItem blueprint)
                BuildGrid.RequestPlace(local, blueprint, TestBuildCell, 1);
        }
        else if (_scenarioStep == 5 && t >= 6.0)
        {
            _scenarioStep++;
            // A ray straight down through the cell must hit the structure's collision, i.e. its static
            // body was placed at the built cell rather than left at the origin.
            Vector3 centre = BuildGrid.CellToWorld(TestBuildCell);
            var ray = GameWorld.Raycast(centre + Vector3.Up * BuildGrid.CellSize, centre);
            bool hitsStructure = ray["hit"].AsBool() && BuildGrid.FindStructure(ray["collider"].AsGodotObject() as Node) != null;
            _buildSnapshot = $"{TestBlockCounts()}{(hitsStructure ? "" : "(no collision)")}";

            Vector3I[] longBlock = [Vector3I.Zero, new Vector3I(1, 0, 0)];
            _snapOk = BuildGrid.TryGetPlacementTarget(centre - Vector3.Right * 6f, Vector3.Right, 10f, longBlock, 0, out Vector3I snapped)
                && snapped == TestBuildCell - new Vector3I(2, 0, 0);
            if (!_snapOk)
                Log($"TEST snap check failed: got {snapped}, expected {TestBuildCell - new Vector3I(2, 0, 0)}");

            // Thin structures snap by their whole cell: aim across the museum line's first conveyor from its side,
            // 1.5 m above the floor (clear over its belt and walls), and expect the cell on that side of it.
            Structure start = GameWorld.syncedObjs.Values.OfType<Structure>().FirstOrDefault(st => st.Name == "Start" && st.GetParent()?.Name == "ConveyorDemo");
            Vector3I startConveyor = start?.anchor ?? Vector3I.Zero;
            Vector3 side = BuildGrid.QuarterTurnBasis(start?.quarterTurns ?? 0) * Vector3.Right;
            Vector3 aimFrom = BuildGrid.CellToWorld(startConveyor) - side * 6f + Vector3.Up * (1.5f - BuildGrid.CellSize / 2);
            Vector3I expectedThin = startConveyor - new Vector3I(Mathf.RoundToInt(side.X), 0, Mathf.RoundToInt(side.Z));
            Vector3I thinSnap = default;
            bool thinOk = start != null && BuildGrid.TryGetPlacementTarget(aimFrom, side, 12f, [Vector3I.Zero], 0, out thinSnap)
                && thinSnap == expectedThin;
            if (!thinOk)
                Log($"TEST thin snap check failed: got {thinSnap}, expected {expectedThin}");
            _snapOk &= thinOk;
        }
        else if (_scenarioStep == 6 && t >= 6.5)
        {
            _scenarioStep++;
            FactoryPlayer local = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
            if (Lobby.isHost && local != null && BuildGrid.GetStructureAt(TestBuildCell) is Structure built)
                BuildGrid.RequestDeconstruct(local, built);
        }
        else if (_scenarioStep == 7 && t >= 7.0)
        {
            _scenarioStep++;
            FactoryPlayer local = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
            if (Lobby.isHost && local != null && HoldSeedBlueprint(local) && ItemInfo.Fetch(SeedItemId) is BlueprintItem blueprint && TryFindLevelWallCell(out Vector3I sunk))
            {
                _embeddedAttempted = true;
                BuildGrid.RequestPlace(local, blueprint, sunk, 0);
            }
        }
        else if (_scenarioStep == 8 && t >= 7.3)
        {
            _scenarioStep++;
            FactoryPlayer local = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
            // Open floor in the museum (its floor's top is at y=0, a cell boundary, so the block rests flush).
            if (Lobby.isHost && local != null && TryFindOpenFloorCell(out Vector3I floorCell) && HoldSeedBlueprint(local) && ItemInfo.Fetch(SeedItemId) is BlueprintItem blueprint)
                BuildGrid.RequestPlace(local, blueprint, floorCell, 0);
        }
    }

    // A free anchor on bare museum floor near (-19, -19) for a footprint (default one cell, unrotated): every
    // footprint cell is free in the grid, and above each of its columns a ray straight down lands on level geometry
    // at y=0 rather than a structure or prop. Searched so level edits don't break the test.
    private static bool TryFindOpenFloorCell(out Vector3I cell, Vector3I[] offsets = null)
    {
        offsets ??= [Vector3I.Zero];
        for (int r = 0; r <= 6; r++)
        for (int dx = -r; dx <= r; dx++)
        for (int dz = -r; dz <= r; dz++)
        {
            if (Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)) != r) continue; // ring by ring, nearest first
            Vector3I anchor = BuildGrid.WorldToCell(new Vector3(-19 + dx * BuildGrid.CellSize, 0.05f, -19 + dz * BuildGrid.CellSize));
            bool clear = BuildGrid.CanPlace(anchor, offsets, 0);
            foreach (Vector3I c in offsets)
            {
                if (!clear) break;
                Vector3 at = BuildGrid.CellToWorld(anchor + new Vector3I(c.X, 0, c.Z)) with { Y = 0 };
                var ray = GameWorld.Raycast(at + Vector3.Up * 10, at + Vector3.Down * 10);
                clear = ray["hit"].AsBool() && Mathf.Abs(ray["position"].AsVector3().Y) <= 0.01f
                    && BuildGrid.FindStructure(ray["collider"].AsGodotObject() as Node) == null;
            }
            if (clear)
            {
                cell = anchor;
                return true;
            }
        }
        cell = default;
        return false;
    }

    // Builds consume the blueprint in the active hotbar slot, so hold the seed blueprint wherever the bootstrap put it.
    private static bool HoldSeedBlueprint(FactoryPlayer player) => HoldBlueprint(player, SeedItemId);

    private static bool HoldBlueprint(FactoryPlayer player, string itemID)
    {
        for (int i = 0; i < Inventory.HotbarSlots; i++)
        {
            if (player.inventory.GetSlot(i).itemID == itemID)
            {
                player.inventory.ActiveHotbarSlot = i;
                return true;
            }
        }
        return false;
    }

    private static Lever FindTestLever() => GameWorld.b3droot?.FindChild("SpawnLever", true, false) as Lever;
    private static BasicButton FindTestButton() => GameWorld.b3droot?.FindChild("Button", true, false) as BasicButton;

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
