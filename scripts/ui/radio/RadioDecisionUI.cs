using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;

/// <summary>
/// Полноэкранный экран радио-решения. Чёрно-белое переходное видео управляет
/// прозрачностью всей сцены: чёрное оставляет прошлый экран, белое открывает UI.
///
/// Размер ScreenContent приходится выставлять руками. Якоря Control считаются
/// от прямоугольника родителя, а родитель здесь — CanvasGroup, то есть Node2D:
/// прямоугольника у него нет, и «растянуть на весь экран» якорями не выйдет —
/// получится 0x0, и всё содержимое схлопнется в начало координат.
///
/// Поэтому размер живёт в двух местах. В сцене он записан как 1280x720, чтобы
/// экран было видно и можно было править в редакторе; в игре его переписывает
/// FitScreenContentToWindow под фактический вьюпорт.
/// </summary>
public partial class RadioDecisionUI : Control
{
	[Export] public NodePath TransitionPlayerPath { get; set; } = new("TransitionPlayer");
	[Export] public NodePath TransitionGroupPath { get; set; } = new("TransitionGroup");
	[Export] public NodePath ScreenContentPath { get; set; } = new("TransitionGroup/ScreenContent");
	[Export] public NodePath PreviousScreenBlurPath { get; set; } = new("PreviousScreenBlur");
	[Export] public NodePath InputBlockerPath { get; set; } = new("InputBlocker");
	[Export] public NodePath ScreenOverlayPath { get; set; } = new("ScreenOverlay");
	[Export] public NodePath HeaderPath { get; set; } = new("ScreenOverlay/Header");
	[Export] public NodePath SituationLabelPath { get; set; } = new("ScreenOverlay/SituationLabel");
	[Export] public NodePath IllustrationPath { get; set; } = new("TransitionGroup/ScreenContent/Illustration");
	[Export] public NodePath StatusLinePath { get; set; } = new("ScreenOverlay/StatusLine");
	[Export] public NodePath OptionOneButtonPath { get; set; } = new("ScreenOverlay/OptionOneButton");
	[Export] public NodePath OptionTwoButtonPath { get; set; } = new("ScreenOverlay/OptionTwoButton");
	[Export] public NodePath OptionThreeButtonPath { get; set; } = new("ScreenOverlay/OptionThreeButton");

	private VideoStreamPlayer _transitionPlayer = null!;
	private CanvasGroup _transitionGroup = null!;
	private Control _screenContent = null!;
	private ColorRect _previousScreenBlur = null!;
	private ColorRect _inputBlocker = null!;
	/// <summary>
	/// Слой поверх маски: заголовок, текст, плашки.
	///
	/// Маска его не режет — буквы и рамки кнопок не должны рваться по краям
	/// кляксы. Но исчезать вместе с окном он обязан, поэтому на переходах
	/// уводится прозрачностью.
	/// </summary>
	private Control _screenOverlay = null!;

	private Label _header = null!;
	/// <summary>Доклад группы приходит с разметкой движка, поэтому не Label.</summary>
	private RichTextLabel _situationLabel = null!;

	/// <summary>Кадр с места во весь экран: он же фон всего интерфейса.</summary>
	private TextureRect _illustration = null!;

	/// <summary>Материал иллюстрации, если на неё повешена маска картинки.</summary>
	private ShaderMaterial _illustrationMaterial;

	/// <summary>Строка состояния под докладом: «указание передано», «связь завершена».</summary>
	private Label _statusLine = null!;
	private readonly List<Button> _optionButtons = new();
	private ShaderMaterial _transitionMaterial = null!;
	private ShaderMaterial _previousScreenBlurMaterial = null!;
	private bool _isTransitionPlaying;
	private string _incidentId = string.Empty;
	private string _missionId = string.Empty;

	/// <summary>Какой вариант выбрали: по нему берётся кадр итога. -1 — не выбирали.</summary>
	private int _chosenOptionIndex = -1;

	private string _contentId = string.Empty;
	private IReadOnlyList<RadioOptionOffer> _options = Array.Empty<RadioOptionOffer>();
	private bool _awaitingOutcome;
	private bool _showingOutcome;

