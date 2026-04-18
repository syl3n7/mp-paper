using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;

public partial class Server : Node
{
	private ENetMultiplayerPeer _peer;

	// Tracks every peer ID that currently has a player in the world (server = 1).
	private readonly List<long> _connectedPlayers = new();

	// Per-peer connection timestamps (used for session duration in the JSON).
	private readonly Dictionary<long, float> _peerConnectTime = new();

	// Stats HUD
	private Label _statsLabel;
	private float _elapsedTime = 0f;
	private float _statTimer   = 0f;
	private const float StatInterval = 1.0f;

	// JSON export
	private float _jsonTimer = 0f;
	[Export] public float JsonInterval { get; set; } = 5.0f;  // seconds between writes
	/// <summary>
	/// Absolute path for the stats JSON file.
	/// Defaults to &lt;user data dir&gt;/server_stats.json when left empty.
	/// </summary>
	[Export] public string StatsFilePath { get; set; } = "";

	[Export] public int Port       { get; set; } = 7777;
	[Export] public int MaxPlayers { get; set; } = 32;

	public override void _Ready()
	{
		GD.Print("[Server] _Ready() called");
		var args = OS.GetCmdlineUserArgs();
		GD.Print($"[Server] Command-line user args: [{string.Join(", ", args)}]");

		foreach (var arg in args)
		{
			if (arg == "--client" || arg == "--bot")
			{
				GD.Print("[Server] client/bot flag detected - skipping server startup");
				return;
			}
		}

		StartServer();
	}

	// ─── Server startup ───────────────────────────────────────────────────────

	private void StartServer()
	{
		GD.Print($"[Server] Starting server on port {Port} (max {MaxPlayers} players)...");
		_peer = new ENetMultiplayerPeer();
		Error err = _peer.CreateServer(Port, MaxPlayers);
		GD.Print($"[Server] CreateServer result: {err}");
		if (err != Error.Ok)
		{
			GD.PrintErr($"[Server] Failed to create server: {err}");
			return;
		}

		Multiplayer.MultiplayerPeer = _peer;
		GD.Print($"[Server] Running. My peer ID: {Multiplayer.GetUniqueId()}");

		_peer.PeerConnected    += OnPeerConnected;
		_peer.PeerDisconnected += OnPeerDisconnected;

		// Spawn the server's own player (peer 1) so the host can also move around.
		SpawnPlayerLocally(1);
		_connectedPlayers.Add(1);
		_peerConnectTime[1] = 0f;
		GD.Print("[Server] Server player (Player_1) spawned");

		SetupStatsHud();
	}

	// ─── Stats HUD ───────────────────────────────────────────────────────────

	private void SetupStatsHud()
	{
		// No window in headless mode — skip UI entirely.
		if (DisplayServer.GetName() == "headless") return;

		// The UIManager (sibling via CanvasLayer) owns the VBoxContainer;
		// it creates StatsLabel inside it during its own _Ready().
		var ui = GetNodeOrNull<UIManager>("../CanvasLayer/UIManager");
		if (ui != null)
			_statsLabel = ui.StatsLabel;
		else
			GD.PrintErr("[Server] SetupStatsHud: could not find UIManager node");
	}

	public override void _Process(double delta)
	{
		if (_peer == null) return;

		_elapsedTime += (float)delta;

		_statTimer += (float)delta;
		if (_statTimer >= StatInterval)
		{
			_statTimer = 0f;
			UpdateStats();
		}

		_jsonTimer += (float)delta;
		if (_jsonTimer >= JsonInterval)
		{
			_jsonTimer = 0f;
			DumpStatsToJson();
		}
	}

	private void UpdateStats()
	{
		int players = _connectedPlayers.Count;
		var uptime  = TimeSpan.FromSeconds(_elapsedTime);

		var sb = new StringBuilder();
		sb.AppendLine("═══ Server Statistics ═══");
		sb.AppendLine($"Players : {players} / {MaxPlayers}");
		sb.AppendLine($"Uptime  : {uptime:hh\\:mm\\:ss}");

		bool hasClients = _connectedPlayers.Exists(id => id != 1);
		if (hasClients)
		{
			sb.AppendLine("─── Peers ───────────────────────────────");
			sb.AppendLine("  ID          RTT    Jitter   Loss");
			foreach (long id in _connectedPlayers)
			{
				if (id == 1) continue;
				var pp = _peer.GetPeer((int)id);
				if (pp == null) continue;
				double rtt    = pp.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
				double jitter = pp.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTimeVariance);
				double loss   = pp.GetStatistic(ENetPacketPeer.PeerStatistic.PacketLoss) / 65536.0 * 100.0;
				sb.AppendLine($"  {id,-8}  {rtt,5:F0} ms  {jitter,5:F0} ms  {loss:F1}%");
			}
		}

