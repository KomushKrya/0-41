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

	/// <summary>
	/// Ходовые разрешения — 16:9, 16:10 и два сверхшироких. Список общий для всех
	/// мониторов: то, что в конкретный экран не влезет, отсеется при сборке списка.
	/// </summary>
	private static readonly Vector2I[] Resolutions =
	{
		new Vector2I(1280, 720),
		new Vector2I(1366, 768),
		new Vector2I(1440, 900),
		new Vector2I(1600, 900),
		new Vector2I(1680, 1050),
		new Vector2I(1920, 1080),
		new Vector2I(1920, 1200),
		new Vector2I(2560, 1080),
		new Vector2I(2560, 1440),
		new Vector2I(3440, 1440),
		new Vector2I(3840, 2160)
	};

	/// <summary>
	/// Режим экрана. Держим отдельным списком, а не строкой среди разрешений:
	/// полноэкранный режим отменяет выбранный размер, и в одном списке игроку
	/// пришлось бы помнить, что две соседние строки означают разное.
	/// </summary>
	private enum ScreenMode
	{
		Windowed = 0,
		Borderless = 1,
		Fullscreen = 2
	}

	private OptionButton _screenMode;
	private OptionButton _resolution;

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

		// Старые настройки хранили только флаг «полный экран». Читаем его как запасной
		// вариант, иначе у тех, кто уже играл, режим сбросился бы на оконный.
		bool legacyFullscreen = (bool)file.GetValue("video", "fullscreen", false);
		var mode = (ScreenMode)(int)file.GetValue(
			"video",
			"mode",
			(int)(legacyFullscreen ? ScreenMode.Fullscreen : ScreenMode.Windowed));

		ApplyScreenMode(mode, size);
	}

	/// <summary>
	/// Ставит режим экрана. Размер важен только оконному режиму: остальные два
	/// занимают монитор целиком, и запомненное разрешение им нечего делать.
	/// </summary>
	private static void ApplyScreenMode(ScreenMode mode, Vector2I windowedSize)
	{
		switch (mode)
		{
			case ScreenMode.Fullscreen:
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
				return;

			case ScreenMode.Borderless:
				DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
				return;

			default:
				ApplyWindowedSize(windowedSize);
				return;
		}
	}

	/// <summary>Что на экране прямо сейчас — по состоянию окна, а не по файлу настроек.</summary>
	private static ScreenMode CurrentScreenMode()
	{
		switch (DisplayServer.WindowGetMode())
		{
			case DisplayServer.WindowMode.ExclusiveFullscreen:
				return ScreenMode.Fullscreen;
			case DisplayServer.WindowMode.Fullscreen:
				return ScreenMode.Borderless;
			default:
				return ScreenMode.Windowed;
		}
	}

	/// <summary>
	/// Ставит окну размер — и делает это надёжно.
	///
	/// Здесь два подвоха, из-за которых наивный WindowSetSize молча ничего не
	/// делает. Первый: развёрнутое или полноэкранное окно размер не меняет, из
	/// него сначала надо выйти. Второй: размер больше экрана система обрежет
	/// сама, и настройка «сработает» совсем не так, как выбрал игрок.
	/// </summary>
	private static void ApplyWindowedSize(Vector2I size)
	{
		if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
		}

		DisplayServer.WindowSetSize(FitToScreen(size));
		CenterWindow();
	}

	/// <summary>Ужимает размер до экрана, оставляя запас на рамку и панель задач.</summary>
	private static Vector2I FitToScreen(Vector2I size)
	{
		Vector2I screen = DisplayServer.ScreenGetSize();
		if (screen.X <= 0 || screen.Y <= 0)
		{
			return size;
		}

		return new Vector2I(
			Mathf.Min(size.X, screen.X - 32),
			Mathf.Min(size.Y, screen.Y - 64));
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
		column.AddChild(new Label { Text = Content.Label("ui_settings_window_mode") });

		_screenMode = new OptionButton();
		_screenMode.AddItem(Content.Label("ui_settings_window_windowed"), (int)ScreenMode.Windowed);
		_screenMode.AddItem(Content.Label("ui_settings_window_borderless"), (int)ScreenMode.Borderless);
		_screenMode.AddItem(Content.Label("ui_settings_window_fullscreen"), (int)ScreenMode.Fullscreen);
		_screenMode.ItemSelected += OnScreenModeSelected;
		column.AddChild(_screenMode);

		column.AddChild(new Label { Text = Content.Label("ui_settings_resolution") });

		_resolution = new OptionButton();

		Vector2I screen = DisplayServer.ScreenGetSize();
		for (int i = 0; i < Resolutions.Length; i++)
		{
			// Больше монитора — показывать нечем: система всё равно обрежет окно,
			// и игрок решит, что настройка сломана.
			if (screen.X > 0 && (Resolutions[i].X > screen.X || Resolutions[i].Y > screen.Y))
			{
				continue;
			}

			_resolution.AddItem($"{Resolutions[i].X} x {Resolutions[i].Y}", i);
		}

		SelectCurrentMode();
		_resolution.ItemSelected += OnResolutionSelected;
		column.AddChild(_resolution);
	}

	/// <summary>
	/// Влезает ли размер в монитор именно как окно. Запас — на рамку и панель задач:
	/// без него окно «1920 x 1080» на экране 1920x1080 система молча ужмёт, и
	/// выбранное разрешение окажется не тем, что показано в списке.
	/// </summary>
	private static bool FitsAsWindow(Vector2I size, Vector2I screen)
	{
		return screen.X <= 0 || (size.X <= screen.X - 32 && size.Y <= screen.Y - 64);
	}

	/// <summary>Отмечает в списках то, что видно на экране прямо сейчас.</summary>
	private void SelectCurrentMode()
	{
		_screenMode.Select(_screenMode.GetItemIndex((int)CurrentScreenMode()));

		// Список размеров живой в любом режиме: выбрать размер — это и значит
		// выйти из полного экрана, и запирать единственный обратный путь нельзя.
		Vector2I current = DisplayServer.WindowGetSize();
		for (int i = 0; i < _resolution.ItemCount; i++)
		{
			if (Resolutions[_resolution.GetItemId(i)] == current)
			{
				_resolution.Select(i);
				return;
			}
		}
	}

	// ------------------------------------------------------------------ действия

	public void Open()
	{
		Visible = true;

		// Экран живёт между открытиями, а окно за это время могли развернуть мимо
		// настроек — системной кнопкой или Alt+Enter. Сверяемся с окном заново.
		SelectCurrentMode();
	}

	public void Close()
	{
		Visible = false;
		Closed?.Invoke();
	}

	/// <summary>Кого вернуть управление, решает тот, кто открыл: меню или пауза.</summary>
	public event System.Action Closed;

	private void OnScreenModeSelected(long index)
	{
		var mode = (ScreenMode)_screenMode.GetItemId((int)index);

		ApplyScreenMode(mode, SelectedResolution());
		Save("video", "mode", (int)mode);

		// Старый флаг держим в согласии с новым: иначе игра, откатившаяся на
		// прежнюю сборку, прочитала бы из файла режим, который игрок уже сменил.
		Save("video", "fullscreen", mode == ScreenMode.Fullscreen);
	}

	/// <summary>
	/// Выбран размер окна. Приходит индекс строки, а не наш идентификатор:
	/// строки, не влезающие в монитор, в список не попали, и нумерация сдвинута.
	/// </summary>
	private void OnResolutionSelected(long index)
	{
		Vector2I size = Resolutions[_resolution.GetItemId((int)index)];
		Save("video", "resolution", size);

		// Размер во весь монитор окном не показать: рамка и панель задач его
		// ужмут, и вместо «1920 x 1080» игрок получит молча обрезанное окно.
		// Такой выбор честнее отыграть режимом без рамки — картинка выйдет ровно
		// той, что написано в списке.
		ScreenMode mode = FitsAsWindow(size, DisplayServer.ScreenGetSize())
			? ScreenMode.Windowed
			: ScreenMode.Borderless;

		ApplyScreenMode(mode, size);
		Save("video", "mode", (int)mode);
		Save("video", "fullscreen", false);

		// Режим мог смениться сам — список сверху обязан это показать.
		_screenMode.Select(_screenMode.GetItemIndex((int)mode));
	}

	/// <summary>Размер, отмеченный в списке. Пустой список — оставляем что есть.</summary>
	private Vector2I SelectedResolution()
	{
		int selected = _resolution.Selected;
		if (selected < 0 || selected >= _resolution.ItemCount)
		{
			return DisplayServer.WindowGetSize();
		}

		return Resolutions[_resolution.GetItemId(selected)];
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