	public override void _Ready()
	{
		// Экран лежит поверх всего и обязан жить, даже когда дерево встало:
		// иначе меню паузы, открытое поверх перехода, замораживает видео —
		// а вместе с ним и невидимый блокировщик ввода на весь экран.
		ProcessMode = ProcessModeEnum.Always;

		_transitionPlayer = GetNode<VideoStreamPlayer>(TransitionPlayerPath);
		_transitionGroup = GetNode<CanvasGroup>(TransitionGroupPath);
		_screenContent = GetNode<Control>(ScreenContentPath);
		_previousScreenBlur = GetNode<ColorRect>(PreviousScreenBlurPath);
		_inputBlocker = GetNode<ColorRect>(InputBlockerPath);
		_screenOverlay = GetNode<Control>(ScreenOverlayPath);
		_header = GetNode<Label>(HeaderPath);
		_situationLabel = GetNode<RichTextLabel>(SituationLabelPath);
		_illustration = GetNode<TextureRect>(IllustrationPath);
		_illustrationMaterial = _illustration.Material as ShaderMaterial;
		_illustration.Resized += UpdateIllustrationRect;
		_statusLine = GetNode<Label>(StatusLinePath);
		_optionButtons.Add(GetNode<Button>(OptionOneButtonPath));
		_optionButtons.Add(GetNode<Button>(OptionTwoButtonPath));
		_optionButtons.Add(GetNode<Button>(OptionThreeButtonPath));
		_transitionMaterial = _transitionGroup.Material as ShaderMaterial
			?? throw new InvalidOperationException("RadioDecisionUI: TransitionGroup requires a ShaderMaterial.");
		_previousScreenBlurMaterial = _previousScreenBlur.Material as ShaderMaterial
			?? throw new InvalidOperationException("RadioDecisionUI: PreviousScreenBlur requires a ShaderMaterial.");
		_transitionPlayer.Finished += OnTransitionFinished;
		for (int index = 0; index < _optionButtons.Count; index++)
		{
			int capturedIndex = index;
			_optionButtons[index].Pressed += () => ChooseOption(capturedIndex);
		}

		Resized += FitScreenContentToWindow;
		FitScreenContentToWindow();
	}

	public override void _Process(double delta)
	{
		if (_isTransitionPlaying)
		{
			SetMaskTexture();

			// Верхний слой проступает вместе с окном. Маска его не режет,
			// поэтому иначе заголовок и кнопки висели бы над комнатой
			// с первого кадра перехода, когда окна ещё нет.
			double openLength = _transitionPlayer.GetStreamLength();
			SetOverlayFade((float)(_transitionElapsed / (openLength > 0.0 ? openLength : TransitionTimeoutSeconds)));

			TickTransitionGuard(delta);
		}

		if (_isClosing)
		{
			TickCloseTransition(delta);
		}

		TickOutcomeLock(delta);
	}

	public override void _ExitTree()
	{
		if (_transitionPlayer != null)
		{
			_transitionPlayer.Finished -= OnTransitionFinished;
		}

		Resized -= FitScreenContentToWindow;

		if (_illustration != null)
		{
			_illustration.Resized -= UpdateIllustrationRect;
		}
	}

	/// <summary>
	/// Маски перехода, из которых экран выбирает случайную. Пусто — играет
	/// то, что стоит в самом проигрывателе.
	/// </summary>
	[Export] public Godot.Collections.Array<VideoStream> TransitionMasks { get; set; } = new();

	private readonly RandomNumberGenerator _rng = new();

	public void ShowWithTransition()
	{
		// Экран мог пролежать скрытым всю смену, пока игрок менял размер окна:
		// сигнал Resized до скрытого узла не доходит, и размер остаётся старым.
		FitScreenContentToWindow();
		CallDeferred(nameof(UpdateIllustrationRect));
		Show();
		_transitionGroup.Show();
		_transitionGroup.Modulate = Colors.White;
		_inputBlocker.Show();
		_previousScreenBlur.Show();
		_transitionPlayer.Stop();
		PickTransitionMask();
		_transitionPlayer.Show();
		_transitionPlayer.Play();
		_isTransitionPlaying = true;
		_transitionElapsed = 0.0;
		SetOverlayFade(0f);
		SetMaskTexture();
	}

