#nullable enable

using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Config;

public enum ComputerScreen
{
	Employees,
	Encyclopedia,
	Equipment,
	MissionDispatch,
}

public interface IComputerScreen
{
	void OnScreenOpened();
}

/// <summary>
/// Оболочка терминала в духе текстового режима DOS.
///
/// Нижняя панель видна всегда и не прячется под навигацией: на рабочей станции
/// нет «назад», есть три вкладки, между которыми переключаются в один клик.
/// Экран отправки в панель не попадает намеренно — он открывается сам по вызову
/// и на время выбора забирает терминал целиком.
/// </summary>
public partial class ComputerUI : Control
{
	[Export] public PackedScene EmployeesScreenScene { get; set; } = null!;
	[Export] public PackedScene MissionDispatchScreenScene { get; set; } = null!;
	[Export] public PackedScene EquipmentScreenScene { get; set; } = null!;
	[Export] public PackedScene EncyclopediaScreenScene { get; set; } = null!;

	/// <summary>
	/// Отступ от края текстуры до рамки терминала, пиксели вьюпорта.
	///
	/// Стекло у модели монитора скруглено по краям, и вплотную к краю текстуры
	/// содержимое уезжает за изгиб: нижняя панель срезалась, а рамки упирались
	/// в кромку. Поле по периметру остаётся чёрным и принимает изгиб на себя.
	/// </summary>
	[Export] public Vector2 ScreenInset { get; set; } = new(34.0f, 26.0f);

	private const ComputerScreen DefaultScreen = ComputerScreen.Employees;

	private readonly Dictionary<ComputerScreen, Control> _screens = new();
	private readonly Dictionary<ComputerScreen, Button> _tabs = new();
	private Control _screenContainer = null!;
	private HBoxContainer _bottomBar = null!;
	private ComputerScreen _currentScreen = DefaultScreen;
	private bool _hasOpenScreen;
	private string _dispatchIncidentId = string.Empty;
	private System.Action? _dispatchCompleted;
	private string[] _dispatchEmployeeIds = System.Array.Empty<string>();
	private string[] _mainEquipmentIds = System.Array.Empty<string>();
	private string[] _consumableIds = System.Array.Empty<string>();
	private MissionDispatchUI? _missionDispatchUi;
	private EquipmentScreenUI? _equipmentUi;

	/// <summary>Слот снаряжения, который ждёт выбора со склада. −1 — никакой.</summary>
	private int _pendingEquipmentSlot = -1;
	private bool _pendingEquipmentIsConsumable;

	public event System.Action<int>? DispatchSlotRequested;

	public bool IsDispatchSelectionActive => !string.IsNullOrEmpty(_dispatchIncidentId);

	public override void _Ready()
	{
		Theme = DosTerminal.CreateTheme();
		BuildShell();

		AddScreen(ComputerScreen.Employees, EmployeesScreenScene);
		AddScreen(ComputerScreen.Encyclopedia, EncyclopediaScreenScene);
		AddScreen(ComputerScreen.Equipment, EquipmentScreenScene);
		AddScreen(ComputerScreen.MissionDispatch, MissionDispatchScreenScene);

		if (_screens.TryGetValue(ComputerScreen.MissionDispatch, out Control? dispatchScreen)
			&& dispatchScreen is MissionDispatchUI missionDispatch)
		{
			_missionDispatchUi = missionDispatch;
			_missionDispatchUi.EmployeeSlotRequested += OnDispatchSlotRequested;
			_missionDispatchUi.EquipmentSlotRequested += OnEquipmentSlotRequested;
			_missionDispatchUi.DispatchRequested += OnDispatchRequested;
		}

		if (_screens.TryGetValue(ComputerScreen.Equipment, out Control? equipmentScreen)
			&& equipmentScreen is EquipmentScreenUI equipmentUi)
		{
			_equipmentUi = equipmentUi;
			_equipmentUi.ItemConfirmed += OnEquipmentChosen;
		}

		OpenScreen(DefaultScreen, false);
	}

	// ------------------------------------------------------------------ оболочка

