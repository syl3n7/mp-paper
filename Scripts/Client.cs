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
	[Export] public NetworkBackend Backend { get; set; } = NetworkBackend.BuiltInEnet;

	private ENetMultiplayerPeer _peer;
	private StreamPeerTcp _customSocket;
	private StreamPeerTcp.Status _customStatus = StreamPeerTcp.Status.None;

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
			if (args[i] == "--client")
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

	public override void _Process(double delta)
	{
		if (_customSocket == null)
			return;

		_customSocket.Poll();
		var status = _customSocket.GetStatus();
		if (status == _customStatus)
			return;

		_customStatus = status;
		GD.Print($"[Client] Custom socket status: {_customStatus}");
	}

	public override void _ExitTree()
	{
		if (_customSocket != null)
			_customSocket.DisconnectFromHost();
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

		_peer.PeerConnected    += OnPeerConnected;
		_peer.PeerDisconnected += OnPeerDisconnected;
		Multiplayer.ConnectionFailed += OnConnectionFailed;

		GD.Print($"[Client] Waiting for connection to {Host}:{Port}...");
	}

	private void ConnectToCustomServer()
	{
		GD.Print($"[Client] ConnectToCustomServer() - connecting to {Host}:{CustomPort}...");
		_customSocket = new StreamPeerTcp();
		var err = _customSocket.ConnectToHost(Host, CustomPort);
		if (err != Error.Ok)
		{
			GD.PrintErr($"[Client] Failed to connect to custom server at {Host}:{CustomPort} ({err})");
			_customSocket = null;
			return;
		}

		SetProcess(true);
		GD.Print("[Client] Custom server connection attempt started");
	}

	private void OnPeerConnected(long id) => GD.Print($"[Client] OnPeerConnected - server peer id: {id}, my id: {Multiplayer.GetUniqueId()}");
	private void OnPeerDisconnected(long id) => GD.Print($"[Client] Disconnected from peer {id}");
	private void OnConnectionFailed()         => GD.PrintErr("[Client] Connection FAILED - server unreachable or refused");
}
