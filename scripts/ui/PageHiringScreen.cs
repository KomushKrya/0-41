using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Model;

/// <summary>
/// Набор людей листами бумаги: по трое за раз, берёшь одного.
///
/// Отличие от прежнего экрана не в оформлении, а в цене решения. Раньше игрок
/// видел весь пул и спокойно набирал лучших — выбором это не было, была
/// сортировка. Здесь на каждое свободное место приходит своя тройка, и двое
/// непринятых уходят навсегда. Отказ становится решением, а не отсрочкой.
///
/// Заодно исчезает и путаница с галочками. На прежнем экране «Взять» было
/// переключателем: выбрав одного при единственном свободном месте, игрок не мог
/// передумать в пользу другого — сначала надо было снять галочку. Здесь нажатие
/// сразу нанимает: одно действие, одно последствие, отменять нечего.
///
/// Кнопки «Пропустить» нет намеренно. Свободные места закрываются до конца,
/// а экран уходит сам, когда мест не осталось или кончились кандидаты.
///
/// Вёрстка своя, отдельно от HiringScreen.tscn: тот экран ведут товарищи,
/// и переписывать его целиком значило бы поссориться на слиянии.
///
/// Всё держится на контейнерах, без единой заданной вручную координаты.
/// Первая версия расставляла листы по offset'ам и крутила их на пару градусов;
/// выглядело это красиво ровно до запуска — подписи разъезжались, а листы
/// налезали друг на друга.
/// </summary>
public partial class PageHiringScreen : Control
{
	/// <summary>Чистая бумага без печатной рамки: свою разметку рисуем сами.</summary>
	private const string PaperTexturePath = "res://assets/textures/Note_texture 3.jpg";

	private const string FontPath = "res://assets/fonts/Old-Soviet.otf";
	private const string FallbackPortraitPath = "res://assets/portraits/port_generic_1.png";

	private static readonly Color Ink = new(0.16f, 0.14f, 0.11f);
	private static readonly Color InkFaded = new(0.30f, 0.26f, 0.20f);
	private static readonly Color InkAccent = new(0.42f, 0.24f, 0.10f);
	private static readonly Color Paper = new(0.86f, 0.82f, 0.72f);

	[Export] public double DealSeconds { get; set; } = 0.35;
	[Export] public double DiscardSeconds { get; set; } = 0.4;

	/// <summary>Ширина листа. Высота набирается содержимым.</summary>
	[Export] public int PageWidth { get; set; } = 300;

	/// <summary>
	/// Высота под разбор перков. Шесть строк — потолок: трое кандидатов,
	/// у каждого со третьего уровня по два перка.
	/// </summary>
	[Export] public int LegendReservedHeight { get; set; } = 120;

	private readonly List<HireCandidateView> _pool = new();
	private readonly List<Control> _pages = new();

	private Label _title;
	private Label _counter;
	private Label _legend;
	private HBoxContainer _row;

	private int _day = 1;
	private int _slotsLeft;
	private int _hired;
	private bool _busy;

	public override void _Ready()
	{
		AnchorRight = 1.0f;
		AnchorBottom = 1.0f;

		// Сюда приходят прямо из кабинета, где мышь захвачена игроком.
		CursorMode.Show(this);

		if (GameFlow.Instance != null)
		{
			_day = GameFlow.Instance.HiringDay;
		}

		BuildChrome();
		LoadPool();
		DealNextDraw();
	}

	// ------------------------------------------------------------------ данные

	private void LoadPool()
	{
		KonturSimulation simulation = GameFlow.Instance?.Simulation;
		if (simulation == null)
		{
			_title.Text = Content.Label("ui_hiring_no_core");
			_slotsLeft = 0;
			return;
		}

		_pool.Clear();
		_pool.AddRange(simulation.GetHireCandidates(_day));

		int living = 0;
		IReadOnlyList<EmployeeView> roster = simulation.GetRoster();
		for (int i = 0; i < roster.Count; i++)
		{
			if (roster[i].Status != EmployeeStatus.Dead)
			{
				living++;
			}
		}

		_slotsLeft = Mathf.Max(0, simulation.GetStatus().StaffLimit - living);
		_title.Text = Content.Label("ui_hiring_title_day", "day", _day.ToString());
	}