	private void BuildShell()
	{
		AddChild(DosTerminal.CreateBackground());

		// Имя не косметическое: по нему SubViewportInputController берёт границы
		// для курсора (CursorBoundsPath в NewDeskComputer.tscn), чтобы тот не
		// заезжал на чёрное поле за рамкой.
		var safeArea = new MarginContainer { Name = "SafeArea" };
		safeArea.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		safeArea.AddThemeConstantOverride("margin_left", Mathf.RoundToInt(ScreenInset.X));
		safeArea.AddThemeConstantOverride("margin_right", Mathf.RoundToInt(ScreenInset.X));
		safeArea.AddThemeConstantOverride("margin_top", Mathf.RoundToInt(ScreenInset.Y));
		safeArea.AddThemeConstantOverride("margin_bottom", Mathf.RoundToInt(ScreenInset.Y));
		AddChild(safeArea);

		// Рамка по периметру: она же подсказывает, где кончается читаемая область.
		var bezel = new PanelContainer();
		safeArea.AddChild(bezel);

		var layout = new VBoxContainer();
		layout.AddThemeConstantOverride("separation", 0);
		bezel.AddChild(layout);

		var content = new MarginContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		content.AddThemeConstantOverride("margin_left", 6);
		content.AddThemeConstantOverride("margin_right", 6);
		content.AddThemeConstantOverride("margin_top", 4);
		content.AddThemeConstantOverride("margin_bottom", 4);
		layout.AddChild(content);

		_screenContainer = new Control { SizeFlagsVertical = SizeFlags.ExpandFill };
		content.AddChild(_screenContainer);

		layout.AddChild(DosTerminal.CreateSeparator());

		_bottomBar = new HBoxContainer { CustomMinimumSize = new Vector2(0.0f, 26.0f) };
		_bottomBar.AddThemeConstantOverride("separation", 2);
		layout.AddChild(_bottomBar);

		AddTab("Сотрудники", ComputerScreen.Employees);
		AddTab("Энциклопедия", ComputerScreen.Encyclopedia);
		AddTab("Склад", ComputerScreen.Equipment);

		// Четвёртая кнопка появляется только когда есть что открывать: вне
		// сценария отправки текущего задания не существует.
		AddTab("Текущее задание", ComputerScreen.MissionDispatch);
		_tabs[ComputerScreen.MissionDispatch].Visible = false;

		// Курсор лежит в сцене первым ребёнком, а оболочка добавляется сверху
		// в рантайме — без перестановки он оказался бы под ней.
		Control? cursor = GetNodeOrNull<Control>("ComputerCursor");
		if (cursor != null)
		{
			MoveChild(cursor, GetChildCount() - 1);
		}
	}

	/// <summary>
	/// Кнопка нижней панели: подпись во всю ширину, активная вкладка инверсией.
	///
	/// Без номеров: клавиатура в терминал не пробрасывается, и цифра обещала бы
	/// горячую клавишу, которой нет.
	/// </summary>
	private void AddTab(string caption, ComputerScreen screen)
	{
		Button tab = DosTerminal.CreateRow(caption);
		tab.Alignment = HorizontalAlignment.Center;
		tab.Pressed += () => OpenScreen(screen);
		_bottomBar.AddChild(tab);
		_tabs[screen] = tab;
	}

	private void RefreshTabs()
	{
		foreach (KeyValuePair<ComputerScreen, Button> pair in _tabs)
		{
			DosTerminal.SetRowSelected(pair.Value, pair.Key == _currentScreen);
		}
	}

	// ------------------------------------------------------------------ навигация

	public void OpenScreen(ComputerScreen screen)
	{
		OpenScreen(screen, true);
	}

