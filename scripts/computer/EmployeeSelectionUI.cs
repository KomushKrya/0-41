using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Model;

/// <summary>Экран выбора сотрудников. До подтверждения отправки выбор существует только в UI.</summary>
public partial class EmployeeSelectionUI : Control, IComputerScreen
{
	[Export] public NodePath RosterListPath { get; set; } = new("RosterScroll/RosterList");
	[Export] public NodePath SelectionSummaryPath { get; set; } = new("SelectionSummary");
	[Export] public NodePath DispatchButtonPath { get; set; } = new("DispatchButton");
	[Export] public NodePath DispatchFeedbackPath { get; set; } = new("DispatchFeedback");

	private readonly HashSet<string> _selectedEmployeeIds = new();
	private VBoxContainer _rosterList = null!;
	private Label _selectionSummary = null!;
	private Button _dispatchButton = null!;
	private Label _dispatchFeedback = null!;
	private bool _isDispatchSelection;

	public IReadOnlyCollection<string> SelectedEmployeeIds => _selectedEmployeeIds;

	public override void _Ready()
	{
		_rosterList = GetNode<VBoxContainer>(RosterListPath);
		_selectionSummary = GetNode<Label>(SelectionSummaryPath);
		_dispatchButton = GetNode<Button>(DispatchButtonPath);
		_dispatchFeedback = GetNode<Label>(DispatchFeedbackPath);
		_dispatchButton.Pressed += DispatchSelectedEmployees;
		_dispatchButton.Visible = false;
		RefreshRoster();
	}

	public override void _ExitTree()
	{
		_dispatchButton.Pressed -= DispatchSelectedEmployees;
	}

	public void OnScreenOpened()
	{
		RefreshRoster();
	}

	public void BeginDispatchSelection()
	{
		_isDispatchSelection = true;
		_selectedEmployeeIds.Clear();
		_dispatchFeedback.Text = string.Empty;
		_dispatchButton.Visible = true;
		RefreshRoster();
	}

	public void EndDispatchSelection()
	{
		_isDispatchSelection = false;
		_dispatchButton.Visible = false;
		_dispatchFeedback.Text = string.Empty;
	}

	private void RefreshRoster()
	{
		foreach (Node child in _rosterList.GetChildren())
		{
			_rosterList.RemoveChild(child);
			child.QueueFree();
		}

		KonturRuntime runtime = KonturRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			_rosterList.AddChild(new Label { Text = "ЯДРО НЕДОСТУПНО" });
			_selectionSummary.Text = "СОСТАВ: —";
			return;
		}

		IReadOnlyList<EmployeeView> roster = runtime.Simulation.GetRoster();
		var knownIds = new HashSet<string>();
		for (int index = 0; index < roster.Count; index++)
		{
			EmployeeView employee = roster[index];
			knownIds.Add(employee.Id);
			_rosterList.AddChild(CreateEmployeeCard(employee));
		}

		_selectedEmployeeIds.RemoveWhere(id => !knownIds.Contains(id));
		if (roster.Count == 0)
		{
			_rosterList.AddChild(new Label { Text = "СОТРУДНИКОВ НЕТ" });
		}

		UpdateSelectionSummary();
	}

	private Control CreateEmployeeCard(EmployeeView employee)
	{
		bool isAvailable = employee.Status == EmployeeStatus.Available;
		var card = new VBoxContainer
		{
			CustomMinimumSize = new Vector2(0.0f, 72.0f),
		};
		card.AddThemeConstantOverride("separation", 2);

		var check = new CheckBox
		{
			Text = $"{employee.Name}  //  {employee.RankTitle}  //  LVL {employee.Level}",
			ButtonPressed = _selectedEmployeeIds.Contains(employee.Id),
			Disabled = !isAvailable,
		};
		string employeeId = employee.Id;
		check.Toggled += isSelected => SetEmployeeSelected(employeeId, isSelected);
		card.AddChild(check);

		string state = isAvailable
			? employee.IsInjured ? "AVAILABLE · INJURED" : "AVAILABLE"
			: employee.Status == EmployeeStatus.Dead ? "DEAD" : "ON MISSION";
		string stats = $"STR {employee.Stats.Strength}  PER {employee.Stats.Intellect}  END {employee.Stats.Combat}  " +
			$"CHA {employee.Stats.Charisma}  COM {employee.Stats.Agility}";
		card.AddChild(new Label
		{
			Text = $"   {state}   |   {stats}",
			Modulate = isAvailable ? new Color(0.55f, 0.75f, 0.57f) : new Color(0.48f, 0.48f, 0.48f),
		});

		return card;
	}

	private void SetEmployeeSelected(string employeeId, bool isSelected)
	{
		if (isSelected)
		{
			_selectedEmployeeIds.Add(employeeId);
		}
		else
		{
			_selectedEmployeeIds.Remove(employeeId);
		}

		UpdateSelectionSummary();
	}

	private void UpdateSelectionSummary()
	{
		_selectionSummary.Text = _selectedEmployeeIds.Count == 0
			? "СОСТАВ: НЕ ВЫБРАН"
			: $"ВЫБРАНО СОТРУДНИКОВ: {_selectedEmployeeIds.Count}";
	}

	private void DispatchSelectedEmployees()
	{
		if (!_isDispatchSelection)
		{
			return;
		}

		Node current = GetParent();
		while (current != null && current is not ComputerUI)
		{
			current = current.GetParent();
		}

		if (current is not ComputerUI computerUi)
		{
			_dispatchFeedback.Text = "ИНТЕРФЕЙС ОТПРАВКИ НЕДОСТУПЕН";
			return;
		}

		var employeeIds = new List<string>(_selectedEmployeeIds);
		CommandResult result = computerUi.DispatchSelectedEmployees(employeeIds);
		if (!result.IsSuccess)
		{
			_dispatchFeedback.Text = result.Error;
		}
	}
}