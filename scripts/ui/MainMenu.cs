using System.Collections.Generic;
using Godot;

/// <summary>
/// Главное меню. Вёрстка лежит в MainMenu.tscn и правится в редакторе: скрипт
/// не создаёт узлов, а находит готовые по путям и подписывается на кнопки.
///
/// Тексты всё же ставятся отсюда — они приходят из текстового движка, а не
/// из сцены. В сцене написаны те же подписи заглушкой, чтобы экран читался
/// в редакторе, но в игре их перекрывает Content.
///
/// Живой кабинет за подписями — отдельная забота <see cref="MenuBackdrop"/>.
/// Здесь только появление экрана и поведение строк меню.
///
/// Всё, что меню нужно от игры, идёт через GameFlow.
/// </summary>
public partial class MainMenu : Control
{
	[Export] public NodePath ColumnPath { get; set; } = new("Column");
	[Export] public NodePath TitlePath { get; set; } = new("Column/Title");
	[Export] public NodePath SubtitlePath { get; set; } = new("Column/Subtitle");
	[Export] public NodePath ContinueButtonPath { get; set; } = new("Column/ContinueButton");
	[Export] public NodePath NewGameButtonPath { get; set; } = new("Column/NewGameButton");
	[Export] public NodePath SettingsButtonPath { get; set; } = new("Column/SettingsButton");
	[Export] public NodePath QuitButtonPath { get; set; } = new("Column/QuitButton");
	[Export] public NodePath HintPath { get; set; } = new("Column/Hint");
	[Export] public NodePath FadePath { get; set; } = new("Fade");
	[Export] public NodePath BackdropViewportPath { get; set; } = new("Backdrop/Viewport");
	[Export] public NodePath SettingsScreenPath { get; set; } = new("Settings");

	/// <summary>Вступительный ролик: показывается только при старте новой игры.</summary>
	[Export] public string IntroScene { get; set; } = "res://scenes/ui/intro/IntroPlayer.tscn";

	/// <summary>За сколько уходит чёрный экран при входе в меню.</summary>
	[Export] public float FadeSeconds { get; set; } = 1.4f;

	/// <summary>Задержка между появлением соседних строк меню.</summary>
	[Export] public float RowStepSeconds { get; set; } = 0.09f;

	private Button _continueButton;
	private SettingsScreen _settings;
	private Label _hint;

	private SubViewport _backdrop;
	private SubViewport.UpdateMode _backdropMode;

	private readonly List<Button> _rows = new();
	private readonly Dictionary<Control, Tween> _markerTweens = new();

	public override void _Ready()
	{
		// Меню может открыться после кабинета, где курсор захвачен камерой.
		// Сам экран отвечает за свой режим ввода, а не рассчитывает на путь перехода.
		CursorMode.Show(this);

		BindUi();
		RefreshContinueButton();
		PlayIntro();
	}

	private void BindUi()
	{
		GetNode<Label>(TitlePath).Text = Content.Label("ui_menu_title");
		GetNode<Label>(SubtitlePath).Text = Content.Label("ui_menu_subtitle");

		_hint = GetNode<Label>(HintPath);
		_hint.Text = string.Empty;

		_continueButton = BindButton(ContinueButtonPath, "ui_menu_continue", OnContinue);
		BindButton(NewGameButtonPath, "ui_menu_new_game", OnNewGame);
		BindButton(SettingsButtonPath, "ui_menu_settings", OnToggleSettings);
		BindButton(QuitButtonPath, "ui_menu_quit", OnQuit);

		// Экран настроек один на всю игру: тот же самый открывается из паузы.
		// Держать в меню свою урезанную копию значило бы разойтись с ней на первой правке.
		_settings = GetNode<SettingsScreen>(SettingsScreenPath);
		_settings.Visible = false;

		_backdrop = GetNodeOrNull<SubViewport>(BackdropViewportPath);
		if (_backdrop != null)
		{
			_backdropMode = _backdrop.RenderTargetUpdateMode;

			// Слушаем видимость, а не свою же кнопку: у настроек есть собственное
			// «Закрыть», и через него экран уходит мимо OnToggleSettings.
			_settings.VisibilityChanged += () => SetBackdropRunning(!_settings.Visible);
		}
	}

	private Button BindButton(NodePath path, string labelId, System.Action onPressed)
	{
		var button = GetNode<Button>(path);
		button.Text = Content.Label(labelId);
		button.Pressed += () =>
		{
			AudioManager.Instance?.PlayUi(Sfx.ChoicePress);
			onPressed();
		};

		BindMarker(button);
		_rows.Add(button);
		return button;
	}