	private int DrawSize
	{
		get
		{
			KonturSimulation simulation = GameFlow.Instance?.Simulation;
			return simulation == null ? 3 : Mathf.Max(1, simulation.Content.Generator.CandidatesPerDraw);
		}
	}

	// ------------------------------------------------------------------ раздача

	private void DealNextDraw()
	{
		if (_slotsLeft <= 0 || _pool.Count == 0)
		{
			Finish();
			return;
		}

		int count = Mathf.Min(DrawSize, _pool.Count);
		for (int i = 0; i < count; i++)
		{
			Control page = BuildPage(_pool[i]);
			_row.AddChild(page);
			_pages.Add(page);
			AnimateDeal(page, i);
		}

		RefreshCounter();
	}

	/// <summary>
	/// Игрок взял человека. Нанимаем сразу, без подтверждения: тройка и так
	/// невелика, а лишний шаг «выбрал — подтвердил» здесь только создавал
	/// состояние, в котором игрок застревал.
	/// </summary>
	private void OnPick(HireCandidateView candidate)
	{
		if (_busy)
		{
			return;
		}

		KonturSimulation simulation = GameFlow.Instance?.Simulation;
		if (simulation == null)
		{
			Finish();
			return;
		}

		CommandResult result = simulation.HireEmployee(candidate.Id, _day);
		if (!result.IsSuccess)
		{
			_counter.Text = result.Error;
			return;
		}

		_busy = true;
		_slotsLeft--;
		_hired++;

		// Вся выложенная тройка уходит: взятый — в штат, двое других — совсем.
		// Держать их в пуле нельзя, иначе отказ ничего не стоит: те же лица
		// вернутся в следующей раздаче.
		_pool.RemoveRange(0, Mathf.Min(DrawSize, _pool.Count));

		DiscardPages(candidate.Id);
	}

	/// <summary>
	/// Листы уходят со стола.
	///
	/// Анимируем прозрачность и масштаб, но не позицию: позицией детей ведает
	/// HBoxContainer, и на ближайшей пересортировке он вернул бы лист обратно
	/// посреди движения. Масштаб контейнер не трогает — им двигать можно.
	/// </summary>
	private void DiscardPages(string pickedId)
	{
		Tween tween = CreateTween();
		tween.SetParallel(true);

		for (int i = 0; i < _pages.Count; i++)
		{
			Control page = _pages[i];
			bool picked = page.HasMeta("candidate") && (string)page.GetMeta("candidate") == pickedId;

			// Взятый вырастает — его забрали в папку; отвергнутые съёживаются.
			// Одинаковый уход всех троих прочитался бы как «ничего не выбрал».
			page.PivotOffset = page.Size * 0.5f;
			Vector2 target = picked ? new Vector2(1.08f, 1.08f) : new Vector2(0.88f, 0.88f);

			tween.TweenProperty(page, "scale", target, DiscardSeconds).SetEase(Tween.EaseType.Out);
			tween.TweenProperty(page, "modulate:a", 0.0f, DiscardSeconds);
		}

		tween.Chain().TweenCallback(Callable.From(AfterDiscard));
	}

	private void AfterDiscard()
	{
		for (int i = 0; i < _pages.Count; i++)
		{
			_pages[i].QueueFree();
		}

		_pages.Clear();
		_busy = false;
		DealNextDraw();
	}

	private void AnimateDeal(Control page, int index)
	{
		// Листы проявляются по очереди: глаз успевает заметить, что их три,
		// и не воспринимает появление как мигание экрана.
		page.Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f);
		page.Scale = new Vector2(0.96f, 0.96f);

