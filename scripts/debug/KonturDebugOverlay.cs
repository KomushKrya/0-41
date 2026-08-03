using System;
using System.Collections.Generic;
using System.Text;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Отладочный оверлей симуляционного ядра. Отдельная сцена: scenes/debug/KonturDebug.tscn.
/// Никакой диегетики — это инструмент разработки, а не интерфейс игры.
///
/// Позволяет прогнать смену прямо в движке до того, как телефон, карта и компьютер
/// научатся реагировать на события: слева состояние ядра, справа поток сигналов,
/// снизу кнопки команд и ускорение времени. Открывается и закрывается по F6.
/// </summary>
public partial class KonturDebugOverlay : CanvasLayer
{
	private const int MaxLogLines = 200;

	[Export] public Key ToggleKey { get; set; } = Key.F6;
	[Export] public PackedScene RadioDecisionScene { get; set; } = null!;

	private GameRuntime _runtime;
	private RichTextLabel _status;
	private RichTextLabel _log;
	private Label _hint;

	private readonly List<string> _logLines = new();
	private readonly Dictionary<string, List<RadioOptionOffer>> _radioOptions = new();
	private IDisposable _logSubscription;
	private IDisposable _radioSubscription;
	private double _refreshAccumulator;
	private bool _isOpen;
	private Input.MouseModeEnum _previousMouseMode;
	private Control _debugRoot = null!;
	private Control _radioDecisionPreview = null!;

	// --- экран отправки ---
	private VBoxContainer _dispatchList;
	private RichTextLabel _dispatchSummary;
	private string _dispatchSignature = string.Empty;
	private string _dispatchIncidentId;
	private readonly HashSet<string> _pickedEmployees = new();
	private readonly HashSet<string> _pickedEquipment = new();

	public override void _Ready()
	{
		Layer = 100;

		BuildUi();
		Hide();
		SetProcess(false);

		_runtime = GameRuntime.Get(this);

		if (_runtime == null)
		{
			AppendLog("Автозагрузка 'GameRuntime' не найдена. Project → Project Settings → Autoload.");
			return;
		}

		if (!_runtime.IsReady)
		{
			AppendLog("Ядро не загрузилось: " + _runtime.LoadError);
			return;
		}

		_logSubscription = _runtime.Session.Events.SubscribeAll(OnCoreEvent);
		_radioSubscription = _runtime.Session.Events.Subscribe<RadioTriggered>(OnRadioTriggered);

		AppendLog("Ядро подключено. Нажмите «Смена 1».");
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is not InputEventKey keyEvent
			|| !keyEvent.Pressed
			|| keyEvent.Echo
			|| keyEvent.Keycode != ToggleKey)
		{
			return;
		}

		if (_radioDecisionPreview != null && _radioDecisionPreview.Visible)
		{
			if (keyEvent.Keycode == Key.Escape)
			{
				CloseRadioDecisionPreview();
				GetViewport().SetInputAsHandled();
			}

			return;
		}

