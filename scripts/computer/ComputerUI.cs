using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;

public enum ComputerScreen
{
	Home,
	Employees,
	Equipment,
	Encyclopedia,
}

public interface IComputerScreen
{
	void OnScreenOpened();
}

/// <summary>Оболочка терминала: хранит экраны ПК и отвечает только за навигацию между ними.</summary>
public partial class ComputerUI : Control
{
	[Export] public NodePath ScreenContainerPath { get; set; } = new("SafeArea/ScreenContainer");
	[Export] public NodePath BackButtonPath { get; set; } = new("SafeArea/Header/BackButton");
	[Export] public PackedScene HomeScreenScene { get; set; } = null!;
	[Export] public PackedScene EmployeesScreenScene { get; set; } = null!;
	[Export] public PackedScene EquipmentScreenScene { get; set; } = null!;
	[Export] public PackedScene EncyclopediaScreenScene { get; set; } = null!;

	private readonly Dictionary<ComputerScreen, Control> _screens = new();
	private readonly Stack<ComputerScreen> _navigationHistory = new();
	private Control _screenContainer = null!;
	private Button _backButton = null!;
	private ComputerScreen _currentScreen = ComputerScreen.Home;
	private bool _hasOpenScreen;
	private string _dispatchIncidentId = string.Empty;
	private System.Action _dispatchCompleted;

	public bool IsDispatchSelectionActive => !string.IsNullOrEmpty(_dispatchIncidentId);

	public override void _Ready()
	{
		_screenContainer = GetNode<Control>(ScreenContainerPath);
		_backButton = GetNode<Button>(BackButtonPath);
		_backButton.Pressed += GoBack;

		AddScreen(ComputerScreen.Home, HomeScreenScene);
		AddScreen(ComputerScreen.Employees, EmployeesScreenScene);
		AddScreen(ComputerScreen.Equipment, EquipmentScreenScene);
		AddScreen(ComputerScreen.Encyclopedia, EncyclopediaScreenScene);
		OpenScreen(ComputerScreen.Home, false);
	}

	public override void _ExitTree()
	{
		if (_backButton != null)
		{
			_backButton.Pressed -= GoBack;
		}
	}

	public void OpenScreen(ComputerScreen screen)
	{
		OpenScreen(screen, true);
	}

	public void GoBack()
	{
		if (IsDispatchSelectionActive)
		{
			return;
		}

		OpenScreen(_navigationHistory.Count > 0 ? _navigationHistory.Pop() : ComputerScreen.Home, false);
	}

	public void BeginDispatchSelection(string incidentId, System.Action dispatchCompleted)
	{
		_dispatchIncidentId = incidentId;
		_dispatchCompleted = dispatchCompleted;
		_navigationHistory.Clear();
		OpenScreen(ComputerScreen.Employees, false);
		_backButton.Visible = false;
		if (_screens.TryGetValue(ComputerScreen.Employees, out Control screen)
			&& screen is EmployeeSelectionUI employeeSelection)
		{
			employeeSelection.BeginDispatchSelection();
		}
	}

	public CommandResult DispatchSelectedEmployees(IReadOnlyList<string> employeeIds)
	{
		if (string.IsNullOrEmpty(_dispatchIncidentId))
		{
			return CommandResult.Fail("Нет активного вызова для отправки.");
		}

		if (employeeIds.Count == 0)
		{
			return CommandResult.Fail("Выберите хотя бы одного сотрудника.");
		}

		MapMarkerController mapController = GetTree().GetFirstNodeInGroup("map_marker_controller") as MapMarkerController;
		if (mapController == null)
		{
			return CommandResult.Fail("Контроллер маршрута карты недоступен.");
		}

		CommandResult result = mapController.TryDispatchSquad(
			_dispatchIncidentId,
			employeeIds,
			System.Array.Empty<string>());
		if (!result.IsSuccess)
		{
			return result;
		}

		CancelDispatchSelection();
		_navigationHistory.Clear();
		OpenScreen(ComputerScreen.Home, false);
		System.Action dispatchCompleted = _dispatchCompleted;
		_dispatchCompleted = null;
		dispatchCompleted?.Invoke();
		return result;
	}

	public void CancelDispatchSelection()
	{
		_dispatchIncidentId = string.Empty;
		if (_screens.TryGetValue(ComputerScreen.Employees, out Control screen)
			&& screen is EmployeeSelectionUI employeeSelection)
		{
			employeeSelection.EndDispatchSelection();
		}
	}

	private void AddScreen(ComputerScreen screen, PackedScene screenScene)
	{
		if (screenScene == null)
		{
			GD.PushError($"ComputerUI: screen scene for {screen} is not assigned.");
			return;
		}

		Control screenInstance = screenScene.Instantiate<Control>();
		_screenContainer.AddChild(screenInstance);
		screenInstance.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		screenInstance.Visible = false;
		_screens[screen] = screenInstance;
	}

	private void OpenScreen(ComputerScreen screen, bool addToHistory)
	{
		if (!_screens.TryGetValue(screen, out Control nextScreen))
		{
			return;
		}

		if (_screens.TryGetValue(_currentScreen, out Control currentScreen))
		{
			if (_hasOpenScreen && currentScreen == nextScreen)
			{
				return;
			}

			currentScreen.Visible = false;
			if (addToHistory)
			{
				_navigationHistory.Push(_currentScreen);
			}
		}

		nextScreen.Visible = true;
		_currentScreen = screen;
		_hasOpenScreen = true;
		_backButton.Visible = screen != ComputerScreen.Home;
		if (nextScreen is IComputerScreen computerScreen)
		{
			computerScreen.OnScreenOpened();
		}
	}
}