	/// <summary>Прозрачность верхнего слоя: 0 — его нет, 1 — виден целиком.</summary>
	private void SetOverlayFade(float alpha)
	{
		_screenOverlay.Modulate = new Color(1f, 1f, 1f, Math.Clamp(alpha, 0f, 1f));
	}

	/// <summary>
	/// Берёт одну из масок наугад, чтобы вызовы не открывались под копирку.
	/// Форма кляксы у них разная, так что и окно каждый раз рвётся по-своему.
	/// </summary>
	private void PickTransitionMask()
	{
		if (TransitionMasks == null || TransitionMasks.Count == 0)
		{
			return;
		}

		_transitionPlayer.Stream = TransitionMasks[_rng.RandiRange(0, TransitionMasks.Count - 1)];
	}

	/// <summary>Открывает экран по рации и временно останавливает симуляцию.</summary>
	public void ShowRadioDecision(
		string incidentId,
		string missionId,
		string missionTitle,
		IReadOnlyList<RadioOptionOffer> options,
		string missionEventId)
	{
		_incidentId = incidentId;
		_missionId = missionId ?? string.Empty;
		_contentId = missionEventId ?? string.Empty;
		_options = options ?? Array.Empty<RadioOptionOffer>();
		_awaitingOutcome = false;
		_showingOutcome = false;
		_chosenOptionIndex = -1;
		_illustration.Texture = MissionIllustrations.LoadRadioProblem(_missionId);
		_header.Text = missionTitle?.ToUpperInvariant() ?? string.Empty;
		_statusLine.Text = string.Empty;
		SetSituationText(ContentSpanFormatter.ResolveEntryBbcode(
			_contentId,
			string.Empty,
			ContentSpanFormatter.DefaultHighlight));

		for (int index = 0; index < _optionButtons.Count; index++)
		{
			bool hasOption = index < _options.Count;
			_optionButtons[index].Visible = hasOption;
			_optionButtons[index].Disabled = !hasOption || !_options[index].IsAvailable;
			if (hasOption)
			{
				// Без «[ 1 ]» перед текстом: в макете нумерации нет, а цифровые
				// клавиши экран всё равно не слушает — подпись обещала бы несуществующее.
				_optionButtons[index].Text = ContentTextResolver.ResolveOptionName(
					_contentId,
					_options[index].Id,
					string.Empty);
			}
		}

		CursorMode.Show(this);
		ShowWithTransition();
	}

	/// <summary>Дольше этого переход не живёт, чем бы ни кончилось видео.</summary>
	[Export] public double TransitionTimeoutSeconds { get; set; } = 4.0;

	private double _transitionElapsed;

	/// <summary>
	/// Аварийное завершение перехода.
	///
	/// InputBlocker — прозрачный прямоугольник во весь экран с mouse_filter=Stop,
	/// и снимался он ровно одним способом: сигналом Finished от видео. Способ
	/// хрупкий. Стоило видео не доиграть — а оно не доигрывает, если дерево
	/// встало на паузу или поток не открылся, — и сигнала не было уже никогда.
	/// Экран оставался живым на вид и полностью глухим: ни мышь, ни клавиши
	/// до кнопок не доходили.
	///
	/// Теперь у перехода есть предел по времени. Секунда лишнего затемнения
	/// не стоит риска запереть игрока в смене без выхода.
	/// </summary>
	private void TickTransitionGuard(double delta)
	{
		_transitionElapsed += delta;

		if (_transitionElapsed < TransitionTimeoutSeconds)
		{
			// Проигрыватель мог и вовсе не стартовать: например, поток не найден.
			// Полсекунды форы на раскрутку, дальше молчание считаем отказом.
			if (_transitionElapsed < 0.5 || _transitionPlayer.IsPlaying())
			{
				return;
			}
		}

		StopTransition();
	}

	public void StopTransition()
	{
		_isTransitionPlaying = false;
		SetOverlayFade(1f);
		FreezeMaskOnLastFrame();
		_transitionPlayer.Stop();
		_transitionPlayer.Hide();
		_inputBlocker.Hide();

		// Размытие комнаты не снимаем: сквозь рваные края окна её видно всё
		// время, пока экран открыт, и она должна оставаться мутной. Уйдёт
		// вместе с экраном, в CloseDecision.
	}

