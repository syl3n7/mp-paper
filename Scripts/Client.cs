using Godot;

public partial class Client : Node
{
	public enum NetworkBackend
	{
		BuiltInEnet,
		CustomServer,
	}

	[Export] public string Host { get; set; } = "127.0.0.1";
	[Export] public int    Port { get; set; } = 7777;
	[Export] public int    CustomPort { get; set; } = 9000;
	[Export] public int    UdpPort { get; set; } = 7778;
	[Export] public NetworkBackend Backend { get; set; } = NetworkBackend.BuiltInEnet;

	private ENetMultiplayerPeer     _peer;
	private CustomNetworkClient      _customNet;

	public override void _Ready()
	{
		GD.Print("[Client] _Ready() called");
		var args = OS.GetCmdlineUserArgs();
		GD.Print($"[Client] Command-line user args: [{string.Join(", ", args)}]");

		// Only connect when launched with the --client flag.
		// This prevents the server instance from also acting as a client.
		bool isClient = false;
		for (int i = 0; i < args.Length; i++)
		{
			if (args[i] == "--client" || args[i] == "--bot")
				isClient = true;
			if (args[i] == "--host" && i + 1 < args.Length)
			{
				Host = args[i + 1];
				GD.Print($"[Client] Host overridden to: {Host}");
			}
			if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int enetPort))
			{
				Port = enetPort;
				GD.Print($"[Client] ENet port overridden to: {Port}");
			}
			if (args[i] == "--custom-port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int customPort))
			{
				CustomPort = customPort;
				GD.Print($"[Client] Custom server port overridden to: {CustomPort}");
			}
			if (args[i] == "--udp-port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int udpPort))
			{
				UdpPort = udpPort;
				GD.Print($"[Client] UDP port overridden to: {UdpPort}");
			}
			if (args[i] == "--network" && i + 1 < args.Length)
			{
				string requestedBackend = args[i + 1].ToLowerInvariant();
				if (requestedBackend == "custom")
					Backend = NetworkBackend.CustomServer;
				else if (requestedBackend == "enet")
					Backend = NetworkBackend.BuiltInEnet;

				GD.Print($"[Client] Network backend set to: {Backend}");
			}
		}

		GD.Print($"[Client] isClient={isClient}, Host={Host}, Port={Port}, CustomPort={CustomPort}, Backend={Backend}");
		if (!isClient)
		{
			GD.Print("[Client] Not a client instance - skipping connection");
			return;
		}

		ConnectToSelectedBackend();
	}

	public override void _ExitTree()
	{
		_customNet?.DisconnectGracefully();

		if (_peer != null)
		{
			_peer.Close();
			_peer = null;
		}
	}

	private void ConnectToSelectedBackend()
	{
		if (Backend == NetworkBackend.CustomServer)
		{
			ConnectToCustomServer();
			return;
		}

		ConnectToBuiltInServer();
	}

	private void ConnectToBuiltInServer()
	{
		GD.Print($"[Client] ConnectToServer() - connecting to {Host}:{Port}...");
		_peer = new ENetMultiplayerPeer();
		var err = _peer.CreateClient(Host, Port);
		GD.Print($"[Client] ENetMultiplayerPeer.CreateClient result: {err}");
		if (err != Error.Ok)
		{
			GD.PrintErr($"[Client] Failed to connect to {Host}:{Port} ({err})");
			return;
		}

		Multiplayer.MultiplayerPeer = _peer;
		GD.Print($"[Client] Multiplayer peer assigned. My unique ID: {Multiplayer.GetUniqueId()}");

		_peer.PeerConnected              += OnPeerConnected;
		_peer.PeerDisconnected           += OnPeerDisconnected;
		Multiplayer.ConnectedToServer    += OnConnectedToServer;
		Multiplayer.ConnectionFailed     += OnConnectionFailed;

		GD.Print($"[Client] Waiting for connection to {Host}:{Port}...");
	}

