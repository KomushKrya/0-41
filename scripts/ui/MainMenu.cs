using Godot;

/// <summary>
/// Главное меню. Собирает себя кодом, а не из сцены: пунктов мало, а верстать
/// их в редакторе означало бы держать раскладку в двух местах разом.
///
/// Художественного оформления здесь нет намеренно — это рабочий каркас, поверх
/// которого рисуется настоящее меню. Всё, что ему нужно от игры, идёт через GameFlow.
/// </summary>
public partial class MainMenu : Control
{
	private Button _continueButton;
	private SettingsScreen _settings;
	private Label _hint;

	public override void _Ready()
	{
		AnchorRight = 1.0f;
		AnchorBottom = 1.0f;

		BuildUi();
		RefreshContinueButton();
	}

	private void BuildUi()
	{
		var background = new ColorRect
		{
			Color = new Color(0.05f, 0.06f, 0.07f),
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f
		};
		AddChild(background);

		var column = new VBoxContainer
		{
			AnchorLeft = 0.5f,
			AnchorTop = 0.5f,
			AnchorRight = 0.5f,
			AnchorBottom = 0.5f,
			GrowHorizontal = GrowDirection.Both,
			GrowVertical = GrowDirection.Both,
			CustomMinimumSize = new Vector2(320.0f, 0.0f)
		};
		column.AddThemeConstantOverride("separation", 12);
		AddChild(column);

		var title = new Label
		{
			Text = "К.О.Н.Т.У.Р.",
			HorizontalAlignment = HorizontalAlignment.Center
		};
		title.AddThemeFontSizeOverride("font_size", 42);
		column.AddChild(title);

		var subtitle = new Label
		{
			Text = "объект 0-41 · диспетчерская",
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(1.0f, 1.0f, 1.0f, 0.55f)
		};
		column.AddChild(subtitle);

		column.AddChild(new Control { CustomMinimumSize = new Vector2(0.0f, 24.0f) });

		_continueButton = AddButton(column, "Продолжить", OnContinue);
		AddButton(column, "Новая игра", OnNewGame);
		AddButton(column, "Настройки", OnToggleSettings);
		AddButton(column, "Выход", OnQuit);

		_hint = new Label
		{
			HorizontalAlignment = HorizontalAlignment.Center,
			Modulate = new Color(1.0f, 0.7f, 0.4f)
		};
		column.AddChild(_hint);

		// Экран настроек один на всю игру: тот же самый открывается из паузы.
		// Держать в меню свою урезанную копию значило бы разойтись с ней на первой правке.
		_settings = new SettingsScreen { Visible = false };
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
		_continueButton.TooltipText = hasSave ? string.Empty : "Сохранений пока нет";
	}

	// ------------------------------------------------------------------ действия

	private void OnNewGame()
	{
		if (GameFlow.Instance == null)
		{
			ShowHint("Поток игры не запущен — проверьте автозагрузку GameFlow.");
			return;
		}

		GameFlow.Instance.StartNewGame();
	}

	private void OnContinue()
	{
		if (GameFlow.Instance == null)
		{
			ShowHint("Поток игры не запущен.");
			return;
		}

		if (!GameFlow.Instance.ContinueGame())
		{
			ShowHint("Не удалось загрузить сохранение, подробности в Output.");
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

	private void OnQuit()
	{
		GetTree().Quit();
	}

	private void ShowHint(string text)
	{
		_hint.Text = text;
	}
}
