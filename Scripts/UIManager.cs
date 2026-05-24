using Godot;
using System.Collections.Generic;

public partial class UIManager : Node
{
	// Change this to your server's IP when testing across machines.
	[Export] public string ServerHost    { get; set; } = "127.0.0.1";
	[Export] public int    EnetPort      { get; set; } = 7777;
	[Export] public int    EnetUdpPort   { get; set; } = 7778;
	[Export] public int    CustomTcpPort { get; set; } = 7777;
	[Export] public int    CustomUdpPort { get; set; } = 7778;

	/// <summary>Populated at runtime; Server.cs writes stats into this label.</summary>
	public Label StatsLabel { get; private set; }

	private Button _spawnEnetBotButton;
	private Button _spawnCustomBotButton;
	private Button _exportLogButton;
	private int    _customBotIdCounter = 0;

	public override void _Ready()
	{
		GD.Print("[UIManager] _Ready() called");

		// No window exists in headless mode; skip all UI setup.
		if (DisplayServer.GetName() == "headless")
		{
			GD.Print("[UIManager] Headless mode detected - skipping UI setup");
			return;
		}

// Repurpose the scene button as the ENet bot launcher.
			_spawnEnetBotButton = GetNode<Button>("VBoxContainer/SpawnClientButton");
			GD.Print($"[UIManager] ENet bot button found: {_spawnEnetBotButton != null}");
			_spawnEnetBotButton.Text = "Spawn Bot (ENet)";
			_spawnEnetBotButton.Pressed += OnSpawnBotPressed;

			_spawnCustomBotButton = new Button
			{
				Name                = "SpawnCustomBotButton",
				Text                = "Spawn Bot (Custom)",
				SizeFlagsHorizontal = Control.SizeFlags.Fill,
			};
			_spawnCustomBotButton.Pressed += OnSpawnCustomBotPressed;
			GetNode("VBoxContainer").AddChild(_spawnCustomBotButton);
			GD.Print("[UIManager] Bot buttons configured");

			// Disable the button that doesn't match the active backend.
			var clientNode = GetNodeOrNull<Client>("../../Client");
			bool useCustom = clientNode?.Backend == Client.NetworkBackend.CustomServer;
			_spawnEnetBotButton.Disabled   = useCustom;
			_spawnCustomBotButton.Disabled = !useCustom;
			GD.Print($"[UIManager] Backend={(useCustom ? "Custom" : "ENet")}: ENet bot {(useCustom ? "disabled" : "enabled")}, Custom bot {(useCustom ? "enabled" : "disabled")}");

		_exportLogButton = new Button
		{
			Name                = "ExportLogButton",
			Text                = "Export Metrics Logs…",
			SizeFlagsHorizontal = Control.SizeFlags.Fill,
		};
		_exportLogButton.Pressed += OnExportLogPressed;
		GetNode("VBoxContainer").AddChild(_exportLogButton);
		GD.Print("[UIManager] Export log button configured");

		// Disable the export button when using the custom backend — export lives on the server dashboard.
		bool isCustomBackend = GetNodeOrNull<Client>("../../Client")?.Backend == Client.NetworkBackend.CustomServer;
		if (isCustomBackend)
		{
			_exportLogButton.Disabled     = true;
			_exportLogButton.TooltipText  = "Metrics export is available on the server dashboard (port 8080)";
			GD.Print("[UIManager] Custom backend: export button disabled (use server dashboard)");
		}

		// Stats label — added programmatically so the .tscn stays unchanged.
		StatsLabel = new Label
		{
			Name         = "StatsLabel",
			AutowrapMode = TextServer.AutowrapMode.Off,
		};
		GetNode("VBoxContainer").AddChild(StatsLabel);
		GD.Print("[UIManager] StatsLabel added to VBoxContainer");

		// Hide the button when this instance is itself a client.
		var args = OS.GetCmdlineUserArgs();
		GD.Print($"[UIManager] Command-line user args: [{string.Join(", ", args)}]");
		foreach (var arg in args)
		{
			if (arg == "--client" || arg == "--bot")
			{
				GD.Print("[UIManager] Running as client/bot - hiding spawn buttons");
				_spawnEnetBotButton.Visible   = false;
				_spawnCustomBotButton.Visible = false;
				_exportLogButton.Visible      = false;
				return;
			}
		}
		GD.Print("[UIManager] Running as server/host - spawn buttons are visible");
	}