	private void ConnectToCustomServer()
	{
		GD.Print($"[Client] ConnectToCustomServer() — connecting to {Host}:{CustomPort}...");

		_customNet = new CustomNetworkClient();
		AddChild(_customNet);

		_customNet.UdpSharedSecret = "change-me-before-deploying"; // match server appsettings.json SecurityConfig:UdpSharedSecret
		_customNet.UdpHost         = Host;
		_customNet.UdpPort         = UdpPort;

		_customNet.ServerConnected        += OnCustomServerConnected;
		_customNet.ServerDisconnected     += OnCustomServerDisconnected;
		_customNet.TcpMessageReceived     += OnCustomTcpMessage;
		_customNet.Authenticated          += OnAuthenticated;
		_customNet.AutoAuthFailed         += OnAutoAuthFailed;
		_customNet.AuthFailed             += OnAuthFailed;
		_customNet.RoomListReceived       += OnRoomListReceived;
		_customNet.RoomCreated            += OnRoomCreated;
		_customNet.RoomJoined             += OnRoomJoined;
		_customNet.RoomLeft               += OnRoomLeft;
		_customNet.RoomPlayersReceived    += OnRoomPlayersReceived;
		_customNet.GameStarted            += OnGameStarted;
		_customNet.PlayerJoined           += OnPlayerJoined;
		_customNet.PlayerLeft             += OnPlayerLeft;
		_customNet.GameEnded              += OnGameEnded;
		_customNet.RoomError              += OnRoomError;
		_customNet.ChatReceived           += OnChatReceived;
		_customNet.RelayReceived          += OnRelayReceived;
		_customNet.RemotePositionUpdated  += OnRemotePositionUpdated;

		var err = _customNet.ConnectToServer(Host, CustomPort);
		if (err != Error.Ok)
		{
			GD.PrintErr($"[Client] Could not start connection to {Host}:{CustomPort}");
			_customNet.QueueFree();
			_customNet = null;
		}
	}

	// ── CustomNetworkClient callbacks ─────────────────────────────────────────

	private void OnCustomServerConnected(string sessionId)
	{
		GD.Print($"[Client] Connected to custom server — sessionId: {sessionId}");
		_customNet.TryAutoAuth();
	}

	private void OnCustomServerDisconnected()
	{
		GD.PrintErr("[Client] Disconnected from custom server");
	}

	private void OnCustomTcpMessage(string rawJson)
	{
		// Unrecognised messages only — all known commands are handled by CustomNetworkClient.
		GD.Print($"[Client] Unhandled TCP message: {rawJson}");
	}

	private void OnAuthenticated(string username)
	{
		GD.Print($"[Client] Authenticated as: {username}");
		_customNet.ListRooms();
	}

	// ── Room callbacks ────────────────────────────────────────────────────────

	private void OnRoomListReceived(string roomsJson)
		=> GD.Print($"[Client] Room list: {roomsJson}");

	private void OnRoomCreated(string roomId, string roomName)
		=> GD.Print($"[Client] Room created — id: {roomId}, name: {roomName}");

	private void OnRoomJoined(string roomId)
		=> GD.Print($"[Client] Joined room: {roomId}");

	private void OnRoomLeft(string roomId)
		=> GD.Print($"[Client] Left room: {roomId}");

	private void OnRoomPlayersReceived(string roomId, string playersJson)
		=> GD.Print($"[Client] Players in room {roomId}: {playersJson}");

	private bool _gameRunning = false;
	private void OnGameStarted(string spawnPositionsJson)
	{
		// Guard: host may receive GAME_STARTED twice (known server bug — see PHASE3_CONCERNS #5)
		if (_gameRunning) return;
		_gameRunning = true;
		GD.Print($"[Client] Game started — spawn positions: {spawnPositionsJson}");
	}

	private void OnPlayerJoined(string playerId, string playerName)
		=> GD.Print($"[Client] Player joined — id: {playerId}, name: {playerName}");

	private void OnPlayerLeft(string playerId)
		=> GD.Print($"[Client] Player left — id: {playerId}");

	private void OnGameEnded()
	{
		_gameRunning = false;
		GD.Print("[Client] Game ended");
	}

	private void OnRoomError(string message)
		=> GD.PrintErr($"[Client] Room error: {message}");

	private void OnChatReceived(string senderName, string message)
		=> GD.Print($"[Client] Chat from {senderName}: {message}");

	private void OnRelayReceived(string senderId, string senderName, string message)
		=> GD.Print($"[Client] Relay from {senderName} ({senderId}): {message}");

	private void OnRemotePositionUpdated(string sessionId, Vector3 position, Quaternion rotation)
		=> GD.Print($"[Client] Remote pos — {sessionId}: pos={position}, rot={rotation}");

	private void OnAutoAuthFailed()
	{
		GD.Print("[Client] Auto-auth failed — manual login required");
		// TODO: show login UI — for now, demo a register call
		// _customNet.Register("TestPlayer", "password123");
	}

	private void OnAuthFailed(string reason)
	{
		GD.PrintErr($"[Client] Auth failed: {reason}");
	}

	private void OnPeerConnected(long id)    => GD.Print($"[Client] OnPeerConnected - peer id: {id}");
	private void OnConnectedToServer()        => GD.Print($"[Client] Connected to ENet server — my id: {Multiplayer.GetUniqueId()}");
	private void OnPeerDisconnected(long id)  => GD.Print($"[Client] Disconnected from peer {id}");
	private void OnConnectionFailed()         => GD.PrintErr("[Client] Connection FAILED - server unreachable or refused");
}
