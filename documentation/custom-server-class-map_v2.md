# Custom C# Server — Class Map & Implementation Status (Schema v1)

This document lists the recommended C# classes to implement the protocol in the current project, and notes their current implementation status.
Scope is 2D-first with 3D-compatible payloads.

> **Status key**
> - ✅ Implemented — class/code exists and is working
> - 🟡 Partial — scaffolding exists but not complete
> - ❌ Not yet implemented — planned only

---

## Godot client-side (this repo)

### `CustomNetworkClient` (✅ implemented — `Scripts/CustomNetworkClient.cs`)

The Godot-side custom transport client. Uses:
- **TLS TCP** (newline-delimited JSON) for reliable commands and room management.
- **AES-256-CBC UDP** for low-latency position/input broadcasts.

Key implemented features:
- TCP connection state machine: `Idle → TcpConnecting → TlsHandshaking → Connected`
- Authentication: `REGISTER`, `LOGIN`, `AUTO_AUTH`, token persistence (`user://mp_token.dat`)
- Room management: `CREATE_ROOM`, `JOIN_ROOM`, `LEAVE_ROOM`, `GET_ROOM_PLAYERS`, `START_GAME`
- Messaging: `MESSAGE` (chat), `RELAY_MESSAGE` (peer relay)
- UDP position broadcast: `SendPosition(Vector3, Quaternion)`
- UDP input relay: `SendInput(Dictionary)`
- Heartbeat keep-alive every **15 s** (server session timeout: **60 s**)
- Graceful disconnect via `BYE` command
- UDP key derivation: `SHA-256(sessionId + sharedSecret)` → 32-byte AES key + 16-byte CBC IV

UDP wire format: `[4-byte LE int32 length][AES-256-CBC ciphertext]`

---

## Server-side (separate repository)

The classes below are the **planned** server-side implementation. They do not exist in this repository.

---

## 1. Core protocol models (DTOs)

### 1.1 Envelope — ❌ not yet implemented

Class: `NetMessageEnvelope`

Purpose:
- Wrap every request, response, and event.

Suggested fields:
- `int SchemaVersion`
- `Guid MessageId`
- `long TimestampMs`
- `string SessionId`
- `string MatchId`
- `string PlayerId`
- `TransportKind Transport`
- `MessageKind MessageType`
- `ActionKind Action`
- `JsonElement Payload` or generic payload type
- `Guid? AckFor`

### 1.2 Shared coordinate model — ❌ not yet implemented (server-side)

> **Note:** The Godot client already uses `Vector3` + `Quaternion` in `CustomNetworkClient.SendPosition`, which serves as the 3D-ready coordinate representation on the client side.

Class: `NetTransform`

Purpose:
- Dimension-agnostic transform payload for 2D now, 3D later.

Suggested fields:
- `Vector3Dto Position`
- `Vector3Dto Rotation`
- `DimensionMode DimensionMode` (`TwoD` or `ThreeD`)

Class: `Vector3Dto`

Suggested fields:
- `float X`
- `float Y`
- `float Z`

### 1.3 Request DTOs — ❌ not yet implemented

Class: `MoveRequest`
- `Vector3Dto InputVector`
- `Vector3Dto Position`
- `int Tick`

Class: `InventoryGrabRequest`
- `string GroundItemId`
- `int TargetSlot`
- `int RequestQty`
- `Vector3Dto PlayerPosition`

Class: `InventoryDropRequest`
- `string InventoryItemId`
- `int SourceSlot`
- `int DropQty`
- `Vector3Dto DropPosition`

Class: `InventoryMoveSlotRequest`
- `int FromSlot`
- `int ToSlot`
- `int MoveQty`
- `bool AllowSwap`

Class: `PlayerKillRequest` (optional)
- `string TargetPlayerId`
- `KillCause Cause`
- `int LocalTick`

### 1.4 Response DTO — ❌ not yet implemented

Class: `ActionResponse`

Suggested fields:
- `ActionResult Result` (`Ok`, `Rejected`, `Error`)
- `RejectReason ReasonCode`
- `int ServerTick`
- `int LatencyMs`
- `long StateRevision`
- `JsonElement Payload` (optional details)

### 1.5 Event DTOs — ❌ not yet implemented