	private void OnSpawnClientPressed(string networkMode, int port, int udpPort)	{
		GD.Print($"[UIManager] Spawn Client button pressed ({networkMode})");

		string execPath = OS.GetExecutablePath();
		GD.Print($"[UIManager] Executable path: {execPath}");

		var clientNode = GetNodeOrNull<Client>("../../Client");
		string host    = clientNode?.Host ?? ServerHost;

		var argsList = new List<string>();

		// When running inside the editor, the executable is the Godot editor binary.
		// Pass --path so the new instance can find the project.
		if (OS.HasFeature("editor"))
		{
			string projectPath = ProjectSettings.GlobalizePath("res://");
			GD.Print($"[UIManager] Detected editor mode - adding --path {projectPath}");
			argsList.Add("--path");
			argsList.Add(projectPath);
		}
		else
		{
			GD.Print("[UIManager] Running as exported build");
		}

		argsList.Add("--");
		argsList.Add("--client");
		argsList.Add("--host");
		argsList.Add(host);
		argsList.Add("--network");
		argsList.Add(networkMode);
		if (networkMode == "custom")
		{
			argsList.Add("--custom-port");
			argsList.Add(port.ToString());
			argsList.Add("--udp-port");
			argsList.Add(udpPort.ToString());
		}
		else
		{
			argsList.Add("--port");
			argsList.Add(port.ToString());
		}

		GD.Print($"[UIManager] Launching process with args: [{string.Join(" ", argsList)}]");
		long pid = OS.CreateProcess(execPath, argsList.ToArray());
		GD.Print($"[UIManager] OS.CreateProcess returned PID: {pid}");
	}

	private void OnSpawnBotPressed()
	{
		GD.Print("[UIManager] Spawn Bot (ENet) button pressed");
		SpawnBot("enet", EnetPort, EnetUdpPort);
	}

	private void OnSpawnCustomBotPressed()
	{
		GD.Print("[UIManager] Spawn Bot (Custom) button pressed");
		SpawnBot("custom", CustomTcpPort, CustomUdpPort);
	}

	private void SpawnBot(string networkMode, int port, int udpPort)
	{
		string execPath = OS.GetExecutablePath();
		var argsList    = new List<string>();

		if (OS.HasFeature("editor"))
		{
			string projectPath = ProjectSettings.GlobalizePath("res://");
			argsList.Add("--path");
			argsList.Add(projectPath);
		}

		var clientNode = GetNodeOrNull<Client>("../../Client");
		string host    = clientNode?.Host ?? ServerHost;

		argsList.Add("--");
		argsList.Add("--bot");
		argsList.Add("--host");
		argsList.Add(host);
		argsList.Add("--network");
		argsList.Add(networkMode);
		if (networkMode == "custom")
		{
			argsList.Add("--custom-port");
			argsList.Add(port.ToString());
			argsList.Add("--udp-port");
			argsList.Add(udpPort.ToString());
		}
		else
		{
			argsList.Add("--port");
			argsList.Add(port.ToString());
		}

		GD.Print($"[UIManager] Launching bot process with args: [{string.Join(" ", argsList)}]");
		long pid = OS.CreateProcess(execPath, argsList.ToArray());
		GD.Print($"[UIManager] Bot process PID: {pid}");
	}

	// ─── Metrics log export ───────────────────────────────────────────────────

	private FileDialog _exportDialog;

	private void EnsureExportDialog()
	{
		if (_exportDialog != null) return;

		var server   = GetNodeOrNull<Server>("../../Server");
		string start = server != null
			? System.IO.Path.GetDirectoryName(server.ResolveServerCsvPath()) ?? OS.GetUserDataDir()
			: OS.GetUserDataDir();

		_exportDialog = new FileDialog
		{
			Title             = "Choose export folder for metrics logs",
			FileMode          = FileDialog.FileModeEnum.OpenDir,
			Access            = FileDialog.AccessEnum.Filesystem,
			CurrentDir        = start,
			Size              = new Vector2I(800, 500),
			UseNativeDialog   = false,
		};
		_exportDialog.DirSelected += OnExportFolderSelected;
		AddChild(_exportDialog);
	}

	private void OnExportLogPressed()
	{
		GD.Print("[UIManager] Export log button pressed — opening folder picker");
		EnsureExportDialog();
		_exportDialog.PopupCentered();
	}

	private void OnExportFolderSelected(string targetDir)
	{
		GD.Print($"[UIManager] Exporting metrics to: {targetDir}");

		var server = GetNodeOrNull<Server>("../../Server");
		if (server == null)
		{
			GD.PrintErr("[UIManager] Export: Server node not found");
			return;
		}

		server.ExportMetricsTo(targetDir);
	}
}