	private void OpenScreen(ComputerScreen screen, bool userInitiated)
	{
		// Уход со склада снимает незавершённый выбор предмета: иначе выбор
		// «повиснет» и следующее открытие склада начнётся в чужом режиме.
		if (userInitiated && screen != ComputerScreen.Equipment && _pendingEquipmentSlot >= 0)
		{
			CancelEquipmentSelection();
		}

		if (!_screens.TryGetValue(screen, out Control? nextScreen))
		{
			return;
		}

		if (_screens.TryGetValue(_currentScreen, out Control? currentScreen))
		{
			if (_hasOpenScreen && currentScreen == nextScreen)
			{
				return;
			}

			currentScreen.Visible = false;
		}

		nextScreen.Visible = true;
		_currentScreen = screen;
		_hasOpenScreen = true;
		RefreshTabs();
		if (nextScreen is IComputerScreen computerScreen)
		{
			computerScreen.OnScreenOpened();
		}
	}

	private void AddScreen(ComputerScreen screen, PackedScene screenScene)
	{
		if (screenScene == null)
		{
			GD.PushError($"ComputerUI: сцена экрана {screen} не назначена.");
			return;
		}

		Control screenInstance = screenScene.Instantiate<Control>();
		_screenContainer.AddChild(screenInstance);
		screenInstance.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		screenInstance.Visible = false;
		_screens[screen] = screenInstance;
	}

	// ------------------------------------------------------------------ отправка

	public void BeginDispatchSelection(
		string incidentId,
		System.Action dispatchCompleted,
		string? callTitle = null,
		string? callTranscript = null)
	{
		_dispatchIncidentId = incidentId;
		_dispatchCompleted = dispatchCompleted;

		// Слотов ровно столько, сколько разрешает миссия и конфиг снаряжения.
		LootConfig loot = ResolveLootConfig();
		_dispatchEmployeeIds = new string[Mathf.Max(1, ResolveSquadLimit(incidentId))];
		_mainEquipmentIds = new string[Mathf.Max(0, loot.StandardOrStorySlots)];
		_consumableIds = new string[Mathf.Max(0, loot.ConsumableSlots)];

		_tabs[ComputerScreen.MissionDispatch].Visible = true;
		OpenScreen(ComputerScreen.MissionDispatch, false);

		if (_missionDispatchUi != null)
		{
			_missionDispatchUi.ConfigureSlots(
				_dispatchEmployeeIds.Length,
				_mainEquipmentIds.Length,
				_consumableIds.Length);
			_missionDispatchUi.SetFeedback(string.Empty);
			_missionDispatchUi.SetCallDetails(
				string.IsNullOrEmpty(callTitle) ? BuildDispatchTitle(incidentId) : callTitle,
				string.IsNullOrEmpty(callTranscript) ? BuildDispatchTranscript(incidentId) : callTranscript);
			RefreshSlots();
		}
	}

	private int ResolveSquadLimit(string incidentId)
	{
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null && runtime.IsReady)
		{
			foreach (IncidentView incident in runtime.Session.GetActiveIncidents())
			{
				if (incident.Id == incidentId)
				{
					return incident.SquadLimit;
				}
			}
		}

