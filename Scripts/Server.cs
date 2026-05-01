using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Diagnostics;
using System.Text.Json;

public partial class Server : Node
{
	private ENetMultiplayerPeer _peer;

	// Tracks every peer ID that currently has a player in the world (server = 1).
	private readonly List<long> _connectedPlayers = new();

	// Per-peer connection timestamps (used for session duration in the JSON).
	private readonly Dictionary<long, float> _peerConnectTime = new();

	// ── Metrics: export configuration ───────────────────────────────────────────
	/// <summary>Absolute path for the server-level CSV. Defaults to &lt;user data dir&gt;/enet_server_metrics.csv.</summary>
	[Export] public string MetricsCsvPath { get; set; } = "";
	/// <summary>Absolute path for the per-peer CSV. Defaults to &lt;user data dir&gt;/enet_peer_metrics.csv.</summary>
	[Export] public string PeerCsvPath    { get; set; } = "";
	/// <summary>Identifier written into every CSV/JSON row. Auto-set to UTC timestamp at start if left empty.</summary>
	[Export] public string RunTag         { get; set; } = "";

	private bool _serverCsvReady = false;
	private bool _peerCsvReady   = false;

	// ── Metrics: CPU sampling ────────────────────────────────────────────────────
	private Process  _selfProcess;
	private TimeSpan _prevCpuTime;
	private DateTime _prevCpuWall;
	private int      _logicalCores;

	// ── Metrics: GC baseline (counts at server start; subtracted to get run-relative values) ──
	private int _gcGen0Base, _gcGen1Base, _gcGen2Base;

	// ── Metrics: aggregate bandwidth (Godot 4 ENet binding exposes no byte-total API;
	// bandwidth columns in CSV will be 0 for this transport) ──────────────────

	// ── Metrics: latest system snapshot (populated each StatInterval, read by DumpStatsToJson) ──
	private double _latestCpuPct;
	private long   _latestRamBytes;
	private long   _latestGcHeapBytes;
	private int    _latestGcGen0, _latestGcGen1, _latestGcGen2;

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

