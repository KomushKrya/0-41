using Godot;

/// <summary>
/// Экран настроек. Один на всю игру: открывается и из главного меню, и из паузы,
/// поэтому это самостоятельный Control, а не панель внутри меню.
///
/// Собирается кодом: список ползунков зависит от того, какие звуковые шины и
/// режимы окна вообще есть, поэтому верстать его заранее нечем. В меню и в паузе
/// узел вставляется как есть — правится его место, а не содержимое.
///
/// Всё, что игрок здесь меняет, сразу применяется и сразу пишется в
/// <c>user://settings.cfg</c>. Отдельной кнопки «применить» нет намеренно: она
/// нужна только там, где настройку нельзя откатить, а здесь любую видно на месте.
/// </summary>
public partial class SettingsScreen : Control
{
	private const string SettingsPath = "user://settings.cfg";

	/// <summary>
	/// Шины звука. Master есть всегда, остальные — если их завели в проекте.
	/// Отсутствующая шина не ошибка: ползунок для неё просто не появится.
	/// </summary>
	private static readonly (string Bus, string LabelId)[] Buses =
	{
		("Master", "ui_settings_bus_master"),
		("Music", "ui_settings_bus_music"),
		("SFX", "ui_settings_bus_sfx")
	};

	private static readonly Vector2I[] Resolutions =
	{
		new Vector2I(1280, 720),
		new Vector2I(1600, 900),
		new Vector2I(1920, 1080),
		new Vector2I(2560, 1440)
	};

	/// <summary>Чувствительность мыши: игрок крутит её здесь, читает игрок камеры.</summary>
	public static float MouseSensitivity { get; private set; } = 1.0f;

	private OptionButton _resolution;
	private OptionButton _windowMode;

	public override void _Ready()
	{
		AnchorRight = 1.0f;
		AnchorBottom = 1.0f;

		// Настройки открываются поверх паузы, а пауза останавливает дерево.
		ProcessMode = ProcessModeEnum.Always;

		BuildUi();
	}

	/// <summary>
	/// Читает сохранённые настройки и применяет их. Зовётся один раз при запуске
	/// игры — до того, как игрок увидит первый кадр.
	/// </summary>
	public static void LoadAndApply()
	{
		var file = new ConfigFile();
		if (file.Load(SettingsPath) != Error.Ok)
		{
			return;
		}

		for (int i = 0; i < Buses.Length; i++)
		{
			int bus = AudioServer.GetBusIndex(Buses[i].Bus);
			if (bus < 0)
			{
				continue;
			}

			ApplyVolume(bus, (double)file.GetValue("audio", Buses[i].Bus, 1.0));
		}

		var size = (Vector2I)file.GetValue("video", "resolution", DisplayServer.WindowGetSize());
		var mode = (int)file.GetValue("video", "window_mode", (int)DisplayServer.WindowGetMode());
		bool vsync = (bool)file.GetValue("video", "vsync", true);

		ApplyWindowMode((DisplayServer.WindowMode)mode, size);
		DisplayServer.WindowSetVsyncMode(vsync
			? DisplayServer.VSyncMode.Enabled
			: DisplayServer.VSyncMode.Disabled);

		MouseSensitivity = (float)(double)file.GetValue("input", "mouse_sensitivity", 1.0);
	}

	private static void Save(string section, string key, Variant value)
	{
		var file = new ConfigFile();
		file.Load(SettingsPath);          // отсутствующий файл — не беда, будет создан
		file.SetValue(section, key, value);
		file.Save(SettingsPath);
	}

	// ------------------------------------------------------------------ вёрстка

	private void BuildUi()
	{
		var background = new ColorRect
		{
			Color = new Color(0.04f, 0.05f, 0.06f, 0.94f),
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
			CustomMinimumSize = new Vector2(420.0f, 0.0f)
		};
		column.AddThemeConstantOverride("separation", 10);
		AddChild(column);

		var title = new Label { Text = Content.Label("ui_settings_title"), HorizontalAlignment = HorizontalAlignment.Center };
		title.AddThemeFontSizeOverride("font_size", 28);
		column.AddChild(title);

		column.AddChild(new HSeparator());

		BuildAudio(column);
		column.AddChild(new HSeparator());
		BuildVideo(column);
		column.AddChild(new HSeparator());
		BuildInput(column);

		column.AddChild(new Control { CustomMinimumSize = new Vector2(0.0f, 12.0f) });

		var close = new Button { Text = Content.Label("ui_settings_close"), CustomMinimumSize = new Vector2(0.0f, 40.0f) };
		close.Pressed += Close;
		column.AddChild(close);
	}

