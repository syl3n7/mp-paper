# Running the Server in Headless Mode

## What "headless" means

Passing `--headless` instructs Godot to use the **Headless display server** driver.
With that driver active:

- No window is created.
- The **renderer is fully disabled** — no GPU work, no render thread.
- Audio is stubbed out.
- The game loop (`_Process`, `_PhysicsProcess`), timers, signals, and **multiplayer / RPC** all run normally.

At runtime you can detect headless mode with:

```csharp
if (DisplayServer.GetName() == "headless")
{
    // no window exists — skip any UI or rendering code
}
```

---

## Quick-start commands

### Exported build

```bash
# Linux
./network-comparison.x86_64 --headless

# Windows (from PowerShell or cmd)
network-comparison.exe --headless
```

### From the Godot editor binary (during development)

```bash
godot --headless --path /home/lau/Documents/network-comparison
```

`--path` tells the editor binary where the project lives so it can load
`project.godot`.

### Passing your own user args alongside

User args come **after** `--`:

```bash
./network-comparison.x86_64 --headless -- --some-custom-flag
```

The existing code already reads `OS.GetCmdlineUserArgs()`, so anything after
`--` ends up there.

---

## Autostart as a systemd service (Linux)

Create `/etc/systemd/system/godot-server.service`:

```ini
[Unit]
Description=Godot Multiplayer Server
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/network-comparison
ExecStart=/opt/network-comparison/network-comparison.x86_64 --headless
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

Then enable and start it:

```bash
sudo systemctl daemon-reload
sudo systemctl enable --now godot-server
sudo journalctl -u godot-server -f   # tail the logs
```

---

## Autostart on boot without systemd (cron @reboot)

```crontab
@reboot /opt/network-comparison/network-comparison.x86_64 --headless >> /var/log/godot-server.log 2>&1
```

---

## Docker one-liner

```dockerfile
FROM ubuntu:24.04
RUN apt-get update && apt-get install -y libgl1 libxi6 libxrender1
COPY . /app
WORKDIR /app
CMD ["./network-comparison.x86_64", "--headless"]
```

Build and run:

```bash
docker build -t godot-server .
docker run -d -p 7777:7777/udp godot-server
```

Note: expose UDP (ENet uses UDP), not TCP.

---

## Things to watch out for

| Topic | Detail |
|---|---|
| **No `GetViewport().GetWindow()`** | Calling window methods crashes headless; guard with `DisplayServer.GetName() == "headless"` |
| **No Input events** | `Input.IsActionPressed` always returns `false` — the server-side player (peer 1) will never move, which is expected |
| **GD.Print still works** | All print / printerr output goes to stdout/stderr as usual |
| **ResourceLoader still works** | Scenes and resources load normally; textures are parsed but never uploaded to a GPU |
| **ENet port must be open** | If running behind a firewall, open UDP port 7777 (or whatever `Port` is set to) |
| **Process priority** | On Linux, `nice -n -5 ./server.x86_64 --headless` gives the process slightly higher scheduling priority |

---

## Verifying the server is running

```bash
# Check the process is alive
pgrep -a network-comparison

# Confirm the UDP port is bound
ss -ulnp | grep 7777

# Kill a stale instance
pkill -f "network-comparison.*--headless"
```