		return 1;
	}

	private LootConfig ResolveLootConfig()
	{
		GameRuntime runtime = GameRuntime.Get(this);
		return runtime != null && runtime.IsReady ? runtime.Session.Config.Loot : new LootConfig();
	}

	private void RefreshSlots()
	{
		if (_missionDispatchUi == null)
		{
			return;
		}

		_missionDispatchUi.SetEmployeeNames(GetDispatchEmployeeNames());
		_missionDispatchUi.SetMainEquipmentNames(ResolveEquipmentNames(_mainEquipmentIds));
		_missionDispatchUi.SetConsumableNames(ResolveEquipmentNames(_consumableIds));
	}

	private IReadOnlyList<string> ResolveEquipmentNames(IReadOnlyList<string> ids)
	{
		var names = new string[ids.Count];
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return names;
		}

		for (int index = 0; index < ids.Count; index++)
		{
			if (string.IsNullOrEmpty(ids[index]))
			{
				continue;
			}

			foreach (EquipmentSlotView item in runtime.Session.GetAvailableEquipment())
			{
				if (item.Id == ids[index])
				{
					names[index] = item.Name;
					break;
				}
			}

			names[index] ??= ids[index];
		}

		return names;
	}

	private void OnEquipmentSlotRequested(int slotIndex, bool isConsumable)
	{
		if (!IsDispatchSelectionActive || _equipmentUi == null)
		{
			return;
		}

		_pendingEquipmentSlot = slotIndex;
		_pendingEquipmentIsConsumable = isConsumable;
		_equipmentUi.BeginSelection(isConsumable);
		OpenScreen(ComputerScreen.Equipment, false);
	}

	private void OnEquipmentChosen(string equipmentId)
	{
		if (_pendingEquipmentSlot < 0)
		{
			return;
		}

		string[] target = _pendingEquipmentIsConsumable ? _consumableIds : _mainEquipmentIds;
		if (_pendingEquipmentSlot < target.Length)
		{
			// Один и тот же предмет не должен занять два слота: ядро всё равно
			// откажет, но лучше не давать игроку собрать заведомо битый состав.
			for (int index = 0; index < target.Length; index++)
			{
				if (index != _pendingEquipmentSlot && target[index] == equipmentId)
				{
					target[index] = string.Empty;
				}
			}

			target[_pendingEquipmentSlot] = equipmentId;
		}

		CancelEquipmentSelection();
		RefreshSlots();
		OpenScreen(ComputerScreen.MissionDispatch, false);
	}

	private void CancelEquipmentSelection()
	{
		_pendingEquipmentSlot = -1;
		_equipmentUi?.EndSelection();
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

		MapMarkerController? mapController = GetTree().GetFirstNodeInGroup("map_marker_controller") as MapMarkerController;
		if (mapController == null)
		{
			return CommandResult.Fail("Контроллер маршрута карты недоступен.");
		}

		CommandResult result = mapController.TryDispatchSquad(
			_dispatchIncidentId,
			employeeIds,
			CollectEquipmentIds());
		if (!result.IsSuccess)
		{
			return result;
		}

		CancelDispatchSelection();
		OpenScreen(DefaultScreen, false);
		System.Action? dispatchCompleted = _dispatchCompleted;
		_dispatchCompleted = null;
		dispatchCompleted?.Invoke();
		return result;
	}

	public void CancelDispatchSelection()
	{
		_dispatchIncidentId = string.Empty;
		System.Array.Clear(_dispatchEmployeeIds, 0, _dispatchEmployeeIds.Length);
		System.Array.Clear(_mainEquipmentIds, 0, _mainEquipmentIds.Length);
		System.Array.Clear(_consumableIds, 0, _consumableIds.Length);
		CancelEquipmentSelection();
		_tabs[ComputerScreen.MissionDispatch].Visible = false;
	}

	private List<string> CollectEquipmentIds()
	{
		var ids = new List<string>();
		foreach (string[] slots in new[] { _mainEquipmentIds, _consumableIds })
		{
			for (int index = 0; index < slots.Length; index++)
			{
				if (!string.IsNullOrEmpty(slots[index]))
				{
					ids.Add(slots[index]);
				}
			}
		}

		return ids;
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
		RefreshSlots();
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
		GameRuntime runtime = GameRuntime.Get(this);
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

			foreach (EmployeeView employee in runtime.Session.GetRoster())
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
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null && runtime.IsReady)
		{
			foreach (IncidentView incident in runtime.Session.GetActiveIncidents())
			{
				if (incident.Id == incidentId)
				{
					return $"ВХОДЯЩИЙ ВЫЗОВ: {ContentTextResolver.ResolveCallMeta(incident.CallId, incident.CallId)}";
				}
			}
		}

		return "ВХОДЯЩИЙ ВЫЗОВ";
	}

	private string BuildDispatchTranscript(string incidentId)
	{
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null && runtime.IsReady)
		{
			foreach (IncidentView incident in runtime.Session.GetActiveIncidents())
			{
				if (incident.Id == incidentId)
				{
					return ContentSpanFormatter.ResolveEntryBbcode(
						incident.CallId,
						incident.CallId,
						DosTerminal.Marker);
				}
			}
		}

		return "Стенограмма вызова недоступна.";
	}
}
