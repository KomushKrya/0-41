#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Config;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Склад: слева наименование и количество, справа описание выбранного.
///
/// Выдать предмет отсюда нельзя, и это не упущение: ядро привязывает
/// снаряжение к группе исключительно в момент отправки, отдельной команды
/// «выдать» у него нет. Поэтому в описании заодно напоминаются лимиты слотов —
/// иначе игрок узнавал бы о них только по отказу на экране отправки.
/// </summary>
public partial class EquipmentScreenUI : DosSplitScreen
{
	private const int NameColumn = 26;

	private bool _isSelecting;
	private bool _consumablesOnly;

	/// <summary>Игрок подтвердил предмет для слота снаряжения.</summary>
	public event Action<string>? ItemConfirmed;

	protected override string ListCaption => _isSelecting ? "ВЫБОР СНАРЯЖЕНИЯ" : "СКЛАД";

	protected override string DetailsCaption => "ОПИСАНИЕ";

	protected override string ListHeader =>
		DosTerminal.Column("НАИМЕНОВАНИЕ", NameColumn) + "КОЛ-ВО";

	protected override void Subscribe(List<IDisposable> subscriptions)
	{
		GameRuntime? runtime = GetReadyRuntime(this);
		if (runtime == null)
		{
			return;
		}

		// Склад живёт сам по себе: расходники сгорают на выезде, обычное
		// возвращается только после удачи, между сменами всё пополняется.
		IEventBus events = runtime.Session.Events;
		subscriptions.Add(events.Subscribe<EquipmentAcquired>(_ => Refresh()));
		subscriptions.Add(events.Subscribe<EquipmentConsumed>(_ => Refresh()));
		subscriptions.Add(events.Subscribe<EquipmentLost>(_ => Refresh()));
		subscriptions.Add(events.Subscribe<SquadDispatched>(_ => Refresh()));
		subscriptions.Add(events.Subscribe<ShiftStarted>(_ => Refresh()));
	}

	public override void _Ready()
	{
		base._Ready();
		ActionButton.Pressed += ConfirmSelection;
	}

	/// <summary>Оболочка терминала: по ней видно, идёт ли сейчас сбор группы.</summary>
	private ComputerUI? FindShell()
	{
		Node? current = GetParent();
		while (current != null)
		{
			if (current is ComputerUI shell)
			{
				return shell;
			}

			current = current.GetParent();
		}

		return null;
	}

	/// <summary>
	/// Открыть склад как выбор предмета под слот. Список сужается до нужного
	/// вида: в слот расходников нельзя положить сюжетное, и наоборот.
	/// </summary>
	public void BeginSelection(bool consumablesOnly)
	{
		_isSelecting = true;
		_consumablesOnly = consumablesOnly;
		ActionButton.Text = "Выбрать";
		ActionButton.Visible = true;
		Refresh();
	}

	public void EndSelection()
	{
		_isSelecting = false;
		ActionButton.Visible = false;
		Refresh();
	}

	protected override IReadOnlyList<(string Id, string Text)> GetRows()
	{
		var rows = new List<(string, string)>();
		GameRuntime? runtime = GetReadyRuntime(this);
		if (runtime == null)
		{
			return rows;
		}

		foreach (EquipmentSlotView item in runtime.Session.GetAvailableEquipment())
		{
			if (_isSelecting && (item.Kind == EquipmentKind.Consumable) != _consumablesOnly)
			{
				continue;
			}

			rows.Add((item.Id, DosTerminal.Column(item.Name, NameColumn) + item.Quantity));
		}

		return rows;
	}

	private void ConfirmSelection()
	{
		if (_isSelecting && !string.IsNullOrEmpty(SelectedId))
		{
			ItemConfirmed?.Invoke(SelectedId);
		}
	}

	protected override string GetDetails(string equipmentId)
	{
		GameRuntime? runtime = GetReadyRuntime(this);
		if (runtime == null)
		{
			return string.Empty;
		}

		foreach (EquipmentSlotView item in runtime.Session.GetAvailableEquipment())
		{
			if (item.Id != equipmentId)
			{
				continue;
			}

			// Название и описание снаряжения приходят из equipment.json, а не из
			// текстового движка: разметки в них нет, но экранировать всё равно надо.
			var text = new System.Text.StringBuilder();
			text.AppendLine(ContentSpanFormatter.Escape(item.Name.ToUpperInvariant()));
			text.AppendLine();
			text.AppendLine(ContentSpanFormatter.Escape(DescribeKind(item.Kind)));
			if (item.IsShiftOnly)
			{
				text.AppendLine(ContentSpanFormatter.Escape("Выдано только на текущую смену."));
			}

			text.AppendLine();
			text.AppendLine(ContentSpanFormatter.Escape($"На складе: {item.Quantity}"));
			if (!string.IsNullOrWhiteSpace(item.Description))
			{
				text.AppendLine();
				text.AppendLine(ContentSpanFormatter.Escape(item.Description));
			}

			return text.ToString();
		}

		return string.Empty;
	}

	protected override string GetSummary()
	{
		GameRuntime? runtime = GetReadyRuntime(this);
		if (runtime == null)
		{
			return "ЯДРО НЕДОСТУПНО";
		}

		int total = 0;
		foreach (EquipmentSlotView item in runtime.Session.GetAvailableEquipment())
		{
			total += item.Quantity;
		}

		// Лимиты слотов имеют смысл, только когда есть куда класть: вне сбора
		// группы это просто цифры, которые не к чему приложить.
		if (FindShell()?.IsDispatchSelectionActive != true)
		{
			return $"ВСЕГО: {total}";
		}

		LootConfig loot = runtime.Session.Config.Loot;
		return $"ВСЕГО: {total}   СЛОТЫ: {loot.StandardOrStorySlots}+{loot.ConsumableSlots}";
	}

	/// <summary>Вид снаряжения — это правило возврата, поэтому пишем именно его.</summary>
	private static string DescribeKind(EquipmentKind kind)
	{
		switch (kind)
		{
			case EquipmentKind.Consumable: return "Расходник: тратится за выезд.";
			case EquipmentKind.Standard: return "Обычное: возвращается после удачного выезда.";
			case EquipmentKind.Story: return "Сюжетное: теряется вместе с группой.";
			default: return kind.ToString();
		}
	}
}
