using Godot;
using System.Collections.Generic;

public partial class UIManager : Node
{
	// Change this to your server's IP when testing across machines.
	[Export] public string ServerHost { get; set; } = "127.0.0.1";

	/// <summary>Populated at runtime; Server.cs writes stats into this label.</summary>
	public Label StatsLabel { get; private set; }

	private Button _spawnClientButton;

	public override void _Ready()
	{
		GD.Print("[UIManager] _Ready() called");

		// No window exists in headless mode; skip all UI setup.
		if (DisplayServer.GetName() == "headless")
		{
			GD.Print("[UIManager] Headless mode detected - skipping UI setup");
			return;
		}

		// The button lives under VBoxContainer/SpawnClientButton in the scene.
		_spawnClientButton = GetNode<Button>("VBoxContainer/SpawnClientButton");
		GD.Print($"[UIManager] Button node found: {_spawnClientButton != null}");
		_spawnClientButton.Pressed += OnSpawnClientPressed;
		GD.Print("[UIManager] Button Pressed signal connected");

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
			if (arg == "--client")
			{
				GD.Print("[UIManager] Running as client - hiding spawn button");
				_spawnClientButton.Visible = false;
				return;
			}
		}
		GD.Print("[UIManager] Running as server/host - spawn button is visible");
	}

	private void OnSpawnClientPressed()
	{
		GD.Print("[UIManager] Spawn Client button pressed!");

		string execPath = OS.GetExecutablePath();
		GD.Print($"[UIManager] Executable path: {execPath}");

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
		argsList.Add(ServerHost);

		GD.Print($"[UIManager] Launching process with args: [{string.Join(" ", argsList)}]");
		long pid = OS.CreateProcess(execPath, argsList.ToArray());
		GD.Print($"[UIManager] OS.CreateProcess returned PID: {pid}");
	}
}
