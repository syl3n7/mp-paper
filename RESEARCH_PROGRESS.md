# 🎮 Multiplayer Server Benchmark — Research Progress

> **Goal:** Compare a custom C#/.NET server solution against Godot's built-in multiplayer (ENet) across latency, throughput, scalability, and reliability metrics.

---

## 📁 Project Overview

| Field | Details |
|---|---|
| **Game Engine** | Godot (client-side) |
| **Custom Server** | C# / .NET |
| **Built-in Solution** | Godot High-Level Multiplayer API (ENet) |
| **Test Players** | 2 → 4 → 8 → 16 → 32 (synthetic) |
| **Paper Type** | Short comparative benchmark paper |

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
- [ ] Tick rate defined and consistent (e.g. 20 or 30 ticks/sec)
- [ ] Baseline logging added (timestamps per packet)

### Custom C#/.NET Server
- [ ] Server project created and builds successfully
- [ ] Transport protocol chosen and documented (TCP / UDP / LiteNetLib / other)
- [ ] Server accepts client connections from Godot client
- [ ] Basic message round-trip working (ping/pong or state sync)
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
- [ ] Run for defined duration (e.g. 60–120 seconds)
- [ ] Collected for both solutions at all player counts

### Scenario 3 — Stress Burst (Peak Load)
- [ ] All players send at maximum rate simultaneously
- [ ] Identify the point of degradation (packet loss, timeout, desync)
- [ ] Breaking point documented for both solutions

### Scenario 4 — Reconnection / Reliability (optional but recommended)
- [ ] Simulate a client disconnect mid-session
- [ ] Measure reconnection time and state resync time
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

---

## ✅ Phase 6 — Paper Writing

### Structure
- [ ] **Abstract** written
- [ ] **Introduction** — context, motivation, research question
- [ ] **Related Work** — brief mention of ENet, Godot networking, existing benchmarks
- [ ] **Methodology** — hardware, software, test design, synthetic player approach
- [ ] **Implementation** — description of both solutions (architecture, transport, tick rate)
- [ ] **Results** — tables and figures from Phase 5
- [ ] **Discussion** — where custom wins, where built-in holds up, why, limitations
- [ ] **Conclusion** — summary and potential future work
- [ ] **References** — Godot docs, .NET docs, any papers/libraries cited

### Review
- [ ] Methodology is clearly reproducible by a reader
- [ ] Transport protocol difference between solutions is explicitly acknowledged
- [ ] Fairness of comparison discussed (same hardware, same game logic)
- [ ] Limitations section included (synthetic ≠ real players, single machine, etc.)
- [ ] Proofread and spell-checked
- [ ] Figures have captions and are referenced in the text

---

## 📌 Notes & Decisions Log

> Use this section to track important decisions made during the project.

```
[DATE] - Chose [transport protocol] for the custom server because...
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
