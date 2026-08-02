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
/// Художественного оформления нет: это рабочий каркас с настоящими данными.
/// </summary>
public partial class HiringScreen : Control
{
	private readonly List<HireCandidateView> _candidates = new();
	private readonly HashSet<string> _picked = new();
	private readonly Dictionary<string, Panel> _cards = new();

	private Label _title;
	private Label _counter;
	private Button _confirm;
	private HBoxContainer _row;

	private bool _isStartingChoice;
	private int _day = 1;
	private int _slots;

	public override void _Ready()
	{
		AnchorRight = 1.0f;
		AnchorBottom = 1.0f;

		if (GameFlow.Instance != null)
		{
			_isStartingChoice = GameFlow.Instance.HiringIsStartingChoice;
			_day = GameFlow.Instance.HiringDay;
		}

		BuildUi();
		LoadCandidates();
	}

	// ------------------------------------------------------------------ данные

	private void LoadCandidates()
	{
		KonturSimulation simulation = GameFlow.Instance?.Simulation;
		if (simulation == null)
		{
			_title.Text = "Ядро недоступно";
			return;
		}

		_candidates.Clear();
		_candidates.AddRange(_isStartingChoice
			? simulation.GetStartingChoice()
			: simulation.GetHireCandidates(_day));

		// Сколько человек можно взять. При стартовом выборе это весь штат,
		// при доборе — только свободные места.
		ShiftStatusView status = simulation.GetStatus();
		_slots = _isStartingChoice
			? status.StaffLimit
			: CountFreeSlots(simulation, status);

		_title.Text = _isStartingChoice
			? "Соберите бригаду"
			: $"Набор на смену {_day}";

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

	// ------------------------------------------------------------------ вёрстка

	private void BuildUi()
	{
		var background = new ColorRect
		{
			Color = new Color(0.06f, 0.07f, 0.09f),
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f
		};
		AddChild(background);

		var column = new VBoxContainer
		{
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			OffsetLeft = 32.0f,
			OffsetTop = 24.0f,
			OffsetRight = -32.0f,
			OffsetBottom = -24.0f
		};
		column.AddThemeConstantOverride("separation", 16);
		AddChild(column);

		_title = new Label { Text = "Набор" };
		_title.AddThemeFontSizeOverride("font_size", 28);
		column.AddChild(_title);

		// Карточек бывает больше трёх: список найма растёт вместе с дырой в штате,
		// и на поздних сменах их может быть вдвое больше мест. Без прокрутки
		// HBoxContainer сжал бы их до нечитаемой ширины.
		var scroll = new ScrollContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill,
			HorizontalScrollMode = ScrollContainer.ScrollMode.Auto,
			VerticalScrollMode = ScrollContainer.ScrollMode.Disabled
		};
		column.AddChild(scroll);

		_row = new HBoxContainer
		{
			SizeFlagsVertical = SizeFlags.ExpandFill
		};
		_row.AddThemeConstantOverride("separation", 12);
		scroll.AddChild(_row);

		var footer = new HBoxContainer();
		footer.AddThemeConstantOverride("separation", 12);
		column.AddChild(footer);

		_counter = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		footer.AddChild(_counter);

		_confirm = new Button
		{
			Text = "Готово",
			CustomMinimumSize = new Vector2(160.0f, 40.0f)
		};
		_confirm.Pressed += OnConfirm;
		footer.AddChild(_confirm);
	}

	private void BuildCards()
	{
		foreach (Node child in _row.GetChildren())
		{
			child.QueueFree();
		}

		_cards.Clear();

		for (int i = 0; i < _candidates.Count; i++)
		{
			HireCandidateView candidate = _candidates[i];
			Panel card = BuildCard(candidate);
			_row.AddChild(card);
			_cards[candidate.Id] = card;
		}
	}

