using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;

/// <summary>
/// Порядок экранов: меню, стартовый выбор, ролик, смена, найм, снова смена.
///
/// Один узел на всю игру, автозагрузкой. Причина простая: переходы между экранами
/// нужно решать в одном месте, иначе каждая сцена начинает знать про следующую,
/// и «после смены показать ролик» оказывается размазанным по трём файлам.
///
/// Состояние партии живёт в ядре и переживает смену сцен — здесь только навигация.
/// </summary>
public partial class GameFlow : Node
{
	/// <summary>Слот быстрого сохранения. Из него грузит кнопка «Продолжить».</summary>
	public const string QuickSlot = "slot1";

	private const string MainMenuScene = "res://scenes/ui/menu/MainMenu.tscn";
	private const string CutsceneScene = "res://scenes/ui/cutscene/CutscenePlayer.tscn";
	private const string HiringScene = "res://scenes/ui/hiring/HiringScreen.tscn";
	private const string OfficeScene = "res://scenes/main.tscn";

	public static GameFlow Instance { get; private set; }

	private GameRuntime _kontur;
	private System.IDisposable _shiftEnded;
	private System.IDisposable _hiringOpened;

	/// <summary>Куда идти после текущего ролика. Пусто — в главное меню.</summary>
	private string _afterCutscene = string.Empty;

	/// <summary>Ролик, который проигрывается сейчас. Читает CutscenePlayer при старте.</summary>
	public string PendingCutsceneId { get; private set; } = string.Empty;

	/// <summary>День, на который набирают людей. Читает HiringScreen.</summary>
	public int HiringDay { get; private set; } = 1;

	/// <summary>Экран найма открыт для стартового выбора, а не для добора между сменами.</summary>
	public bool HiringIsStartingChoice { get; private set; }

	public override void _Ready()
	{
		Instance = this;

		_kontur = GameRuntime.Get(this);
		if (_kontur == null || !_kontur.IsReady)
		{
			GD.PushError("[FLOW] Ядро недоступно: " + (_kontur?.LoadError ?? "автозагрузка Kontur не найдена"));
			return;
		}

		// Смена кончилась — ролик, потом найм. Порядок задаётся здесь и только здесь.
		_shiftEnded = _kontur.Simulation.Events.Subscribe<ShiftEnded>(OnShiftEnded);
		_hiringOpened = _kontur.Simulation.Events.Subscribe<HiringOpened>(OnHiringOpened);
	}

	public override void _ExitTree()
	{
		_shiftEnded?.Dispose();
		_hiringOpened?.Dispose();

		if (Instance == this)
		{
			Instance = null;
		}
	}

	// ------------------------------------------------------------------ переходы

	public void ShowMainMenu()
	{
		Pause(true);
		GetTree().ChangeSceneToFile(MainMenuScene);
	}

	/// <summary>
	/// Новая партия. Если контент задал пул стартового выбора — сначала он,
	/// иначе сразу первая смена: состав тогда прописан в startingRoster.
	/// </summary>
	public void StartNewGame()
	{
		if (!HasCore())
		{
			return;
		}

		_kontur.Simulation.ResetToNewGame();

		if (_kontur.Simulation.NeedsStartingChoice)
		{
			ShowHiring(1, isStartingChoice: true);
			return;
		}

		BeginShift(1);
	}

	/// <summary>Продолжить из быстрого слота. Ложь — сохранения нет или оно негодное.</summary>
	public bool ContinueGame()
	{
		if (!HasCore() || !_kontur.HasSlot(QuickSlot))
		{
			return false;
		}

		if (!_kontur.LoadFromSlot(QuickSlot))
		{
			return false;
		}

		// Ядро после загрузки держит время остановленным, пока интерфейс не готов.
		// Отпускаем его уже в кабинете, когда сцена собралась.
		GoToOffice();
		return true;
	}

	/// <summary>
	/// Проигрывает ролик, затем переходит к указанной сцене.
	/// Пустой id ролика — сразу к следующей сцене, без пустого экрана.
	/// </summary>
	public void PlayCutscene(string cutsceneId, string nextScene)
	{
		if (string.IsNullOrEmpty(cutsceneId))
		{
			GoTo(nextScene);
			return;
		}

		PendingCutsceneId = cutsceneId;
		_afterCutscene = nextScene;

		Pause(true);
		GetTree().ChangeSceneToFile(CutsceneScene);
	}

	/// <summary>Зовёт CutscenePlayer, когда ролик кончился или его пропустили.</summary>
	public void OnCutsceneFinished()
	{
		PendingCutsceneId = string.Empty;

		string next = _afterCutscene;
		_afterCutscene = string.Empty;

		GoTo(string.IsNullOrEmpty(next) ? MainMenuScene : next);
	}

	public void ShowHiring(int day, bool isStartingChoice)
	{
		HiringDay = day;
		HiringIsStartingChoice = isStartingChoice;

		Pause(true);
		GetTree().ChangeSceneToFile(HiringScene);
	}

	/// <summary>Зовёт HiringScreen, когда игрок закончил с набором.</summary>
	public void OnHiringFinished(int nextDay)
	{
		BeginShift(nextDay);
	}

	/// <summary>Начинает смену и уводит игрока в кабинет.</summary>
	public void BeginShift(int day)
	{
		if (!HasCore())
		{
			return;
		}

		CommandResult result = _kontur.Simulation.StartShift(day);
		if (!result.IsSuccess)
		{
			GD.PushError("[FLOW] Смена не началась: " + result.Error);
			ShowMainMenu();
			return;
		}

		GoToOffice();
	}

	private void GoToOffice()
	{
		GetTree().ChangeSceneToFile(OfficeScene);

		// Пауза снимается в конце кадра: сцена кабинета должна успеть собраться,
		// иначе первый Tick пройдёт по ещё не подписавшимся предметам на столе.
		CallDeferred(nameof(ResumeAfterSceneReady));
	}

	private void ResumeAfterSceneReady()
	{
		Pause(false);

		// Если пришли из загрузки, ядро всё ещё держит время — отпускаем.
		if (HasCore() && _kontur.Simulation.IsTimeFrozen)
		{
			_kontur.Simulation.ResumeAfterLoad();
		}
	}

	private void GoTo(string scenePath)
	{
		if (scenePath == OfficeScene)
		{
			GoToOffice();
			return;
		}

		Pause(true);
		GetTree().ChangeSceneToFile(scenePath);
	}

	// ------------------------------------------------------------------ реакции ядра

	private void OnShiftEnded(ShiftEnded e)
	{
		// Ролик, а после него — найм. Если брать некого, HiringOpened не придёт,
		// и найм сам решит начать следующую смену.
		PlayCutscene(e.OutroCutsceneId, HiringScene);

		HiringDay = e.Day + 1;
		HiringIsStartingChoice = false;
	}

	private void OnHiringOpened(HiringOpened e)
	{
		// Сигнал приходит внутри ShiftEnded, до перехода. Запоминаем день,
		// а показывать будем после ролика.
		HiringDay = e.NextDay;
		HiringIsStartingChoice = false;
	}

	// ------------------------------------------------------------------ мелочи

	private void Pause(bool paused)
	{
		if (_kontur != null)
		{
			_kontur.IsPaused = paused;
		}
	}

	private bool HasCore()
	{
		if (_kontur != null && _kontur.IsReady)
		{
			return true;
		}

		GD.PushError("[FLOW] Ядро недоступно.");
		return false;
	}

	public KonturSimulation Simulation
	{
		get { return _kontur != null ? _kontur.Simulation : null; }
	}

	public GameRuntime Runtime
	{
		get { return _kontur; }
	}
}
