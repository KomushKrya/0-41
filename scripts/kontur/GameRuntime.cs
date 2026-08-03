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

	[Export] public int Seed { get; set; } = 41;

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
			Session = new KonturSimulation(content, Seed);
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

		GD.Print($"[KONTUR] Ядро готово. Миссий: {Session.Content.Missions.Count}, seed {Seed}.");
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
}
