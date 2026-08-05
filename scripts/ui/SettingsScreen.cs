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

	/// <summary>
	/// Полный экран стоит в том же списке, что и размеры окна.
	/// Отдельного переключателя режима нет: для игрока это один вопрос — «какого
	/// размера картинка», — и разносить его по двум спискам значит заставлять
	/// помнить, что одна настройка отменяет другую.
	/// Идентификатор заведомо вне диапазона индексов массива.
	/// </summary>
	private const int FullscreenItemId = 1000;

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
		bool fullscreen = (bool)file.GetValue("video", "fullscreen", false);

		if (fullscreen)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
			return;
		}

		ApplyWindowedSize(size);
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
		column.AddChild(new Label { Text = Content.Label("ui_settings_resolution") });

		_resolution = new OptionButton();

		Vector2I screen = DisplayServer.ScreenGetSize();
		for (int i = 0; i < Resolutions.Length; i++)
		{
			// Размер, который заведомо не влезет в монитор, в списке не нужен:
			// система обрежет окно, и игрок решит, что настройка сломана.
			if (screen.X > 0 && (Resolutions[i].X > screen.X || Resolutions[i].Y > screen.Y))
			{
				continue;
			}

			_resolution.AddItem($"{Resolutions[i].X} x {Resolutions[i].Y}", i);
		}

		_resolution.AddItem(Content.Label("ui_settings_window_fullscreen"), FullscreenItemId);

		SelectCurrentMode();
		_resolution.ItemSelected += OnResolutionSelected;
		column.AddChild(_resolution);
	}

	/// <summary>Отмечает в списке то, что видно на экране прямо сейчас.</summary>
	private void SelectCurrentMode()
	{
		if (DisplayServer.WindowGetMode() != DisplayServer.WindowMode.Windowed)
		{
			_resolution.Select(_resolution.GetItemIndex(FullscreenItemId));
			return;
		}

		Vector2I current = DisplayServer.WindowGetSize();
		for (int i = 0; i < _resolution.ItemCount; i++)
		{
			int id = _resolution.GetItemId(i);
			if (id != FullscreenItemId && Resolutions[id] == current)
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
	}

	public void Close()
	{
		Visible = false;
		Closed?.Invoke();
	}

	/// <summary>Кого вернуть управление, решает тот, кто открыл: меню или пауза.</summary>
	public event System.Action Closed;

	/// <summary>
	/// Выбран пункт списка. Приходит индекс строки, а не наш идентификатор:
	/// строки, не влезающие в монитор, в список не попали, и нумерация сдвинута.
	/// </summary>
	private void OnResolutionSelected(long index)
	{
		int id = _resolution.GetItemId((int)index);

		if (id == FullscreenItemId)
		{
			DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
			Save("video", "fullscreen", true);
			return;
		}

		ApplyWindowedSize(Resolutions[id]);
		Save("video", "fullscreen", false);
		Save("video", "resolution", Resolutions[id]);
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