	/// <summary>
	/// Оставляет на экране форму последнего кадра маски.
	///
	/// Клякса из маски — это и есть форма окна, а не только способ его проявить:
	/// после перехода окно продолжает жить с рваными краями. Держаться при этом
	/// за текстуру проигрывателя нельзя — она принадлежит ему, и что с ней
	/// станет после Stop, не наша забота. Поэтому снимаем копию в свою
	/// ImageTexture и дальше живём с ней.
	/// </summary>
	private void FreezeMaskOnLastFrame()
	{
		Texture2D videoTexture = _transitionPlayer.GetVideoTexture();
		Image frame = videoTexture?.GetImage();
		if (frame == null || frame.GetWidth() == 0)
		{
			return;
		}

		var frozen = ImageTexture.CreateFromImage(frame);
		_transitionMaterial.SetShaderParameter("mask_texture", frozen);
		_previousScreenBlurMaterial.SetShaderParameter("mask_texture", frozen);
	}

	/// <summary>Reuses the radio screen as the result confirmation screen.</summary>
	public void ShowOutcome(MissionOutcomeReady outcome)
	{
		if (!Visible || !string.Equals(_incidentId, outcome.IncidentId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		_awaitingOutcome = false;
		_showingOutcome = true;

		// Итог пришёл — значит, миссия отыграна и переход давно неактуален.
		// Если он всё ещё «идёт», это сбой: снимаем принудительно, иначе
		// блокировщик ввода не даст нажать кнопку подтверждения.
		StopTransition();

		_contentId = outcome.SummaryContentId ?? string.Empty;
		_illustration.Texture = MissionIllustrations.LoadOutcome(
			outcome.MissionId,
			_chosenOptionIndex,
			outcome.IsSuccess);
		_header.Text = outcome.IsSuccess ? "\u041e\u041f\u0415\u0420\u0410\u0426\u0418\u042f \u0417\u0410\u0412\u0415\u0420\u0428\u0415\u041d\u0410" : "\u041e\u041f\u0415\u0420\u0410\u0426\u0418\u042f \u041f\u0420\u041e\u0412\u0410\u041b\u0415\u041d\u0410";
		SetSituationText(ContentSpanFormatter.ResolveEntryBbcode(
			_contentId,
			string.Empty,
			ContentSpanFormatter.DefaultHighlight));
		_statusLine.Text = "\u0421\u0412\u042f\u0417\u042c \u0417\u0410\u0412\u0415\u0420\u0428\u0415\u041d\u0410";

		for (int index = 0; index < _optionButtons.Count; index++)
		{
			bool isConfirm = index == 0;
			_optionButtons[index].Visible = isConfirm;
			_optionButtons[index].Disabled = !isConfirm;
		}

		StartOutcomeLock();
	}

	// ------------------------------------------------------------------ пауза на итог

	/// <summary>Сколько секунд итог нельзя закрыть.</summary>
	[Export] public double OutcomeLockSeconds { get; set; } = 3.0;

	private double _outcomeLock;

	/// <summary>
	/// Держит экран итога закрытым для ввода первые секунды.
	///
	/// Игрок выбирает вариант нажатием ENTER, и итог появляется под тем же
	/// пальцем: второе нажатие — своё или отскок клавиши — смахивало экран
	/// раньше, чем взгляд успевал дойти до первой строки. Замок снимает гонку
	/// целиком, а заодно даёт прочитать, чем всё кончилось.
	///
	/// Кнопка не прячется, а показывает остаток: исчезнувшая кнопка читается
	/// как поломка, а обратный отсчёт — как «подожди, идёт приём».
	/// </summary>
	private void StartOutcomeLock()
	{
		_outcomeLock = OutcomeLockSeconds;
		UpdateOutcomeButton();
	}

	private void TickOutcomeLock(double delta)
	{
		if (_outcomeLock <= 0.0)
		{
			return;
		}

		_outcomeLock -= delta;
		UpdateOutcomeButton();
	}

	private void UpdateOutcomeButton()
	{
		if (_optionButtons.Count == 0)
		{
			return;
		}

		bool locked = _outcomeLock > 0.0;
		bool wasLocked = _optionButtons[0].Disabled;
		_optionButtons[0].Disabled = locked;

		// Disabled снимает фокус, и после разблокировки кнопка остаётся ничьей.
		// Возвращаем — тогда работает и ENTER, и пробел, и стрелки.
		if (wasLocked && !locked)
		{
			_optionButtons[0].GrabFocus();
		}
		// \u041f\u0440\u043e ENTER \u043d\u0430 \u043a\u043d\u043e\u043f\u043a\u0435 \u043d\u0435 \u043f\u0438\u0448\u0435\u043c \u2014 \u0432 \u043c\u0430\u043a\u0435\u0442\u0435 \u043f\u043e\u0434\u043f\u0438\u0441\u0435\u0439 \u043a\u043b\u0430\u0432\u0438\u0448 \u043d\u0435\u0442.
		// \u0421\u0430\u043c\u0430 \u043a\u043b\u0430\u0432\u0438\u0448\u0430 \u0440\u0430\u0431\u043e\u0442\u0430\u0435\u0442: \u0435\u0451 \u043b\u043e\u0432\u0438\u0442 _UnhandledInput.
		_optionButtons[0].Text = locked
			? $"\u041f\u0420\u0418\u0401\u041c \u0417\u0410\u041f\u0418\u0421\u0418\u2026  {System.Math.Ceiling(_outcomeLock):0}"
			: "\u041f\u041e\u0414\u0422\u0412\u0415\u0420\u0414\u0418\u0422\u042c \u0418\u0422\u041e\u0413";
	}

	private void ChooseOption(int optionIndex)
	{
		if (_showingOutcome)
		{
			if (_outcomeLock > 0.0)
			{
				return;
			}

			CloseDecision(false);
			return;
		}

		if (_awaitingOutcome)
		{
			return;
		}

		if (string.IsNullOrEmpty(_incidentId) || optionIndex < 0 || optionIndex >= _options.Count)
		{
			return;
		}

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			GD.PushWarning("RadioDecisionUI: GameRuntime is not ready.");
			return;
		}

		CommandResult result = runtime.Session.ChooseRadioOption(_incidentId, _options[optionIndex].Id);
		if (!result.IsSuccess)
		{
			GD.PushWarning($"RadioDecisionUI: {result.Error}");
			return;
		}

		_chosenOptionIndex = optionIndex;
		_awaitingOutcome = true;
		_statusLine.Text = "\u0423\u041a\u0410\u0417\u0410\u041d\u0418\u0415 \u041f\u0415\u0420\u0415\u0414\u0410\u041d\u041e. \u041e\u0416\u0418\u0414\u0410\u0415\u041c \u0414\u041e\u041a\u041b\u0410\u0414 \u0413\u0420\u0423\u041f\u041f\u042b\u2026";
		for (int index = 0; index < _optionButtons.Count; index++)
		{
			_optionButtons[index].Disabled = true;
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Кнопка обещает «[ ENTER ] ПОДТВЕРДИТЬ ИТОГ», но клавишу никто не слушал:
		// сработать могла только мышь. Обещание в интерфейсе надо выполнять —
		// иначе игрок жмёт ENTER, ничего не происходит, и экран выглядит зависшим.
		if (Visible && _showingOutcome && _outcomeLock <= 0.0 && @event.IsActionPressed("ui_accept"))
		{
			CloseDecision(false);
			GetViewport().SetInputAsHandled();
			return;
		}

		if (Visible && !_awaitingOutcome && !_showingOutcome && @event.IsActionPressed("ui_cancel"))
		{
			CloseDecision(true);
			GetViewport().SetInputAsHandled();
		}
	}

	private void CloseDecision(bool closeRadio)
	{
		if (closeRadio && !string.IsNullOrEmpty(_incidentId))
		{
			GameRuntime runtime = GameRuntime.Get(this);
			if (runtime != null && runtime.IsReady)
			{
				runtime.Session.CloseRadio(_incidentId);
			}
		}
		else if (_showingOutcome && !string.IsNullOrEmpty(_incidentId))
		{
			GameRuntime runtime = GameRuntime.Get(this);
			if (runtime != null && runtime.IsReady)
			{
				runtime.Session.CloseMissionOutcome(_incidentId);
			}
		}

		// Ядру сообщили сразу — время идёт дальше, пока окно доигрывает уход.
		// Само окно прячет FinishClose, когда маска его доест.
		StopTransition();
		StartCloseTransition();

		_incidentId = string.Empty;
		_missionId = string.Empty;
		_chosenOptionIndex = -1;
		_contentId = string.Empty;
		_options = Array.Empty<RadioOptionOffer>();
		_awaitingOutcome = false;
		_showingOutcome = false;

		// Курсор отпускает FinishClose: пока окно доигрывает уход, отдавать
		// мышь обратно в кабинет рано — игрок начнёт крутить камерой сквозь него.
	}

	private void OnTransitionFinished()
	{
		if (_isClosing)
		{
			FinishClose();
			return;
		}

		StopTransition();
	}

	// ------------------------------------------------------------------ закрытие

	private bool _isClosing;
	private double _closeElapsed;
	private double _closeDuration;

	/// <summary>
	/// Убирает окно тем же рисунком, каким оно открывалось.
	///
	/// Обратного видео у нас нет, и VideoStreamPlayer назад играть не умеет.
	/// Но оно и не нужно: та же маска, проигранная вперёд, работает ластиком —
	/// шейдер оставляет только то, что было в окне и ещё не закрашено новой
	/// кляксой. Рисунок совпадает с открытием, потому что это буквально он.
	/// </summary>
	public void StartCloseTransition()
	{
		_isClosing = true;
		_closeElapsed = 0.0;
		_inputBlocker.Show();
		_transitionPlayer.Stop();
		PickTransitionMask();
		_transitionPlayer.Show();
		_transitionPlayer.Play();

		double length = _transitionPlayer.GetStreamLength();
		_closeDuration = length > 0.0 ? length : TransitionTimeoutSeconds;

		_transitionMaterial.SetShaderParameter("closing", 1.0f);
		SetCloseMaskTexture();
	}

	private void TickCloseTransition(double delta)
	{
		_closeElapsed += delta;
		SetCloseMaskTexture();

		// Комната возвращается в резкость по мере того, как окно съедается,
		// а верхний слой уходит вместе с ним — маска его не трогает.
		float fade = 1.0f - (float)Math.Clamp(_closeElapsed / _closeDuration, 0.0, 1.0);
		_previousScreenBlur.Modulate = new Color(1f, 1f, 1f, fade);
		SetOverlayFade(fade);

		// Тот же предохранитель, что и на открытии: не доиграло — снимаем сами,
		// иначе блокировщик ввода останется висеть на весь экран.
		if (_closeElapsed >= _closeDuration + 0.5
			|| (_closeElapsed > 0.5 && !_transitionPlayer.IsPlaying()))
		{
			FinishClose();
		}
	}

	private void SetCloseMaskTexture()
	{
		Texture2D videoTexture = _transitionPlayer.GetVideoTexture();
		if (videoTexture != null)
		{
			_transitionMaterial.SetShaderParameter("close_mask", videoTexture);
		}
	}

	private void FinishClose()
	{
		_isClosing = false;
		_transitionPlayer.Stop();
		_transitionPlayer.Hide();
		_inputBlocker.Hide();
		_previousScreenBlur.Hide();
		_previousScreenBlur.Modulate = Colors.White;
		SetOverlayFade(1f);

		// Снимаем режим ластика и пустой кадр: следующее открытие начинается
		// с чистого листа, иначе окно проявится уже наполовину съеденным.
		_transitionMaterial.SetShaderParameter("closing", 0.0f);
		_transitionMaterial.SetShaderParameter("close_mask", default(Variant));

		Hide();
		_illustration.Texture = null;
		CursorMode.Hide(this);
	}

	private void FitScreenContentToWindow()
	{
		Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
		if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
		{
			return;
		}

		_screenContent.Position = Vector2.Zero;
		_screenContent.Size = viewportSize;
	}

	/// <summary>
	/// Сообщает шейдеру иллюстрации, какую часть экрана он занимает.
	///
	/// Маска картинки нарисована под рамку на экране, а не под сам кадр,
	/// поэтому шейдеру нужно знать положение рамки. Из шейдера его не достать:
	/// FRAGCOORD знает только про экран, про свой узел — ничего.
	/// </summary>
	private void UpdateIllustrationRect()
	{
		if (_illustrationMaterial == null)
		{
			return;
		}

		Vector2 viewport = GetViewport().GetVisibleRect().Size;
		if (viewport.X <= 0f || viewport.Y <= 0f)
		{
			return;
		}

		Rect2 rect = _illustration.GetGlobalRect();
		_illustrationMaterial.SetShaderParameter("node_rect", new Vector4(
			rect.Position.X / viewport.X,
			rect.Position.Y / viewport.Y,
			rect.Size.X / viewport.X,
			rect.Size.Y / viewport.Y));
	}

	private void SetMaskTexture()
	{
		Texture2D videoTexture = _transitionPlayer.GetVideoTexture();
		if (videoTexture != null)
		{
			_transitionMaterial.SetShaderParameter("mask_texture", videoTexture);
			_previousScreenBlurMaterial.SetShaderParameter("mask_texture", videoTexture);
		}
	}

	// ------------------------------------------------------------------ подгонка текста

	/// <summary>Крупнее этого текст не станет: 22 pt макета в координатах 1280x720.</summary>
	private const int SituationFontMax = 15;

	/// <summary>Мельче этого читать уже нельзя, дальше включается прокрутка.</summary>
	private const int SituationFontMin = 11;

	/// <summary>Сколько кадров ждать, пока рамке назначат размер.</summary>
	private const int SituationFitAttempts = 8;

	private int _situationFitAttempts;

	/// <summary>
	/// Ставит доклад в рамку с выключкой по ширине.
	///
	/// В макете текст выровнен по обоим краям, с переносами по слогам — так же,
	/// как печатали на машинке. RichTextLabel умеет это только через разметку,
	/// свойства выравнивания у него нет, поэтому оборачиваем здесь, а не в сцене:
	/// текст приходит из движка уже с bbcode, и в редакторе его никто не увидит.
	/// </summary>
	private void SetSituationText(string bbcode)
	{
		_situationLabel.Text = $"[p align=fill]{bbcode}[/p]";
		RequestSituationFit();
	}

	private void RequestSituationFit()
	{
		_situationFitAttempts = SituationFitAttempts;
		CallDeferred(nameof(FitSituationText));
	}

	/// <summary>
	/// Подгоняет размер шрифта под рамку.
	///
	/// Рамка у сводки фиксированная, а длину текста пишут авторы, и она гуляет
	/// от двух строк до полутора десятков. Лишнее просто вылезало за край и
	/// уходило под строку «СВЯЗЬ ЗАВЕРШЕНА» — обрывалось на полуслове, причём
	/// молча: ни прокрутки, ни многоточия, никакого признака, что текст есть
	/// дальше. Игрок терял часть отчёта, не зная об этом.
	///
	/// Уменьшаем по одному пункту, пока не влезет. Прокрутка оставлена на
	/// крайний случай: колесо в отчёте, который читают один раз, легко
	/// не заметить, а вот шрифт на пару пунктов мельче — не помеха.
	/// </summary>
	private void FitSituationText()
	{
		if (_situationLabel == null)
		{
			return;
		}

		float available = _situationLabel.Size.Y;
		if (available <= 1.0f)
		{
			// Рамке ещё не назначили размер. Ждём следующего кадра, но не вечно:
			// на скрытом экране высота нулевая всегда, и цикл был бы бесконечным.
			if (_situationFitAttempts-- > 0)
			{
				CallDeferred(nameof(FitSituationText));
			}

			return;
		}

		for (int size = SituationFontMax; size >= SituationFontMin; size--)
		{
			_situationLabel.AddThemeFontSizeOverride("normal_font_size", size);
			if (_situationLabel.GetContentHeight() <= available)
			{
				_situationLabel.ScrollActive = false;
				return;
			}
		}

		_situationLabel.ScrollActive = true;
	}
}
