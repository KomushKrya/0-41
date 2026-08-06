using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Model;

/// <summary>
/// Экран набора людей карточками в ряд — как выбор дупликантов в Oxygen Not Included.
///
/// Два режима, одна вёрстка:
///   • стартовый выбор — собрать первую бригаду из предложенного пула;
///   • добор между сменами — взять людей на освободившиеся места.
/// Разница только в источнике списка и в правиле «сколько можно взять», поэтому
/// делать два экрана было бы копированием без смысла.
///
/// Вёрстка лежит в HiringScreen.tscn и правится в редакторе. Сама карточка —
/// отдельная сцена HireCandidateCard.tscn: их число зависит от пула, поэтому
/// в сцене экрана их нет, скрипт создаёт нужное количество копий шаблона.
/// </summary>
public partial class HiringScreen : Control
{
	[Export] public NodePath TitlePath { get; set; } = new("Column/Title");
	[Export] public NodePath CardRowPath { get; set; } = new("Column/Scroll/Row");
	[Export] public NodePath CounterPath { get; set; } = new("Column/Footer/Counter");
	[Export] public NodePath ConfirmButtonPath { get; set; } = new("Column/Footer/ConfirmButton");

	/// <summary>Сцена карточки кандидата. Задаётся в HiringScreen.tscn.</summary>
	[Export] public PackedScene CandidateCardScene { get; set; }

	private readonly List<HireCandidateView> _candidates = new();
	private readonly HashSet<string> _picked = new();
	private readonly Dictionary<string, HireCandidateCard> _cards = new();

	private Label _title;
	private Label _counter;
	private Button _confirm;
	private Control _row;

	private bool _isStartingChoice;
	private int _day = 1;
	private int _slots;

	public override void _Ready()
	{
		// Сюда приходят прямо из кабинета, где мышь захвачена игроком.
		// Без этой строки карточки кандидатов не нажать: курсора не видно.
		CursorMode.Show(this);

		if (GameFlow.Instance != null)
		{
			_isStartingChoice = GameFlow.Instance.HiringIsStartingChoice;
			_day = GameFlow.Instance.HiringDay;
		}

		BindUi();
		LoadCandidates();
	}

	// ------------------------------------------------------------------ данные

	private void LoadCandidates()
	{
		KonturSimulation simulation = GameFlow.Instance?.Simulation;
		if (simulation == null)
		{
			_title.Text = Content.Label("ui_hiring_no_core");
			return;
		}

		_candidates.Clear();
		_candidates.AddRange(_isStartingChoice
			? simulation.GetStartingChoice()
			: simulation.GetHireCandidates(_day));

		// Сколько человек можно взять. При стартовом выборе это весь штат первой
		// смены, при доборе — только свободные места.
		//
		// Штат берётся у первого дня, а не из текущего состояния: до начала смены
		// день ещё нулевой, и GetStatus вернул бы лимит для несуществующего дня.
		// Ядро при подтверждении сверяется именно с лимитом первой смены.
		ShiftStatusView status = simulation.GetStatus();
		_slots = _isStartingChoice
			? simulation.Config.GetStaffLimit(1)
			: CountFreeSlots(simulation, status);

		_title.Text = _isStartingChoice
			? Content.Label("ui_hiring_title_starting")
			: Content.Label("ui_hiring_title_day", "day", _day.ToString());

		// Брать некого — экран показывать незачем, сразу дальше.
		if (_candidates.Count == 0 || _slots <= 0)
		{
			CallDeferred(nameof(Finish));
			return;
		}

		BuildCards();
		RefreshCounter();
	}

	private static int CountFreeSlots(KonturSimulation simulation, ShiftStatusView status)
	{
		int living = 0;
		IReadOnlyList<EmployeeView> roster = simulation.GetRoster();
		for (int i = 0; i < roster.Count; i++)
		{
			if (roster[i].Status != EmployeeStatus.Dead)
			{
				living++;
			}
		}

		int free = status.StaffLimit - living;
		return free < 0 ? 0 : free;
	}

	// ------------------------------------------------------------------ узлы сцены

	private void BindUi()
	{
		_title = GetNode<Label>(TitlePath);
		_row = GetNode<Control>(CardRowPath);
		_counter = GetNode<Label>(CounterPath);

		_confirm = GetNode<Button>(ConfirmButtonPath);
		_confirm.Text = Content.Label("ui_hiring_confirm");
		_confirm.Pressed += OnConfirm;

		_title.Text = Content.Label("ui_hiring_title");
	}

	private void BuildCards()
	{
		foreach (Node child in _row.GetChildren())
		{
			_row.RemoveChild(child);
			child.QueueFree();
		}

		_cards.Clear();

		if (CandidateCardScene == null)
		{
			// Без шаблона рисовать нечего, а молчать нельзя: экран выглядел бы
			// пустым, и причину пришлось бы искать в сцене вслепую.
			GD.PushError("[НАЙМ] Не задана сцена карточки кандидата (CandidateCardScene).");
			return;
		}

		for (int i = 0; i < _candidates.Count; i++)
		{
			HireCandidateView candidate = _candidates[i];

			var card = CandidateCardScene.Instantiate<HireCandidateCard>();
			_row.AddChild(card);
			card.Setup(candidate);
			card.PickToggled += OnCardToggled;

			_cards[candidate.Id] = card;
		}
	}

	// ------------------------------------------------------------------ выбор

	private void OnCardToggled(HireCandidateCard card, bool pressed)
	{
		if (pressed)
		{
			// Лишний человек в бригаду не влезет. Молча снимать чужую галочку
			// нельзя: игрок не поймёт, куда делся его выбор.
			if (_picked.Count >= _slots)
			{
				card.SetPickedNoSignal(false);
				return;
			}

			_picked.Add(card.CandidateId);
		}
		else
		{
			_picked.Remove(card.CandidateId);
		}

		RefreshCounter();
	}

	private void RefreshCounter()
	{
		_counter.Text = _isStartingChoice
			? Content.Label("ui_hiring_picked_starting",
				"picked", _picked.Count.ToString(), "slots", _slots.ToString())
			: Content.Label("ui_hiring_picked",
				"picked", _picked.Count.ToString(), "slots", _slots.ToString());

		// При стартовом выборе бригада должна быть укомплектована полностью:
		// выйти на смену вдвоём вместо троих — не решение игрока, а недосмотр.
		_confirm.Disabled = _isStartingChoice && _picked.Count != _slots;
		_confirm.Text = _picked.Count == 0 && !_isStartingChoice
			? Content.Label("ui_hiring_skip")
			: Content.Label("ui_hiring_confirm");
	}

	// ------------------------------------------------------------------ подтверждение

	private void OnConfirm()
	{
		KonturSimulation simulation = GameFlow.Instance?.Simulation;
		if (simulation == null)
		{
			Finish();
			return;
		}

		var chosen = new List<string>(_picked);

		if (_isStartingChoice)
		{
			CommandResult result = simulation.ConfirmStartingRoster(chosen);
			if (!result.IsSuccess)
			{
				_counter.Text = result.Error;
				return;
			}
		}
		else
		{
			for (int i = 0; i < chosen.Count; i++)
			{
				CommandResult result = simulation.HireEmployee(chosen[i], _day);
				if (!result.IsSuccess)
				{
					// Один отказ не должен отменять весь набор: остальные уже наняты.
					GD.PushWarning($"[НАЙМ] {chosen[i]}: {result.Error}");
				}
			}
		}

		Finish();
	}

	private void Finish()
	{
		if (GameFlow.Instance == null)
		{
			return;
		}

		GameFlow.Instance.OnHiringFinished(_day);
	}
}