		Tween tween = CreateTween();
		tween.TweenInterval(index * 0.08);
		tween.SetParallel(true);
		tween.TweenProperty(page, "modulate:a", 1.0f, DealSeconds);
		tween.TweenProperty(page, "scale", Vector2.One, DealSeconds).SetEase(Tween.EaseType.Out);
	}

	// ------------------------------------------------------------------ вёрстка

	private void BuildChrome()
	{
		AddChild(new ColorRect
		{
			Color = new Color(0.09f, 0.08f, 0.07f),
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			MouseFilter = MouseFilterEnum.Ignore
		});

		var column = new VBoxContainer
		{
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			OffsetLeft = 32.0f,
			OffsetTop = 20.0f,
			OffsetRight = -32.0f,
			OffsetBottom = -20.0f
		};
		column.AddThemeConstantOverride("separation", 12);
		AddChild(column);

		_title = MakeLabel(Content.Label("ui_hiring_title"), 30, Paper);
		_title.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(_title);

		_counter = MakeLabel(string.Empty, 17, new Color(0.72f, 0.66f, 0.54f));
		_counter.HorizontalAlignment = HorizontalAlignment.Center;
		column.AddChild(_counter);

		// Листы по центру и не растягиваются: ряд занимает ровно свою ширину.
		var centre = new CenterContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		column.AddChild(centre);

		_row = new HBoxContainer();
		_row.AddThemeConstantOverride("separation", 22);
		centre.AddChild(_row);

		_legend = MakeLabel(string.Empty, 14, new Color(0.66f, 0.61f, 0.5f));
		_legend.HorizontalAlignment = HorizontalAlignment.Center;
		_legend.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		_legend.VerticalAlignment = VerticalAlignment.Top;

		// Место под разбор резервируем сразу под худший случай: трое по два перка
		// это шесть строк. Иначе ряд листов подпрыгивал бы при каждой раздаче,
		// подстраиваясь под то, сколько строк выпало на этот раз.
		_legend.CustomMinimumSize = new Vector2(0.0f, LegendReservedHeight);
		column.AddChild(_legend);
	}

	/// <summary>Один лист с анкетой кандидата.</summary>
	private Control BuildPage(HireCandidateView candidate)
	{
		var page = new PanelContainer
		{
			CustomMinimumSize = new Vector2(PageWidth, 0.0f),

			// Fill, а не ShrinkCenter: с третьего уровня у оперативника два перка,
			// и лист с двумя строками способностей выше соседних. При сжатии по
			// содержимому ряд выходил ступеньками. Теперь все листы тянутся до
			// высоты самого длинного — бумага лежит ровно.
			SizeFlagsVertical = SizeFlags.Fill
		};
		page.AddThemeStyleboxOverride("panel", MakePaperStyle());
		page.SetMeta("candidate", candidate.Id);

		var body = new VBoxContainer();
		body.AddThemeConstantOverride("separation", 6);
		page.AddChild(body);

		Label name = MakeLabel(candidate.Name, 20, Ink);
		name.HorizontalAlignment = HorizontalAlignment.Center;
		name.ClipText = true;
		name.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
		body.AddChild(name);

		Label rank = MakeLabel(
			Content.Label("ui_hiring_candidate_rank",
				"rank", candidate.RankTitle,
				"level", candidate.Level.ToString()),
			14,
			InkFaded);
		rank.HorizontalAlignment = HorizontalAlignment.Center;
		rank.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		body.AddChild(rank);

		body.AddChild(new HSeparator());

		// Портрет по центру и фиксированной высоты: иначе листы в тройке
		// получаются разной длины и ряд выглядит рваным.
		var portraitBox = new CenterContainer();
		portraitBox.AddChild(new TextureRect
		{
			Texture = LoadPortrait(candidate.PortraitId),
			CustomMinimumSize = new Vector2(120.0f, 150.0f),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered
		});
		body.AddChild(portraitBox);

		body.AddChild(new HSeparator());

		// Сетка в две колонки: числа выстраиваются в столбик сами, без распорок
		// и подгонки ширин. Ровный столбец чисел — половина читаемости анкеты.
		var stats = new GridContainer { Columns = 2 };
		stats.AddThemeConstantOverride("h_separation", 10);
		stats.AddThemeConstantOverride("v_separation", 2);
		body.AddChild(stats);

		for (int i = 0; i < StatKinds.All.Length; i++)
		{
			StatKind kind = StatKinds.All[i];

			Label statName = MakeLabel(Content.NameOf(kind.ToString().ToLowerInvariant()), 15, Ink);
			statName.SizeFlagsHorizontal = SizeFlags.ExpandFill;
			statName.ClipText = true;
			statName.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
			stats.AddChild(statName);

			Label statValue = MakeLabel(candidate.Stats[kind].ToString(), 15, Ink);
			statValue.HorizontalAlignment = HorizontalAlignment.Right;
			stats.AddChild(statValue);
		}

		if (candidate.AbilityIds.Count > 0)
		{
			body.AddChild(new HSeparator());

			for (int i = 0; i < candidate.AbilityIds.Count; i++)
			{
				Label perk = MakeLabel("· " + ResolveName(candidate.AbilityIds[i]), 14, InkAccent);
				perk.AutowrapMode = TextServer.AutowrapMode.WordSmart;
				body.AddChild(perk);
			}
		}

		var take = new Button
		{
			Text = Content.Label("ui_hiring_take"),
			CustomMinimumSize = new Vector2(0.0f, 34.0f)
		};
		take.AddThemeFontOverride("font", GD.Load<Font>(FontPath));
		take.AddThemeColorOverride("font_color", Ink);
		take.AddThemeColorOverride("font_hover_color", InkAccent);
		take.Pressed += () => OnPick(candidate);
		body.AddChild(take);

		return page;
	}

	/// <summary>
	/// Бумага листа.
	///
	/// StyleBoxTexture, а не TextureRect за спиной: стиль сам держит поля,
	/// и содержимое никогда не выедет за край листа, каким бы длинным оно
	/// ни оказалось. Именно на этом разъезжалась первая версия.
	/// </summary>
	private static StyleBox MakePaperStyle()
	{
		Texture2D paper = GD.Load<Texture2D>(PaperTexturePath);
		if (paper == null)
		{
			var flat = new StyleBoxFlat { BgColor = Paper };
			flat.SetContentMarginAll(16.0f);
			return flat;
		}

		var style = new StyleBoxTexture { Texture = paper };
		style.SetContentMarginAll(16.0f);
		return style;
	}

	private void RefreshCounter()
	{
		_counter.Text = Content.Label("ui_hiring_picked",
			"picked", _hired.ToString(),
			"slots", _slotsLeft.ToString());

		// Разбор перков текущей тройки — под листами, по строке на перк.
		var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
		var lines = new List<string>();
		int count = Mathf.Min(DrawSize, _pool.Count);

		for (int i = 0; i < count; i++)
		{
			IReadOnlyList<string> abilities = _pool[i].AbilityIds;
			for (int a = 0; a < abilities.Count; a++)
			{
				if (!seen.Add(abilities[a]))
				{
					continue;
				}

				string effect = AbilityText.Describe(this, abilities[a]);
				lines.Add(string.IsNullOrEmpty(effect)
					? "· " + ResolveName(abilities[a])
					: "· " + ResolveName(abilities[a]) + " — " + effect);
			}
		}

		_legend.Text = string.Join("\n", lines);
	}

	private Label MakeLabel(string text, int size, Color colour)
	{
		var label = new Label { Text = text };
		label.AddThemeFontOverride("font", GD.Load<Font>(FontPath));
		label.AddThemeFontSizeOverride("font_size", size);
		label.AddThemeColorOverride("font_color", colour);
		return label;
	}

	private static Texture2D LoadPortrait(string portraitId)
	{
		if (!string.IsNullOrWhiteSpace(portraitId))
		{
			Texture2D portrait = GD.Load<Texture2D>($"res://assets/portraits/{portraitId}.png");
			if (portrait != null)
			{
				return portrait;
			}
		}

		return GD.Load<Texture2D>(FallbackPortraitPath);
	}

	private static string ResolveName(string entryId)
	{
		if (Content.Instance == null)
		{
			return entryId;
		}

		ContentEntry entry;
		return Content.Instance.TryGetEntry(entryId, out entry) && !string.IsNullOrEmpty(entry.Name)
			? entry.Name
			: entryId;
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
