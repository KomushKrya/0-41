using System;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Content;

/// <summary>
/// Мост между симуляционным ядром и движком. Регистрируется автозагрузкой под именем "GameRuntime".
///
/// Единственное место, где Godot встречается с ядром. Сцены и предметы на столе
/// подписываются на <c>Session.Events</c> и отдают команды через <c>Session</c>;
/// напрямую в системы ядра никто не обращается.
///
/// Узел инертен, пока не начата смена: контент загружается в _Ready, но Tick
/// ничего не делает, пока StartShift не вызван. Поэтому автозагрузка безопасна
/// для существующих сцен.
/// </summary>
public partial class GameRuntime : Node
{
	[Export] public string ContentRoot { get; set; } = "res://data/";

	/// <summary>
	/// Зерно ГПСЧ. Значение используется, только если снят RandomizeSeed:
	/// тогда прогон воспроизводится один в один — этим живут отладка и самопроверки.
	/// </summary>
	[Export] public int Seed { get; set; } = 41;

	/// <summary>
	/// Брать случайное зерно при каждом запуске. Иначе порядок миссий, выбранные
	/// здания и паузы между звонками были бы одинаковыми в каждой партии.
	/// </summary>
	[Export] public bool RandomizeSeed { get; set; } = true;

	/// <summary>Пауза симуляции — для меню, роликов и отладки.</summary>
	private bool _isPaused;

	/// <summary>
	/// Совместимый флаг для старых сцен. Реальная пауза хранится в ядре как владелец
	/// <c>godot.runtime</c>, поэтому новая модалка не может случайно снять чужую паузу.
	/// </summary>
	[Export]
	public bool IsPaused
	{
		get => _isPaused;
		set
		{
			if (_isPaused == value)
			{
				return;
			}

			_isPaused = value;
			Session?.SetTimeFreeze("godot.runtime", value);
		}
	}

	/// <summary>Ускорение прогона. 1 — реальное время, 10 — смена за 30 секунд.</summary>
	[Export] public float TimeScale { get; set; } = 1.0f;

	/// <summary>Печатать все сигналы ядра в Output. Полезно на этапе отладки.</summary>
	[Export] public bool LogEvents { get; set; } = true;

	private IDisposable _logSubscription;

	/// <summary>Ядро. Null, если контент не загрузился — проверяйте IsReady.</summary>
	public KonturSimulation Session { get; private set; }

	/// <summary>Имя, которое использует верхний flow-слой. Старый Session остаётся для существующих сцен.</summary>
	public KonturSimulation Simulation => Session;

	/// <summary>Ядро загрузилось и готово принимать команды.</summary>
	public bool IsReady => Session != null;

	/// <summary>Текст ошибки загрузки контента, если она была.</summary>
	public string LoadError { get; private set; } = string.Empty;

	public override void _Ready()
	{
		try
		{
			// Каталог текстов нужен, чтобы ядро сверило свои id со статьями энциклопедии.
			// Если автозагрузка Content почему-то ещё не поднялась, сверку пропускаем:
			// уронить игру из-за порядка автозагрузок хуже, чем не проверить контент.
			ITextCatalog textCatalog = null;
			if (Content.Instance != null)
			{
				textCatalog = new GodotTextCatalog();
			}
			else
			{
				GD.PushWarning("[KONTUR] Автозагрузка Content не готова — сверка текстовых id пропущена.");
			}

			ContentDatabase content = ContentLoader.Load(new GodotContentSource(ContentRoot), textCatalog);
			Session = new KonturSimulation(content, ResolveSeed());
			Session.SetTimeFreeze("godot.runtime", _isPaused);
		}
		catch (ContentException exception)
		{
			// Ошибка контента не должна ронять игру: узел остаётся инертным,
			// а причина видна в Output и в отладочном оверлее.
			LoadError = exception.Message;
			GD.PushError("[KONTUR] " + exception.Message);
			return;
		}

		if (LogEvents)
		{
			_logSubscription = Session.Events.SubscribeAll(e => GD.Print("[KONTUR] ", e.ToString()));
		}

		GD.Print($"[KONTUR] Ядро готово. Миссий: {Session.Content.Missions.Count}, seed {Session.Seed}.");
	}

	/// <summary>
	/// Зерно партии. Случайное — чтобы порядок миссий, здания вызова и паузы между
	/// звонками не повторялись от запуска к запуску. Зерно печатается в лог: по нему
	/// прогон воспроизводится, если снять RandomizeSeed и вписать число в Seed.
	/// </summary>
	private int ResolveSeed()
	{
		if (!RandomizeSeed)
		{
			return Seed;
		}

		// Ноль ядро принимает, но XorShift от нуля вырождается — берём соседнее число.
		int seed = (int)(GD.Randi() & 0x7FFFFFFF);
		return seed != 0 ? seed : 41;
	}

	public override void _Process(double delta)
	{
		if (Session == null)
		{
			return;
		}

		Session.Tick(delta * TimeScale);
	}

	public override void _ExitTree()
	{
		_logSubscription?.Dispose();
		_logSubscription = null;
	}

	/// <summary>Удобный доступ из любой сцены: GameRuntime.Get(this).Session.</summary>
	public static GameRuntime Get(Node caller)
	{
		return caller.GetNodeOrNull<GameRuntime>("/root/GameRuntime");
	}

	// ------------------------------------------------------------------ сохранения

	public const string SaveFolder = "user://saves/";

	public static string GetSlotPath(string slot)
	{
		return SaveFolder + slot + ".json";
	}

	public bool SaveToSlot(string slot, string label = "")
	{
		if (Session == null)
		{
			GD.PushError("[KONTUR] Сохранять нечего: ядро не загрузилось.");
			return false;
		}

		DirAccess.MakeDirRecursiveAbsolute(SaveFolder);
		string path = GetSlotPath(slot);
		string temporary = path + ".tmp";
		using (FileAccess file = FileAccess.Open(temporary, FileAccess.ModeFlags.Write))
		{
			if (file == null)
			{
				GD.PushError($"[KONTUR] Не удалось открыть на запись {temporary}: {FileAccess.GetOpenError()}");
				return false;
			}

			file.StoreString(Session.Save(label));
		}

		if (FileAccess.FileExists(path))
		{
			DirAccess.RemoveAbsolute(path);
		}

		if (DirAccess.RenameAbsolute(temporary, path) != Error.Ok)
		{
			GD.PushError($"[KONTUR] Не удалось заменить сохранение {path}.");
			return false;
		}

		return true;
	}

	public bool LoadFromSlot(string slot)
	{
		if (Session == null || !FileAccess.FileExists(GetSlotPath(slot)))
		{
			return false;
		}

		using FileAccess file = FileAccess.Open(GetSlotPath(slot), FileAccess.ModeFlags.Read);
		if (file == null)
		{
			return false;
		}

		CommandResult result = Session.Load(file.GetAsText());
		if (!result.IsSuccess)
		{
			GD.PushError("[KONTUR] Загрузка не удалась: " + result.Error);
			return false;
		}

		return true;
	}

	public bool HasSlot(string slot)
	{
		return FileAccess.FileExists(GetSlotPath(slot));
	}
}