		if (_statsLabel != null)
			_statsLabel.Text = sb.ToString();
	}

	private void DumpStatsToJson()
	{
		// Build the peer array.
		var peers = new List<object>();
		foreach (long id in _connectedPlayers)
		{
			double rtt = 0, jitter = 0, loss = 0;

			if (id != 1)   // peer 1 is the local host — no ENetPacketPeer entry
			{
				var pp = _peer.GetPeer((int)id);
				if (pp != null)
				{
					rtt     = pp.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
					jitter  = pp.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTimeVariance);
					loss    = pp.GetStatistic(ENetPacketPeer.PeerStatistic.PacketLoss) / 65536.0 * 100.0;
				}
			}

			float sessionAge = _peerConnectTime.TryGetValue(id, out float t) ? _elapsedTime - t : 0f;

			peers.Add(new
			{
				id,
				is_host          = id == 1,
				session_duration = Math.Round(sessionAge, 2),
				rtt_ms           = Math.Round(rtt,    2),
				jitter_ms        = Math.Round(jitter, 2),
				packet_loss_pct  = Math.Round(loss,   4),
			});
		}

		var doc = new
		{
			timestamp      = DateTime.UtcNow.ToString("o"),
			uptime_seconds = Math.Round(_elapsedTime, 2),
			players = new
			{
				total   = _connectedPlayers.Count,
				max     = MaxPlayers,
				clients = _connectedPlayers.Count - 1,   // excludes host
			},
			peers,
		};

		string json = JsonSerializer.Serialize(doc, new JsonSerializerOptions { WriteIndented = true });

		// Resolve the output path.
		string path = string.IsNullOrWhiteSpace(StatsFilePath)
			? Path.Combine(OS.GetUserDataDir(), "server_stats.json")
			: StatsFilePath;

		try
		{
			File.WriteAllText(path, json);
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[Server] Failed to write stats JSON to '{path}': {ex.Message}");
		}
	}

	// ─── Peer lifecycle ───────────────────────────────────────────────────────

	private void OnPeerConnected(long id)
	{
		GD.Print($"[Server] Peer {id} connected. Existing players: [{string.Join(", ", _connectedPlayers)}]");

		// 1. Tell the new peer to spawn every player that already exists.
		foreach (long existingId in _connectedPlayers)
		{
			GD.Print($"[Server] → telling peer {id} to spawn Player_{existingId}");
			RpcId(id, nameof(SpawnPlayerRpc), existingId);
		}

		// 2. Spawn the new player on the server.
		SpawnPlayerLocally(id);
		_connectedPlayers.Add(id);
		_peerConnectTime[id] = _elapsedTime;

		// 3. Tell ALL peers (old clients + new one) to spawn the new player.
		//    Old clients get a puppet; new peer gets its own authoritative node.
		GD.Print($"[Server] Broadcasting SpawnPlayerRpc({id}) to all peers");
		Rpc(nameof(SpawnPlayerRpc), id);
	}

	private void OnPeerDisconnected(long id)
	{
		GD.Print($"[Server] Peer {id} disconnected");
		_connectedPlayers.Remove(id);
		_peerConnectTime.Remove(id);

		// Remove locally on the server.
		GetNodeOrNull($"Player_{id}")?.QueueFree();
		GD.Print($"[Server] Removed Player_{id} locally");

		// Tell all remaining clients to remove it too.
		Rpc(nameof(DespawnPlayerRpc), id);
	}

	// ─── Shared spawn/despawn helpers (run on every instance) ────────────────

	private void SpawnPlayerLocally(long clientId)
	{
		GD.Print($"[Network] SpawnPlayerLocally: creating Player_{clientId}");
		var playerScene = ResourceLoader.Load<PackedScene>("res://Prefabs/p_player.tscn");
		if (playerScene == null)
		{
			GD.PrintErr("[Network] Could not load p_player.tscn!");
			return;
		}

		var instance = playerScene.Instantiate();
		instance.Name = $"Player_{clientId}";
		AddChild(instance);
		instance.SetMultiplayerAuthority((int)clientId);
		GD.Print($"[Network] Player_{clientId} added to tree with authority={clientId}");
	}

	/// <summary>
	/// Received on ALL clients when a new peer joins — each instance spawns a
	/// copy of that player.  The instance whose peer ID matches clientId will
	/// be the authority (can move it); everyone else gets a puppet.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void SpawnPlayerRpc(long clientId)
	{
		GD.Print($"[Network] SpawnPlayerRpc({clientId}) received on this instance");
		SpawnPlayerLocally(clientId);
	}

	/// <summary>
	/// Received on ALL clients when a peer disconnects — removes that player
	/// from the local scene tree.
	/// </summary>
	[Rpc(MultiplayerApi.RpcMode.Authority)]
	public void DespawnPlayerRpc(long clientId)
	{
		GD.Print($"[Network] DespawnPlayerRpc({clientId}) received — removing Player_{clientId}");
		GetNodeOrNull($"Player_{clientId}")?.QueueFree();
	}
}