Class: `InventoryChangedEvent`
- `string PlayerId`
- `List<InventorySlotChange> Changes`
- `long StateRevision`

Class: `GroundItemSpawnedEvent`
- `string GroundItemId`
- `string ItemId`
- `int Qty`
- `Vector3Dto Position`
- `string OwnerPlayerId`
- `long StateRevision`

Class: `GroundItemRemovedEvent`
- `string GroundItemId`
- `string RemovedBy`
- `long StateRevision`

Class: `PlayerDiedEvent` (optional)
- `string PlayerId`
- `string? KillerPlayerId`
- `bool DropLoot`
- `long StateRevision`

### 1.6 Snapshot DTOs — ❌ not yet implemented

Class: `SnapshotSyncEvent`
- `List<PlayerStateDto> Players`
- `List<GroundItemDto> GroundItems`
- `InventoryStateDto Inventory`
- `long StateRevision`
- `int ServerTick`

Class: `PlayerStateDto`
- `string PlayerId`
- `NetTransform Transform`
- `bool IsAlive`

Class: `GroundItemDto`
- `string GroundItemId`
- `string ItemId`
- `int Qty`
- `Vector3Dto Position`

Class: `InventoryStateDto`
- `string OwnerPlayerId`
- `List<InventorySlotDto> Slots`

Class: `InventorySlotDto`
- `int Slot`
- `string? ItemId`
- `int Qty`

Class: `InventorySlotChange`
- `int Slot`
- `string? ItemId`
- `int Qty`

## 2. Server domain models (authoritative state)

### 2.1 Player aggregate — ❌ not yet implemented

Class: `PlayerSession`

Purpose:
- Runtime connection/session state for each player.

Suggested fields:
- `string PlayerId`
- `string SessionId`
- `ConnectionHandle Connection`
- `NetTransform LastTransform`
- `Inventory Inventory`
- `bool IsAlive`
- `int LastProcessedClientTick`

### 2.2 Inventory domain — ❌ not yet implemented

Class: `Inventory`
- `int Capacity`
- `InventorySlot[] Slots`
- methods: `TryAdd`, `TryRemove`, `TryMove`, `TrySwap`, `Validate`

Class: `InventorySlot`
- `int Index`
- `string? ItemId`
- `int Qty`

Class: `ItemStack`
- `string ItemId`
- `int Qty`

### 2.3 World item domain — ❌ not yet implemented

Class: `GroundItem`
- `string GroundItemId`
- `string ItemId`
- `int Qty`
- `Vector3Dto Position`
- `string OwnerPlayerId`
- `long SpawnedAtMs`

### 2.4 World state root — ❌ not yet implemented

Class: `WorldState`

Purpose:
- Single authoritative source of game state.

Suggested fields:
- `Dictionary<string, PlayerSession> Players`
- `Dictionary<string, GroundItem> GroundItems`
- `long StateRevision`
- `int ServerTick`

## 3. Server services and handlers

### 3.1 Transport layer abstraction — ❌ not yet implemented

Interface: `ITransportServer`

Methods:
- `Task StartAsync(CancellationToken ct)`
- `Task StopAsync(CancellationToken ct)`
- `Task SendAsync(ConnectionHandle connection, NetMessageEnvelope message, CancellationToken ct)`
- `Task BroadcastAsync(IEnumerable<ConnectionHandle> connections, NetMessageEnvelope message, CancellationToken ct)`

Events:
- `OnClientConnected`
- `OnClientDisconnected`
- `OnMessageReceived`