		InitMetrics();
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
			CollectAndExportMetrics();
		}

		_jsonTimer += (float)delta;
		if (_jsonTimer >= JsonInterval)
		{
			_jsonTimer = 0f;
			DumpStatsToJson();
		}
	}

	// ─── Metrics: initialisation ─────────────────────────────────────────────

	private void InitMetrics()
	{
		// RunTag defaults to UTC timestamp so each run is uniquely identified.
		if (string.IsNullOrWhiteSpace(RunTag))
			RunTag = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'");

		// CPU sampling bootstrap.
		_selfProcess  = Process.GetCurrentProcess();
		_logicalCores = System.Environment.ProcessorCount;
		_prevCpuTime  = _selfProcess.TotalProcessorTime;
		_prevCpuWall  = DateTime.UtcNow;

		// GC baseline — run-relative increments rather than process-lifetime totals.
		_gcGen0Base = GC.CollectionCount(0);
		_gcGen1Base = GC.CollectionCount(1);
		_gcGen2Base = GC.CollectionCount(2);

		// Write CSV header rows once (only if the file is new/empty — re-runs accumulate).
		EnsureCsvHeader(ResolveServerCsvPath(), ref _serverCsvReady,
			"timestamp_utc,run_tag,solution,uptime_s,player_count," +
			"avg_rtt_ms,max_rtt_ms,min_rtt_ms," +
			"avg_jitter_ms,max_jitter_ms," +
			"avg_packet_loss_pct," +
			"bytes_sent_delta,bytes_recv_delta," +
			"ram_bytes,gc_heap_bytes," +
			"gc_gen0_delta,gc_gen1_delta,gc_gen2_delta," +
			"cpu_pct");

		EnsureCsvHeader(ResolvePeerCsvPath(), ref _peerCsvReady,
			"timestamp_utc,run_tag,solution,uptime_s,peer_id,is_host," +
			"session_duration_s,rtt_ms,jitter_ms,packet_loss_pct," +
			"bytes_sent_delta,bytes_recv_delta");

		GD.Print($"[Metrics] Initialised — RunTag={RunTag}, LogicalCores={_logicalCores}");
		GD.Print($"[Metrics] ServerCSV → {ResolveServerCsvPath()}");
		GD.Print($"[Metrics] PeerCSV   → {ResolvePeerCsvPath()}");
	}

	public string ResolveServerCsvPath() =>
		string.IsNullOrWhiteSpace(MetricsCsvPath)
			? Path.Combine(OS.GetUserDataDir(), "enet_server_metrics.csv")
			: MetricsCsvPath;

	public string ResolvePeerCsvPath() =>
		string.IsNullOrWhiteSpace(PeerCsvPath)
			? Path.Combine(OS.GetUserDataDir(), "enet_peer_metrics.csv")
			: PeerCsvPath;

	private static void EnsureCsvHeader(string path, ref bool ready, string header)
	{
		if (ready) return;
		try
		{
			if (!File.Exists(path) || new FileInfo(path).Length == 0)
				File.WriteAllText(path, header + "\n");
			ready = true;
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[Metrics] Failed to write CSV header for '{path}': {ex.Message}");
		}
	}

	private static void AppendCsvRow(string path, string row)
	{
		try   { File.AppendAllText(path, row + "\n"); }
		catch (Exception ex)
		{
			GD.PrintErr($"[Metrics] CSV append failed for '{path}': {ex.Message}");
		}
	}

	// ─── Metrics: on-demand export to a user-chosen directory ─────────────────

	/// <summary>
	/// Copies both metric CSV files into <paramref name="targetDir"/>.
	/// Called by UIManager when the user confirms the folder picker dialog.
	/// </summary>
	public void ExportMetricsTo(string targetDir)
	{
		TryCopyMetricsFile(ResolveServerCsvPath(), Path.Combine(targetDir, "enet_server_metrics.csv"));
		TryCopyMetricsFile(ResolvePeerCsvPath(),   Path.Combine(targetDir, "enet_peer_metrics.csv"));
	}

	private static void TryCopyMetricsFile(string src, string dst)
	{
		try
		{
			if (!File.Exists(src))
			{
				GD.PrintErr($"[Export] Source file not found: {src}");
				return;
			}
			File.Copy(src, dst, overwrite: true);
			GD.Print($"[Export] Saved: {dst}");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[Export] Failed to copy '{src}' → '{dst}': {ex.Message}");
		}
	}

	// ─── Metrics: sample collection and export ────────────────────────────────

	private void CollectAndExportMetrics()
	{
		string now      = DateTime.UtcNow.ToString("o");
		double uptime   = Math.Round(_elapsedTime, 2);
		int playerCount = _connectedPlayers.Count;

		// ── System metrics ──────────────────────────────────────────────────
		_selfProcess.Refresh();
		long ramBytes    = _selfProcess.WorkingSet64;
		long gcHeapBytes = GC.GetTotalMemory(false);

		int gcGen0 = GC.CollectionCount(0) - _gcGen0Base;
		int gcGen1 = GC.CollectionCount(1) - _gcGen1Base;
		int gcGen2 = GC.CollectionCount(2) - _gcGen2Base;

		TimeSpan curCpuTime  = _selfProcess.TotalProcessorTime;
		DateTime curWall     = DateTime.UtcNow;
		double   deltaCpuMs  = (curCpuTime - _prevCpuTime).TotalMilliseconds;
		double   deltaWallMs = (curWall    - _prevCpuWall).TotalMilliseconds;
		double   cpuPct      = deltaWallMs > 0
			? Math.Min(100.0, deltaCpuMs / (deltaWallMs * _logicalCores) * 100.0)
			: 0.0;
		_prevCpuTime = curCpuTime;
		_prevCpuWall = curWall;

		// Cache for DumpStatsToJson (runs on a different interval).
		_latestCpuPct      = cpuPct;
		_latestRamBytes    = ramBytes;
		_latestGcHeapBytes = gcHeapBytes;
		_latestGcGen0      = gcGen0;
		_latestGcGen1      = gcGen1;
		_latestGcGen2      = gcGen2;

		// ── Per-peer ENet metrics ───────────────────────────────────────────
		double sumRtt = 0, maxRtt = 0, minRtt = double.MaxValue;
		double sumJitter = 0, maxJitter = 0;
		double sumLoss = 0;
		// Bandwidth: Godot 4 ENet binding exposes no byte-total API; reported as 0.
		const double totalSentDelta = 0.0;
		const double totalRecvDelta = 0.0;
		int    clientCount = 0;

		var sb       = new StringBuilder();
		var peerRows = new List<string>();

		sb.AppendLine("═══ Server Statistics ═══");
		sb.AppendLine($"Players : {playerCount} / {MaxPlayers}");
		sb.AppendLine($"Uptime  : {TimeSpan.FromSeconds(_elapsedTime):hh\\:mm\\:ss}");
		sb.AppendLine($"CPU     : {cpuPct:F1}%   RAM: {ramBytes / 1_048_576.0:F1} MB");
		sb.AppendLine($"GC Heap : {gcHeapBytes / 1_048_576.0:F1} MB  (G0:{gcGen0} G1:{gcGen1} G2:{gcGen2})");

		bool hasClients = _connectedPlayers.Exists(id => id != 1);
		if (hasClients)
		{
			sb.AppendLine("─── Peers ───────────────────────────────────────────");
			sb.AppendLine("  ID      RTT    Jitter   Loss");

			foreach (long id in _connectedPlayers)
			{
				if (id == 1) continue;
				var pp = _peer.GetPeer((int)id);
				if (pp == null) continue;

				double rtt    = pp.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
				double jitter = pp.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTimeVariance);
				double loss   = pp.GetStatistic(ENetPacketPeer.PeerStatistic.PacketLoss) / 65536.0 * 100.0;

				// Per-peer byte totals are not available in Godot 4's ENet binding; use 0.
				const double sentDelta = 0.0;
				const double recvDelta = 0.0;

				sumRtt    += rtt;    maxRtt    = Math.Max(maxRtt, rtt);
				minRtt     = Math.Min(minRtt,  rtt);
				sumJitter += jitter; maxJitter = Math.Max(maxJitter, jitter);
				sumLoss   += loss;
				clientCount++;

				float sessionAge = _peerConnectTime.TryGetValue(id, out float t)
					? _elapsedTime - t : 0f;

sb.AppendLine($"  {id,-6}  {rtt,5:F0} ms  {jitter,5:F0} ms  {loss:F1}%");

				peerRows.Add(
					$"{now},{RunTag},enet,{uptime},{id},false," +
					$"{Math.Round(sessionAge, 2)}," +
					$"{Math.Round(rtt, 2)},{Math.Round(jitter, 2)},{Math.Round(loss, 4)}," +
					$"{Math.Round(sentDelta, 0)},{Math.Round(recvDelta, 0)}");
			}
		}

		if (_statsLabel != null)
			_statsLabel.Text = sb.ToString();

		// ── Write CSVs ───────────────────────────────────────────────────────
		double avgRtt    = clientCount > 0 ? sumRtt    / clientCount : 0;
		double avgJitter = clientCount > 0 ? sumJitter / clientCount : 0;
		double avgLoss   = clientCount > 0 ? sumLoss   / clientCount : 0;
		if (clientCount == 0) minRtt = 0;

		AppendCsvRow(ResolveServerCsvPath(),
			$"{now},{RunTag},enet,{uptime},{playerCount}," +
			$"{Math.Round(avgRtt, 2)},{Math.Round(maxRtt, 2)},{Math.Round(minRtt, 2)}," +
			$"{Math.Round(avgJitter, 2)},{Math.Round(maxJitter, 2)}," +
			$"{Math.Round(avgLoss, 4)}," +
			$"{Math.Round(totalSentDelta, 0)},{Math.Round(totalRecvDelta, 0)}," +
			$"{ramBytes},{gcHeapBytes}," +
			$"{gcGen0},{gcGen1},{gcGen2}," +
			$"{Math.Round(cpuPct, 2)}");

		foreach (var row in peerRows)
			AppendCsvRow(ResolvePeerCsvPath(), row);
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
			run_tag        = RunTag,
			solution       = "enet",
			uptime_seconds = Math.Round(_elapsedTime, 2),
			players = new
			{
				total   = _connectedPlayers.Count,
				max     = MaxPlayers,
				clients = _connectedPlayers.Count - 1,   // excludes host
			},
			system = new
			{
				cpu_pct       = Math.Round(_latestCpuPct, 2),
				ram_bytes     = _latestRamBytes,
				gc_heap_bytes = _latestGcHeapBytes,
				gc_gen0_delta = _latestGcGen0,
				gc_gen1_delta = _latestGcGen1,
				gc_gen2_delta = _latestGcGen2,
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
