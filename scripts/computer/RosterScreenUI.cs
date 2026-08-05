#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Личный состав: таблица «кто цел и где сейчас находится».
///
/// Состояние сотрудника ядро не хранит одним полем: сам он знает лишь, что
/// занят вызовом, а стадия выезда лежит в инциденте. Поэтому «возвращается»
/// вычисляется сопоставлением его CurrentIncidentId с фазой инцидента.
/// </summary>
public partial class RosterScreenUI : Control, IComputerScreen
{
	private const int NameColumn = 26;
	private const int HealthColumn = 12;

	private VBoxContainer _rows = null!;
	private Label _summary = null!;
	private readonly List<IDisposable> _subscriptions = new();

	public override void _Ready()
	{
		BuildLayout();
		Subscribe();
		Refresh();
	}

	public override void _ExitTree()
	{
		for (int i = 0; i < _subscriptions.Count; i++)
		{
			_subscriptions[i]?.Dispose();
		}

		_subscriptions.Clear();
	}

	public void OnScreenOpened() => Refresh();

	private void BuildLayout()
	{
		VBoxContainer column = DosTerminal.CreateFramedColumn("ЛИЧНЫЙ СОСТАВ", out PanelContainer frame);
		frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(frame);

		column.AddChild(DosTerminal.CreateLine(
			DosTerminal.Column("ФИО", NameColumn)
			+ DosTerminal.Column("СОСТОЯНИЕ", HealthColumn)
			+ "ПОЛОЖЕНИЕ",
			DosTerminal.TextDim));
		column.AddChild(DosTerminal.CreateSeparator());

		var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		column.AddChild(scroll);

		_rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_rows.AddThemeConstantOverride("separation", 2);
		scroll.AddChild(_rows);

		column.AddChild(DosTerminal.CreateSeparator());
		_summary = DosTerminal.CreateLine(string.Empty, DosTerminal.TextDim);
		column.AddChild(_summary);
	}

	/// <summary>Состав меняется по ходу смены — от отправки до возвращения и потерь.</summary>
	private void Subscribe()
	{
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return;
		}

		IEventBus events = runtime.Session.Events;
		_subscriptions.Add(events.Subscribe<SquadDispatched>(_ => Refresh()));
		_subscriptions.Add(events.Subscribe<SquadReturning>(_ => Refresh()));
		_subscriptions.Add(events.Subscribe<SquadReturned>(_ => Refresh()));
		_subscriptions.Add(events.Subscribe<EmployeeInjured>(_ => Refresh()));
		_subscriptions.Add(events.Subscribe<EmployeeKilled>(_ => Refresh()));
		_subscriptions.Add(events.Subscribe<EmployeeHired>(_ => Refresh()));
		_subscriptions.Add(events.Subscribe<ShiftStarted>(_ => Refresh()));
	}

	private void Refresh()
	{
		foreach (Node child in _rows.GetChildren())
		{
			_rows.RemoveChild(child);
			child.QueueFree();
		}

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			_rows.AddChild(DosTerminal.CreateLine("ЯДРО НЕДОСТУПНО", DosTerminal.TextDim));
			_summary.Text = string.Empty;
			return;
		}

		IReadOnlyList<EmployeeView> roster = runtime.Session.GetRoster();
		Dictionary<string, IncidentPhase> phases = BuildIncidentPhases(runtime);

		int ready = 0;
		int onMission = 0;
		int lost = 0;
		for (int i = 0; i < roster.Count; i++)
		{
			EmployeeView employee = roster[i];
			if (employee.Status == EmployeeStatus.Dead)
			{
				lost++;
			}
			else if (employee.Status == EmployeeStatus.OnMission)
			{
				onMission++;
			}
			else
			{
				ready++;
			}

			_rows.AddChild(CreateRow(employee, phases));
		}

		if (roster.Count == 0)
		{
			_rows.AddChild(DosTerminal.CreateLine("СОТРУДНИКОВ НЕТ", DosTerminal.TextDim));
		}

		_summary.Text = $"ВСЕГО: {roster.Count}   НА БАЗЕ: {ready}   НА ЗАДАНИИ: {onMission}   ПОТЕРИ: {lost}";
	}

	private static Dictionary<string, IncidentPhase> BuildIncidentPhases(GameRuntime runtime)
	{
		var phases = new Dictionary<string, IncidentPhase>();
		foreach (IncidentView incident in runtime.Session.GetActiveIncidents())
		{
			phases[incident.Id] = incident.Phase;
		}

		return phases;
	}

	private static Control CreateRow(EmployeeView employee, Dictionary<string, IncidentPhase> phases)
	{
		string health = DescribeHealth(employee);
		string position = DescribePosition(employee, phases);
		Label row = DosTerminal.CreateLine(
			DosTerminal.Column(employee.Name, NameColumn)
			+ DosTerminal.Column(health, HealthColumn)
			+ position,
			ResolveRowColor(employee));
		return row;
	}

	private static string DescribeHealth(EmployeeView employee)
	{
		if (employee.Status == EmployeeStatus.Dead)
		{
			return "УБИТ";
		}

		return employee.IsInjured ? "РАНЕН" : "ЗДОРОВ";
	}

	private static string DescribePosition(EmployeeView employee, Dictionary<string, IncidentPhase> phases)
	{
		if (employee.Status == EmployeeStatus.Dead)
		{
			return "—";
		}

		if (employee.Status != EmployeeStatus.OnMission)
		{
			return "НА БАЗЕ";
		}

		if (!string.IsNullOrEmpty(employee.CurrentIncidentId)
			&& phases.TryGetValue(employee.CurrentIncidentId, out IncidentPhase phase)
			&& phase == IncidentPhase.Returning)
		{
			return "ВОЗВРАЩАЕТСЯ";
		}

		return "НА ЗАДАНИИ";
	}

	private static Color ResolveRowColor(EmployeeView employee)
	{
		if (employee.Status == EmployeeStatus.Dead)
		{
			return DosTerminal.TextDim;
		}

		return employee.IsInjured ? DosTerminal.Text : DosTerminal.TextBright;
	}
}
