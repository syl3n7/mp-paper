using Godot;

public partial class Client : Node
{
    [Export] public string Host { get; set; } = "127.0.0.1";
    [Export] public int    Port { get; set; } = 7777;

    private ENetMultiplayerPeer _peer;

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
        }

        GD.Print($"[Client] isClient={isClient}, Host={Host}, Port={Port}");
        if (!isClient)
        {
            GD.Print("[Client] Not a client instance - skipping connection");
            return;
        }

        ConnectToServer();
    }

    private void ConnectToServer()
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

    private void OnPeerConnected(long id) => GD.Print($"[Client] OnPeerConnected - server peer id: {id}, my id: {Multiplayer.GetUniqueId()}");
    private void OnPeerDisconnected(long id) => GD.Print($"[Client] Disconnected from peer {id}");
    private void OnConnectionFailed()         => GD.PrintErr("[Client] Connection FAILED - server unreachable or refused");
}