	/// <summary>
	/// Уголок слева от строки: плашек в меню нет, и без него наведение читалось бы
	/// только по цвету текста. Узел лежит в сцене — если его убрали, строка просто
	/// остаётся без уголка.
	/// </summary>
	private void BindMarker(Button button)
	{
		var marker = button.GetNodeOrNull<Control>("Marker");
		if (marker == null)
		{
			return;
		}

		button.MouseEntered += () => FadeMarker(button, marker, true);
		button.MouseExited += () => FadeMarker(button, marker, false);
		button.FocusEntered += () => FadeMarker(button, marker, true);
		button.FocusExited += () => FadeMarker(button, marker, false);
	}

	private void FadeMarker(Button button, Control marker, bool shown)
	{
		// Курсор способен пробежать по строкам быстрее, чем доигрывает подсветка:
		// прошлый твин гасим, иначе они тянут прозрачность каждый в свою сторону.
		if (_markerTweens.TryGetValue(marker, out Tween running) && running != null && running.IsValid())
		{
			running.Kill();
		}

		float target = shown && !button.Disabled ? 1.0f : 0.0f;
		Tween tween = marker.CreateTween();
		tween.TweenProperty(marker, "modulate:a", target, 0.12);
		_markerTweens[marker] = tween;
	}

	/// <summary>
	/// Появление экрана: сначала уходит чёрный, следом проявляется колонка,
	/// и уже поверх неё — строки одна за другой. Фон к этому времени уже едет,
	/// поэтому кадр не выглядит стоп-кадром, пока текст собирается.
	/// </summary>
	private void PlayIntro()
	{
		if (GetNodeOrNull<ColorRect>(FadePath) is ColorRect fade)
		{
			Tween fadeTween = CreateTween();
			fadeTween.TweenProperty(fade, "color:a", 0.0f, FadeSeconds)
				.SetTrans(Tween.TransitionType.Sine);
			fadeTween.TweenCallback(Callable.From(() => fade.Visible = false));
		}

		if (GetNodeOrNull<Control>(ColumnPath) is Control column)
		{
			FadeIn(column, FadeSeconds * 0.35f, 0.9f);
		}

		// Строки гасим поверх колонки: прозрачности перемножаются, поэтому
		// кнопки дособерутся уже после того, как проявился заголовок.
		float delay = FadeSeconds * 0.6f;
		foreach (Button row in _rows)
		{
			FadeIn(row, delay, 0.5f);
			delay += RowStepSeconds;
		}
	}

	private void FadeIn(Control target, float delay, float seconds)
	{
		Color rest = target.Modulate;
		target.Modulate = new Color(rest.R, rest.G, rest.B, 0.0f);

		Tween tween = CreateTween();
		tween.TweenInterval(delay);
		tween.TweenProperty(target, "modulate:a", rest.A, seconds)
			.SetTrans(Tween.TransitionType.Sine);
	}

	/// <summary>
	/// «Продолжить» гасится, если сохранения нет. Кнопка, которая ничего не делает,
	/// хуже отсутствующей: игрок жмёт её и решает, что игра сломана.
	/// </summary>
	private void RefreshContinueButton()
	{
		bool hasSave = GameFlow.Instance != null
			&& GameFlow.Instance.Runtime != null
			&& GameFlow.Instance.Runtime.HasSlot(GameFlow.QuickSlot);

		_continueButton.Disabled = !hasSave;
		_continueButton.TooltipText = hasSave ? string.Empty : Content.Label("ui_hint_no_saves");
	}

	// ------------------------------------------------------------------ действия

	private void OnNewGame()
	{
		if (GameFlow.Instance == null)
		{
			ShowHint(Content.Label("ui_hint_no_flow_autoload"));
			return;
		}

		// Новая игра начинается со вступительного ролика: по его окончании
		// IntroPlayer сам зовёт StartNewGame. «Продолжить» ролик не показывает.
		GetTree().ChangeSceneToFile(IntroScene);
	}

	private void OnContinue()
	{
		if (GameFlow.Instance == null)
		{
			ShowHint(Content.Label("ui_hint_no_flow"));
			return;
		}

		if (!GameFlow.Instance.ContinueGame())
		{
			ShowHint(Content.Label("ui_hint_load_failed"));
			RefreshContinueButton();
		}
	}

	private void OnToggleSettings()
	{
		if (_settings.Visible)
		{
			_settings.Close();
		}
		else
		{
			_settings.Open();
		}
	}

	/// <summary>
	/// Настройки закрывают экран непрозрачной подложкой, а кабинет за ней продолжал бы
	/// рисоваться в никуда. На время останавливаем перерисовку; исходный режим запомнен,
	/// поэтому статичный фон так статичным и остаётся.
	/// </summary>
	private void SetBackdropRunning(bool running)
	{
		if (_backdrop == null)
		{
			return;
		}

		_backdrop.RenderTargetUpdateMode = running ? _backdropMode : SubViewport.UpdateMode.Disabled;
	}

	private void OnQuit()
	{
		GetTree().Quit();
	}

	private void ShowHint(string text)
	{
		_hint.Text = text;
	}
}
