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
	private Panel _settingsPanel;
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

		_settingsPanel = BuildSettingsPanel();
		AddChild(_settingsPanel);
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
		_settingsPanel.Visible = !_settingsPanel.Visible;
	}

	private void OnQuit()
	{
		GetTree().Quit();
	}

	private void ShowHint(string text)
	{
		_hint.Text = text;
	}

	// ------------------------------------------------------------------ настройки

	private Panel BuildSettingsPanel()
	{
		var panel = new Panel
		{
			Visible = false,
			AnchorLeft = 1.0f,
			AnchorRight = 1.0f,
			OffsetLeft = -320.0f,
			OffsetTop = 24.0f,
			OffsetRight = -24.0f,
			OffsetBottom = 260.0f
		};

		var column = new VBoxContainer
		{
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			OffsetLeft = 16.0f,
			OffsetTop = 16.0f,
			OffsetRight = -16.0f,
			OffsetBottom = -16.0f
		};
		column.AddThemeConstantOverride("separation", 8);
		panel.AddChild(column);

		column.AddChild(new Label { Text = "НАСТРОЙКИ" });

		column.AddChild(new Label { Text = "Громкость" });
		var volume = new HSlider
		{
			MinValue = 0.0,
			MaxValue = 1.0,
			Step = 0.05,
			Value = GetMasterVolume()
		};
		volume.ValueChanged += OnVolumeChanged;
		column.AddChild(volume);

		var fullscreen = new CheckBox
		{
			Text = "Полный экран",
			ButtonPressed = DisplayServer.WindowGetMode() == DisplayServer.WindowMode.Fullscreen
				|| DisplayServer.WindowGetMode() == DisplayServer.WindowMode.ExclusiveFullscreen
		};
		fullscreen.Toggled += OnFullscreenToggled;
		column.AddChild(fullscreen);

		return panel;
	}

	/// <summary>
	/// Громкость шины хранится в децибелах, а ползунок линейный от нуля до единицы.
	/// Перевод обязателен: без него первая четверть ползунка была бы неотличима
	/// от тишины, а последняя — от максимума.
	/// </summary>
	private static double GetMasterVolume()
	{
		int bus = AudioServer.GetBusIndex("Master");
		if (bus < 0)
		{
			return 1.0;
		}

		return Mathf.DbToLinear(AudioServer.GetBusVolumeDb(bus));
	}

	private void OnVolumeChanged(double value)
	{
		int bus = AudioServer.GetBusIndex("Master");
		if (bus < 0)
		{
			return;
		}

		// Нулевая громкость в линейной шкале — это минус бесконечность в децибелах;
		// шину в таком случае честнее заглушить, а не считать логарифм от нуля.
		if (value <= 0.001)
		{
			AudioServer.SetBusMute(bus, true);
			return;
		}

		AudioServer.SetBusMute(bus, false);
		AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb((float)value));
	}

	private void OnFullscreenToggled(bool pressed)
	{
		DisplayServer.WindowSetMode(pressed
			? DisplayServer.WindowMode.Fullscreen
			: DisplayServer.WindowMode.Windowed);
	}
}
