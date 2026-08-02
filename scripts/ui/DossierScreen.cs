using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Досье сотрудников: разворот с фото, характеристиками и распределением очков.
///
/// Экран живёт внутри SubViewport на модели папки — поэтому это обычный Control,
/// который сам себя верстает, а не сцена с расставленными вручную узлами.
/// Оформление здесь рабочее: бумага, шрифты и рамки навешиваются поверх.
///
/// Главное, ради чего он существует помимо просмотра: потратить очки навыков.
/// Плюсик у характеристики появляется, только когда очки есть, — кнопка, которая
/// ничего не делает, читается как поломка.
/// </summary>
public partial class DossierScreen : Control
{
	/// <summary>Сколько людей показывать одновременно: разворот — две страницы.</summary>
	private const int PagesPerSpread = 2;

	private HBoxContainer _spread;
	private Label _pager;
	private Button _prev;
	private Button _next;

	private readonly List<EmployeeView> _roster = new();
	private readonly List<IDisposable> _subscriptions = new();
	private int _firstOnSpread;

	public override void _Ready()
	{
		AnchorRight = 1.0f;
		AnchorBottom = 1.0f;

		BuildUi();
		Subscribe();
		Refresh();
	}

	public override void _ExitTree()
	{
		// Подписка живёт дольше узла и утащила бы за собой ссылку на удалённое досье.
		for (int i = 0; i < _subscriptions.Count; i++)
		{
			_subscriptions[i]?.Dispose();
		}

		_subscriptions.Clear();
	}

	/// <summary>
	/// Досье лежит на столе открытым и должно быть верным всегда, а не только
	/// в момент, когда игрок к нему подошёл. Перечитываем на всё, что меняет штат.
	/// </summary>
	private void Subscribe()
	{
		KonturRuntime kontur = KonturRuntime.Get(this);
		if (kontur == null || !kontur.IsReady)
		{
			return;
		}

		IEventBus bus = kontur.Simulation.Events;
		_subscriptions.Add(bus.Subscribe<EmployeeLeveledUp>(_ => Refresh()));
		_subscriptions.Add(bus.Subscribe<EmployeeStatsChanged>(_ => Refresh()));
		_subscriptions.Add(bus.Subscribe<EmployeeHired>(_ => Refresh()));
		_subscriptions.Add(bus.Subscribe<EmployeeInjured>(_ => Refresh()));
		_subscriptions.Add(bus.Subscribe<EmployeeKilled>(_ => Refresh()));
		_subscriptions.Add(bus.Subscribe<GameLoaded>(_ => Refresh()));
	}

	// ------------------------------------------------------------------ вёрстка

	private void BuildUi()
	{
		var background = new ColorRect
		{
			Color = new Color(0.12f, 0.095f, 0.055f),
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
		column.AddThemeConstantOverride("separation", 12);
		AddChild(column);

		var header = new Label { Text = "ДОСЬЕ СОТРУДНИКОВ" };
		header.AddThemeFontSizeOverride("font_size", 30);
		column.AddChild(header);

		_spread = new HBoxContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		_spread.AddThemeConstantOverride("separation", 24);
		column.AddChild(_spread);

		var footer = new HBoxContainer();
		footer.AddThemeConstantOverride("separation", 12);
		column.AddChild(footer);

		_prev = new Button { Text = "◀", CustomMinimumSize = new Vector2(56.0f, 36.0f) };
		_prev.Pressed += () => Turn(-PagesPerSpread);
		footer.AddChild(_prev);

		_pager = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		footer.AddChild(_pager);

		_next = new Button { Text = "▶", CustomMinimumSize = new Vector2(56.0f, 36.0f) };
		_next.Pressed += () => Turn(PagesPerSpread);
		footer.AddChild(_next);
	}

	// ------------------------------------------------------------------ данные

	/// <summary>
	/// Перечитывает штат и рисует разворот заново.
	///
	/// Полная перерисовка, а не точечное обновление подписей: страниц две, узлов
	/// на них десятки, и держать ссылки на каждый ярлык ради экономии нескольких
	/// миллисекунд — верный способ забыть обновить один из них.
	/// </summary>
	public void Refresh()
	{
		_roster.Clear();

		KonturRuntime kontur = KonturRuntime.Get(this);
		if (kontur != null && kontur.IsReady)
		{
			_roster.AddRange(kontur.Simulation.GetRoster());
		}

		if (_firstOnSpread >= _roster.Count)
		{
			_firstOnSpread = 0;
		}

		BuildSpread();
	}

	private void Turn(int by)
	{
		int next = _firstOnSpread + by;
		if (next < 0 || next >= _roster.Count)
		{
			return;
		}

		_firstOnSpread = next;
		BuildSpread();
	}

	private void BuildSpread()
	{
		foreach (Node child in _spread.GetChildren())
		{
			child.QueueFree();
		}

		if (_roster.Count == 0)
		{
			_spread.AddChild(new Label { Text = "В штате никого." });
			_pager.Text = string.Empty;
			_prev.Disabled = true;
			_next.Disabled = true;
			return;
		}

		for (int i = 0; i < PagesPerSpread; i++)
		{
			int index = _firstOnSpread + i;
			if (index >= _roster.Count)
			{
				// Пустая половина разворота: без распорки вторая страница
				// растянулась бы на всю ширину и разворот перестал бы быть разворотом.
				_spread.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });
				continue;
			}

			_spread.AddChild(BuildPage(_roster[index]));
		}