		SetOpen(!_isOpen);
		GetViewport().SetInputAsHandled();
	}

	public override void _Process(double delta)
	{
		_refreshAccumulator += delta;
		if (_refreshAccumulator < 0.1)
		{
			return;
		}

		_refreshAccumulator = 0.0;
		RefreshStatus();
		RefreshDispatch();
	}

	private void SetOpen(bool isOpen)
	{
		_isOpen = isOpen;
		SetProcess(isOpen);

		if (isOpen)
		{
			_previousMouseMode = Input.MouseMode;
			Input.MouseMode = Input.MouseModeEnum.Visible;
			Show();
			RefreshStatus();
			RefreshDispatch();
			return;
		}

		CloseRadioDecisionPreview();
		Hide();
		Input.MouseMode = _previousMouseMode;
	}

	// ------------------------------------------------------------------ экран отправки

	/// <summary>
	/// Аналог экрана отправки на компьютере: выбор конкретных оперативников и снаряжения
	/// под конкретную метку. Именно здесь появляется главное решение смены — кого
	/// не отправлять, оставив в резерве под следующий вызов.
	/// </summary>
	private void RefreshDispatch()
	{
		if (!HasCore(false))
		{
			return;
		}

		KonturSimulation simulation = _runtime.Session;

		var markers = new List<IncidentView>();
		IReadOnlyList<IncidentView> incidents = simulation.GetActiveIncidents();
		for (int i = 0; i < incidents.Count; i++)
		{
			if (incidents[i].Phase == IncidentPhase.MarkerActive)
			{
				markers.Add(incidents[i]);
			}
		}

		bool selectionStillValid = false;
		for (int i = 0; i < markers.Count; i++)
		{
			if (markers[i].Id == _dispatchIncidentId)
			{
				selectionStillValid = true;
				break;
			}
		}

		if (!selectionStillValid)
		{
			// Метку отработали или она истекла — выбор состава сбрасываем.
			_dispatchIncidentId = markers.Count > 0 ? markers[0].Id : null;
			_pickedEmployees.Clear();
			_pickedEquipment.Clear();
		}

		IReadOnlyList<EmployeeView> roster = simulation.GetRoster();
		IReadOnlyList<EquipmentSlotView> stock = simulation.GetAvailableEquipment();

		string signature = BuildDispatchSignature(markers, roster, stock);
		if (signature != _dispatchSignature)
		{
			_dispatchSignature = signature;
			RebuildDispatch(markers, roster, stock);
		}

		UpdateDispatchSummary(simulation, stock);
	}

	private string BuildDispatchSignature(
		List<IncidentView> markers,
		IReadOnlyList<EmployeeView> roster,
		IReadOnlyList<EquipmentSlotView> stock)
	{
		var builder = new StringBuilder();
		builder.Append(_dispatchIncidentId).Append('|');

		for (int i = 0; i < markers.Count; i++)
		{
			builder.Append(markers[i].Id).Append(',');
		}

		builder.Append('|');
		for (int i = 0; i < roster.Count; i++)
		{
			builder.Append(roster[i].Id).Append(':').Append(roster[i].Status)
				.Append(roster[i].IsInjured ? "!" : string.Empty).Append(',');
		}

		builder.Append('|');
		for (int i = 0; i < stock.Count; i++)
		{
			builder.Append(stock[i].Id).Append('x').Append(stock[i].Quantity).Append(',');
		}

		return builder.ToString();
	}

	private void RebuildDispatch(
		List<IncidentView> markers,
		IReadOnlyList<EmployeeView> roster,
		IReadOnlyList<EquipmentSlotView> stock)
	{
		foreach (Node child in _dispatchList.GetChildren())
		{
			_dispatchList.RemoveChild(child);
			child.QueueFree();
		}

		_dispatchSummary = null;

		if (markers.Count == 0)
		{
			_dispatchList.AddChild(new Label
			{
				Text = "Нет активной метки.\nОтветьте на звонок через телефон.",
				AutowrapMode = TextServer.AutowrapMode.WordSmart
			});

			return;
		}

		// Меток может быть несколько одновременно — выбираем, для какой собираем группу.
		if (markers.Count > 1)
		{
			var picker = new HBoxContainer();
			picker.AddChild(new Label { Text = "Метка:" });

			for (int i = 0; i < markers.Count; i++)
			{
				string incidentId = markers[i].Id;
				var button = new Button
				{
					Text = incidentId,
					Disabled = incidentId == _dispatchIncidentId
				};

				button.Pressed += () =>
				{
					_dispatchIncidentId = incidentId;
					_pickedEmployees.Clear();
					_pickedEquipment.Clear();
					_dispatchSignature = string.Empty;
				};

				picker.AddChild(button);
			}

			_dispatchList.AddChild(picker);
		}

		IncidentView selected = markers[0];
		for (int i = 0; i < markers.Count; i++)
		{
			if (markers[i].Id == _dispatchIncidentId)
			{
				selected = markers[i];
				break;
			}
		}

		_dispatchList.AddChild(new Label
		{
			Text = ContentTextResolver.ResolveCallMeta(selected.CallId, selected.CallId),
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		});

		AddDimLabel(_dispatchList, $"{selected.Id}   район: {selected.BuildingId}");
		AddSectionLabel(_dispatchList, "ОПЕРАТИВНИКИ");

		int available = 0;
		for (int i = 0; i < roster.Count; i++)
		{
			EmployeeView employee = roster[i];

			if (employee.Status != EmployeeStatus.Available)
			{
				string reason = employee.Status == EmployeeStatus.Dead ? "погиб" : "на выезде";
				AddDimLabel(_dispatchList, $"   {employee.Name} — {reason}");
				continue;
			}

			available++;
			AddEmployeeCheck(employee);
		}

		if (available == 0)
		{
			AddDimLabel(_dispatchList, "   свободных нет — все на выездах");
		}

		AddSectionLabel(_dispatchList, "СНАРЯЖЕНИЕ (на группу)");

		if (stock.Count == 0)
		{
			AddDimLabel(_dispatchList, "   склад пуст");
		}

		for (int i = 0; i < stock.Count; i++)
		{
			AddEquipmentCheck(stock[i]);
		}

		_dispatchSummary = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			ScrollActive = false
		};

		_dispatchSummary.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_dispatchSummary.CustomMinimumSize = new Vector2(0, 90);
		_dispatchList.AddChild(_dispatchSummary);

		var send = new Button { Text = "Отправить выбранных" };
		send.Pressed += DispatchSelected;
		_dispatchList.AddChild(send);
	}

	private void AddEmployeeCheck(EmployeeView employee)
	{
		var box = new VBoxContainer();

		var check = new CheckBox
		{
			Text = $"{employee.Name}  ур.{employee.Level}",
			ButtonPressed = _pickedEmployees.Contains(employee.Id)
		};

		string employeeId = employee.Id;
		check.Toggled += pressed =>
		{
			if (pressed)
			{
				_pickedEmployees.Add(employeeId);
			}
			else
			{
				_pickedEmployees.Remove(employeeId);
			}
		};

		box.AddChild(check);

		string perks = employee.AbilityIds.Count > 0
			? "   перки: " + string.Join(", ", PerkNames(employee.AbilityIds))
			: string.Empty;

		AddDimLabel(box, $"   [{employee.Stats}]{(employee.IsInjured ? "  ТРАВМА −1 ко всему" : string.Empty)}");

		if (perks.Length > 0)
		{
			AddDimLabel(box, perks);
		}

		_dispatchList.AddChild(box);
	}

	private void AddEquipmentCheck(EquipmentSlotView slot)
	{
		var check = new CheckBox
		{
			Text = $"{slot.Name}  ({KindName(slot.Kind)}, x{slot.Quantity})",
			ButtonPressed = _pickedEquipment.Contains(slot.Id)
		};

		string equipmentId = slot.Id;
		check.Toggled += pressed =>
		{
			if (pressed)
			{
				_pickedEquipment.Add(equipmentId);
			}
			else
			{
				_pickedEquipment.Remove(equipmentId);
			}
		};

		_dispatchList.AddChild(check);
	}

	private void UpdateDispatchSummary(KonturSimulation simulation, IReadOnlyList<EquipmentSlotView> stock)
	{
		if (_dispatchSummary == null || _dispatchIncidentId == null)
		{
			return;
		}

		var employees = new List<string>(_pickedEmployees);
		var equipment = new List<string>(_pickedEquipment);

		DispatchEstimateView estimate = simulation.EstimateDispatch(_dispatchIncidentId, employees, equipment);
		if (estimate == null)
		{
			return;
		}

		int heavy = 0;
		int consumables = 0;
		for (int i = 0; i < equipment.Count; i++)
		{
			for (int s = 0; s < stock.Count; s++)
			{
				if (stock[s].Id != equipment[i])
				{
					continue;
				}

				if (stock[s].Kind == EquipmentKind.Consumable)
				{
					consumables++;
				}
				else
				{
					heavy++;
				}

				break;
			}
		}

		int heavyLimit = simulation.Config.Loot.StandardOrStorySlots;
		int consumableLimit = simulation.Config.Loot.ConsumableSlots;

		var builder = new StringBuilder();
		builder.Append("Требуется [color=#ffd166]").Append(estimate.Requirements).Append("[/color]\n");
		builder.Append("Группа    ").Append(employees.Count == 0 ? "—" : estimate.SquadStats.ToString()).Append('\n');

		if (estimate.IsAutoSuccess && employees.Count > 0)
		{
			builder.Append("[color=#9fd6a6]Требования покрыты — успех без броска[/color]\n");
		}
		else
		{
			builder.Append("Покрытие ").Append(estimate.Coverage.ToString("0.00"))
				.Append(" → шанс ").Append((estimate.SuccessChance * 100.0).ToString("0"))
				.Append(" %\n");
		}

		builder.Append("Слоты: тяжёлое ").Append(heavy).Append('/').Append(heavyLimit)
			.Append(", расходники ").Append(consumables).Append('/').Append(consumableLimit).Append('\n');

		if (heavy > heavyLimit || consumables > consumableLimit)
		{
			builder.Append("[color=#ff6b6b]Превышен лимит слотов — отправка не пройдёт[/color]");
		}
		else if (employees.Count == 0)
		{
			builder.Append("[color=#6f7a6f]Отметьте хотя бы одного оперативника[/color]");
		}

		_dispatchSummary.Text = builder.ToString();
	}

	private void DispatchSelected()
	{
		if (!HasCore() || _dispatchIncidentId == null)
		{
			AppendLog("Метка не выбрана.");
			return;
		}

		if (_pickedEmployees.Count == 0)
		{
			AppendLog("Не отмечен ни один оперативник.");
			return;
		}

		string incidentId = _dispatchIncidentId;
		_runtime.Session.OpenDispatchScreen(incidentId);

		CommandResult result = DispatchUsingMapRoute(
			incidentId,
			new List<string>(_pickedEmployees),
			new List<string>(_pickedEquipment));

		Report("Отправка " + incidentId, result);

		if (result.IsSuccess)
		{
			_pickedEmployees.Clear();
			_pickedEquipment.Clear();
			_dispatchSignature = string.Empty;
		}
	}

	private static string KindName(EquipmentKind kind)
	{
		switch (kind)
		{
			case EquipmentKind.Consumable: return "расходник";
			case EquipmentKind.Standard: return "обычное";
			case EquipmentKind.Story: return "сюжетное";
			default: return kind.ToString();
		}
	}

	private static void AddSectionLabel(Control parent, string text)
	{
		var label = new Label { Text = text };
		label.AddThemeColorOverride("font_color", new Color(0.62f, 0.85f, 0.66f));
		parent.AddChild(label);
	}

	/// <summary>
	/// Ядро отдаёт только id перков — названия лежат в текстовом движке
	/// (content/raw/UI/perks). Если текста нет, показываем сам id: для отладочного
	/// оверлея это полезнее пустого места.
	/// </summary>
	private static List<string> PerkNames(IReadOnlyList<string> abilityIds)
	{
		var names = new List<string>();
		for (int i = 0; i < abilityIds.Count; i++)
		{
			ContentEntry entry = Content.Instance?.GetEntry(abilityIds[i]);
			names.Add(entry != null && entry.Name.Length > 0 ? entry.Name : abilityIds[i]);
		}

		return names;
	}

	private static void AddDimLabel(Control parent, string text)
	{
		var label = new Label
		{
			Text = text,
			AutowrapMode = TextServer.AutowrapMode.WordSmart
		};

		label.AddThemeColorOverride("font_color", new Color(0.55f, 0.6f, 0.55f));
		parent.AddChild(label);
	}

	public override void _ExitTree()
	{
		_logSubscription?.Dispose();
		_radioSubscription?.Dispose();
		_logSubscription = null;
		_radioSubscription = null;
	}

	// ------------------------------------------------------------------ интерфейс

	private void BuildUi()
	{
		var root = new Control();
		_debugRoot = root;
		root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		root.MouseFilter = Control.MouseFilterEnum.Ignore;
		AddChild(root);

		var margin = new MarginContainer();
		margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		margin.MouseFilter = Control.MouseFilterEnum.Ignore;
		margin.AddThemeConstantOverride("margin_left", 12);
		margin.AddThemeConstantOverride("margin_top", 12);
		margin.AddThemeConstantOverride("margin_right", 12);
		margin.AddThemeConstantOverride("margin_bottom", 12);
		root.AddChild(margin);

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 8);
		margin.AddChild(column);

		var panes = new HBoxContainer();
		panes.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		panes.AddThemeConstantOverride("separation", 8);
		column.AddChild(panes);

		// Состояние перерисовывается 10 раз в секунду: автопрокрутка утащила бы панель
		// в конец и спрятала шапку со шкалами. Логу автопрокрутка, наоборот, нужна.
		_status = CreatePane(panes, "СОСТОЯНИЕ ЯДРА", false);
		_dispatchList = CreateListPane(panes, "ОТПРАВКА ГРУППЫ");
		_log = CreatePane(panes, "СИГНАЛЫ", true);

		column.AddChild(BuildShiftButtons());
		column.AddChild(BuildCommandButtons());
		column.AddChild(BuildTimeButtons());

		_hint = new Label
		{
			Text = "F6 — закрыть. Ядро не знает о сценах — команды идут через KonturSimulation."
		};
		_hint.AddThemeColorOverride("font_color", new Color(0.55f, 0.62f, 0.55f));
		column.AddChild(_hint);
	}

	private RichTextLabel CreatePane(Control parent, string title, bool followScroll)
	{
		var panel = new PanelContainer();
		panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		panel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		parent.AddChild(panel);

		var inner = new MarginContainer();
		inner.AddThemeConstantOverride("margin_left", 10);
		inner.AddThemeConstantOverride("margin_top", 8);
		inner.AddThemeConstantOverride("margin_right", 10);
		inner.AddThemeConstantOverride("margin_bottom", 8);
		panel.AddChild(inner);

		var column = new VBoxContainer();
		inner.AddChild(column);

		var header = new Label { Text = title };
		header.AddThemeColorOverride("font_color", new Color(0.62f, 0.85f, 0.66f));
		column.AddChild(header);

		var text = new RichTextLabel
		{
			BbcodeEnabled = true,
			ScrollFollowing = followScroll,
			SelectionEnabled = true
		};

		text.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		text.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		column.AddChild(text);

		return text;
	}

	/// <summary>Панель со списком: прокручиваемая колонка, в которую складываются галочки.</summary>
	private VBoxContainer CreateListPane(Control parent, string title)
	{
		var panel = new PanelContainer();
		panel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		panel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		parent.AddChild(panel);

		var inner = new MarginContainer();
		inner.AddThemeConstantOverride("margin_left", 10);
		inner.AddThemeConstantOverride("margin_top", 8);
		inner.AddThemeConstantOverride("margin_right", 10);
		inner.AddThemeConstantOverride("margin_bottom", 8);
		panel.AddChild(inner);

		var column = new VBoxContainer();
		inner.AddChild(column);

		var header = new Label { Text = title };
		header.AddThemeColorOverride("font_color", new Color(0.62f, 0.85f, 0.66f));
		column.AddChild(header);

		var scroll = new ScrollContainer();
		scroll.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		column.AddChild(scroll);

		var list = new VBoxContainer();
		list.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		scroll.AddChild(list);

		return list;
	}

	private HBoxContainer BuildShiftButtons()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		row.AddChild(new Label { Text = "Смена:" });

		for (int day = 1; day <= 4; day++)
		{
			int captured = day;
			row.AddChild(CreateButton(day.ToString(), () => StartShift(captured)));
		}

		row.AddChild(CreateButton("Сброс партии", ResetGame));
		row.AddChild(CreateButton("START ROSTER", ConfirmDebugStartingRoster));
		row.AddChild(CreateButton("Закрыть смену", ForceEndShift));

		return row;
	}

	private HBoxContainer BuildCommandButtons()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		row.AddChild(new Label { Text = "Команды:" });
		row.AddChild(CreateButton("Ответить", AnswerFirstCall));
		row.AddChild(CreateButton("Отправить всех", DispatchFirstMarker));

		for (int index = 0; index < 3; index++)
		{
			int captured = index;
			row.AddChild(CreateButton($"Радио {index + 1}", () => ChooseRadio(captured)));
		}

		row.AddChild(CreateButton("Экран рации", ShowRadioDecisionPreview));

		return row;
	}

	private void ShowRadioDecisionPreview()
	{
		if (RadioDecisionScene == null)
		{
			AppendLog("Не назначена сцена RadioDecisionUI.");
			return;
		}

		if (_radioDecisionPreview == null)
		{
			_radioDecisionPreview = RadioDecisionScene.Instantiate<Control>();
			AddChild(_radioDecisionPreview);
		}

		_debugRoot.Hide();
		if (_radioDecisionPreview is RadioDecisionUI radioDecisionUi)
		{
			radioDecisionUi.ShowWithTransition();
			return;
		}

		_radioDecisionPreview.Show();
	}

	private void CloseRadioDecisionPreview()
	{
		if (_radioDecisionPreview == null || !_radioDecisionPreview.Visible)
		{
			return;
		}

		if (_radioDecisionPreview is RadioDecisionUI radioDecisionUi)
		{
			radioDecisionUi.StopTransition();
		}

		_radioDecisionPreview.Hide();
		if (_isOpen)
		{
			_debugRoot.Show();
		}
	}

	private HBoxContainer BuildTimeButtons()
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 6);

		row.AddChild(new Label { Text = "Время:" });
		row.AddChild(CreateButton("x1", () => SetTimeScale(1.0f)));
		row.AddChild(CreateButton("x5", () => SetTimeScale(5.0f)));
		row.AddChild(CreateButton("x20", () => SetTimeScale(20.0f)));
		row.AddChild(CreateButton("Пауза", TogglePause));
		row.AddChild(CreateButton("Очистить лог", () => { _logLines.Clear(); _log.Text = string.Empty; }));

		return row;
	}

	private Button CreateButton(string text, Action onPressed)
	{
		var button = new Button { Text = text };
		button.Pressed += () => onPressed();
		return button;
	}

	// ------------------------------------------------------------------ команды

	private void StartShift(int day)
	{
		if (!HasCore())
		{
			return;
		}

		Report($"Смена {day}", _runtime.Session.StartShift(day));
	}

	private void ResetGame()
	{
		if (!HasCore())
		{
			return;
		}

		_radioOptions.Clear();
		_runtime.Session.ResetToNewGame();
		AppendLog("Партия сброшена.");
	}

	private void ConfirmDebugStartingRoster()
	{
		if (!HasCore()) return;
		IReadOnlyList<HireCandidateView> candidates = _runtime.Session.GetStartingChoice();
		if (candidates.Count == 0)
		{
			AppendLog("Starting roster selection is disabled or already confirmed.");
			return;
		}

		int limit = _runtime.Session.Config.GetStaffLimit(1);
		var ids = new List<string>();
		for (int i = 0; i < candidates.Count && i < limit; i++) ids.Add(candidates[i].Id);
		Report("Confirm starting roster", _runtime.Session.ConfirmStartingRoster(ids));
	}

	private void ForceEndShift()
	{
		if (!HasCore())
		{
			return;
		}

		_runtime.Session.ForceEndShift();
	}

	private void AnswerFirstCall()
	{
		IncidentView incident = FindFirst(IncidentPhase.Ringing);
		if (incident == null)
		{
			AppendLog("Нет звонящего телефона.");
			return;
		}

		CommandResult answer = _runtime.Session.AnswerCall(incident.Id);
		Report("Ответить", answer);
		if (answer.IsSuccess)
		{
			Report("Подтвердить брифинг", _runtime.Session.ConfirmBriefing(incident.Id));
		}
	}

	private void DispatchFirstMarker()
	{
		IncidentView incident = FindFirst(IncidentPhase.MarkerActive);
		if (incident == null)
		{
			AppendLog("Нет активной метки на карте.");
			return;
		}

		_runtime.Session.OpenDispatchScreen(incident.Id);

		var squad = new List<string>();
		IReadOnlyList<EmployeeView> roster = _runtime.Session.GetRoster();
		for (int i = 0; i < roster.Count && squad.Count < 3; i++)
		{
			if (roster[i].Status == EmployeeStatus.Available)
			{
				squad.Add(roster[i].Id);
			}
		}

		if (squad.Count == 0)
		{
			AppendLog("Свободных сотрудников нет.");
			return;
		}

		Report("Отправка", DispatchUsingMapRoute(incident.Id, squad, PickEquipment()));
	}

	private CommandResult DispatchUsingMapRoute(
		string incidentId,
		IReadOnlyList<string> employeeIds,
		IReadOnlyList<string> equipmentIds)
	{
		MapMarkerController mapController = GetNodeOrNull<MapMarkerController>("../WallMap/MapMarkerController");
		return mapController == null
			? CommandResult.Fail("Контроллер маршрута карты не найден.")
			: mapController.TryDispatchSquad(incidentId, employeeIds, equipmentIds);
	}

	private List<string> PickEquipment()
	{
		var result = new List<string>();
		int heavy = 0;
		int consumables = 0;

		IReadOnlyList<EquipmentSlotView> stock = _runtime.Session.GetAvailableEquipment();
		for (int i = 0; i < stock.Count; i++)
		{
			if (stock[i].Kind == EquipmentKind.Consumable)
			{
				if (consumables >= _runtime.Session.Config.Loot.ConsumableSlots)
				{
					continue;
				}

				consumables++;
			}
			else
			{
				if (heavy >= _runtime.Session.Config.Loot.StandardOrStorySlots)
				{
					continue;
				}

				heavy++;
			}

			result.Add(stock[i].Id);
		}

		return result;
	}

	private void ChooseRadio(int optionIndex)
	{
		IncidentView incident = FindFirst(IncidentPhase.RadioPending);
		if (incident == null)
		{
			AppendLog("Радио сейчас не активно.");
			return;
		}

		List<RadioOptionOffer> options;
		if (!_radioOptions.TryGetValue(incident.Id, out options) || optionIndex >= options.Count)
		{
			AppendLog($"Вариант {optionIndex + 1} недоступен.");
			return;
		}

		Report($"Радио {optionIndex + 1}", _runtime.Session.ChooseRadioOption(incident.Id, options[optionIndex].Id));
	}

	private void SetTimeScale(float scale)
	{
		if (_runtime == null)
		{
			return;
		}

		_runtime.TimeScale = scale;
		_runtime.IsPaused = false;
		AppendLog($"Скорость: x{scale}");
	}

	private void TogglePause()
	{
		if (_runtime == null)
		{
			return;
		}

		_runtime.IsPaused = !_runtime.IsPaused;
		AppendLog(_runtime.IsPaused ? "Пауза." : "Продолжение.");
	}

	// ------------------------------------------------------------------ состояние

	private void RefreshStatus()
	{
		if (!HasCore(false))
		{
			return;
		}

		KonturSimulation simulation = _runtime.Session;
		ShiftStatusView status = simulation.GetStatus();
		var builder = new StringBuilder();

		builder.Append("День ").Append(status.Day)
			.Append("   время ").Append(status.ShiftTime.ToString("0.0"))
			.Append(" с   ").Append(status.IsShiftActive ? "смена идёт" : "смена не идёт")
			.Append(_runtime.IsPaused ? "   [ПАУЗА]" : string.Empty)
			.Append("   x").Append(_runtime.TimeScale.ToString("0.#"))
			.Append('\n');

		builder.Append("Окно вызовов: ").Append(status.IsCallWindowClosed ? "закрыто" : "открыто")
			.Append("   в очереди: ").Append(status.PendingCalls)
			.Append("   открыто: ").Append(status.OpenIncidents)
			.Append("   лимит штата: ").Append(status.StaffLimit)
			.Append("\n\n");

		builder.Append("[color=#9fd6a6]ШКАЛЫ[/color]\n")
			.Append("  Заражение  ").Append(Bar(status.Scales.Infection)).Append('\n')
			.Append("  Гласность  ").Append(Bar(status.Scales.Publicity)).Append('\n')
			.Append("  Лояльность ").Append(Bar(status.Scales.Loyalty)).Append('\n');

		if (status.IsGameOver && status.GameOverReason.HasValue)
		{
			builder.Append("[color=#ff6b6b]GAME OVER: ").Append(status.GameOverReason.Value).Append("[/color]\n");
		}

		builder.Append("\n[color=#9fd6a6]ВЫЗОВЫ[/color]\n");
		IReadOnlyList<IncidentView> incidents = simulation.GetActiveIncidents();
		if (incidents.Count == 0)
		{
			builder.Append("  (нет активных)\n");
		}

		for (int i = 0; i < incidents.Count; i++)
		{
			IncidentView incident = incidents[i];
			builder.Append("  ").Append(incident.Id)
				.Append("  ").Append(PhaseName(incident.Phase));

			if (incident.RemainingSeconds > 0.0)
			{
				builder.Append(" ").Append(incident.RemainingSeconds.ToString("0.0")).Append(" с");
			}
			else if (IsWaitingForPlayer(incident.Phase))
			{
				// Обучающая смена: таймеры игрока отключены, вызов ждёт действия сколько угодно.
				builder.Append(" [color=#6f7a6f]ждёт действия[/color]");
			}

			builder.Append("\n    ").Append(ContentTextResolver.ResolveCallMeta(incident.CallId, incident.CallId))
				.Append("  требуется [").Append(incident.Requirements).Append("]\n");

			if (incident.SquadEmployeeIds.Count > 0)
			{
				builder.Append("    группа: ").Append(string.Join(", ", incident.SquadEmployeeIds)).Append('\n');
			}
		}

		builder.Append("\n[color=#9fd6a6]ШТАТ[/color]\n");
		IReadOnlyList<EmployeeView> roster = simulation.GetRoster();
		for (int i = 0; i < roster.Count; i++)
		{
			EmployeeView employee = roster[i];
			string state = employee.Status == EmployeeStatus.Dead
				? "[color=#ff6b6b]погиб[/color]"
				: employee.Status == EmployeeStatus.OnMission
					? "на выезде"
					: employee.IsInjured ? "[color=#ffd166]травма[/color]" : "в строю";

			builder.Append("  ").Append(employee.Name)
				.Append("  ур.").Append(employee.Level)
				.Append("  [").Append(employee.Stats).Append("]  ")
				.Append(state);

			if (employee.UnspentSkillPoints > 0)
			{
				builder.Append("  [color=#ffd166]очков навыков: ")
					.Append(employee.UnspentSkillPoints).Append("[/color]");
			}

			builder.Append('\n');

			if (employee.AbilityIds.Count > 0)
			{
				builder.Append("    перки: ")
					.Append(string.Join(", ", PerkNames(employee.AbilityIds))).Append('\n');
			}
		}

		builder.Append("\n[color=#9fd6a6]СКЛАД[/color]\n");
		IReadOnlyList<EquipmentSlotView> stock = simulation.GetAvailableEquipment();
		if (stock.Count == 0)
		{
			builder.Append("  (пусто)\n");
		}

		for (int i = 0; i < stock.Count; i++)
		{
			builder.Append("  ").Append(stock[i].Name)
				.Append(" x").Append(stock[i].Quantity)
				.Append("  (").Append(stock[i].Kind).Append(")\n");
		}

		AppendEncyclopedia(builder, simulation);
		AppendReports(builder, simulation);

		_status.Text = builder.ToString();
	}

	/// <summary>
	/// Энциклопедия существ. В игре это раздел компьютера (ДД, раздел 2);
	/// здесь — тот же самый срез ядра, только без диегетики.
	/// Запись появляется после первого успешного опознания, абзацы 1–3 открываются,
	/// когда соответствующее свойство проявилось на вызове и группа выжила.
	/// </summary>
	private void AppendEncyclopedia(StringBuilder builder, KonturSimulation simulation)
	{
		builder.Append("\n[color=#9fd6a6]ЭНЦИКЛОПЕДИЯ[/color]\n");

		IReadOnlyList<EncyclopediaEntryView> entries = simulation.GetEncyclopedia();
		if (entries.Count == 0)
		{
			builder.Append("  (пусто — существо попадёт сюда после первого вызова, с которого вернулась группа)\n");
			return;
		}

		for (int i = 0; i < entries.Count; i++)
		{
			EncyclopediaEntryView entry = entries[i];

			// Ядро отдаёт только id: имя и абзацы лежат в текстовом движке.
			ContentEntry article = Content.Instance?.GetEntry(entry.CreatureId);
			string title = article != null && article.Name.Length > 0 ? article.Name : entry.CreatureId;

			builder.Append("  [b]").Append(title).Append("[/b]  ")
				.Append("свойств ").Append(entry.RevealedPropertyIds.Count)
				.Append(" из ").Append(entry.TotalProperties).Append('\n');

			for (int p = 0; p < entry.RevealedPropertyIds.Count; p++)
			{
				string propertyId = entry.RevealedPropertyIds[p];
				builder.Append("    • ").Append(propertyId);

				string paragraph = FindParagraph(article, propertyId);
				if (paragraph.Length > 0)
				{
					builder.Append(" — ").Append(Shorten(paragraph, 120));
				}

				builder.Append('\n');
			}

			int hidden = entry.TotalProperties - entry.RevealedPropertyIds.Count;
			if (hidden > 0)
			{
				builder.Append("    [color=#6f7a6f]скрыто свойств: ").Append(hidden).Append("[/color]\n");
			}
		}
	}

	/// <summary>Абзац статьи, помеченный этим свойством. Пусто, если статьи нет.</summary>
	private static string FindParagraph(ContentEntry article, string propertyId)
	{
		if (article == null)
		{
			return string.Empty;
		}

		foreach (ContentChunk chunk in article.Chunks)
		{
			if (chunk.Reveal == propertyId)
			{
				return chunk.Text;
			}
		}

		return string.Empty;
	}

	/// <summary>Отчёты, которые в игре появляются на компьютере после возвращения группы.</summary>
	private void AppendReports(StringBuilder builder, KonturSimulation simulation)
	{
		builder.Append("\n[color=#9fd6a6]ОТЧЁТЫ[/color]\n");

		IReadOnlyList<MissionReport> reports = simulation.GetReports();
		if (reports.Count == 0)
		{
			builder.Append("  (пусто)\n");
			return;
		}

		int from = Math.Max(0, reports.Count - 5);
		for (int i = from; i < reports.Count; i++)
		{
			MissionReport report = reports[i];
			builder.Append("  ").Append(report.IncidentId).Append("  ")
				.Append(report.IsSuccess ? "[color=#9fd6a6]УСПЕХ[/color]" : "[color=#ff6b6b]ПРОВАЛ[/color]")
				.Append("  ")
				.Append(string.IsNullOrEmpty(report.CreatureId) ? "существо не опознано" : report.CreatureId)
				.Append('\n');

			if (report.RevealedPropertyIds.Count > 0)
			{
				builder.Append("    открыты свойства: ")
					.Append(string.Join(", ", report.RevealedPropertyIds)).Append('\n');
			}
		}
	}

	private static string Shorten(string text, int maxLength)
	{
		if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
		{
			return text;
		}

		return text.Substring(0, maxLength) + "…";
	}

	private static string Bar(double value)
	{
		int filled = (int)Math.Round(value / 5.0);
		filled = Math.Max(0, Math.Min(20, filled));

		var builder = new StringBuilder();
		builder.Append('[');
		for (int i = 0; i < 20; i++)
		{
			builder.Append(i < filled ? '#' : '.');
		}

		builder.Append("] ").Append(value.ToString("0.0"));
		return builder.ToString();
	}

	private static bool IsWaitingForPlayer(IncidentPhase phase)
	{
		return phase == IncidentPhase.Ringing
			|| phase == IncidentPhase.Briefing
			|| phase == IncidentPhase.MarkerActive
			|| phase == IncidentPhase.RadioPending;
	}

	private static string PhaseName(IncidentPhase phase)
	{
		switch (phase)
		{
			case IncidentPhase.Ringing: return "[color=#ffd166]ТЕЛЕФОН ЗВОНИТ[/color]";
			case IncidentPhase.Briefing: return "[color=#ffd166]ЭКРАН ЗАДАНИЯ[/color]";
			case IncidentPhase.MarkerActive: return "[color=#ffd166]МЕТКА НА КАРТЕ[/color]";
			case IncidentPhase.Travelling: return "в пути";
			case IncidentPhase.OnSite: return "на объекте";
			case IncidentPhase.RadioPending: return "[color=#ff9f43]РАДИО[/color]";
			case IncidentPhase.Returning: return "возвращается";
			default: return phase.ToString();
		}
	}

	// ------------------------------------------------------------------ лог

	private void OnCoreEvent(IGameEvent gameEvent)
	{
		AppendLog(gameEvent.GetType().Name + ": " + gameEvent);
	}

	private void OnRadioTriggered(RadioTriggered radioEvent)
	{
		var options = new List<RadioOptionOffer>(radioEvent.Options);
		_radioOptions[radioEvent.IncidentId] = options;

		var builder = new StringBuilder("Варианты по радио:");
		for (int i = 0; i < options.Count; i++)
		{
			builder.Append("\n   ").Append(i + 1).Append(") ").Append(options[i].Id).Append(" — ").Append(options[i].Requirements);
		}

		AppendLog(builder.ToString());
	}

	private void AppendLog(string line)
	{
		_logLines.Add(line.Replace("[", "[lb]"));

		if (_logLines.Count > MaxLogLines)
		{
			_logLines.RemoveRange(0, _logLines.Count - MaxLogLines);
		}

		if (_log != null)
		{
			_log.Text = string.Join("\n", _logLines);
		}
	}

	private IncidentView FindFirst(IncidentPhase phase)
	{
		if (!HasCore())
		{
			return null;
		}

		IReadOnlyList<IncidentView> incidents = _runtime.Session.GetActiveIncidents();
		for (int i = 0; i < incidents.Count; i++)
		{
			if (incidents[i].Phase == phase)
			{
				return incidents[i];
			}
		}

		return null;
	}

	private bool HasCore(bool logIfMissing = true)
	{
		if (_runtime != null && _runtime.IsReady)
		{
			return true;
		}

		if (logIfMissing)
		{
			AppendLog("Ядро недоступно.");
		}

		return false;
	}

	private void Report(string action, CommandResult result)
	{
		AppendLog(result.IsSuccess ? $"{action}: принято." : $"{action}: {result.Error}");
	}
}