	private Panel BuildCard(HireCandidateView candidate)
	{
		var card = new Panel
		{
			CustomMinimumSize = new Vector2(240.0f, 0.0f),
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			SizeFlagsVertical = SizeFlags.ExpandFill
		};

		var column = new VBoxContainer
		{
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			OffsetLeft = 12.0f,
			OffsetTop = 12.0f,
			OffsetRight = -12.0f,
			OffsetBottom = -12.0f
		};
		column.AddThemeConstantOverride("separation", 8);
		card.AddChild(column);

		var name = new Label { Text = candidate.Name };
		name.AddThemeFontSizeOverride("font_size", 20);
		column.AddChild(name);

		column.AddChild(new Label
		{
			Text = $"{candidate.RankTitle}, уровень {candidate.Level}",
			Modulate = new Color(1.0f, 1.0f, 1.0f, 0.6f)
		});

		column.AddChild(new HSeparator());

		// Характеристики. Название берётся у ядра, а не пишется здесь строкой:
		// иначе переименование характеристики пришлось бы ловить по всему проекту.
		for (int i = 0; i < StatKinds.All.Length; i++)
		{
			StatKind kind = StatKinds.All[i];

			var line = new HBoxContainer();
			line.AddChild(new Label
			{
				Text = StatKinds.GetDisplayName(kind),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			});
			line.AddChild(new Label { Text = candidate.Stats[kind].ToString() });
			column.AddChild(line);
		}

		column.AddChild(new HSeparator());
		column.AddChild(new Label
		{
			Text = "ОСОБЕННОСТИ",
			Modulate = new Color(1.0f, 1.0f, 1.0f, 0.6f)
		});

		if (candidate.AbilityIds.Count == 0)
		{
			column.AddChild(new Label
			{
				Text = "нет",
				Modulate = new Color(1.0f, 1.0f, 1.0f, 0.4f)
			});
		}
		else
		{
			for (int i = 0; i < candidate.AbilityIds.Count; i++)
			{
				column.AddChild(BuildPerkLabel(candidate.AbilityIds[i]));
			}
		}

		column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

		var pick = new Button
		{
			Text = "Взять",
			ToggleMode = true,
			CustomMinimumSize = new Vector2(0.0f, 36.0f)
		};
		pick.Toggled += pressed => OnCardToggled(candidate.Id, pressed, pick);
		column.AddChild(pick);

		return card;
	}

	/// <summary>
	/// Название перка и пояснение берутся из текстового движка по тому же id,
	/// что лежит в данных способности. Нет записи — показываем сам id: так
	/// пропущенный текст виден сразу, а не выглядит пустым местом.
	/// </summary>
	private Label BuildPerkLabel(string abilityId)
	{
		string caption = abilityId;
		string tooltip = string.Empty;

		Content content = Content.Instance;
		if (content != null && content.TryGetEntry(abilityId, out ContentEntry entry))
		{
			if (!string.IsNullOrEmpty(entry.Name))
			{
				caption = entry.Name;
			}

			var builder = new System.Text.StringBuilder();
			for (int i = 0; i < entry.Chunks.Count; i++)
			{
				if (i > 0)
				{
					builder.Append('\n');
				}

				builder.Append(entry.Chunks[i].Text);
			}

			tooltip = builder.ToString();
		}

		return new Label
		{
			Text = "· " + caption,
			TooltipText = tooltip,
			MouseFilter = MouseFilterEnum.Stop,
			Modulate = new Color(0.62f, 0.85f, 0.62f)
		};
	}

	// ------------------------------------------------------------------ выбор

	private void OnCardToggled(string candidateId, bool pressed, Button button)
	{
		if (pressed)
		{
			// Лишний человек в бригаду не влезет. Молча снимать чужую галочку
			// нельзя: игрок не поймёт, куда делся его выбор.
			if (_picked.Count >= _slots)
			{
				button.SetPressedNoSignal(false);
				return;
			}

			_picked.Add(candidateId);
		}
		else
		{
			_picked.Remove(candidateId);
		}

		RefreshCounter();
	}

	private void RefreshCounter()
	{
		_counter.Text = _isStartingChoice
			? $"Выбрано {_picked.Count} из {_slots}"
			: $"Взято {_picked.Count}, свободных мест {_slots}";

		// При стартовом выборе бригада должна быть укомплектована полностью:
		// выйти на смену вдвоём вместо троих — не решение игрока, а недосмотр.
		_confirm.Disabled = _isStartingChoice && _picked.Count != _slots;
		_confirm.Text = _picked.Count == 0 && !_isStartingChoice ? "Пропустить" : "Готово";
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
