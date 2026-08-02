using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;

public enum ComputerScreen
{
	Home,
	Employees,
	MissionDispatch,
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
	[Export] public PackedScene MissionDispatchScreenScene { get; set; } = null!;
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
	private readonly string[] _dispatchEmployeeIds = new string[3];
	private MissionDispatchUI _missionDispatchUi = null!;

	public event System.Action<int>? DispatchSlotRequested;

	public bool IsDispatchSelectionActive => !string.IsNullOrEmpty(_dispatchIncidentId);

	public override void _Ready()
	{
		_screenContainer = GetNode<Control>(ScreenContainerPath);
		_backButton = GetNode<Button>(BackButtonPath);
		_backButton.Pressed += GoBack;

		AddScreen(ComputerScreen.Home, HomeScreenScene);
		AddScreen(ComputerScreen.Employees, EmployeesScreenScene);
		AddScreen(ComputerScreen.MissionDispatch, MissionDispatchScreenScene);
		AddScreen(ComputerScreen.Equipment, EquipmentScreenScene);
		AddScreen(ComputerScreen.Encyclopedia, EncyclopediaScreenScene);
		OpenScreen(ComputerScreen.Home, false);
		if (_screens.TryGetValue(ComputerScreen.MissionDispatch, out Control dispatchScreen)
			&& dispatchScreen is MissionDispatchUI missionDispatch)
		{
			_missionDispatchUi = missionDispatch;
			_missionDispatchUi.EmployeeSlotRequested += OnDispatchSlotRequested;
			_missionDispatchUi.DispatchRequested += OnDispatchRequested;
		}
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

	public void BeginDispatchSelection(
		string incidentId,
		System.Action dispatchCompleted,
		string callTitle = null,
		string callTranscript = null)
	{
		_dispatchIncidentId = incidentId;
		_dispatchCompleted = dispatchCompleted;
		System.Array.Clear(_dispatchEmployeeIds, 0, _dispatchEmployeeIds.Length);
		_navigationHistory.Clear();
		OpenScreen(ComputerScreen.MissionDispatch, false);
		_backButton.Visible = false;
		if (_missionDispatchUi != null)
		{
			_missionDispatchUi.SetEmployeeNames(System.Array.Empty<string>());
			_missionDispatchUi.SetFeedback(string.Empty);
			_missionDispatchUi.SetCallDetails(
				string.IsNullOrEmpty(callTitle) ? BuildDispatchTitle(incidentId) : callTitle,
				string.IsNullOrEmpty(callTranscript) ? BuildDispatchTranscript(incidentId) : callTranscript);
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
		System.Array.Clear(_dispatchEmployeeIds, 0, _dispatchEmployeeIds.Length);
		if (_screens.TryGetValue(ComputerScreen.Employees, out Control screen)
			&& screen is EmployeeSelectionUI employeeSelection)
		{
			employeeSelection.EndDispatchSelection();
		}
	}

	public bool AssignEmployeeToDispatchSlot(int slotIndex, EmployeeView employee)
	{
		if (!IsDispatchSelectionActive || slotIndex < 0 || slotIndex >= _dispatchEmployeeIds.Length)
		{
			return false;
		}

		if (employee.Status != Kontur.Core.Model.EmployeeStatus.Available)
		{
			_missionDispatchUi?.SetFeedback("СОТРУДНИК НЕДОСТУПЕН");
			return false;
		}

		for (int index = 0; index < _dispatchEmployeeIds.Length; index++)
		{
			if (index != slotIndex && _dispatchEmployeeIds[index] == employee.Id)
			{
				_dispatchEmployeeIds[index] = string.Empty;
			}
		}

		_dispatchEmployeeIds[slotIndex] = employee.Id;
		_missionDispatchUi?.SetEmployeeNames(GetDispatchEmployeeNames());
		return true;
	}

	public bool IsEmployeeSelectedForDispatch(string employeeId)
	{
		for (int index = 0; index < _dispatchEmployeeIds.Length; index++)
		{
			if (_dispatchEmployeeIds[index] == employeeId)
			{
				return true;
			}
		}

		return false;
	}

	private void OnDispatchSlotRequested(int slotIndex)
	{
		if (IsDispatchSelectionActive)
		{
			DispatchSlotRequested?.Invoke(slotIndex);
		}
	}

	private void OnDispatchRequested()
	{
		var employeeIds = new List<string>();
		for (int index = 0; index < _dispatchEmployeeIds.Length; index++)
		{
			if (!string.IsNullOrEmpty(_dispatchEmployeeIds[index]))
			{
				employeeIds.Add(_dispatchEmployeeIds[index]);
			}
		}

		CommandResult result = DispatchSelectedEmployees(employeeIds);
		if (!result.IsSuccess)
		{
			_missionDispatchUi?.SetFeedback(result.Error);
		}
	}

	private IReadOnlyList<string> GetDispatchEmployeeNames()
	{
		KonturRuntime runtime = KonturRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return System.Array.Empty<string>();
		}

		var names = new string[_dispatchEmployeeIds.Length];
		for (int index = 0; index < _dispatchEmployeeIds.Length; index++)
		{
			string employeeId = _dispatchEmployeeIds[index];
			if (string.IsNullOrEmpty(employeeId))
			{
				continue;
			}

			foreach (EmployeeView employee in runtime.Simulation.GetRoster())
			{
				if (employee.Id == employeeId)
				{
					names[index] = employee.Name;
					break;
				}
			}
		}

		return names;
	}

	private string BuildDispatchTitle(string incidentId)
	{
		KonturRuntime runtime = KonturRuntime.Get(this);
		if (runtime != null && runtime.IsReady)
		{
			foreach (IncidentView incident in runtime.Simulation.GetActiveIncidents())
			{
				if (incident.Id == incidentId)
				{
					return $"ВХОДЯЩИЙ ВЫЗОВ: {KonturUiText.MissionTitle(incident)}";
				}
			}
		}

		return "ВХОДЯЩИЙ ВЫЗОВ";
	}

	private string BuildDispatchTranscript(string incidentId)
	{
		KonturRuntime runtime = KonturRuntime.Get(this);
		if (runtime != null && runtime.IsReady)
		{
			foreach (IncidentView incident in runtime.Simulation.GetActiveIncidents())
			{
				if (incident.Id == incidentId)
				{
					return $"Вызов от: {KonturUiText.CallerName(incident)}. Требуется направить группу на объект: {incident.BuildingId}.";
				}
			}
		}

		return "Стенограмма вызова недоступна.";
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