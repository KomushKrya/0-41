using Godot;

/// <summary>
/// Вступительный ролик: первое, что видит игрок при запуске. По окончании — главное меню.
///
/// Пропуск сделан удержанием, а не одиночным нажатием: случайный тычок по клавише
/// не должен смахивать интро, которое человек, возможно, видит впервые. Подсказка со
/// шкалой появляется только когда игрок реально жмёт кнопку — до этого экран чистый.
/// </summary>
public partial class IntroPlayer : Control
{
	/// <summary>Ролики лежат здесь же, где и остальные катсцены.</summary>
	[Export] public string VideoPath { get; set; } = "res://assets/video/cutscenes/intro.ogv";

	/// <summary>Куда уходить, если игру начать не удалось: ядро не поднялось.</summary>
	[Export] public string FallbackScene { get; set; } = "res://scenes/ui/menu/MainMenu.tscn";

	/// <summary>Сколько держать клавишу, чтобы пропустить.</summary>
	[Export] public float HoldToSkipSeconds { get; set; } = 3.0f;

	/// <summary>За сколько подсказка проявляется и гаснет.</summary>
	[Export] public float HintFadeSeconds { get; set; } = 0.35f;

	private VideoStreamPlayer _video;
	private Control _hint;
	private Label _hintLabel;
	private ProgressBar _hintBar;

	private double _heldSeconds;
	private float _hintAlpha;
	private bool _leaving;

	public override void _Ready()
	{
		AnchorRight = 1.0f;
		AnchorBottom = 1.0f;
		ProcessMode = ProcessModeEnum.Always;

		CursorMode.Hide(this);
		BuildUi();

		if (!TryPlay())
		{
			// Ролика нет или он не читается — молча уходим в меню, а не показываем чёрный экран.
			GoNext();
		}
	}

	/// <summary>
	/// Пока идёт ролик, Escape — это «пропустить», а не «меню паузы».
	/// Помечаем ввод обработанным здесь: PauseMenu слушает _UnhandledInput,
	/// который идёт после всех _Input, поэтому до него событие не дойдёт.
	/// </summary>
	public override void _Input(InputEvent @event)
	{
		if (_leaving)
		{
			return;
		}

		if (@event.IsActionPressed("ui_cancel") || @event.IsActionPressed("ui_accept"))
		{
			GetViewport().SetInputAsHandled();
		}
	}

	public override void _Process(double delta)
	{
		if (_leaving)
		{
			return;
		}

		bool held = IsSkipKeyHeld();

		if (held)
		{
			_heldSeconds += delta;
			_hintBar.Value = Mathf.Clamp(_heldSeconds / HoldToSkipSeconds, 0.0, 1.0) * 100.0;

			if (_heldSeconds >= HoldToSkipSeconds)
			{
				GoNext();
				return;
			}
		}
		else
		{
			// Отпустили — прогресс сбрасывается, но подсказка не исчезает рывком.
			_heldSeconds = 0.0;
			_hintBar.Value = 0.0;
		}

		FadeHint(held, delta);
	}

	/// <summary>Подсказка проявляется, пока клавишу держат, и плавно гаснет после.</summary>
	private void FadeHint(bool visible, double delta)
	{
		float step = HintFadeSeconds > 0.0f ? (float)(delta / HintFadeSeconds) : 1.0f;
		_hintAlpha = Mathf.Clamp(_hintAlpha + (visible ? step : -step), 0.0f, 1.0f);

		_hint.Visible = _hintAlpha > 0.001f;
		_hint.Modulate = new Color(1.0f, 1.0f, 1.0f, _hintAlpha);
	}

	private static bool IsSkipKeyHeld()
	{
		return Input.IsKeyPressed(Key.Escape)
			|| Input.IsKeyPressed(Key.Space)
			|| Input.IsKeyPressed(Key.Enter)
			|| Input.IsKeyPressed(Key.KpEnter);
	}

	private bool TryPlay()
	{
		if (!ResourceLoader.Exists(VideoPath))
		{
			GD.PushWarning($"[ИНТРО] Файл не найден: {VideoPath}");
			return false;
		}

		var stream = GD.Load<VideoStream>(VideoPath);
		if (stream == null)
		{
			GD.PushWarning($"[ИНТРО] Не удалось загрузить видео: {VideoPath}");
			return false;
		}

		_video.Stream = stream;
		_video.Finished += GoNext;
		_video.Play();
		return true;
	}

	/// <summary>Ролик кончился или его пропустили — начинаем партию.</summary>
	private void GoNext()
	{
		if (_leaving)
		{
			return;
		}

		_leaving = true;
		_video.Stop();
		CursorMode.Show(this);

		if (GameFlow.Instance != null)
		{
			GameFlow.Instance.StartNewGame();
			return;
		}

		// Без GameFlow игру не начать — возвращаем в меню, а не оставляем на чёрном экране.
		GD.PushWarning("[ИНТРО] GameFlow недоступен, возвращаемся в меню.");
		GetTree().ChangeSceneToFile(FallbackScene);
	}

	private void BuildUi()
	{
		var background = new ColorRect
		{
			Color = Colors.Black,
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f
		};
		AddChild(background);

		_video = new VideoStreamPlayer
		{
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			Expand = true
		};
		AddChild(_video);

		// Подсказка живёт в правом нижнем углу и не перехватывает ввод.
		_hint = new VBoxContainer
		{
			AnchorLeft = 1.0f,
			AnchorRight = 1.0f,
			AnchorTop = 1.0f,
			AnchorBottom = 1.0f,
			OffsetLeft = -360.0f,
			OffsetRight = -40.0f,
			OffsetTop = -84.0f,
			OffsetBottom = -40.0f,
			GrowHorizontal = GrowDirection.Begin,
			GrowVertical = GrowDirection.Begin,
			MouseFilter = MouseFilterEnum.Ignore,
			Visible = false,
			Modulate = new Color(1.0f, 1.0f, 1.0f, 0.0f)
		};
		AddChild(_hint);

		// Секунды берём из настройки, иначе подсказка разойдётся с реальным порогом.
		_hintLabel = new Label
		{
			Text = $"Удерживайте {Mathf.RoundToInt(HoldToSkipSeconds)} секунды, чтобы пропустить",
			HorizontalAlignment = HorizontalAlignment.Right,
			MouseFilter = MouseFilterEnum.Ignore
		};
		_hintLabel.AddThemeColorOverride("font_color", new Color(0.86f, 0.84f, 0.78f));
		_hintLabel.AddThemeFontSizeOverride("font_size", 18);
		_hint.AddChild(_hintLabel);

		_hintBar = new ProgressBar
		{
			MinValue = 0.0,
			MaxValue = 100.0,
			Value = 0.0,
			ShowPercentage = false,
			CustomMinimumSize = new Vector2(0.0f, 6.0f),
			MouseFilter = MouseFilterEnum.Ignore
		};
		_hint.AddChild(_hintBar);
	}
}