Implementations:
- `EnetTransportServerAdapter` (benchmark baseline adapter)
- `CustomTransportServer` (your C# server)

### 3.2 Serialization — ❌ not yet implemented

Interface: `IMessageSerializer`
- `byte[] Serialize(NetMessageEnvelope message)`
- `NetMessageEnvelope Deserialize(ReadOnlySpan<byte> data)`

Implementation:
- `SystemTextJsonMessageSerializer`

### 3.3 Routing and action execution — ❌ not yet implemented

Class: `MessageRouter`

Purpose:
- Routes incoming envelopes to action handlers.

Dependencies:
- `Dictionary<ActionKind, IActionHandler>`

Interface: `IActionHandler`
- `Task<ActionResponse> HandleAsync(ActionContext context, CancellationToken ct)`

Concrete handlers:
- `MoveActionHandler`
- `InventoryGrabActionHandler`
- `InventoryDropActionHandler`
- `InventoryMoveSlotActionHandler`
- `PlayerKillActionHandler` (optional)

### 3.4 Validation and anti-cheat — ❌ not yet implemented

Class: `ActionValidator`

Methods:
- `ValidateSession`
- `ValidateRange`
- `ValidateInventorySlots`
- `ValidateQuantity`
- `ValidateRateLimit`
- `ValidateStateRevision`

Class: `IdempotencyService`

Purpose:
- Ignore duplicate `MessageId` values per player/session within a time window.

### 3.5 Concurrency and ordering — ❌ not yet implemented

Class: `TickScheduler`

Purpose:
- Processes actions in deterministic server-tick order.

Class: `ActionQueue`
- Per-player or global queues.

Class: `ConflictResolver`

Purpose:
- Resolves race conditions (for example two players grabbing same ground item).

### 3.6 State change publication — ❌ not yet implemented

Class: `EventPublisher`

Purpose:
- Builds and sends `inventory_changed`, `ground_item_spawned`, `ground_item_removed`, and other events.

Class: `SnapshotService`

Purpose:
- Sends full snapshot on join/reconnect.

## 4. Logging and benchmark instrumentation — ❌ not yet implemented

Class: `BenchmarkMetricsCollector`

Track:
- RTT
- action latency per action type
- rejection counts and reasons
- throughput (messages/sec)
- bandwidth in/out
- CPU and RAM sampling hooks
- state divergence counters

> **Note:** The ENet baseline already collects RTT and packet loss per peer in `Server.UpdateStats()` using `ENetPacketPeer.PeerStatistic`. A structured equivalent for the custom server path is still needed.

Class: `StructuredLogWriter`

Purpose:
- Output CSV or JSON logs tagged with solution type, scenario, player count, and timestamp.

## 5. Recommended enums

Enum: `TransportKind`
- `Enet`
- `Custom`

Enum: `MessageKind`
- `Request`
- `Response`
- `Event`

Enum: `ActionKind`
- `Move`
- `InventoryGrab`
- `InventoryDrop`
- `InventoryMoveSlot`
- `PlayerKill`
- `SnapshotSync`
- `Heartbeat`

Enum: `ActionResult`
- `Ok`
- `Rejected`
- `Error`

Enum: `RejectReason`
- `None`
- `OutOfRange`
- `InvalidSlot`
- `ItemNotFound`
- `Conflict`
- `RateLimited`
- `InvalidState`

Enum: `DimensionMode`
- `TwoD`
- `ThreeD`

Enum: `KillCause`
- `Combat`
- `TestCommand`
- `Environment`

## 6. Suggested project layout (server)

```text
Server/
  Protocol/
    NetMessageEnvelope.cs
    Dtos/
    Enums/
  Domain/
    WorldState.cs
    PlayerSession.cs
    Inventory/
    Items/
  Transport/
    ITransportServer.cs
    CustomTransportServer.cs
    EnetTransportServerAdapter.cs
  Application/
    MessageRouter.cs
    Handlers/
    Validation/
    Scheduling/
    Publishing/
  Observability/
    BenchmarkMetricsCollector.cs
    StructuredLogWriter.cs
```

## 7. Minimum implementation order

1. Envelope + enums + serializer.
2. `WorldState`, `PlayerSession`, `Inventory`, `GroundItem`.
3. `ITransportServer` and one concrete transport.
4. `MessageRouter` + movement handler.
5. Inventory handlers: grab, drop, move slot.
6. Event publisher + snapshot service.
7. Idempotency, conflict resolver, and metrics collector.

## 8. Notes for your current phase

- The Godot client (`CustomNetworkClient.cs`) uses a simpler command-based JSON protocol over TLS TCP for room/auth management; the envelope schema above is intended for game-action traffic only.
- Keep kill/death logic optional until movement and inventory actions are stable.
- Prefer explicit response payloads and reason codes over silent failures.
- For 2D gameplay, still keep position/rotation payload in `Vector3Dto` for forward compatibility.
- The `Client.cs` `NetworkBackend` enum (`BuiltInEnet` / `CustomServer`) is the runtime switch — ensure the selected backend matches the running server before benchmarking.
