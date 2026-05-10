using Godot;
using System.Collections.Generic;

public partial class Client : Node
{
	public enum NetworkBackend
	{
		BuiltInEnet,
		CustomServer,
	}

	[Export] public string Host { get; set; } = "127.0.0.1";
	[Export] public int    EnetPort      { get; set; } = 7777;
	[Export] public int    EnetUdpPort   { get; set; } = 7778;
	[Export] public int    CustomTcpPort { get; set; } = 7777;
	[Export] public int    CustomUdpPort { get; set; } = 7778;
	[Export] public NetworkBackend Backend { get; set; } = NetworkBackend.BuiltInEnet;
	[Export] public string AutoJoinToken { get; set; } = "test-lab";

	private ENetMultiplayerPeer     _peer;
	private CustomNetworkClient      _customNet;
	private bool                     _isBot = false;
	private readonly Dictionary<string, Player> _remotePlayers = new();

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
			if (args[i] == "--bot")
				_isBot = true;
			if (args[i] == "--host" && i + 1 < args.Length)
			{
				Host = args[i + 1];
				GD.Print($"[Client] Host overridden to: {Host}");
			}
			if (args[i] == "--port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int enetPort))
			{
				EnetPort = enetPort;
				GD.Print($"[Client] ENet port overridden to: {EnetPort}");
			}
			if (args[i] == "--enet-udp-port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int enetUdpPort))
			{
				EnetUdpPort = enetUdpPort;
				GD.Print($"[Client] ENet UDP port overridden to: {EnetUdpPort}");
			}
			if (args[i] == "--custom-port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int customPort))
			{
				CustomTcpPort = customPort;
				GD.Print($"[Client] Custom TCP port overridden to: {CustomTcpPort}");
			}
			if (args[i] == "--udp-port" && i + 1 < args.Length && int.TryParse(args[i + 1], out int udpPort))
			{
				CustomUdpPort = udpPort;
				GD.Print($"[Client] Custom UDP port overridden to: {CustomUdpPort}");
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

		GD.Print($"[Client] isClient={isClient}, Host={Host}, EnetPort={EnetPort}, EnetUdpPort={EnetUdpPort}, CustomTcpPort={CustomTcpPort}, CustomUdpPort={CustomUdpPort}, Backend={Backend}");
		// For the CustomServer backend the Godot process is always the client
		// (the server is an external process), so connect even without --client.
		if (!isClient && Backend != NetworkBackend.CustomServer)
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
		GD.Print($"[Client] ConnectToServer() - connecting to {Host}:{EnetPort}...");
		_peer = new ENetMultiplayerPeer();
		var err = _peer.CreateClient(Host, EnetPort);
		GD.Print($"[Client] ENetMultiplayerPeer.CreateClient result: {err}");
		if (err != Error.Ok)
		{
			GD.PrintErr($"[Client] Failed to connect to {Host}:{EnetPort} ({err})");
			return;
		}

		Multiplayer.MultiplayerPeer = _peer;
		GD.Print($"[Client] Multiplayer peer assigned. My unique ID: {Multiplayer.GetUniqueId()}");

		_peer.PeerConnected              += OnPeerConnected;
		_peer.PeerDisconnected           += OnPeerDisconnected;
		Multiplayer.ConnectedToServer    += OnConnectedToServer;
		Multiplayer.ConnectionFailed     += OnConnectionFailed;

		GD.Print($"[Client] Waiting for connection to {Host}:{EnetPort}...");
	}

	private void ConnectToCustomServer()
	{
		GD.Print($"[Client] ConnectToCustomServer() — connecting to {Host}:{CustomTcpPort}...");

		_customNet = new CustomNetworkClient();
		AddChild(_customNet);

		_customNet.UdpSharedSecret = "change-me-before-deploying"; // match server appsettings.json SecurityConfig:UdpSharedSecret
		_customNet.UdpHost         = Host;
		_customNet.UdpPort         = CustomUdpPort;

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

		var err = _customNet.ConnectToServer(Host, CustomTcpPort);
		if (err != Error.Ok)
		{
			GD.PrintErr($"[Client] Could not start connection to {Host}:{CustomTcpPort}");
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
		_customNet.AutoJoin(AutoJoinToken);
	}

	// ── Room callbacks ────────────────────────────────────────────────────────

	private void OnRoomListReceived(string roomsJson)
		=> GD.Print($"[Client] Room list: {roomsJson}");

	private void OnRoomCreated(string roomId, string roomName)
		=> GD.Print($"[Client] Room created — id: {roomId}, name: {roomName}");

	private void OnRoomJoined(string roomId)
	{
		GD.Print($"[Client] Joined room: {roomId}");
		// All custom-backend clients spawn immediately on joining — GAME_STARTED is never
		// sent in the auto-join flow (only when a host explicitly calls StartGame()).
		if (Backend == NetworkBackend.CustomServer && !_gameRunning)
		{
			_gameRunning = true;
			SpawnLocalCustomPlayer();
		}
	}

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
		SpawnLocalCustomPlayer();
	}

	private void SpawnLocalCustomPlayer()
	{
		var playerScene = ResourceLoader.Load<PackedScene>("res://Prefabs/p_player.tscn");
		if (playerScene == null)
		{
			GD.PrintErr("[Client] SpawnLocalCustomPlayer: could not load p_player.tscn");
			return;
		}
		var player = playerScene.Instantiate<Player>();
		player.Name    = "Player_" + (_customNet?.SessionId ?? "local");
		player.CustomNet = _customNet;
		GetParent().AddChild(player);
		GD.Print($"[Client] Spawned local player for custom server: {player.Name}");
	}

	private void OnPlayerJoined(string playerId, string playerName)
		=> GD.Print($"[Client] Player joined — id: {playerId}, name: {playerName}");

	private void OnPlayerLeft(string playerId)
	{
		GD.Print($"[Client] Player left — id: {playerId}");
		if (_remotePlayers.TryGetValue(playerId, out var ghost))
		{
			ghost.QueueFree();
			_remotePlayers.Remove(playerId);
		}
	}

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
	{
		if (!_remotePlayers.TryGetValue(sessionId, out var ghost))
		{
			var scene = ResourceLoader.Load<PackedScene>("res://Prefabs/p_player.tscn");
			if (scene == null) return;
			ghost = scene.Instantiate<Player>();
			ghost.Name          = $"Remote_{sessionId}";
			ghost.IsRemoteGhost = true;
			GetParent().AddChild(ghost);
			_remotePlayers[sessionId] = ghost;
			GD.Print($"[Client] Spawned remote player ghost: {sessionId}");
		}
		ghost.GlobalPosition = new Vector2(position.X, position.Y);
	}

	private void OnAutoAuthFailed()
	{
		// No stored token — register a fresh guest account so the AUTO_JOIN flow can proceed.
		string guestName = $"Guest_{Godot.Time.GetTicksMsec() % 100_000}";
		string guestPass = $"pw_{guestName}";
		GD.Print($"[Client] Auto-auth failed — registering as guest: {guestName}");
		_customNet.Register(guestName, guestPass);
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
