#nullable enable

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Экран текущего задания: наверху вызов, внизу состав группы и снаряжение.
///
/// Слоты не свёрстаны заранее: людей столько, сколько разрешает миссия
/// (<c>IncidentView.SquadLimit</c>), а слотов снаряжения — сколько объявлено в
/// <c>Config.Loot</c>. Раньше здесь стояли три статичных слота, и они спорили
/// с ядром: у всех текущих миссий предел равен двум.
/// </summary>
public partial class MissionDispatchUI : Control, IComputerScreen
{
	private Label _callTitle = null!;
	private RichTextLabel _callTranscript = null!;
	private VBoxContainer _employeeColumn = null!;
	private VBoxContainer _equipmentColumn = null!;
	private Label _feedback = null!;
	private Button _dispatchButton = null!;

	private readonly List<Slot> _employeeSlots = new();
	private readonly List<Slot> _mainSlots = new();
	private readonly List<Slot> _consumableSlots = new();

	/// <summary>Кнопка слота вместе со своей подсказкой: у каждого слота она своя, с номером.</summary>
	private sealed class Slot
	{
		public Slot(Button button, string placeholder)
		{
			Button = button;
			Placeholder = placeholder;
		}

		public Button Button { get; }

		public string Placeholder { get; }
	}

	public event Action<int>? EmployeeSlotRequested;

	/// <summary>Нажали слот снаряжения: индекс слота и признак «это расходник».</summary>
	public event Action<int, bool>? EquipmentSlotRequested;

	public event Action? DispatchRequested;

	public override void _Ready()
	{
		BuildLayout();
	}

	public void OnScreenOpened()
	{
		_feedback.Text = string.Empty;
	}

	public void SetCallDetails(string title, string transcript)
	{
		_callTitle.Text = title;
		_callTranscript.Text = transcript;
	}

	/// <summary>Пересобирает слоты под конкретный вызов.</summary>
	public void ConfigureSlots(int squadLimit, int mainSlots, int consumableSlots)
	{
		BuildSlotColumn(_employeeColumn, _employeeSlots, "СОСТАВ ГРУППЫ", squadLimit, 1, "Выберите сотрудника",
			index => EmployeeSlotRequested?.Invoke(index));

		foreach (Node child in _equipmentColumn.GetChildren())
		{
			_equipmentColumn.RemoveChild(child);
			child.QueueFree();
		}

		_mainSlots.Clear();
		_consumableSlots.Clear();

		// Нумерация сквозная по всей колонке: два слота с подписью «1» друг под
		// другом читались бы как ошибка, даже под разными заголовками.
		AppendSlots(_equipmentColumn, _mainSlots, "ОСНОВНОЕ СНАРЯЖЕНИЕ", mainSlots, 1, "Выберите оборудование",
			index => EquipmentSlotRequested?.Invoke(index, false));
		_equipmentColumn.AddChild(DosTerminal.CreateSeparator());
		AppendSlots(_equipmentColumn, _consumableSlots, "РАСХОДНИКИ", consumableSlots, mainSlots + 1, "Выберите оборудование",
			index => EquipmentSlotRequested?.Invoke(index, true));
	}

	public void SetEmployeeNames(IReadOnlyList<string> names) => FillSlots(_employeeSlots, names);

	public void SetMainEquipmentNames(IReadOnlyList<string> names) => FillSlots(_mainSlots, names);

	public void SetConsumableNames(IReadOnlyList<string> names) => FillSlots(_consumableSlots, names);

	public void SetFeedback(string message) => _feedback.Text = message;

	// ------------------------------------------------------------------ вёрстка

	private void BuildLayout()
	{
		var column = new VBoxContainer();
		column.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		column.AddThemeConstantOverride("separation", 8);
		AddChild(column);

		VBoxContainer call = DosTerminal.CreateFramedColumn("ТЕКУЩЕЕ ЗАДАНИЕ", out PanelContainer callFrame);
		callFrame.SizeFlagsVertical = SizeFlags.ExpandFill;
		callFrame.SizeFlagsStretchRatio = 1.0f;
		column.AddChild(callFrame);

		_callTitle = DosTerminal.CreateLine(string.Empty, DosTerminal.TextBright);
		_callTitle.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		call.AddChild(_callTitle);
		call.AddChild(DosTerminal.CreateSeparator());

		_callTranscript = new RichTextLabel
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			BbcodeEnabled = true,
			ScrollActive = true,
			FitContent = false
		};
		call.AddChild(_callTranscript);

		var bottom = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		bottom.AddThemeConstantOverride("separation", 8);
		bottom.SizeFlagsStretchRatio = 1.0f;
		column.AddChild(bottom);

		var employeeFrame = new PanelContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		bottom.AddChild(employeeFrame);
		_employeeColumn = new VBoxContainer();
		_employeeColumn.AddThemeConstantOverride("separation", 4);
		employeeFrame.AddChild(_employeeColumn);

		var equipmentFrame = new PanelContainer
		{
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		bottom.AddChild(equipmentFrame);
		_equipmentColumn = new VBoxContainer();
		_equipmentColumn.AddThemeConstantOverride("separation", 4);
		equipmentFrame.AddChild(_equipmentColumn);

		var footer = new HBoxContainer();
		footer.AddThemeConstantOverride("separation", 10);
		column.AddChild(footer);

		_feedback = DosTerminal.CreateLine(string.Empty, DosTerminal.TextDim);
		_feedback.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		footer.AddChild(_feedback);

		_dispatchButton = DosTerminal.CreateRow("ОТПРАВИТЬ ГРУППУ");
		_dispatchButton.SizeFlagsHorizontal = SizeFlags.ShrinkEnd;
		_dispatchButton.Alignment = HorizontalAlignment.Center;
		_dispatchButton.CustomMinimumSize = new Vector2(220.0f, 0.0f);
		_dispatchButton.Pressed += () => DispatchRequested?.Invoke();
		footer.AddChild(_dispatchButton);

		ConfigureSlots(1, 1, 2);
	}

	private static void BuildSlotColumn(
		VBoxContainer column,
		List<Slot> slots,
		string caption,
		int count,
		int firstNumber,
		string placeholderPrefix,
		Action<int> onPressed)
	{
		foreach (Node child in column.GetChildren())
		{
			column.RemoveChild(child);
			child.QueueFree();
		}

		slots.Clear();
		AppendSlots(column, slots, caption, count, firstNumber, placeholderPrefix, onPressed);
	}

	private static void AppendSlots(
		VBoxContainer column,
		List<Slot> slots,
		string caption,
		int count,
		int firstNumber,
		string placeholderPrefix,
		Action<int> onPressed)
	{
		column.AddChild(DosTerminal.CreateLine(caption, DosTerminal.TextDim));

		for (int i = 0; i < count; i++)
		{
			int index = i;
			string placeholder = $"{placeholderPrefix} {firstNumber + i}";
			Button button = DosTerminal.CreateRow(placeholder);
			button.Pressed += () => onPressed(index);
			column.AddChild(button);
			slots.Add(new Slot(button, placeholder));
		}
	}

	private static void FillSlots(List<Slot> slots, IReadOnlyList<string> names)
	{
		for (int i = 0; i < slots.Count; i++)
		{
			bool isFilled = i < names.Count && !string.IsNullOrEmpty(names[i]);
			slots[i].Button.Text = isFilled ? names[i] : slots[i].Placeholder;
			DosTerminal.SetRowSelected(slots[i].Button, isFilled);
		}
	}
}
