using Godot;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

/// <summary>
/// Custom multiplayer client — TLS TCP (newline-delimited JSON) + AES-256-CBC UDP.
/// </summary>
public partial class CustomNetworkClient : Node
{
	// ── Signals ──────────────────────────────────────────────────────────────

	/// <summary>Fired once the server sends the CONNECTED welcome and sessionId is saved.</summary>
	[Signal] public delegate void ServerConnectedEventHandler(string sessionId);

	/// <summary>Fired when the TLS link goes down (connect failure or remote close).</summary>
	[Signal] public delegate void ServerDisconnectedEventHandler();

	/// <summary>Fired for every TCP message that is NOT the initial CONNECTED welcome.</summary>
	[Signal] public delegate void TcpMessageReceivedEventHandler(string rawJson);

	/// <summary>Fired after a successful REGISTER, LOGIN, or AUTO_AUTH.</summary>
	[Signal] public delegate void AuthenticatedEventHandler(string username);

	/// <summary>Fired when AUTO_AUTH fails — caller should show login UI.</summary>
	[Signal] public delegate void AutoAuthFailedEventHandler();

	/// <summary>Fired when REGISTER or LOGIN is explicitly rejected by the server.</summary>
	[Signal] public delegate void AuthFailedEventHandler(string reason);

	// ── Room signals ─────────────────────────────────────────────────────────

	/// <summary>LIST_ROOMS response. roomsJson is the serialised rooms array.</summary>
	[Signal] public delegate void RoomListReceivedEventHandler(string roomsJson);

	/// <summary>Room was successfully created. You are now in it as host.</summary>
	[Signal] public delegate void RoomCreatedEventHandler(string roomId, string roomName);

	/// <summary>Successfully joined an existing room.</summary>
	[Signal] public delegate void RoomJoinedEventHandler(string roomId);

	/// <summary>Successfully left a room.</summary>
	[Signal] public delegate void RoomLeftEventHandler(string roomId);

	/// <summary>GET_ROOM_PLAYERS response. playersJson is the serialised players array.</summary>
	[Signal] public delegate void RoomPlayersReceivedEventHandler(string roomId, string playersJson);

	/// <summary>
	/// GAME_STARTED broadcast. spawnPositionsJson is the serialised spawnPositions dict
	/// keyed by sessionId — look up your own SessionId to find your spawnIndex.
	/// NOTE: the host receives this twice (bug in current server); guard against double-start.
	/// </summary>
	[Signal] public delegate void GameStartedEventHandler(string spawnPositionsJson);

	// ── Phase-3 room signals (handlers ready, server not yet sending them) ───

	/// <summary>Another player joined your room.</summary>
	[Signal] public delegate void PlayerJoinedEventHandler(string playerId, string playerName);

	/// <summary>A player left or disconnected from your room.</summary>
	[Signal] public delegate void PlayerLeftEventHandler(string playerId);

	/// <summary>The game in your room has ended.</summary>
	[Signal] public delegate void GameEndedEventHandler();

	/// <summary>A server ERROR message was returned (usually in response to a room command).</summary>
	[Signal] public delegate void RoomErrorEventHandler(string message);

	/// <summary>An in-room chat message arrived from another player.</summary>
	[Signal] public delegate void ChatReceivedEventHandler(string senderName, string message);

	/// <summary>A RELAY_MESSAGE arrived from another player.</summary>
	[Signal] public delegate void RelayReceivedEventHandler(string senderId, string senderName, string message);

	// ── UDP signals ───────────────────────────────────────────────────────────

	/// <summary>Position update received for another player via UDP.</summary>
	[Signal] public delegate void RemotePositionUpdatedEventHandler(
		string sessionId, Vector3 position, Quaternion rotation);

	// ── Public state ─────────────────────────────────────────────────────────

	public string SessionId       { get; private set; } = "";
	public bool   IsAuthenticated  { get; private set; } = false;
	public string Username         { get; private set; } = "";    public string CurrentRoomId    { get; private set; } = "";    public bool   IsTcpReady       => _connState == ConnState.Connected;

