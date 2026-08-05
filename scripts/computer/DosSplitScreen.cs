#nullable enable

using System;
using System.Collections.Generic;
using Godot;

/// <summary>
/// Каркас вкладки «список слева, текст справа».
///
/// Энциклопедия и склад устроены одинаково, отличаясь только тем, чем набивают
/// строки и правую панель, — поэтому разметка, прокрутка и выделение живут
/// здесь, а наследникам остаётся отдать данные.
/// </summary>
public abstract partial class DosSplitScreen : Control, IComputerScreen
{
	private VBoxContainer _listColumn = null!;
	private VBoxContainer _rows = null!;
	private Label _listCaption = null!;
	private Label _listHeader = null!;
	private RichTextLabel _details = null!;
	private Label _summary = null!;
	private readonly List<Button> _rowButtons = new();
	private readonly List<IDisposable> _subscriptions = new();

	protected string SelectedId { get; private set; } = string.Empty;

	protected abstract string ListCaption { get; }

	protected abstract string DetailsCaption { get; }

	/// <summary>Шапка колонок списка. Пустая строка — шапки нет.</summary>
	protected virtual string ListHeader => string.Empty;

	public override void _Ready()
	{
		BuildLayout();
		Subscribe(_subscriptions);
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

	/// <summary>Строки списка: пара «id, готовая строка».</summary>
	protected abstract IReadOnlyList<(string Id, string Text)> GetRows();

	/// <summary>
	/// Текст правой панели для выбранной строки — в BBCode.
	///
	/// Панель разбирает разметку, поэтому обычный текст обязан проходить через
	/// <see cref="ContentSpanFormatter.Escape"/>: квадратная скобка из контента
	/// иначе будет съедена как открытие тега.
	/// </summary>
	protected abstract string GetDetails(string id);

	/// <summary>Подпись внизу: сводка по разделу.</summary>
	protected abstract string GetSummary();

	protected virtual void Subscribe(List<IDisposable> subscriptions)
	{
	}

	/// <summary>Кнопка под правой панелью. Наследники включают её, когда нужна.</summary>
	protected Button ActionButton { get; private set; } = null!;

	protected static GameRuntime? GetReadyRuntime(Node node)
	{
		GameRuntime runtime = GameRuntime.Get(node);
		return runtime != null && runtime.IsReady ? runtime : null;
	}

	protected void Refresh()
	{
		foreach (Node child in _rows.GetChildren())
		{
			_rows.RemoveChild(child);
			child.QueueFree();
		}

		_rowButtons.Clear();

		IReadOnlyList<(string Id, string Text)> rows = GetRows();
		if (rows.Count == 0)
		{
			_rows.AddChild(DosTerminal.CreateLine("ЗАПИСЕЙ НЕТ", DosTerminal.TextDim));
			SelectedId = string.Empty;
		}
		else
		{
			// Выбор мог указывать на строку, которой больше нет: склад пустеет,
			// а картотека пополняется между открытиями экрана.
			bool selectionSurvived = false;
			for (int i = 0; i < rows.Count; i++)
			{
				if (rows[i].Id == SelectedId)
				{
					selectionSurvived = true;
					break;
				}
			}

			if (!selectionSurvived)
			{
				SelectedId = rows[0].Id;
			}

			for (int i = 0; i < rows.Count; i++)
			{
				string id = rows[i].Id;
				Button row = DosTerminal.CreateRow(rows[i].Text);
				row.Pressed += () => Select(id);
				_rows.AddChild(row);
				_rowButtons.Add(row);
			}
		}

		// Подпись перечитывается каждый раз: у склада она зависит от режима.
		_listCaption.Text = ListCaption;
		_listHeader.Visible = !string.IsNullOrEmpty(ListHeader);
		_listHeader.Text = ListHeader;
		_summary.Text = GetSummary();
		RefreshSelection(rows);
	}

	private void Select(string id)
	{
		SelectedId = id;
		RefreshSelection(GetRows());
	}

	private void RefreshSelection(IReadOnlyList<(string Id, string Text)> rows)
	{
		for (int i = 0; i < _rowButtons.Count && i < rows.Count; i++)
		{
			DosTerminal.SetRowSelected(_rowButtons[i], rows[i].Id == SelectedId);
		}

		_details.Text = string.IsNullOrEmpty(SelectedId) ? string.Empty : GetDetails(SelectedId);
	}

	private void BuildLayout()
	{
		var split = new HBoxContainer();
		split.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		split.AddThemeConstantOverride("separation", 8);
		AddChild(split);

		_listColumn = DosTerminal.CreateFramedColumn(ListCaption, out PanelContainer listFrame, out _listCaption);
		listFrame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		listFrame.SizeFlagsStretchRatio = 1.0f;
		split.AddChild(listFrame);

		_listHeader = DosTerminal.CreateLine(string.Empty, DosTerminal.TextDim);
		_listColumn.AddChild(_listHeader);

		var listScroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		listScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		_listColumn.AddChild(listScroll);

		_rows = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		_rows.AddThemeConstantOverride("separation", 1);
		listScroll.AddChild(_rows);

		_listColumn.AddChild(DosTerminal.CreateSeparator());
		_summary = DosTerminal.CreateLine(string.Empty, DosTerminal.TextDim);
		_listColumn.AddChild(_summary);

		VBoxContainer detailsColumn = DosTerminal.CreateFramedColumn(DetailsCaption, out PanelContainer detailsFrame);
		detailsFrame.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		detailsFrame.SizeFlagsStretchRatio = 1.3f;
		split.AddChild(detailsFrame);

		_details = new RichTextLabel
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			BbcodeEnabled = true,
			ScrollActive = true,
			FitContent = false
		};
		detailsColumn.AddChild(_details);

		ActionButton = DosTerminal.CreateRow(string.Empty);
		ActionButton.Alignment = HorizontalAlignment.Center;
		ActionButton.Visible = false;
		detailsColumn.AddChild(ActionButton);
	}
}
