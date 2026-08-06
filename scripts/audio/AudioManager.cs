using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Звук игры. Регистрируется автозагрузкой под именем "AudioManager" — после GameRuntime,
/// чтобы ядро было готово к моменту подписки.
///
/// Слой намеренно односторонний: он только слушает: события ядра через
/// <c>Session.Events</c> и уже существующие публичные состояния сцен
/// (<c>SubViewportInputController.IsActive</c>, <c>InspectableItemController.IsViewActive</c>).
/// Ни один чужой скрипт и ни одна сцена ради звука не правились — поэтому звук можно
/// целиком выключить, убрав одну строку автозагрузки, и игра не заметит.
///
/// Плата за это — звук не позиционный: телефон и рация звучат «в кабинете», а не из своей
/// точки. Когда у предметов появятся собственные сигналы, эти три-четыре подписки
/// переедут в их сцены как AudioStreamPlayer3D.
/// </summary>
public partial class AudioManager : Node
{
	/// <summary>Печатать проигранные звуки в Output. Тот же приём, что GameRuntime.LogEvents.</summary>
	[Export] public bool LogSounds { get; set; } = true;

	/// <summary>
	/// Плеер играет в полную силу: стартовая громкость музыки задана шиной Music
	/// в default_bus_layout.tres (−8 дБ ≈ 40%). Так ползунок в настройках
	/// показывает то же, что слышно, и не спорит с плеером.
	/// </summary>
	[Export] public float MusicVolumeDb { get; set; }

	[Export] public float SfxVolumeDb { get; set; }

	/// <summary>Как часто искать в дереве предметы кабинета. Сцена приходит не сразу — из меню.</summary>
	[Export] public float RescanSeconds { get; set; } = 0.25f;

	/// <summary>Пауза после сборки кабинета, прежде чем заводить фон.</summary>
	[Export] public float OfficeMusicDelaySeconds { get; set; } = 1.0f;

	/// <summary>За сколько секунд фон выходит на полную громкость.</summary>
	[Export] public float MusicFadeInSeconds { get; set; } = 2.0f;

	/// <summary>
	/// На сколько дБ фон главного меню тише игрового. Меню слушают вполуха, пока
	/// решают, продолжать ли партию, и полная громкость там навязчива.
	/// </summary>
	[Export] public float MenuMusicQuieterDb { get; set; } = 6.0f;

	/// <summary>Пауза перед вводом фона в меню: экран открывается из черноты.</summary>
	[Export] public float MenuMusicDelaySeconds { get; set; } = 0.6f;

	public static AudioManager Instance { get; private set; }

	private AudioStreamPlayer _music;
	private AudioStreamPlayer _sfx;

	/// <summary>Второй канал эффектов: нужен, когда два звука звучат разом (блокнот + карандаш).</summary>
	private AudioStreamPlayer _sfxOverlay;

	private AudioStreamPlayer _ring;

	/// <summary>Шум рации: отдельный канал, чтобы его не обрывали звуки предметов.</summary>
	private AudioStreamPlayer _radio;

	private AudioStreamPlayer _computerHum;

	private readonly List<string> _ambientQueue = new();
	private readonly Random _random = new();
	private bool _isShiftMusicActive;
	private bool _isMenuMusicActive;
	private double _menuSettleTimer;

	/// <summary>Играет короткая мелодия перехода; после неё включится длинная версия.</summary>
	private bool _isBetweenShiftsIntro;

	// Наблюдаемые состояния сцен: их нельзя узнать событием, только опросом.
	private SubViewportInputController _computerInput;
	private SubViewportInputController _dossierInput;

	// Луч взгляда игрока и сама рация: нужны, чтобы поймать именно клик по ней.
	private RayCast3D _interactionRay;
	private DeskRadio _deskRadio;
	private RadioDecisionUI _radioDecisionUi;

	/// <summary>Ждала ли рация ответа на прошлом опросе: шум запускается по фронту.</summary>
	private bool _wasRadioWaiting;