	/// <summary>
	/// Must match <c>SecurityConfig:UdpSharedSecret</c> in the server's appsettings.json.
	/// Set this before calling ConnectToServer().
	/// </summary>
	public string UdpSharedSecret { get; set; } = "change-me-before-deploying";

	// ── Private ───────────────────────────────────────────────────────────────

	private StreamPeerTcp _tcp;
	private StreamPeerTls _tls;
	private readonly List<byte> _tcpBuf = new();

	/// Hard cap to defend against a runaway server sending garbage with no newlines.
	private const int MaxLineBytes = 65_536;

	private const string TokenFile = "user://mp_token.dat";

	/// <summary>Seconds between heartbeat packets. Server drops sessions idle for 60 s.</summary>
	private const double HeartbeatInterval = 15.0;
	private double _hbTimer = 0.0;

	private enum ConnState { Idle, TcpConnecting, TlsHandshaking, Connected, Disconnected }
	private ConnState _connState = ConnState.Idle;

	// ── UDP / crypto ──────────────────────────────────────────────────────────

	private PacketPeerUdp _udp = new();
	private byte[] _aesKey;    // 32 bytes — AES-256
	private byte[] _aesIv;     // 16 bytes — CBC IV (fixed per session)
	private bool   _udpReady = false;
	public  string UdpHost { get; set; } = "127.0.0.1";
	public  int    UdpPort { get; set; } = 7778;
	private string _serverHost = "";

	// ── Public API ────────────────────────────────────────────────────────────

	/// <summary>
	/// Start an async TLS connection. Progress is driven by _Process().
	/// Emits <see cref="ServerConnectedEventHandler"/> on success.
	/// </summary>
	public Error ConnectToServer(string host, int port)
	{
		_tcpBuf.Clear();
		SessionId    = "";
		_serverHost  = host;

		_tcp = new StreamPeerTcp();
		var err = _tcp.ConnectToHost(host, port);
		if (err != Error.Ok)
		{
			GD.PrintErr($"[CustomNet] TCP connect error: {err}");
			return err;
		}

		_connState = ConnState.TcpConnecting;
		GD.Print($"[CustomNet] Connecting to {host}:{port} …");
		return Error.Ok;
	}

	/// <summary>Send a JSON object over the TLS stream (newline-terminated).</summary>
	public void Send(Godot.Collections.Dictionary data)
	{
		if (_tls == null || _connState != ConnState.Connected)
		{
			GD.PrintErr("[CustomNet] Send() called while not connected — ignored");
			return;
		}

		var line = Json.Stringify(data) + "\n";
		_tls.PutData(Encoding.UTF8.GetBytes(line));
	}

	// ── UDP public API ────────────────────────────────────────────────────────

	/// <summary>
	/// Send this player's world position + rotation to the server (broadcast to room).
	/// Call every game frame or at a fixed rate (30–60 Hz recommended; server cap is 120 Hz).
	/// </summary>
	public void SendPosition(Vector3 position, Quaternion rotation)
	{
		if (!_udpReady) return;
		var data = new Godot.Collections.Dictionary
		{
			{ "command",   "UPDATE"    },
			{ "sessionId", SessionId   },
			{ "position",  new Godot.Collections.Dictionary
				{ { "x", position.X }, { "y", position.Y }, { "z", position.Z } } },
			{ "rotation",  new Godot.Collections.Dictionary
				{ { "x", rotation.X }, { "y", rotation.Y },
				  { "z", rotation.Z }, { "w", rotation.W } } },
		};
		_udp.PutPacket(MakeUdpPacket(data));
	}

	/// <summary>
	/// Send raw input to all room members (use when relaying inputs rather than positions).
	/// <paramref name="input"/> is a free-form dictionary, e.g.
	/// <c>{ "throttle": 1.0, "steer": -0.3, "brake": 0.0 }</c>.
	/// </summary>
	public void SendInput(Godot.Collections.Dictionary input)
	{
		if (!_udpReady || string.IsNullOrEmpty(CurrentRoomId)) return;
		var data = new Godot.Collections.Dictionary
		{
			{ "command",   "INPUT"       },
			{ "sessionId", SessionId     },
			{ "roomId",    CurrentRoomId },
			{ "input",     input         },
		};
		_udp.PutPacket(MakeUdpPacket(data));
	}