		int last = System.Math.Min(_firstOnSpread + PagesPerSpread, _roster.Count);
		_pager.Text = $"{_firstOnSpread + 1}–{last} из {_roster.Count}";
		_prev.Disabled = _firstOnSpread == 0;
		_next.Disabled = last >= _roster.Count;
	}

	private Control BuildPage(EmployeeView employee)
	{
		var page = new PanelContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };

		var column = new VBoxContainer();
		column.AddThemeConstantOverride("separation", 8);
		page.AddChild(column);

		BuildTitle(column, employee);
		BuildPortrait(column, employee);
		BuildStats(column, employee);
		BuildAbilities(column, employee);
		BuildBio(column, employee);

		column.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

		var experience = new Label
		{
			Text = $"ОПЫТ СОТРУДНИКА: {employee.Experience}/{employee.ExperienceToNextLevel}",
			HorizontalAlignment = HorizontalAlignment.Right
		};
		column.AddChild(experience);

		BuildSkillPoints(column, employee);
		return page;
	}

	private void BuildTitle(Container column, EmployeeView employee)
	{
		var title = new Label
		{
			Text = employee.Name.ToUpperInvariant(),
			HorizontalAlignment = HorizontalAlignment.Center
		};
		title.AddThemeFontSizeOverride("font_size", 22);
		column.AddChild(title);

		var rank = new Label
		{
			Text = $"УРОВЕНЬ: {employee.Level}   {employee.RankTitle}",
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(1.0f, 1.0f, 1.0f, 0.7f)
		};
		column.AddChild(rank);

		// Значок повышения. Висит у заголовка, а не только внизу страницы: игрок,
		// пролистывающий разворот, должен видеть, кому он должен внимание,
		// не дочитывая анкету до конца.
		if (employee.UnspentSkillPoints > 0)
		{
			var badge = new Label
			{
				Text = $"▲ ПОВЫШЕНИЕ: нераспределённых очков {employee.UnspentSkillPoints}",
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color(1.0f, 0.78f, 0.35f)
			};
			column.AddChild(badge);
		}

		string state = employee.Status == EmployeeStatus.Dead
			? "ПОГИБ"
			: employee.IsInjured ? "ТРАВМИРОВАН" : string.Empty;

		if (state.Length > 0)
		{
			column.AddChild(new Label
			{
				Text = state,
				HorizontalAlignment = HorizontalAlignment.Center,
				Modulate = new Color(1.0f, 0.45f, 0.4f)
			});
		}
	}

	/// <summary>
	/// Фото. Пока художник не нарисовал лица, в рамке стоит id портрета — так
	/// видно и то, что портреты действительно не повторяются, и чего не хватает.
	/// </summary>
	private void BuildPortrait(Container column, EmployeeView employee)
	{
		var frame = new PanelContainer
		{
			CustomMinimumSize = new Vector2(0.0f, 96.0f),
			SizeFlagsHorizontal = SizeFlags.ShrinkCenter
		};

		var texture = GD.Load<Texture2D>($"res://assets/portraits/{employee.PortraitId}.png");
		if (texture != null)
		{
			frame.AddChild(new TextureRect
			{
				Texture = texture,
				ExpandMode = TextureRect.ExpandModeEnum.FitWidthProportional,
				StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
				CustomMinimumSize = new Vector2(96.0f, 96.0f)
			});
		}
		else
		{
			frame.AddChild(new Label
			{
				Text = employee.PortraitId.Length > 0 ? employee.PortraitId : "без фото",
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center,
				CustomMinimumSize = new Vector2(128.0f, 96.0f),
				Modulate = new Color(1.0f, 1.0f, 1.0f, 0.45f)
			});
		}

		column.AddChild(frame);
	}

	private void BuildStats(Container column, EmployeeView employee)
	{
		column.AddChild(new Label { Text = "ХАРАКТЕРИСТИКИ" });

		var grid = new GridContainer { Columns = 3 };
		grid.AddThemeConstantOverride("h_separation", 12);
		column.AddChild(grid);

		bool canSpend = employee.UnspentSkillPoints > 0
			&& employee.Status != EmployeeStatus.Dead;

		for (int i = 0; i < StatKinds.All.Length; i++)
		{
			StatKind kind = StatKinds.All[i];

			grid.AddChild(new Label
			{
				Text = StatKinds.GetDisplayName(kind) + ':',
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			});

			grid.AddChild(new Label
			{
				Text = employee.Stats[kind].ToString(),
				HorizontalAlignment = HorizontalAlignment.Right,
				CustomMinimumSize = new Vector2(40.0f, 0.0f)
			});

			// Плюсик появляется только когда очки есть. Постоянно висящая серая
			// кнопка приучает игрока её не замечать — а замечать надо как раз тогда,
			// когда она работает.
			if (!canSpend)
			{
				grid.AddChild(new Control { CustomMinimumSize = new Vector2(32.0f, 0.0f) });
				continue;
			}

			var plus = new Button
			{
				Text = "+",
				CustomMinimumSize = new Vector2(32.0f, 28.0f),
				TooltipText = $"Вложить очко в «{StatKinds.GetDisplayName(kind)}»"
			};

			string employeeId = employee.Id;
			StatKind target = kind;
			plus.Pressed += () => Spend(employeeId, target);
			grid.AddChild(plus);
		}
	}

	private void BuildAbilities(Container column, EmployeeView employee)
	{
		if (employee.AbilityIds.Count == 0)
		{
			return;
		}

		column.AddChild(new Label { Text = "ОСОБЫЕ НАВЫКИ" });

		for (int i = 0; i < employee.AbilityIds.Count; i++)
		{
			// Название перка живёт в текстовом движке: ядро носит только id.
			column.AddChild(new Label { Text = "· " + ResolveName(employee.AbilityIds[i]) });
		}
	}

	private void BuildBio(Container column, EmployeeView employee)
	{
		if (employee.Age <= 0 && employee.BioIds.Count == 0)
		{
			return;
		}

		column.AddChild(new Label { Text = "ДРУГИЕ ДАННЫЕ" });

		var parts = new List<string>();
		if (employee.Age > 0)
		{
			parts.Add(FormatAge(employee.Age));
		}

		for (int i = 0; i < employee.BioIds.Count; i++)
		{
			string line = ResolveText(employee.BioIds[i]);
			if (line.Length > 0)
			{
				parts.Add(line);
			}
		}

		column.AddChild(new Label
		{
			Text = string.Join(" ", parts),
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
			CustomMinimumSize = new Vector2(0.0f, 72.0f)
		});
	}

	private void BuildSkillPoints(Container column, EmployeeView employee)
	{
		if (employee.UnspentSkillPoints <= 0)
		{
			return;
		}

		var label = new Label
		{
			Text = $"ДОСТУПНЫ ОЧКИ ХАРАКТЕРИСТИК: {employee.UnspentSkillPoints}",
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(1.0f, 0.78f, 0.35f)
		};
		label.AddThemeFontSizeOverride("font_size", 18);
		column.AddChild(label);
	}

	// ------------------------------------------------------------------ действия

	private void Spend(string employeeId, StatKind stat)
	{
		KonturRuntime kontur = KonturRuntime.Get(this);
		if (kontur == null || !kontur.IsReady)
		{
			return;
		}

		CommandResult result = kontur.Simulation.SpendSkillPoint(employeeId, stat);
		if (!result.IsSuccess)
		{
			GD.PushWarning("Досье: " + result.Error);
			return;
		}

		// Очко потрачено — изменились и характеристика, и остаток, и сам факт
		// наличия плюсиков. Перечитываем штат целиком.
		Refresh();
	}

	/// <summary>
	/// «37 лет», «22 года», «41 год» — склонение по последним цифрам.
	/// Текстовый движок такого не умеет, и это правильно: числа приходят из ядра,
	/// а согласование с ними — работа интерфейса.
	/// </summary>
	private static string FormatAge(int years)
	{
		int tail = years % 100;
		if (tail >= 11 && tail <= 14)
		{
			return $"{years} лет.";
		}

		switch (years % 10)
		{
			case 1: return $"{years} год.";
			case 2:
			case 3:
			case 4: return $"{years} года.";
			default: return $"{years} лет.";
		}
	}

	private static string ResolveName(string entryId)
	{
		if (Content.Instance == null)
		{
			return entryId;
		}

		ContentEntry entry = Content.Instance.GetEntry(entryId);
		return entry != null && entry.Name.Length > 0 ? entry.Name : entryId;
	}

	private static string ResolveText(string entryId)
	{
		if (Content.Instance == null)
		{
			return string.Empty;
		}

		ContentEntry entry = Content.Instance.GetEntry(entryId);
		if (entry == null || entry.Chunks.Count == 0)
		{
			return string.Empty;
		}

		return entry.Chunks[0].Text;
	}
}