	private void BuildAudio(Container column)
	{
		for (int i = 0; i < Buses.Length; i++)
		{
			int bus = AudioServer.GetBusIndex(Buses[i].Bus);
			if (bus < 0)
			{
				// Шины в проекте нет — ползунок, который ни на что не влияет, хуже,
				// чем его отсутствие: игрок крутит и решает, что звук сломан.
				continue;
			}

			column.AddChild(new Label { Text = Content.Label(Buses[i].LabelId) });

			var slider = new HSlider
			{
				MinValue = 0.0,
				MaxValue = 1.0,
				Step = 0.05,
				Value = ReadVolume(bus)
			};

			string busName = Buses[i].Bus;
			int busIndex = bus;
			slider.ValueChanged += value =>
			{
				ApplyVolume(busIndex, value);
				Save("audio", busName, value);
			};

			column.AddChild(slider);
		}
	}

	private void BuildVideo(Container column)
	{
		column.AddChild(new Label { Text = Content.Label("ui_settings_resolution") });

		_resolution = new OptionButton();
		Vector2I current = DisplayServer.WindowGetSize();
		for (int i = 0; i < Resolutions.Length; i++)
		{
			_resolution.AddItem($"{Resolutions[i].X} x {Resolutions[i].Y}", i);
			if (Resolutions[i] == current)
			{
				_resolution.Select(i);
			}
		}

		_resolution.ItemSelected += OnResolutionSelected;
		column.AddChild(_resolution);

		column.AddChild(new Label { Text = Content.Label("ui_settings_window_mode") });

		_windowMode = new OptionButton();
		_windowMode.AddItem(Content.Label("ui_settings_window_windowed"), (int)DisplayServer.WindowMode.Windowed);
		_windowMode.AddItem(Content.Label("ui_settings_window_fullscreen"), (int)DisplayServer.WindowMode.Fullscreen);
		_windowMode.AddItem(Content.Label("ui_settings_window_borderless"), (int)DisplayServer.WindowMode.ExclusiveFullscreen);
		_windowMode.Select(_windowMode.GetItemIndex((int)DisplayServer.WindowGetMode()));
		_windowMode.ItemSelected += OnWindowModeSelected;
		column.AddChild(_windowMode);

		var vsync = new CheckBox
		{
			Text = Content.Label("ui_settings_vsync"),
			ButtonPressed = DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Disabled
		};
		vsync.Toggled += OnVsyncToggled;
		column.AddChild(vsync);
	}

	private void BuildInput(Container column)
	{
		column.AddChild(new Label { Text = Content.Label("ui_settings_mouse_sensitivity") });

		var slider = new HSlider
		{
			MinValue = 0.2,
			MaxValue = 3.0,
			Step = 0.1,
			Value = MouseSensitivity
		};

		slider.ValueChanged += value =>
		{
			MouseSensitivity = (float)value;
			Save("input", "mouse_sensitivity", value);
		};

		column.AddChild(slider);
	}

	// ------------------------------------------------------------------ действия

	public void Open()
	{
		Visible = true;
	}

	public void Close()
	{
		Visible = false;
		Closed?.Invoke();
	}

	/// <summary>Кого вернуть управление, решает тот, кто открыл: меню или пауза.</summary>
	public event System.Action Closed;

	private void OnResolutionSelected(long index)
	{
		Vector2I size = Resolutions[(int)index];
		DisplayServer.WindowSetSize(size);
		CenterWindow();
		Save("video", "resolution", size);
	}

	private void OnWindowModeSelected(long index)
	{
		var mode = (DisplayServer.WindowMode)_windowMode.GetItemId((int)index);
		ApplyWindowMode(mode, DisplayServer.WindowGetSize());
		Save("video", "window_mode", (int)mode);
	}

	private void OnVsyncToggled(bool pressed)
	{
		DisplayServer.WindowSetVsyncMode(pressed
			? DisplayServer.VSyncMode.Enabled
			: DisplayServer.VSyncMode.Disabled);
		Save("video", "vsync", pressed);
	}

	private static void ApplyWindowMode(DisplayServer.WindowMode mode, Vector2I windowedSize)
	{
		DisplayServer.WindowSetMode(mode);

		if (mode == DisplayServer.WindowMode.Windowed)
		{
			DisplayServer.WindowSetSize(windowedSize);
		}
	}

	private static void CenterWindow()
	{
		Vector2I screen = DisplayServer.ScreenGetSize();
		Vector2I window = DisplayServer.WindowGetSize();
		DisplayServer.WindowSetPosition((screen - window) / 2);
	}

	/// <summary>
	/// Громкость шины хранится в децибелах, а ползунок линейный от нуля до единицы.
	/// Перевод обязателен: без него первая четверть ползунка была бы неотличима
	/// от тишины, а последняя — от максимума.
	/// </summary>
	private static double ReadVolume(int bus)
	{
		return AudioServer.IsBusMute(bus) ? 0.0 : Mathf.DbToLinear(AudioServer.GetBusVolumeDb(bus));
	}

	private static void ApplyVolume(int bus, double value)
	{
		// Ноль в линейной шкале — это минус бесконечность в децибелах; шину в таком
		// случае честнее заглушить, а не считать логарифм от нуля.
		if (value <= 0.001)
		{
			AudioServer.SetBusMute(bus, true);
			return;
		}

		AudioServer.SetBusMute(bus, false);
		AudioServer.SetBusVolumeDb(bus, Mathf.LinearToDb((float)value));
	}
}
