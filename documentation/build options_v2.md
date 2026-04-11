## Build options

### dotnet configurations

The project has two named build configurations defined in `mp-paper.csproj`:

```bash
dotnet build -c ENet      # defines USE_ENET   — activates the ENet/built-in Godot transport path (Server.cs)
dotnet build -c AltNet    # defines USE_ALTNET — activates the custom C# server path (CustomNetworkClient.cs)
dotnet build              # default Debug build — no backend constant defined
```

Both configurations target **net8.0** (net9.0 for Android).

---

### Command-line flags (runtime)

User arguments are read via `OS.GetCmdlineUserArgs()` and must come **after** `--`.

#### Server instance (default — no flags needed)

```bash
./network-comparison.x86_64 --headless          # headless server, no window
./network-comparison.x86_64                     # server with UI window
```

#### ENet client

```bash
./network-comparison.x86_64 -- \
    --client \
    --host 127.0.0.1 \
    --port 7777 \
    --network enet
```

#### Custom C# server client

```bash
./network-comparison.x86_64 -- \
    --client \
    --host 127.0.0.1 \
    --custom-port 9000 \
    --udp-port 7778 \
    --network custom
```

#### Flag reference

| Flag | Default | Description |
|---|---|---|
| `--client` | — | Activate client mode; skips server startup |
| `--host <ip>` | `127.0.0.1` | Server IP to connect to |
| `--port <n>` | `7777` | ENet server port (used with `--network enet`) |
| `--custom-port <n>` | `9000` | Custom server TCP port (used with `--network custom`) |
| `--udp-port <n>` | `7778` | Custom server UDP port (used with `--network custom`) |
| `--network enet\|custom` | `enet` | Network backend selector |

---

### Launching from the Godot editor

The `UIManager` spawn buttons call `OS.CreateProcess` with the editor binary and automatically append `--path <project_dir>` when running inside the editor:

```
Open Client (Built-in)          →  godot --path <proj> -- --client --host 127.0.0.1 --port 7777      --network enet
Open Client (Custom C# Server)  →  godot --path <proj> -- --client --host 127.0.0.1 --custom-port 9000 --udp-port 7778 --network custom
```
