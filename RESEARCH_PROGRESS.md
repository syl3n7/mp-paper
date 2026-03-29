# 🎮 Multiplayer Server Benchmark — Research Progress

> **Goal:** Compare a custom C#/.NET server solution against Godot's built-in multiplayer (ENet) across latency, throughput, scalability, reliability, and gameplay interaction integrity (movement + inventory actions).

---

## 📁 Project Overview

| Field | Details |
|---|---|
| **Game Engine** | Godot (client-side) |
| **Custom Server** | C# / .NET |
| **Built-in Solution** | Godot High-Level Multiplayer API (ENet) |
| **Primary Gameplay Scope** | 2D multiplayer interactions |
| **Compatibility Goal** | 3D-compatible protocol/architecture (optional in current phase) |
| **Test Players** | 2 → 4 → 8 → 16 → 32 (synthetic) |
| **Paper Type** | Short comparative benchmark paper |

---

## ✅ Phase 0 — Functional Requirements (Gameplay Workload)

### Core interactions to implement and benchmark
- [ ] Player movement replication
- [ ] Inventory grab (pick item from ground into inventory)
- [ ] Inventory drop (remove from inventory and spawn on ground)
- [ ] Inventory slot move (move/swap/stack inside inventory)
- [ ] Player kill/death event (lower priority, include if time permits)

### Server authority and integrity (C# server requirement)
- [ ] C# server is authoritative for world state and inventory state
- [ ] Client sends action requests only; server validates and applies state changes
- [ ] No item duplication under concurrent pickup/drop operations
- [ ] Rejected actions return explicit reason (out of range, invalid slot, missing item, etc.)
- [ ] Action processing order is deterministic and logged

### Dimension scope (2D now, 3D-ready design)
- [ ] 2D is the default and required benchmark target
- [ ] Network message schema is dimension-agnostic where possible (Vector2 now, extensible to Vector3)
- [ ] Shared action contracts avoid hardcoding 2D-only assumptions outside movement payload
- [ ] Document what would be needed to run the same benchmark in 3D later

### Network action schema (v1 draft)

Use a single message envelope for both ENet baseline and custom C# server tests.

#### Envelope (all messages)

```json
{
  "schemaVersion": 1,
  "messageId": "uuid",
  "timestampMs": 1711700000000,
  "sessionId": "string",
  "matchId": "string",
  "playerId": "string",
  "transport": "enet|custom",
  "messageType": "request|response|event",
  "action": "move|inventory_grab|inventory_drop|inventory_move_slot|player_kill|snapshot_sync|heartbeat",
  "payload": {},
  "ackFor": "uuid-or-null"
}
```

#### Coordinate payload (2D now, 3D-ready)

```json
{
  "position": { "x": 12.5, "y": 7.25, "z": 0.0 },
  "rotation": { "x": 0.0, "y": 0.0, "z": 1.57 },
  "dimensionMode": "2d|3d"
}
```

Rules:
- In 2D mode, `z` is sent as `0.0` for compatibility.
- In 3D mode, all axes are active without changing schema shape.

#### Request messages (client -> server)

`move`

```json
{
  "inputVector": { "x": 1.0, "y": 0.0, "z": 0.0 },
  "position": { "x": 24.0, "y": 9.5, "z": 0.0 },
  "tick": 10234
}
```

`inventory_grab`

```json
{
  "groundItemId": "item-uuid",
  "targetSlot": 5,
  "requestQty": 1,
  "playerPosition": { "x": 10.0, "y": 2.0, "z": 0.0 }
}
```

`inventory_drop`

```json
{
  "inventoryItemId": "inv-item-uuid",
  "sourceSlot": 5,
  "dropQty": 1,
  "dropPosition": { "x": 11.0, "y": 2.0, "z": 0.0 }
}
```

`inventory_move_slot`

```json
{
  "fromSlot": 5,
  "toSlot": 9,
  "moveQty": 1,
  "allowSwap": true
}
```

`player_kill` (optional in this phase)

```json
{
  "targetPlayerId": "player-uuid",
  "cause": "combat|test_command|environment",
  "localTick": 10300
}
```

#### Response messages (server -> requesting client)

```json
{
  "result": "ok|rejected|error",
  "reasonCode": "none|out_of_range|invalid_slot|item_not_found|conflict|rate_limited|invalid_state",
  "serverTick": 20500,
  "latencyMs": 42,
  "stateRevision": 9912,
  "payload": {}
}
```

#### Event messages (server -> all relevant clients)

`inventory_changed`

```json
{
  "playerId": "player-uuid",
  "changes": [
    { "slot": 5, "itemId": "apple", "qty": 2 },
    { "slot": 9, "itemId": "wood", "qty": 1 }
  ],
  "stateRevision": 9913
}
```