	private PageHiringScreen _hiringScreen;
	private bool _wasHiringOpen;
	private MainMenu _mainMenu;
	private InspectableItemController _shiftNote;
	private InspectableItemController _notebook;
	private readonly HashSet<ulong> _wiredButtons = new();
	private readonly HashSet<ulong> _wiredDossierUis = new();

	private bool _wasComputerActive;
	private bool _wasDossierOpen;
	private bool _wasNoteOpen;
	private bool _wasNotebookOpen;
	private bool _hasUnreadScaleChanges;

	private bool _wasPhoneRinging;
	/// <summary>На сколько дБ фон приглушается перед плавным вводом.</summary>
	private const float MusicFadeInDropDb = 24.0f;

	private double _officeSettleTimer;
	private double _rescanTimer;

	private readonly List<IDisposable> _subscriptions = new();

	public override void _Ready()
	{
		Instance = this;

		// Звук живёт и на паузе: иначе, пока открыто меню, нечем остановить звонок.
		ProcessMode = ProcessModeEnum.Always;

		_music = CreatePlayer("Music", "Music", MusicVolumeDb);
		_sfx = CreatePlayer("Sfx", "SFX", SfxVolumeDb);
		_sfxOverlay = CreatePlayer("SfxOverlay", "SFX", SfxVolumeDb);
		_ring = CreatePlayer("Ring", "SFX", SfxVolumeDb);

		// У рации свой канал: иначе взятый в руки предмет обрывал бы её шум.
		_radio = CreatePlayer("Radio", "SFX", SfxVolumeDb);

		_computerHum = CreatePlayer("ComputerHum", "SFX", SfxVolumeDb - 4.0f);

		_music.Finished += OnMusicFinished;
		_ring.Finished += RepeatRingWhileCallsWait;
		_computerHum.Finished += RepeatHumWhileInUse;

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			GD.PushWarning("[AUDIO] Ядро недоступно — звук событий отключён.");
			return;
		}

		IEventBus events = runtime.Session.Events;

		// Музыка
		// Фон заводится не по началу смены, а по готовности кабинета — см. PollOfficeMusic.
		// К моменту, когда игрок жмёт «Начать смену», музыка уже играет.
		Listen(events.Subscribe<ShiftEnded>(_ => StopMusic()));
		// На HiringOpened музыку не заводим: сигнал приходит внутри ShiftEnded, до ролика
		// (см. GameFlow.OnHiringOpened), и тогда она играла бы под текстовый экран.
		// Ждём появления самого экрана найма — см. PollHiringScreen.
		Listen(events.Subscribe<GameOverTriggered>(_ => StopMusic()));

		// Телефон. Сам звонок держится фазой вызова (см. PollPhone), а не счётчиком:
		// счётчик застревал, если вызов закрывался не ответом и не пропуском —
		// например, в конце смены или при новой партии, и аппарат звонил без остановки.
		Listen(events.Subscribe<CallAnswered>(_ => Play(_sfx, Sfx.PhoneTake)));
		Listen(events.Subscribe<BriefingConfirmed>(_ => Play(_sfx, Sfx.PhonePut)));

		// Рация
		Listen(events.Subscribe<RadioTriggered>(_ => Play(_radio, Sfx.Radio)));

		// Шум держится ровно до того, как вызов по рации перестал ждать ответа.
		// Одной видимости меню мало: игрок может ответить, когда экран ещё не собрался,
		// а вызов — просрочиться или закрыться вообще без открытия меню.
		Listen(events.Subscribe<RadioAnswered>(_ => _radio.Stop()));
		Listen(events.Subscribe<RadioMissed>(_ => _radio.Stop()));
		Listen(events.Subscribe<ShiftEnded>(_ => _radio.Stop()));
		// На открытие меню выбора звука нет: щелчок ответа звучит по клику (см. _Input),
		// а choice_ambient тонул в нём — играли одновременно и почти одинаковой длины.
		Listen(events.Subscribe<RadioOptionChosen>(_ =>
		{
			_radio.Stop();
			Play(_sfx, Sfx.ChoicePress);
		}));

