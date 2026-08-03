using Godot;

/// <summary>
/// Меню паузы. Автозагрузка: живёт над сценами и переживает переход между ними,
/// потому что пауза нужна и в кабинете, и на экране найма.
///
/// Открывается по Escape, когда игрок ни во что не смотрит. Цепочка Escape внутри
/// кабинета остаётся прежней (расфокус предмета → встать со стула), пауза встаёт
/// последним звеном — там, где раньше просто переключался курсор.
///
/// Пока меню открыто, дерево остановлено целиком: вместе с ним встаёт и ядро,
/// потому что KonturRuntime тикает обычным _Process. Само меню помечено
/// ProcessModeEnum.Always, иначе оно замерло бы вместе со всем остальным
/// и закрыть его было бы нечем.
/// </summary>
public partial class PauseMenu : CanvasLayer
{
	public static PauseMenu Instance { get; private set; }

	private Control _root;
	private SettingsScreen _settings;
	private Label _hint;
	private Button _loadButton;

	/// <summary>Что было с курсором до паузы — чтобы вернуть ровно это.</summary>
	private Input.MouseModeEnum _mouseModeBeforePause = Input.MouseModeEnum.Captured;

	public bool IsOpen => _root != null && _root.Visible;

	public override void _Ready()
	{
		Instance = this;

		// Слой поверх всего, включая экраны найма и катсцены.
		Layer = 128;
		ProcessMode = ProcessModeEnum.Always;

		BuildUi();

		// Настройки применяются до первого кадра: игрок не должен видеть,
		// как разрешение прыгает уже после запуска.
		SettingsScreen.LoadAndApply();
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	/// <summary>
	/// Escape внутри паузы: закрывает настройки, если открыты, иначе снимает паузу.
	///
	/// Обрабатывается в _Input, а не в _UnhandledInput, потому что кнопки меню
	/// съедают ввод раньше — до необработанного он бы просто не дошёл.
	/// </summary>
	public override void _Input(InputEvent @event)
	{
		if (!IsOpen || !@event.IsActionPressed("ui_cancel"))
		{
			return;
		}

		if (_settings.Visible)
		{
			_settings.Close();
		}
		else
		{
			Close();
		}

		GetViewport().SetInputAsHandled();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (IsOpen || !@event.IsActionPressed("ui_cancel"))
		{
			return;
		}

		// В меню Escape не должен открывать ещё одно меню поверх главного.
		string scenePath = GetTree().CurrentScene?.SceneFilePath ?? string.Empty;
		if (scenePath == "res://scenes/ui/menu/MainMenu.tscn")
		{
			return;
		}

		Open();
		GetViewport().SetInputAsHandled();
	}

	// ------------------------------------------------------------------ вёрстка

	private void BuildUi()
	{
		_root = new Control
		{
			Visible = false,
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		AddChild(_root);

		var background = new ColorRect
		{
			Color = new Color(0.0f, 0.0f, 0.0f, 0.72f),
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f
		};
		_root.AddChild(background);

		var column = new VBoxContainer
		{
			AnchorLeft = 0.5f,
			AnchorTop = 0.5f,
			AnchorRight = 0.5f,
			AnchorBottom = 0.5f,
			GrowHorizontal = Control.GrowDirection.Both,
			GrowVertical = Control.GrowDirection.Both,
			CustomMinimumSize = new Vector2(300.0f, 0.0f)
		};
		column.AddThemeConstantOverride("separation", 12);
		_root.AddChild(column);

		var title = new Label { Text = Content.Label("ui_pause_title"), HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 32);
		column.AddChild(title);

		column.AddChild(new Control { CustomMinimumSize = new Vector2(0.0f, 16.0f) });

		AddButton(column, Content.Label("ui_pause_resume"), Close);
		_loadButton = AddButton(column, Content.Label("ui_pause_load"), OnLoad);
		AddButton(column, Content.Label("ui_pause_settings"), OnSettings);
		AddButton(column, Content.Label("ui_pause_main_menu"), OnMainMenu);

		_hint = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(1.0f, 0.7f, 0.4f)
		};
		column.AddChild(_hint);

		_settings = new SettingsScreen { Visible = false };
		_settings.Closed += () => _root.Visible = true;
		AddChild(_settings);
	}

	private Button AddButton(Container parent, string text, System.Action onPressed)
	{
		var button = new Button
		{
			Text = text,
			CustomMinimumSize = new Vector2(0.0f, 40.0f)
		};

		button.Pressed += () => onPressed();
		parent.AddChild(button);
		return button;
	}

	// ------------------------------------------------------------------ действия

	public void Open()
	{
		if (IsOpen)
		{
			return;
		}

		_mouseModeBeforePause = Input.MouseMode;
		Input.MouseMode = Input.MouseModeEnum.Visible;

		_hint.Text = string.Empty;
		_loadButton.Disabled = !HasSave();
		_loadButton.TooltipText = HasSave() ? string.Empty : Content.Label("ui_hint_no_saves");

		_root.Visible = true;
		GetTree().Paused = true;
	}

	public void Close()
	{
		if (!IsOpen)
		{
			return;
		}

		_settings.Visible = false;
		_root.Visible = false;
		GetTree().Paused = false;

		Input.MouseMode = _mouseModeBeforePause;
	}

	private static bool HasSave()
	{
		return GameFlow.Instance != null
			&& GameFlow.Instance.Runtime != null
			&& GameFlow.Instance.Runtime.HasSlot(GameFlow.QuickSlot);
	}

	private void OnSettings()
	{
		_root.Visible = false;
		_settings.Open();
	}

	private void OnLoad()
	{
		if (GameFlow.Instance == null)
		{
			_hint.Text = Content.Label("ui_hint_no_flow");
			return;
		}

		// Снимаем паузу до загрузки: сцена сменится, и оставленный флаг
		// заморозил бы уже новую сцену, в которой этого меню ещё нет.
		Close();

		if (!GameFlow.Instance.ContinueGame())
		{
			Open();
			_hint.Text = Content.Label("ui_hint_load_failed");
		}
	}

	private void OnMainMenu()
	{
		if (GameFlow.Instance == null)
		{
			_hint.Text = Content.Label("ui_hint_no_flow");
			return;
		}

		Close();
		GameFlow.Instance.ShowMainMenu();
	}
}