`ground_item_spawned`

```json
{
  "groundItemId": "ground-uuid",
  "itemId": "apple",
  "qty": 1,
  "position": { "x": 11.0, "y": 2.0, "z": 0.0 },
  "ownerPlayerId": "player-uuid",
  "stateRevision": 9913
}
```

`ground_item_removed`

```json
{
  "groundItemId": "ground-uuid",
  "removedBy": "player-uuid",
  "stateRevision": 9914
}
```

`player_died` (optional)

```json
{
  "playerId": "player-uuid",
  "killerPlayerId": "player-uuid-or-null",
  "dropLoot": true,
  "stateRevision": 9920
}
```

#### Snapshot sync message (join/reconnect)

```json
{
  "players": [],
  "groundItems": [],
  "inventory": {
    "ownerPlayerId": "player-uuid",
    "slots": []
  },
  "stateRevision": 10000,
  "serverTick": 21000
}
```

#### Validation and ordering rules

- The C# server is authoritative and applies actions in server tick order.
- `stateRevision` increments on every accepted world/inventory mutation.
- Duplicate `messageId` from same player/session is ignored (idempotency window).
- Simultaneous grab of same ground item: first valid action wins, later actions receive `conflict`.
- Any client state older than current `stateRevision` must be reconciled via delta or snapshot.

---

## ✅ Phase 1 — Setup & Infrastructure

### Environment
- [ ] Development machine specs documented (CPU, RAM, OS)
- [ ] Godot version pinned and noted
- [ ] .NET version pinned and noted
- [ ] Network configuration defined (localhost / LAN / loopback)
- [ ] All tests confirmed to run on the **same hardware** for fairness

### Godot Built-in Multiplayer (Baseline)
- [ ] Multiplayer example scene running and stable
- [ ] ENet transport confirmed as the active transport layer
- [ ] Server and client roles working correctly
- [ ] Runtime selector working on client (Built-in vs Custom backend)
- [ ] Tick rate defined and consistent (e.g. 20 or 30 ticks/sec)
- [ ] Baseline logging added (timestamps per packet)

### Custom C#/.NET Server
- [ ] Server project created and builds successfully
- [ ] Transport protocol chosen and documented (TCP / UDP / LiteNetLib / other)
- [ ] Server accepts client connections from Godot client
- [ ] Basic message round-trip working (ping/pong or state sync)
- [ ] Movement action flow implemented end-to-end
- [ ] Inventory action flows implemented end-to-end (grab/drop/slot move)
- [ ] (Optional) kill/death action flow implemented
- [ ] Tick rate matches the Godot baseline
- [ ] Logging added (timestamps, player ID, message type)

---

## ✅ Phase 2 — Synthetic Player Setup