		// Признак для скрипа карандаша: шкалы менялись с прошлого открытия блокнота.
		Listen(events.Subscribe<ScalesChanged>(_ => _hasUnreadScaleChanges = true));

		GD.Print("[AUDIO] Звук подключён к ядру.");
	}

	public override void _ExitTree()
	{
		for (int i = 0; i < _subscriptions.Count; i++)
		{
			_subscriptions[i]?.Dispose();
		}

		_subscriptions.Clear();

		if (Instance == this)
		{
			Instance = null;
		}
	}

	public override void _Process(double delta)
	{
		_rescanTimer -= delta;
		if (_rescanTimer <= 0.0)
		{
			_rescanTimer = RescanSeconds;
			RescanOfficeNodes();
		}

		PollOfficeSilence();
		PollPhone();
		PollModalAudioPause();
		PollMainMenuMusic(delta);
		PollOfficeMusic(delta);
		PollHiringScreen();
		PollComputer();
		PollDossier();
		PollInspectableItem(_shiftNote, ref _wasNoteOpen, Sfx.NoteTake, Sfx.NotePut, false);
		PollInspectableItem(_notebook, ref _wasNotebookOpen, Sfx.NotepadTake, string.Empty, true);
	}

	/// <summary>Кабинет собран. Телефон, рация и гул монитора звучат только в нём.</summary>
	private bool IsOfficeLoaded => IsValid(_deskRadio);

	/// <summary>
	/// Кабинет исчез — вместе с ним замолкают его звуки.
	///
	/// Телефон и рация держатся не сценой, а состоянием ядра, а оно смену сцены
	/// переживает: партия остаётся жива, когда игрок уходит в меню или на ролик.
	/// Без этой проверки звонок и шум рации тянулись бы поверх главного меню —
	/// вызов-то в ядре всё ещё ждёт ответа.
	/// </summary>
	private void PollOfficeSilence()
	{
		if (IsOfficeLoaded)
		{
			return;
		}

		_ring.Stop();
		_radio.Stop();
		_computerHum.Stop();

		// Признаки сбрасываем тоже: иначе по возвращении в кабинет опрос решит,
		// что звонок уже звучит, и не заведёт его заново.
		_wasPhoneRinging = false;
		_wasRadioWaiting = false;
	}

	/// <summary>
	/// Клик по рации ловится здесь, а не по событию ядра: игрок помечает нажатие
	/// обработанным, а _Input идёт раньше, поэтому щелчок звучит ровно в момент клика,
	/// не дожидаясь, пока соберётся меню выбора.
	/// </summary>
	public override void _Input(InputEvent @event)
	{
		if (@event.IsActionPressed("interact") && IsAimingAtActiveRadio())
		{
			Play(_sfx, PickRandom(Sfx.RadioAnswer));
		}
	}

	/// <summary>Короткий звук без позиции: щелчки, интерфейс. Доступен и другим сценам.</summary>
	public void PlayUi(string path)
	{
		Play(_sfx, path);
	}

	// --- Предметы кабинета: состояние читается опросом, потому что своих сигналов у них нет ---

	/// <summary>
	/// Кабинет приходит не сразу (игра стартует с главного меню), а при выходе в меню
	/// узлы исчезают. Поэтому ссылки перепроверяются, а не берутся один раз.
	/// </summary>
	private void RescanOfficeNodes()
	{
		bool needComputer = !IsValid(_computerInput);
		bool needDossier = !IsValid(_dossierInput);
		bool needNote = !IsValid(_shiftNote);
		bool needNotebook = !IsValid(_notebook);
		bool needRadio = !IsValid(_deskRadio);
		bool needRay = !IsValid(_interactionRay);
		bool needRadioUi = !IsValid(_radioDecisionUi);
		bool needHiring = !IsValid(_hiringScreen);
		bool needMenu = !IsValid(_mainMenu);

		if (needComputer || needDossier || needNote || needNotebook || needRadio || needRay
			|| needRadioUi || needHiring || needMenu)
		{
			_radioDecisionUi = needRadioUi ? null : _radioDecisionUi;
			_hiringScreen = needHiring ? null : _hiringScreen;
			_mainMenu = needMenu ? null : _mainMenu;
			_computerInput = needComputer ? null : _computerInput;
			_dossierInput = needDossier ? null : _dossierInput;
			_shiftNote = needNote ? null : _shiftNote;
			_notebook = needNotebook ? null : _notebook;
			_deskRadio = needRadio ? null : _deskRadio;
			_interactionRay = needRay ? null : _interactionRay;
			ScanTree(GetTree().Root);
		}

		WireComputerButtons();
	}

	private void ScanTree(Node node)
	{
		if (node is SubViewportInputController controller)
		{
			// У компьютера и у папки свои контроллеры ввода, различаются по имени узла.
			string controllerName = controller.Name.ToString();
			if (_computerInput == null && controllerName == "ViewportInput"
				&& FindOwnerName(controller).Contains("Computer", StringComparison.OrdinalIgnoreCase))
			{
				_computerInput = controller;
				LogFound("экран компьютера", controller);
			}
			else if (_dossierInput == null && controllerName == "DossierViewportInput"
				&& FindOwnerName(controller).Contains("Dossier", StringComparison.OrdinalIgnoreCase))
			{
				// Такой же узел есть и у компьютера — там это режим досье на экране.
				// Нужен тот, что принадлежит папке на столе (EmployeeDossierFolder).
				_dossierInput = controller;
				LogFound("папка на столе", controller);
			}
		}
		else if (node is DossierDispatchUI dossierUi && _wiredDossierUis.Add(dossierUi.GetInstanceId()))
		{
			WireDossierUi(dossierUi);
		}
		else if (node is DeskRadio radio && _deskRadio == null)
		{
			_deskRadio = radio;
			LogFound("рация", radio);
		}
		else if (node is RadioDecisionUI decisionUi && _radioDecisionUi == null)
		{
			_radioDecisionUi = decisionUi;
			LogFound("меню рации", decisionUi);
		}
		else if (node is PageHiringScreen hiring && _hiringScreen == null)
		{
			_hiringScreen = hiring;
			LogFound("экран найма", hiring);
		}
		else if (node is MainMenu menu && _mainMenu == null)
		{
			_mainMenu = menu;
			LogFound("главное меню", menu);
		}
		else if (node is RayCast3D ray && _interactionRay == null && ray.Name.ToString() == "InteractionRay")
		{
			_interactionRay = ray;
			LogFound("луч взаимодействия", ray);
		}
		else if (node is InspectableItemController item)
		{
			// Контроллер висит на корне сцены предмета, поэтому различаем по его же имени.
			string name = item.Name.ToString();
			if (_shiftNote == null && name.Contains("ShiftNote", StringComparison.OrdinalIgnoreCase))
			{
				_shiftNote = item;
				LogFound("записка сменщика", item);
			}
			else if (_notebook == null && name.Contains("Notebook", StringComparison.OrdinalIgnoreCase))
			{
				_notebook = item;
				LogFound("блокнот", item);
			}
		}

		foreach (Node child in node.GetChildren())
		{
			ScanTree(child);
		}
	}

	/// <summary>
	/// Разворот папки сам сообщает о нажатии на фотографию и о перелистывании —
	/// подписываемся на его публичные события, ничего в нём не меняя.
	/// </summary>
	private void WireDossierUi(DossierDispatchUI dossierUi)
	{
		dossierUi.EmployeeChosen += OnEmployeePortraitPressed;
		dossierUi.PreviousPageRequested += OnDossierPageTurned;
		dossierUi.NextPageRequested += OnDossierPageTurned;
		LogFound("разворот папки", dossierUi);
	}

	/// <summary>Нажали на фотографию — на экране «печатается» фамилия.</summary>
	private void OnEmployeePortraitPressed()
	{
		Play(_sfx, PickRandom(Sfx.KeyboardTyping));
	}

	private void OnDossierPageTurned()
	{
		Play(_sfx, Sfx.DocumentTurnPage);
	}

	/// <summary>
	/// Игрок целится в рацию, и та ждёт ответа. Наведение берём из его же луча
	/// взаимодействия — тот, что подсвечивает предметы.
	/// </summary>
	private bool IsAimingAtActiveRadio()
	{
		if (!IsValid(_interactionRay) || !IsValid(_deskRadio) || !_deskRadio.IsActive)
		{
			return false;
		}

		// Меню уже открыто: повторный клик по рации ничего не делает, щёлкать не о чем.
		if (IsValid(_radioDecisionUi) && _radioDecisionUi.Visible)
		{
			return false;
		}

		if (_interactionRay.GetCollider() is not Node collider)
		{
			return false;
		}

		for (Node node = collider; node != null; node = node.GetParent())
		{
			if (node is DeskRadioInteractable)
			{
				return true;
			}
		}

		return false;
	}

	/// <summary>
	/// Музыка перехода включается, только когда на экране действительно найм.
	/// Ролик с текстом идёт раньше и должен остаться тихим.
	/// </summary>
	private void PollHiringScreen()
	{
		bool isOpen = IsValid(_hiringScreen);
		if (isOpen == _wasHiringOpen)
		{
			return;
		}

		_wasHiringOpen = isOpen;
		if (isOpen)
		{
			StartBetweenShiftsMusic(GetHiringDay());
		}
		else
		{
			StopMusic();
		}
	}

	/// <summary>День берётся у GameFlow: он же решает, на какой день идёт набор.</summary>
	private int GetHiringDay()
	{
		Node flow = GetNodeOrNull("/root/GameFlow");
		return flow is GameFlow gameFlow ? gameFlow.HiringDay : 0;
	}

	/// <summary>
	/// Фон главного меню. Меню ищется в дереве опросом, как и остальные экраны:
	/// сам MainMenu про звук не знает и ничего сюда не зовёт — весь слой остаётся
	/// односторонним и снимается одной строкой автозагрузки.
	/// </summary>
	private void PollMainMenuMusic(double delta)
	{
		if (!IsValid(_mainMenu))
		{
			_menuSettleTimer = 0.0;
			if (_isMenuMusicActive)
			{
				// Ушли в игру или на вступительный ролик: под ролик музыка не нужна.
				StopMusic();
			}

			return;
		}

		if (_isMenuMusicActive)
		{
			FadeInMusic(delta, MusicVolumeDb - MenuMusicQuieterDb);
			return;
		}

		// Экран открывается из черноты — трогать звук раньше, чем появилась картинка, рано.
		_menuSettleTimer += delta;
		if (_menuSettleTimer < MenuMusicDelaySeconds)
		{
			return;
		}

		StartMenuMusic();
	}

	/// <summary>
	/// Фон кабинета включается ещё до кнопки «Начать смену», но не в момент подмены
	/// сцены: там кадр занят сборкой кабинета, и музыка успела бы зазвучать раньше
	/// картинки. Поэтому ждём, пока кабинет соберётся и игра снимет паузу, выдерживаем
	/// небольшую задержку и вводим звук плавно.
	/// </summary>
	private void PollOfficeMusic(double delta)
	{
		// Кабинета нет вовсе — вышли в меню или ушли на ролик: фон гасим.
		if (!IsValid(_computerInput))
		{
			_officeSettleTimer = 0.0;
			if (_isShiftMusicActive)
			{
				StopMusic();
			}

			return;
		}

		// Кабинет на месте, но игра стоит на паузе — просто ждём, ничего не трогая.
		if (GetTree().Paused || IsPauseMenuOpen())
		{
			return;
		}

		if (_isShiftMusicActive)
		{
			FadeInMusic(delta, MusicVolumeDb);
			return;
		}

		// Между сменами играет своя музыка — её не перебиваем.
		if (_isBetweenShiftsIntro || IsValid(_hiringScreen))
		{
			return;
		}

		_officeSettleTimer += delta;
		if (_officeSettleTimer < OfficeMusicDelaySeconds)
		{
			return;
		}

		StartShiftMusic();
	}

	/// <summary>
	/// Плавный ввод: резкий старт трека читается как сбой. Потолок передаётся
	/// снаружи — у меню он ниже игрового на <see cref="MenuMusicQuieterDb"/>.
	/// </summary>
	private void FadeInMusic(double delta, float targetDb)
	{
		if (_music.VolumeDb >= targetDb)
		{
			return;
		}

		float step = (float)(delta * (MusicFadeInSeconds > 0.0f ? 60.0f / MusicFadeInSeconds : 60.0f));
		_music.VolumeDb = Mathf.Min(targetDb, _music.VolumeDb + step);
	}

	/// <summary>
	/// Экран рации и меню паузы глушат телефон: разговор идёт «поверх» звонка,
	/// а время в игре в этот момент стоит. Экран компьютера — намеренное исключение:
	/// таймер звонка там тоже заморожен, но сам звонок продолжает звучать,
	/// чтобы игрок помнил про снятую трубку.
	/// </summary>
	private void PollModalAudioPause()
	{
		bool radioOpen = IsValid(_radioDecisionUi) && _radioDecisionUi.Visible;
		bool menuOpen = IsPauseMenuOpen();

		_ring.StreamPaused = radioOpen || menuOpen;
		_radio.StreamPaused = menuOpen;
		_computerHum.StreamPaused = menuOpen;

		// Игрок взял рацию — шум сменился разговором, тянуть его дальше незачем.
		if (radioOpen && _radio.Playing)
		{
			_radio.Stop();
		}

		PollRadioQueue(radioOpen || menuOpen);
	}

	/// <summary>
	/// Рация зовёт снова, когда экран закрылся, а в очереди осталось чужое обращение —
	/// например, отчёт по миссии, прошедшей без выбора.
	///
	/// Считаем по фронту, а не по факту «очередь не пуста»: файл шума не зациклен,
	/// и повтор при каждом опросе превратил бы отдельный сигнал в непрерывное гудение.
	/// Проверка Playing попутно гасит второй запуск сразу после RadioTriggered —
	/// там шум уже пошёл по подписке.
	/// </summary>
	private void PollRadioQueue(bool screenBusy)
	{
		bool waiting = !screenBusy && IsValid(_deskRadio) && _deskRadio.IsActive;

		if (waiting && !_wasRadioWaiting && !_radio.Playing)
		{
			Play(_radio, Sfx.Radio);
		}

		_wasRadioWaiting = waiting;
	}

	private bool IsPauseMenuOpen()
	{
		return GetNodeOrNull("/root/Pause") is PauseMenu pause && pause.IsOpen;
	}

	private void PollDossier()
	{
		bool isOpen = IsValid(_dossierInput) && _dossierInput.IsActive;
		if (isOpen == _wasDossierOpen)
		{
			return;
		}

		_wasDossierOpen = isOpen;
		Play(_sfx, isOpen ? Sfx.DocumentOpen : Sfx.DocumentDrop);
	}

	private static string FindOwnerName(Node node)
	{
		Node parent = node.GetParent();
		return parent == null ? string.Empty : parent.Name.ToString();
	}

	private void LogFound(string what, Node node)
	{
		if (LogSounds)
		{
			GD.Print($"[AUDIO] найдено — {what}: {node.GetPath()}");
		}
	}

	private void PollComputer()
	{
		bool isActive = IsValid(_computerInput) && _computerInput.IsActive;
		if (isActive == _wasComputerActive)
		{
			return;
		}

		_wasComputerActive = isActive;
		if (isActive)
		{
			Play(_computerHum, Sfx.ComputerWorking);
		}
		else
		{
			_computerHum.Stop();
		}
	}

	private void PollInspectableItem(
		InspectableItemController item,
		ref bool wasOpen,
		string takeSound,
		string putSound,
		bool isNotebook)
	{
		bool isOpen = IsValid(item) && item.IsViewActive;
		if (isOpen == wasOpen)
		{
			return;
		}

		wasOpen = isOpen;
		if (!isOpen)
		{
			if (!string.IsNullOrEmpty(putSound))
			{
				Play(_sfx, putSound);
			}

			return;
		}

		Play(_sfx, takeSound);

		// Карандаш скрипит только если с прошлого открытия блокнота шкалы менялись.
		if (isNotebook && _hasUnreadScaleChanges)
		{
			_hasUnreadScaleChanges = false;
			Play(_sfxOverlay, Sfx.PencilWrite);
		}
	}

	/// <summary>
	/// Щелчок клавиши вешается на кнопки экрана компьютера. Экраны собираются в рантайме,
	/// поэтому список досматривается периодически, а уже подключённые помнятся по id.
	/// </summary>
	private void WireComputerButtons()
	{
		if (!IsValid(_computerInput))
		{
			return;
		}

		Node screen = _computerInput.GetParent();
		if (screen != null)
		{
			WireButtons(screen);
		}
	}

	private void WireButtons(Node node)
	{
		if (node is BaseButton button && _wiredButtons.Add(button.GetInstanceId()))
		{
			button.Pressed += OnComputerButtonPressed;
		}

		foreach (Node child in node.GetChildren())
		{
			WireButtons(child);
		}
	}

	private void OnComputerButtonPressed()
	{
		Play(_sfx, Sfx.KeyboardEnter);
	}

	// --- Телефон ---

	/// <summary>
	/// Аппарат звонит ровно пока у ядра есть вызов в фазе Ringing. Состояние спрашиваем,
	/// а не копим: вызовы накладываются, и любой нештатный конец вызова оставлял бы
	/// счётчик ненулевым — тогда звонок не смолкал.
	/// </summary>
	private void PollPhone()
	{
		bool anyRinging = false;
		GameRuntime runtime = GameRuntime.Get(this);
		if (IsOfficeLoaded && runtime != null && runtime.IsReady)
		{
			IReadOnlyList<IncidentView> incidents = runtime.Session.GetActiveIncidents();
			for (int i = 0; i < incidents.Count; i++)
			{
				if (incidents[i].Phase == IncidentPhase.Ringing)
				{
					anyRinging = true;
					break;
				}
			}
		}

		if (anyRinging == _wasPhoneRinging)
		{
			return;
		}

		_wasPhoneRinging = anyRinging;
		if (anyRinging)
		{
			Play(_ring, Sfx.PhoneRing);
		}
		else
		{
			_ring.Stop();
		}
	}

	/// <summary>Зацикливание вручную: не зависим от галочки Loop в .import.</summary>
	private void RepeatRingWhileCallsWait()
	{
		if (_wasPhoneRinging)
		{
			_ring.Play();
		}
	}

	private void RepeatHumWhileInUse()
	{
		if (_wasComputerActive)
		{
			_computerHum.Play();
		}
	}

	// --- Музыка ---

	private void StartShiftMusic()
	{
		_isShiftMusicActive = true;
		_isMenuMusicActive = false;
		_ambientQueue.Clear();

		// Стартуем с тишины — громкость доводит FadeInMusic.
		_music.VolumeDb = MusicVolumeDb - MusicFadeInDropDb;
		PlayNextAmbient();
	}

	/// <summary>
	/// Тихий фон главного меню. Отдельный флаг, а не игровой: у него свой потолок
	/// громкости, и по концу трека он должен закольцеваться, а не тянуть очередь смены.
	/// </summary>
	private void StartMenuMusic()
	{
		_isMenuMusicActive = true;
		_isShiftMusicActive = false;
		_ambientQueue.Clear();

		// Стартуем с тишины — громкость доводит FadeInMusic.
		_music.VolumeDb = MusicVolumeDb - MenuMusicQuieterDb - MusicFadeInDropDb;
		PlayMusic(Sfx.MainMenu);
	}

	private void StopMusic()
	{
		_isShiftMusicActive = false;
		_isBetweenShiftsIntro = false;
		_isMenuMusicActive = false;
		_music.Stop();
	}

	/// <summary>
	/// У каждого перехода между сменами своя мелодия. Доиграла, а игрок всё ещё
	/// набирает людей — дальше идёт длинная версия по кругу.
	/// </summary>
	private void StartBetweenShiftsMusic(int nextDay)
	{
		_isShiftMusicActive = false;
		_isMenuMusicActive = false;
		string intro = Sfx.BetweenShiftsFor(nextDay);
		_isBetweenShiftsIntro = intro != Sfx.BetweenShiftsFull;
		PlayMusic(intro);
	}

	/// <summary>
	/// Перемешанная очередь: пока не сыграли все четыре трека, повторов нет.
	/// Опустела — тасуем заново.
	/// </summary>
	private void PlayNextAmbient()
	{
		if (_ambientQueue.Count == 0)
		{
			RefillAmbientQueue();
		}

		if (_ambientQueue.Count == 0)
		{
			return;
		}

		string path = _ambientQueue[0];
		_ambientQueue.RemoveAt(0);
		PlayMusic(path);
	}

	private void RefillAmbientQueue()
	{
		string lastPlayed = _music.Stream?.ResourcePath ?? string.Empty;

		_ambientQueue.AddRange(Sfx.ShiftAmbient);
		for (int i = _ambientQueue.Count - 1; i > 0; i--)
		{
			int j = _random.Next(i + 1);
			(_ambientQueue[i], _ambientQueue[j]) = (_ambientQueue[j], _ambientQueue[i]);
		}

		// Чтобы на стыке кругов один трек не сыграл два раза подряд.
		if (_ambientQueue.Count > 1 && _ambientQueue[0] == lastPlayed)
		{
			(_ambientQueue[0], _ambientQueue[1]) = (_ambientQueue[1], _ambientQueue[0]);
		}
	}

	private void PlayMusic(string path)
	{
		Play(_music, path);
	}

	private void OnMusicFinished()
	{
		if (_isShiftMusicActive)
		{
			PlayNextAmbient();
			return;
		}

		if (_isBetweenShiftsIntro)
		{
			_isBetweenShiftsIntro = false;
			PlayMusic(Sfx.BetweenShiftsFull);
			return;
		}

		// Длинная версия крутится по кругу, пока состояние не сменится.
		if (_music.Stream != null)
		{
			_music.Play();
		}
	}

	// --- Общее ---

	private void Play(AudioStreamPlayer player, string path)
	{
		AudioStream stream = Load(path);
		if (stream == null)
		{
			return;
		}

		player.Stream = stream;
		player.Play();

		if (LogSounds)
		{
			GD.Print("[AUDIO] ", path.GetFile());
		}
	}

	/// <summary>Загрузка с понятной ошибкой: опечатку в пути видно сразу, а не тишиной.</summary>
	public static AudioStream Load(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return null;
		}

		if (!ResourceLoader.Exists(path))
		{
			GD.PushError($"[AUDIO] Файл не найден: {path}");
			return null;
		}

		return GD.Load<AudioStream>(path);
	}

	public string PickRandom(string[] paths)
	{
		return paths.Length == 0 ? string.Empty : paths[_random.Next(paths.Length)];
	}

	private AudioStreamPlayer CreatePlayer(string name, string bus, float volumeDb)
	{
		var player = new AudioStreamPlayer { Name = name, Bus = bus, VolumeDb = volumeDb };
		AddChild(player);
		return player;
	}

	private void Listen(IDisposable subscription)
	{
		_subscriptions.Add(subscription);
	}

	private static bool IsValid(Node node)
	{
		return node != null && IsInstanceValid(node) && !node.IsQueuedForDeletion();
	}
}
