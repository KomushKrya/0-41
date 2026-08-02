using System;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Content;

/// <summary>
/// Мост между симуляционным ядром и движком. Регистрируется автозагрузкой под именем "Kontur".
///
/// Единственное место, где Godot встречается с ядром. Сцены и предметы на столе
/// подписываются на <c>Simulation.Events</c> и отдают команды через <c>Simulation</c>;
/// напрямую в системы ядра никто не обращается.
///
/// Узел инертен, пока не начата смена: контент загружается в _Ready, но Tick
/// ничего не делает, пока StartShift не вызван. Поэтому автозагрузка безопасна
/// для существующих сцен.
/// </summary>
public partial class KonturRuntime : Node
{
	[Export] public string ContentRoot { get; set; } = "res://data/";

	[Export] public int Seed { get; set; } = 41;

	/// <summary>Пауза симуляции — для меню, роликов и отладки.</summary>
	[Export] public bool IsPaused { get; set; }

	/// <summary>Ускорение прогона. 1 — реальное время, 10 — смена за 30 секунд.</summary>
	[Export] public float TimeScale { get; set; } = 1.0f;

	/// <summary>Печатать все сигналы ядра в Output. Полезно на этапе отладки.</summary>
	[Export] public bool LogEvents { get; set; } = true;

	private IDisposable _logSubscription;

	/// <summary>Ядро. Null, если контент не загрузился — проверяйте IsReady.</summary>
	public KonturSimulation Simulation { get; private set; }

	/// <summary>Ядро загрузилось и готово принимать команды.</summary>
	public bool IsReady => Simulation != null;

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
			Simulation = new KonturSimulation(content, Seed);
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
			_logSubscription = Simulation.Events.SubscribeAll(e => GD.Print("[KONTUR] ", e.ToString()));
		}

		GD.Print($"[KONTUR] Ядро готово. Миссий: {Simulation.Content.Missions.Count}, seed {Seed}.");
	}

	public override void _Process(double delta)
	{
		if (Simulation == null || IsPaused)
		{
			return;
		}

		Simulation.Tick(delta * TimeScale);
	}

	public override void _ExitTree()
	{
		_logSubscription?.Dispose();
		_logSubscription = null;
	}

	/// <summary>Удобный доступ из любой сцены: Kontur.Get(this).Simulation.</summary>
	public static KonturRuntime Get(Node caller)
	{
		return caller.GetNodeOrNull<KonturRuntime>("/root/Kontur");
	}

	// ------------------------------------------------------------------ сохранения

	/// <summary>
	/// Папка сохранений. user:// — это AppData на Windows и ~/.local/share на Linux;
	/// класть их в res:// нельзя, в собранной игре она доступна только на чтение.
	/// </summary>
	public const string SaveFolder = "user://saves/";

	public static string GetSlotPath(string slot)
	{
		return SaveFolder + slot + ".json";
	}

	/// <summary>
	/// Записывает партию в слот. Работает и посреди смены.
	///
	/// Пишет через временный файл: если игру закроют ровно в момент записи,
	/// прежнее сохранение останется целым, а не превратится в обрезанный JSON.
	/// </summary>
	public bool SaveToSlot(string slot, string label = "")
	{
		if (Simulation == null)
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

			file.StoreString(Simulation.Save(label));
		}

		// DirAccess понимает пути вида user://, преобразовывать их не нужно.
		if (FileAccess.FileExists(path))
		{
			DirAccess.RemoveAbsolute(path);
		}

		Error renamed = DirAccess.RenameAbsolute(temporary, path);
		if (renamed != Error.Ok)
		{
			GD.PushError($"[KONTUR] Не удалось заменить сохранение {path}: {renamed}");
			return false;
		}

		GD.Print($"[KONTUR] Сохранено: {path}");
		return true;
	}

	/// <summary>
	/// Читает партию из слота.
	///
	/// После успешной загрузки время стоит: ядро ждёт, пока интерфейс перерисуется
	/// по снимкам Get*, и только потом надо вызвать Simulation.ResumeAfterLoad().
	/// </summary>
	public bool LoadFromSlot(string slot)
	{
		if (Simulation == null)
		{
			GD.PushError("[KONTUR] Загружать некуда: ядро не загрузилось.");
			return false;
		}

		string path = GetSlotPath(slot);
		if (!FileAccess.FileExists(path))
		{
			GD.PushWarning($"[KONTUR] Сохранения нет: {path}");
			return false;
		}

		string json;
		using (FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read))
		{
			if (file == null)
			{
				GD.PushError($"[KONTUR] Не удалось открыть {path}: {FileAccess.GetOpenError()}");
				return false;
			}

			json = file.GetAsText();
		}

		Kontur.Core.Api.CommandResult result = Simulation.Load(json);
		if (!result.IsSuccess)
		{
			GD.PushError("[KONTUR] Загрузка не удалась: " + result.Error);
			return false;
		}

		GD.Print($"[KONTUR] Загружено: {path}");
		return true;
	}

	public bool HasSlot(string slot)
	{
		return FileAccess.FileExists(GetSlotPath(slot));
	}
}