### Strategy chosen
- [ ] Decided on synthetic player method:
  - [ ] Option A — Headless Godot instances (`--headless` flag)
  - [ ] Option B — Multiple simulated clients in a single Godot scene
  - [ ] Option C — Standalone C# console load tester (recommended for scale)
  - [ ] Option D — Hybrid (Godot bots + C# stress tester)

### Bot / Synthetic Client Implementation
- [ ] Synthetic player sends position updates at fixed tick rate
- [ ] Synthetic player receives and processes state from server
- [ ] Synthetic player can execute scripted inventory grab actions
- [ ] Synthetic player can execute scripted inventory drop actions
- [ ] Synthetic player can execute scripted inventory slot move actions
- [ ] Synthetic player can execute kill/death interactions (optional)
- [ ] Bots can be spawned and killed programmatically
- [ ] Player count configurable without code changes (env var / config file)
- [ ] Confirmed **2 players** working
- [ ] Confirmed **4 players** working
- [ ] Confirmed **8 players** working
- [ ] Confirmed **16 players** working
- [ ] Confirmed **32 players** working (stretch goal)

---

## ✅ Phase 3 — Data Collection & Logging

### Metrics instrumented
- [ ] **Round-trip time (RTT)** — per message/tick, per player
- [ ] **Latency variance / jitter** — standard deviation across a session window
- [ ] **Messages per second** — server-side throughput
- [ ] **Bandwidth per player** — bytes sent + received
- [ ] **Server CPU usage** — sampled at fixed intervals
- [ ] **Server RAM usage** — sampled at fixed intervals, watch for leaks
- [ ] **Packet loss rate** — even if running on localhost
- [ ] **GC pressure** — especially relevant for .NET runtime comparison
- [ ] **Action latency per type** — movement, grab, drop, slot move, kill
- [ ] **Action rejection rate** — per action type and reason
- [ ] **Inventory consistency checks** — no negative counts, no duplicate item IDs
- [ ] **State divergence checks** — server vs client world/inventory mismatch rate

### Log output
- [ ] Logs export to CSV or JSON for easy graphing
- [ ] Each run tagged with: solution type, player count, scenario, timestamp
- [ ] At least **3 runs per condition** to average out noise

---

## ✅ Phase 4 — Test Scenarios

### Scenario 1 — Idle Baseline
- [ ] All synthetic players connected, minimal/no movement
- [ ] Run for defined duration (e.g. 60 seconds)
- [ ] Collected for both solutions at all player counts

### Scenario 2 — Active Gameplay (Sustained Load)
- [ ] All synthetic players sending position updates at full tick rate
- [ ] Continuous mix of grab/drop/slot-move inventory actions
- [ ] Run for defined duration (e.g. 60–120 seconds)
- [ ] Collected for both solutions at all player counts

### Scenario 3 — Stress Burst (Peak Load)
- [ ] All players send at maximum rate simultaneously
- [ ] Burst includes inventory-heavy operations (grab/drop/slot move spam)
- [ ] Identify the point of degradation (packet loss, timeout, desync)
- [ ] Breaking point documented for both solutions

### Scenario 4 — Inventory Race Conditions (required)
- [ ] Multiple players attempt to grab the same ground item at once
- [ ] Multiple players drop items into same area simultaneously
- [ ] Slot move and drop requested in near-same tick for same player
- [ ] Confirm deterministic winner/ordering and no dupes/loss

### Scenario 5 — Reconnection / Reliability (optional but recommended)
- [ ] Simulate a client disconnect mid-session
- [ ] Measure reconnection time and state resync time (movement + inventory)
- [ ] Tested on both solutions

---

## ✅ Phase 5 — Analysis & Results

### Data processing
- [ ] Raw logs cleaned and aggregated
- [ ] Mean and standard deviation computed per metric per condition
- [ ] Scaling curves plotted (player count vs. latency, CPU, etc.)
- [ ] Results tables created for each scenario

### Graphs / Figures (for the paper)
- [ ] RTT vs. player count — both solutions on same chart
- [ ] Throughput vs. player count
- [ ] CPU/RAM usage vs. player count
- [ ] Jitter comparison (box plot or std dev bar chart)
- [ ] Breaking point / degradation graph (stress scenario)
- [ ] Action latency by type (movement/grab/drop/slot move/kill)
- [ ] Inventory integrity errors vs player count (should stay zero)

---

## ✅ Phase 6 — Paper Writing

### Structure
- [ ] **Abstract** written
- [ ] **Introduction** — context, motivation, research question
- [ ] **Related Work** — brief mention of ENet, Godot networking, existing benchmarks
- [ ] **Methodology** — hardware, software, test design, synthetic player approach
- [ ] **Implementation** — description of both solutions (architecture, transport, tick rate, action protocol)
- [ ] **Results** — tables and figures from Phase 5
- [ ] **Discussion** — where custom wins, where built-in holds up, why, limitations
- [ ] **Conclusion** — summary and potential future work
- [ ] **References** — Godot docs, .NET docs, any papers/libraries cited

### Review
- [ ] Methodology is clearly reproducible by a reader
- [ ] Transport protocol difference between solutions is explicitly acknowledged
- [ ] Fairness of comparison discussed (same hardware, same game logic)
- [ ] Limitations section included (synthetic ≠ real players, single machine, etc.)
- [ ] 2D-first scope and 3D compatibility plan are explicitly described
- [ ] Inventory integrity guarantees and validation rules are explicitly described
- [ ] Proofread and spell-checked
- [ ] Figures have captions and are referenced in the text

---

## 📌 Notes & Decisions Log

> Use this section to track important decisions made during the project.

```
[DATE] - Chose [transport protocol] for the custom server because...
[DATE] - Confirmed C# server as authoritative for movement + inventory actions because...
[DATE] - Locked benchmark gameplay actions: movement, grab, drop, slot move, optional kill.
[DATE] - Chose 2D as required scope and 3D compatibility as architecture goal because...
[DATE] - Settled on headless Godot instances for synthetic players because...
[DATE] - Capped player count at X because...
[DATE] - 
```

---

## 🔗 Resources

- [Godot High-Level Multiplayer Docs](https://docs.godotengine.org/en/stable/tutorials/networking/high_level_multiplayer.html)
- [Godot ENet Transport Reference](https://docs.godotengine.org/en/stable/classes/class_enetmultiplayerpeer.html)
- [LiteNetLib (lightweight .NET networking)](https://github.com/RevenantX/LiteNetLib)
- [Running Godot headless (server mode)](https://docs.godotengine.org/en/stable/tutorials/export/exporting_for_dedicated_servers.html)
- [Custom C# Server Class Map](documentation/custom-server-class-map.md)