	// ── Room management ───────────────────────────────────────────────────────

	public void ListRooms()
		=> Send(new Godot.Collections.Dictionary { { "command", "LIST_ROOMS" } });

	public void CreateRoom(string roomName, int maxPlayers = 20)
		=> Send(new Godot.Collections.Dictionary
		{
			{ "command",    "CREATE_ROOM" },
			{ "name",       roomName      },
			{ "maxPlayers", maxPlayers    },
		});

	public void JoinRoom(string roomId)
		=> Send(new Godot.Collections.Dictionary { { "command", "JOIN_ROOM" }, { "roomId", roomId } });

	public void LeaveRoom()
		=> Send(new Godot.Collections.Dictionary { { "command", "LEAVE_ROOM" } });

	public void GetRoomPlayers()
		=> Send(new Godot.Collections.Dictionary { { "command", "GET_ROOM_PLAYERS" } });

	/// <summary>Host only — starts the game for all room members.</summary>
	public void StartGame()
		=> Send(new Godot.Collections.Dictionary { { "command", "START_GAME" } });

	public void SendChat(string message)
		=> Send(new Godot.Collections.Dictionary { { "command", "MESSAGE" }, { "message", message } });

	public void SendRelay(string targetSessionId, string message)
		=> Send(new Godot.Collections.Dictionary
		{
			{ "command",  "RELAY_MESSAGE" },
			{ "targetId", targetSessionId },
			{ "message",  message         },
		});

	// ── AES crypto + UDP helpers ──────────────────────────────────────────────

	private void InitUdpCrypto(string host, int udpPort)
	{
		UdpHost = host;
		UdpPort = udpPort;

		// Key derivation: SHA-256( sessionId + sharedSecret )
		// First 32 bytes → AES-256 key, bytes 16-31 → CBC IV
		var material = Encoding.UTF8.GetBytes(SessionId + UdpSharedSecret);
		var hash     = SHA256.HashData(material);   // 32 bytes
		_aesKey = hash[..32];                       // all 32
		_aesIv  = hash[16..32];                     // bytes 16-31

		_udp.Close();
		_udp.ConnectToHost(UdpHost, UdpPort);
		_udpReady = true;
		GD.Print($"[CustomNet] UDP crypto ready — host: {host}:{udpPort}");
	}

	// PKCS7: pad to next 16-byte boundary
	private static byte[] Pkcs7Pad(byte[] data)
	{
		int padLen = 16 - (data.Length % 16);
		var out_ = new byte[data.Length + padLen];
		data.CopyTo(out_, 0);
		for (int i = data.Length; i < out_.Length; i++)
			out_[i] = (byte)padLen;
		return out_;
	}

	private static byte[] Pkcs7Unpad(byte[] data)
	{
		if (data.Length == 0) return data;
		int padLen = data[^1];
		if (padLen < 1 || padLen > 16 || padLen > data.Length)
			return data;
		return data[..^padLen];
	}

	private byte[] AesEncrypt(string plaintext)
	{
		var padded = Pkcs7Pad(Encoding.UTF8.GetBytes(plaintext));
		using var aes    = Aes.Create();
		aes.Key     = _aesKey;
		aes.IV      = _aesIv;
		aes.Mode    = CipherMode.CBC;
		aes.Padding = PaddingMode.None;   // manual PKCS7 above
		using var enc = aes.CreateEncryptor();
		return enc.TransformFinalBlock(padded, 0, padded.Length);
	}

	private string AesDecrypt(byte[] encrypted)
	{
		using var aes    = Aes.Create();
		aes.Key     = _aesKey;
		aes.IV      = _aesIv;
		aes.Mode    = CipherMode.CBC;
		aes.Padding = PaddingMode.None;
		using var dec = aes.CreateDecryptor();
		var decrypted = dec.TransformFinalBlock(encrypted, 0, encrypted.Length);
		return Encoding.UTF8.GetString(Pkcs7Unpad(decrypted));
	}

