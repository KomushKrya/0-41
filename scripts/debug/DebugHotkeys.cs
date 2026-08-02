using Godot;

/// <summary>
/// Глобальная точка входа для отладочных клавиш. Автозагрузка получает _Input
/// раньше интерфейсов сцены, поэтому клавиши не теряются в LineEdit, SubViewport
/// или на экранах меню и роликов.
/// </summary>
public partial class DebugHotkeys : Node
{
	// Главная 3D-сцена служит рабочей debug-сценой: на ней доступны карта,
	// интерактивные предметы и DebugInterfaceOverlay.
	private const string DebugScenePath = "res://scenes/main.tscn";
	private const string PlayerGroup = "debug_player";

	private string _returnScenePath = string.Empty;
	private KonturDebugOverlay _runtimeOverlay;

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventKey keyEvent || !keyEvent.Pressed || keyEvent.Echo)
		{
			return;
		}

		bool handled = keyEvent.Keycode switch
		{
			Key.F1 or Key.F2 or Key.F3 or Key.F4 or Key.F5 or Key.Key1 or Key.Key2 or Key.Key3 or Key.Key4 or Key.Escape
				=> TryHandleInterfaceKey(keyEvent),
			Key.F6 => ToggleCoreOverlay(),
			Key.F9 => RebuildContent(),
			Key.F11 => ToggleDebugScene(),
			Key.F12 => ToggleNoclip(),
			_ => false
		};

		if (handled)
		{
			GetViewport().SetInputAsHandled();
		}
	}

	private bool TryHandleInterfaceKey(InputEventKey keyEvent)
	{
		foreach (Node node in GetTree().GetNodesInGroup(DebugInterfaceOverlay.DebugOverlayGroup))
		{
			if (node is DebugInterfaceOverlay overlay && overlay.HandleDebugKey(keyEvent))
			{
				return true;
			}
		}

		return false;
	}

	private bool ToggleCoreOverlay()
	{
		KonturDebugOverlay overlay = FindCoreOverlay();
		if (overlay == null)
		{
			PackedScene scene = ResourceLoader.Load<PackedScene>(DebugScenePath);
			if (scene == null)
			{
				GD.PushError($"[DEBUG] Не найдена debug-сцена: {DebugScenePath}");
				return false;
			}

			_runtimeOverlay = scene.Instantiate<KonturDebugOverlay>();
			AddChild(_runtimeOverlay);
			overlay = _runtimeOverlay;
		}

		overlay.Toggle();
		return true;
	}

	private KonturDebugOverlay FindCoreOverlay()
	{
		foreach (Node node in GetTree().GetNodesInGroup(KonturDebugOverlay.DebugOverlayGroup))
		{
			if (node is KonturDebugOverlay overlay)
			{
				return overlay;
			}
		}

		return null;
	}

	private bool RebuildContent()
	{
		if (ContentHotReload.Instance == null || !OS.IsDebugBuild())
		{
			return false;
		}

		ContentHotReload.Instance.RebuildAndReload();
		return true;
	}

	private bool ToggleDebugScene()
	{
		Node current = GetTree().CurrentScene;
		string currentPath = current?.SceneFilePath ?? string.Empty;
		bool isDebugScene = currentPath == DebugScenePath;

		if (isDebugScene)
		{
			if (string.IsNullOrEmpty(_returnScenePath))
			{
				return false;
			}

			GetTree().ChangeSceneToFile(_returnScenePath);
			_returnScenePath = string.Empty;
			return true;
		}

		if (!string.IsNullOrEmpty(currentPath))
		{
			_returnScenePath = currentPath;
		}

		if (_runtimeOverlay != null && GodotObject.IsInstanceValid(_runtimeOverlay))
		{
			_runtimeOverlay.QueueFree();
			_runtimeOverlay = null;
		}

		GetTree().ChangeSceneToFile(DebugScenePath);
		return true;
	}

	private bool ToggleNoclip()
	{
		foreach (Node node in GetTree().GetNodesInGroup(PlayerGroup))
		{
			if (node is FlyPlayer player)
			{
				player.ToggleNoclip();
				return true;
			}
		}

		return false;
	}
}
