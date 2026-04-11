# mp-paper

> **Research project:** Compare Godot's built-in ENet multiplayer against a custom C#/.NET server solution across latency, throughput, scalability, reliability, and gameplay interaction integrity.

---

## Project overview

| Field | Details |
|---|---|
| **Game engine** | Godot 4.6 (C#) |
| **Assembly name** | `network-comparison` |
| **Target framework** | .NET 8 (net9 for Android) |
| **Custom client transport** | TLS TCP (newline-delimited JSON) + AES-256-CBC UDP |
| **Built-in transport** | Godot High-Level Multiplayer API (ENet) |
| **Gameplay scope** | 2D movement replication |
| **Test players** | 2 → 4 → 8 → 16 → 32 (synthetic, planned) |
| **Paper type** | Short comparative benchmark paper |

---

## Repository structure

```
mp-paper/
├── Scripts/
│   ├── Server.cs               # ENet server — spawns/despawns players, tracks RTT & packet loss
│   ├── Client.cs               # Client entry-point — selects ENet or custom backend at startup
│   ├── Player.cs               # 2D CharacterBody2D with WASD movement and unreliable RPC sync
│   ├── CustomNetworkClient.cs  # Custom transport client (TLS TCP + AES-256-CBC UDP)
│   └── UIManager.cs            # Server HUD — spawn-client buttons and stats label
├── Scenes/
│   └── mp.tscn                 # Main scene
├── Prefabs/
│   └── p_player.tscn           # Instantiated per connected peer
├── Textures/
├── documentation/
│   ├── build options.md        # dotnet build configurations
│   ├── custom-server-class-map.md  # Planned C# server class layout
│   └── headless-mode.md        # Running Godot without a display
├── project.godot
├── mp-paper.csproj             # Godot .NET project with ENet/AltNet build configs
└── network-comparison.csproj   # Secondary build entry
```

---

## Getting started

### Prerequisites

- [Godot 4.6](https://godotengine.org/download) with .NET support enabled
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

### Run the server (editor)

Open the project in Godot and press **Play** (F5). The server starts automatically on `0.0.0.0:7777` (ENet/UDP).

### Run a client (editor)

Click the **"Open Client (Built-in)"** button in the server window to launch an ENet client, or **"Open Client (Custom C# Server)"** to connect via the custom TLS/UDP backend.

---

## Command-line flags

All user arguments must come **after** `--`:

```bash
# Run as server (default — no flags needed)
./network-comparison.x86_64

# Run as a client connecting to the built-in ENet server
./network-comparison.x86_64 -- --client --host 127.0.0.1 --port 7777 --network enet

# Run as a client connecting to the custom C# server
./network-comparison.x86_64 -- --client --host 127.0.0.1 --custom-port 9000 --udp-port 7778 --network custom

# Run in headless mode (no window, no renderer — suitable for server automation)
./network-comparison.x86_64 --headless
```

| Flag | Type | Default | Description |
|---|---|---|---|
| `--client` | flag | — | Activate client mode (skip server startup) |
| `--host <ip>` | string | `127.0.0.1` | Server IP address |
| `--port <n>` | int | `7777` | ENet server port |
| `--custom-port <n>` | int | `9000` | Custom server TCP port |
| `--udp-port <n>` | int | `7778` | Custom server UDP port |
| `--network enet\|custom` | string | `enet` | Network backend selector |

---

## Build configurations

```bash
dotnet build -c ENet      # defines USE_ENET   — uses ENet transport (Server.cs)
dotnet build -c AltNet    # defines USE_ALTNET — uses custom solution (CustomNetworkClient.cs)
dotnet build              # default Debug build (no backend constant defined)
```

See [`documentation/build options.md`](documentation/build%20options.md) for details.

---

## Network backends

### Built-in ENet (baseline)

Implemented in `Server.cs` and `Client.cs`.

- Creates an `ENetMultiplayerPeer` on port **7777** (configurable via `[Export]`).
- Supports up to **32 players** (configurable).
- Server spawns `p_player.tscn` for every connected peer and broadcasts `SpawnPlayerRpc` / `DespawnPlayerRpc` to all clients.
- Position sync uses an unreliable RPC (`SyncPosition`) called every physics frame from the authoritative peer.
- The server HUD shows **RTT (ms)** and **packet loss (%)** per peer, sampled every second.

### Custom C# server (experimental)

Client-side implemented in `CustomNetworkClient.cs`. Server-side is a separate C#/.NET project (not in this repository).

**TCP channel** — TLS, newline-delimited JSON:

| Command | Direction | Description |
|---|---|---|
| `CONNECTED` | server → client | Welcome; carries `sessionId` |
| `REGISTER` | client → server | Create account |
| `LOGIN` | client → server | Authenticate with credentials |
| `AUTO_AUTH` | client → server | Re-authenticate via stored token |
| `LIST_ROOMS` | client → server | List open rooms |
| `CREATE_ROOM` | client → server | Create and join a room |
| `JOIN_ROOM` | client → server | Join an existing room |
| `LEAVE_ROOM` | client → server | Leave current room |
| `GET_ROOM_PLAYERS` | client → server | List players in current room |
| `START_GAME` | client → server | Host starts the session |
| `MESSAGE` | client → server | In-room chat |
| `RELAY_MESSAGE` | client → server | Relay data to a specific peer |
| `PING` / `heartbeat` | client → server | Keep-alive (every 15 s) |
| `BYE` | client → server | Graceful disconnect |

**UDP channel** — AES-256-CBC encrypted, framed as `[4-byte LE length][ciphertext]`:

| Command | Direction | Description |
|---|---|---|
| `UPDATE` | bidirectional | Position + quaternion rotation broadcast |
| `INPUT` | client → server | Raw input relay (for input-authoritative designs) |

UDP key derivation: `SHA-256(sessionId + sharedSecret)` — first 32 bytes = AES-256 key, bytes 16–31 = CBC IV.  
The shared secret must match `SecurityConfig:UdpSharedSecret` in the server's `appsettings.json`.

> **Known issue:** the host may receive `GAME_STARTED` twice (bug in the current server implementation). The client guards against this with a `_gameRunning` flag.

---

## Player movement

`Player.cs` (`CharacterBody2D`):

- Speed: **200 px/s** (exported, adjustable per instance).
- Input map: `W` = UP, `S` = DOWN, `A` = LEFT, `D` = RIGHT.
- Only the **authoritative peer** reads input; puppets receive unreliable `SyncPosition(Vector2)` RPCs every physics frame.
- Input is ignored when the player's window does not have focus (prevents all instances from moving simultaneously when windows overlap).

---

## Server stats HUD

`Server.cs` updates a `StatsLabel` every **1 second** (skipped in headless mode):

```
═══ Server Statistics ═══
Players : 2 / 32
Uptime  : 00:01:34
─── Peers ───────────────
  ID          RTT    Loss
  2           12 ms  0.0%
```

RTT is taken from `ENetPacketPeer.PeerStatistic.RoundTripTime`. Packet loss is taken from `ENetPacketPeer.PeerStatistic.PacketLoss` divided by 65536 (ENet fixed-point scale).

---

## Headless mode

See [`documentation/headless-mode.md`](documentation/headless-mode.md) for full instructions including systemd, cron, and Docker deployment.

Quick start:

```bash
./network-comparison.x86_64 --headless
```

---

## Resources

- [Godot High-Level Multiplayer Docs](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html)
- [Godot ENet Transport Reference](https://docs.godotengine.org/en/stable/classes/class_enetmultiplayerpeer.html)
- [Running Godot headless (server mode)](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_dedicated_servers.html)
- [Custom C# Server Class Map](documentation/custom-server-class-map.md)
- [Research Progress Tracker](RESEARCH_PROGRESS.md)