	// Wire format: [4-byte LE int32 length][N bytes AES-CBC encrypted JSON]
	private byte[] MakeUdpPacket(Godot.Collections.Dictionary data)
	{
		var encrypted = AesEncrypt(Json.Stringify(data));
		var packet    = new byte[4 + encrypted.Length];
		System.BitConverter.TryWriteBytes(packet, encrypted.Length);  // LE on x86/ARM
		encrypted.CopyTo(packet, 4);
		return packet;
	}

	private Godot.Collections.Dictionary ParseUdpPacket(byte[] raw)
	{
		if (raw.Length < 4) return null;
		int length = System.BitConverter.ToInt32(raw, 0);
		if (length <= 0 || length != raw.Length - 4) return null;
		var jsonStr = AesDecrypt(raw[4..]);
		if (string.IsNullOrEmpty(jsonStr)) return null;
		var parsed = Json.ParseString(jsonStr);
		return parsed.VariantType == Variant.Type.Dictionary
			? parsed.AsGodotDictionary()
			: null;
	}

	private void PollUdp()
	{
		while (_udp.GetAvailablePacketCount() > 0)
		{
			var msg = ParseUdpPacket(_udp.GetPacket());
			if (msg == null) continue;

			var cmd = msg.ContainsKey("command") ? msg["command"].AsString() : "";
			switch (cmd)
			{
				case "UPDATE":
				{
					var senderId = msg.ContainsKey("sessionId") ? msg["sessionId"].AsString() : "";
					if (senderId == SessionId) continue;   // ignore own echo (server shouldn't send, but be safe)

					var posD = msg["position"].AsGodotDictionary();
					var rotD = msg["rotation"].AsGodotDictionary();
					var pos  = new Vector3(
						posD["x"].AsSingle(), posD["y"].AsSingle(), posD["z"].AsSingle());
					var rot  = new Quaternion(
						rotD["x"].AsSingle(), rotD["y"].AsSingle(),
						rotD["z"].AsSingle(), rotD["w"].AsSingle());
					EmitSignal(SignalName.RemotePositionUpdated, senderId, pos, rot);
					break;
				}
				// INPUT packets from other players are forwarded as raw JSON for flexibility
				case "INPUT":
					EmitSignal(SignalName.TcpMessageReceived, Json.Stringify(msg));
					break;
			}
		}
	}

	// ── Auth ─────────────────────────────────────────────────────────────────

	/// <summary>
	/// Try to re-authenticate silently using a saved token.
	/// Emits <see cref="AuthenticatedEventHandler"/> on success,
	/// <see cref="AutoAuthFailedEventHandler"/> if no token exists or the server rejects it.
	/// </summary>
	public void TryAutoAuth()
	{
		var token = LoadToken();
		if (string.IsNullOrEmpty(token))
		{
			GD.Print("[CustomNet] No stored token — AUTO_AUTH skipped");
			EmitSignal(SignalName.AutoAuthFailed);
			return;
		}
		Send(new Godot.Collections.Dictionary { { "command", "AUTO_AUTH" }, { "token", token } });
	}

	/// <summary>Register a new account. Emits <see cref="AuthenticatedEventHandler"/> on success.</summary>
	public void Register(string username, string password, string email = "")
	{
		Send(new Godot.Collections.Dictionary
		{
			{ "command",  "REGISTER" },
			{ "username", username   },
			{ "password", password   },
			{ "email",    email      },
		});
	}

	/// <summary>Log in with existing credentials. Emits <see cref="AuthenticatedEventHandler"/> on success.</summary>
	public void Login(string username, string password)
	{
		Send(new Godot.Collections.Dictionary
		{
			{ "command",  "LOGIN"    },
			{ "username", username   },
			{ "password", password   },
		});
	}

	// ── Token storage ─────────────────────────────────────────────────────────

	private void SaveToken(string token)
	{
		using var f = FileAccess.Open(TokenFile, FileAccess.ModeFlags.Write);
		if (f != null)
			f.StoreString(token);
		else
			GD.PrintErr($"[CustomNet] Could not write token file: {FileAccess.GetOpenError()}");
	}

	private string LoadToken()
	{
		if (!FileAccess.FileExists(TokenFile))
			return "";
		using var f = FileAccess.Open(TokenFile, FileAccess.ModeFlags.Read);
		return f != null ? f.GetAsText().StripEdges() : "";
	}

	private void ClearToken()
	{
		if (FileAccess.FileExists(TokenFile))
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(TokenFile));
	}

	// ── Heartbeat ─────────────────────────────────────────────────────────────

	private void SendHeartbeat()
	{
		if (IsAuthenticated)
			Send(new Godot.Collections.Dictionary
			{
				{ "action",    "heartbeat"                    },
				{ "messageId", Time.GetTicksMsec().ToString() },
			});
		else
			Send(new Godot.Collections.Dictionary { { "command", "PING" } });
	}

	// ── Disconnect ────────────────────────────────────────────────────────────

	/// <summary>
	/// Send BYE then tear down cleanly. Prefer this over <see cref="Disconnect"/> when
	/// the session is active so the server can clean up the room immediately.
	/// </summary>
	public void DisconnectGracefully()
	{
		if (_connState == ConnState.Connected)
		{
			try { Send(new Godot.Collections.Dictionary { { "command", "BYE" } }); }
			catch { /* ignore — socket may already be closing */ }
		}
		Disconnect();
	}

	/// <summary>Force-close without sending BYE. Use DisconnectGracefully() when possible.</summary>
	public void Disconnect()
	{
		_tls?.DisconnectFromStream();
		_tcp?.DisconnectFromHost();
		_udp.Close();
		_tls = null;
		_tcp = null;
		_udpReady       = false;
		_hbTimer        = 0.0;
		_connState      = ConnState.Idle;
		SessionId       = "";
		IsAuthenticated = false;
		Username        = "";
		CurrentRoomId   = "";
	}

	// ── _Process state machine ────────────────────────────────────────────────

	public override void _Process(double delta)
	{
		switch (_connState)
		{
			case ConnState.TcpConnecting:   StepTcpConnect();   break;
			case ConnState.TlsHandshaking:  StepTlsHandshake(); break;
			case ConnState.Connected:       StepPollTls();      break;
		}
		if (_udpReady)
			PollUdp();

		// Heartbeat — keep the session alive (server timeout = 60 s)
		if (_connState == ConnState.Connected && !string.IsNullOrEmpty(SessionId))
		{
			_hbTimer += delta;
			if (_hbTimer >= HeartbeatInterval)
			{
				_hbTimer = 0.0;
				SendHeartbeat();
			}
		}
	}

	private void StepTcpConnect()
	{
		_tcp.Poll();
		var status = _tcp.GetStatus();

		if (status == StreamPeerTcp.Status.Connecting)
			return; // still in flight

		if (status != StreamPeerTcp.Status.Connected)
		{
			GD.PrintErr("[CustomNet] TCP connection failed");
			_connState = ConnState.Disconnected;
			EmitSignal(SignalName.ServerDisconnected);
			return;
		}

		// TCP done — layer TLS on top.
		_tls = new StreamPeerTls();
		var err = _tls.ConnectToStream(_tcp, _serverHost, TlsOptions.ClientUnsafe(null));
		if (err != Error.Ok)
		{
			GD.PrintErr($"[CustomNet] TLS start error: {err}");
			_connState = ConnState.Disconnected;
			EmitSignal(SignalName.ServerDisconnected);
			return;
		}

		_connState = ConnState.TlsHandshaking;
		GD.Print("[CustomNet] TCP connected — TLS handshaking …");
	}

	private void StepTlsHandshake()
	{
		_tls.Poll();
		var status = _tls.GetStatus();

		if (status == StreamPeerTls.Status.Handshaking)
			return; // still negotiating

		if (status != StreamPeerTls.Status.Connected)
		{
			GD.PrintErr("[CustomNet] TLS handshake failed");
			_connState = ConnState.Disconnected;
			EmitSignal(SignalName.ServerDisconnected);
			return;
		}

		_connState = ConnState.Connected;
		GD.Print("[CustomNet] TLS handshake complete — waiting for CONNECTED welcome …");
	}

	private void StepPollTls()
	{
		_tls.Poll();

		// Detect remote close.
		if (_tls.GetStatus() != StreamPeerTls.Status.Connected)
		{
			GD.Print("[CustomNet] TLS link lost");
			_connState = ConnState.Disconnected;
			EmitSignal(SignalName.ServerDisconnected);
			return;
		}

		// Read bytes until we have a complete '\n'-terminated line.
		while (_tls.GetAvailableBytes() > 0)
		{
			var result = _tls.GetData(1);
			if ((Error)(long)result[0] != Error.Ok)
				break;

			byte b = result[1].AsByteArray()[0];

			if (b == (byte)'\n')
			{
				if (_tcpBuf.Count > 0)
				{
					var raw = Encoding.UTF8.GetString(_tcpBuf.ToArray());
					_tcpBuf.Clear();
					OnRawMessage(raw);
				}
			}
			else
			{
				// Guard against malicious/broken server sending huge lines.
				if (_tcpBuf.Count < MaxLineBytes)
					_tcpBuf.Add(b);
			}
		}
	}

	// ── Message dispatch ──────────────────────────────────────────────────────

	private void OnRawMessage(string raw)
	{
		var parsed = Json.ParseString(raw);
		if (parsed.VariantType != Variant.Type.Dictionary)
		{
			GD.PrintErr($"[CustomNet] Non-JSON message ignored: {raw}");
			return;
		}

		var msg     = parsed.AsGodotDictionary();
		var command = msg.ContainsKey("command") ? msg["command"].AsString() : "";

		if (command == "CONNECTED")
		{
			if (!msg.ContainsKey("sessionId"))
			{
				GD.PrintErr("[CustomNet] CONNECTED message missing sessionId");
				return;
			}
			SessionId = msg["sessionId"].AsString();
			GD.Print($"[CustomNet] Session established — sessionId: {SessionId}");
			EmitSignal(SignalName.ServerConnected, SessionId);
			return;
		}

		// ── Auth responses ────────────────────────────────────────────────────
		switch (command)
		{
			case "REGISTER_OK":
			case "LOGIN_OK":
				Username = msg.ContainsKey("username") ? msg["username"].AsString() : "";
				IsAuthenticated = true;
				if (msg.ContainsKey("token"))
					SaveToken(msg["token"].AsString());
				GD.Print($"[CustomNet] Auth OK ({command}) — username: {Username}");
				InitUdpCrypto(_tcp?.GetConnectedHost() ?? UdpHost, UdpPort);
				EmitSignal(SignalName.Authenticated, Username);
				return;

			case "AUTO_AUTH_OK":
				Username = msg.ContainsKey("username") ? msg["username"].AsString() : "";
				IsAuthenticated = true;
				GD.Print($"[CustomNet] AUTO_AUTH_OK — username: {Username}");
				InitUdpCrypto(_tcp?.GetConnectedHost() ?? UdpHost, UdpPort);
				EmitSignal(SignalName.Authenticated, Username);
				return;

			case "AUTO_AUTH_FAILED":
				GD.Print("[CustomNet] AUTO_AUTH_FAILED — clearing stored token");
				ClearToken();
				EmitSignal(SignalName.AutoAuthFailed);
				return;

			case "REGISTER_FAILED":
			case "LOGIN_FAILED":
			{
				var reason = msg.ContainsKey("message") ? msg["message"].AsString() : command;
				GD.PrintErr($"[CustomNet] Auth failed: {reason}");
				EmitSignal(SignalName.AuthFailed, reason);
				return;
			}
		}

		// ── Heartbeat / keep-alive responses (silent) ─────────────────────────
		if (command is "PONG" or "HEARTBEAT_ACK" or "BYE_OK")
			return;

		// ── Room responses ────────────────────────────────────────────────────
		switch (command)
		{
			case "ROOM_LIST":
			{
				var roomsJson = msg.ContainsKey("rooms") ? Json.Stringify(msg["rooms"]) : "[]";
				EmitSignal(SignalName.RoomListReceived, roomsJson);
				return;
			}
			case "ROOM_CREATED":
			{
				CurrentRoomId = msg.ContainsKey("roomId") ? msg["roomId"].AsString() : "";
				var name = msg.ContainsKey("name") ? msg["name"].AsString() : "";
				GD.Print($"[CustomNet] Room created: {CurrentRoomId} ('{name}')");
				EmitSignal(SignalName.RoomCreated, CurrentRoomId, name);
				return;
			}
			case "JOIN_OK":
			{
				CurrentRoomId = msg.ContainsKey("roomId") ? msg["roomId"].AsString() : "";
				GD.Print($"[CustomNet] Joined room: {CurrentRoomId}");
				EmitSignal(SignalName.RoomJoined, CurrentRoomId);
				return;
			}
			case "LEAVE_OK":
			{
				var left = msg.ContainsKey("roomId") ? msg["roomId"].AsString() : CurrentRoomId;
				GD.Print($"[CustomNet] Left room: {left}");
				CurrentRoomId = "";
				EmitSignal(SignalName.RoomLeft, left);
				return;
			}
			case "ROOM_PLAYERS":
			{
				var roomId      = msg.ContainsKey("roomId")  ? msg["roomId"].AsString()        : CurrentRoomId;
				var playersJson = msg.ContainsKey("players") ? Json.Stringify(msg["players"])   : "[]";
				EmitSignal(SignalName.RoomPlayersReceived, roomId, playersJson);
				return;
			}
			case "GAME_STARTED":
			{
				var spawnJson = msg.ContainsKey("spawnPositions") ? Json.Stringify(msg["spawnPositions"]) : "{}";
				GD.Print("[CustomNet] GAME_STARTED received");
				EmitSignal(SignalName.GameStarted, spawnJson);
				return;
			}
			// Phase-3 — handlers ready, server not yet sending these
			case "PLAYER_JOINED":
			{
				var pid   = msg.ContainsKey("playerId")   ? msg["playerId"].AsString()   : "";
				var pname = msg.ContainsKey("playerName") ? msg["playerName"].AsString() : "";
				EmitSignal(SignalName.PlayerJoined, pid, pname);
				return;
			}
			case "PLAYER_LEFT":
			{
				var pid = msg.ContainsKey("playerId") ? msg["playerId"].AsString() : "";
				EmitSignal(SignalName.PlayerLeft, pid);
				return;
			}
			case "GAME_ENDED":
				EmitSignal(SignalName.GameEnded);
				return;
			case "CHAT":
			{
				var sender = msg.ContainsKey("senderName") ? msg["senderName"].AsString() : "";
				var text   = msg.ContainsKey("message")    ? msg["message"].AsString()    : "";
				EmitSignal(SignalName.ChatReceived, sender, text);
				return;
			}
			case "RELAYED_MESSAGE":
			{
				var sid   = msg.ContainsKey("senderId")   ? msg["senderId"].AsString()   : "";
				var sname = msg.ContainsKey("senderName") ? msg["senderName"].AsString() : "";
				var text  = msg.ContainsKey("message")    ? msg["message"].AsString()    : "";
				EmitSignal(SignalName.RelayReceived, sid, sname, text);
				return;
			}
			case "ERROR":
			{
				var errMsg = msg.ContainsKey("message") ? msg["message"].AsString() : "Unknown error";
				GD.PrintErr($"[CustomNet] Server ERROR: {errMsg}");
				EmitSignal(SignalName.RoomError, errMsg);
				return;
			}
		}

		// Anything unrecognised is forwarded for custom handling.
		EmitSignal(SignalName.TcpMessageReceived, raw);
	}

	// ── Cleanup ───────────────────────────────────────────────────────────────

	public override void _ExitTree()
	{
		DisconnectGracefully();
	}
}
