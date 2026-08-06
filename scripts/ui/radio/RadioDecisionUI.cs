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
	[Export] public NodePath HeaderPath { get; set; } = new("TransitionGroup/ScreenContent/Header");
	[Export] public NodePath SituationLabelPath { get; set; } = new("TransitionGroup/ScreenContent/ContentFrame/SituationLabel");
	[Export] public NodePath IllustrationPlaceholderPath { get; set; } = new("TransitionGroup/ScreenContent/ContentFrame/IllustrationPanel/Placeholder");
	[Export] public NodePath IllustrationCaptionPath { get; set; } = new("TransitionGroup/ScreenContent/ContentFrame/IllustrationPanel/IllustrationCaption");
	[Export] public NodePath PromptPath { get; set; } = new("TransitionGroup/ScreenContent/ContentFrame/Prompt");
	[Export] public NodePath OptionOneButtonPath { get; set; } = new("TransitionGroup/ScreenContent/ContentFrame/OptionOneButton");
	[Export] public NodePath OptionTwoButtonPath { get; set; } = new("TransitionGroup/ScreenContent/ContentFrame/OptionTwoButton");
	[Export] public NodePath OptionThreeButtonPath { get; set; } = new("TransitionGroup/ScreenContent/ContentFrame/OptionThreeButton");

	private VideoStreamPlayer _transitionPlayer = null!;
	private CanvasGroup _transitionGroup = null!;
	private Control _screenContent = null!;
	private ColorRect _previousScreenBlur = null!;
	private ColorRect _inputBlocker = null!;
	private Label _header = null!;
	/// <summary>Доклад группы приходит с разметкой движка, поэтому не Label.</summary>
	private RichTextLabel _situationLabel = null!;
	private Label _illustrationPlaceholder = null!;
	private Label _illustrationCaption = null!;
	private Label _prompt = null!;
	private readonly List<Button> _optionButtons = new();
	private ShaderMaterial _transitionMaterial = null!;
	private ShaderMaterial _previousScreenBlurMaterial = null!;
	private bool _isTransitionPlaying;
	private string _incidentId = string.Empty;
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
		_header = GetNode<Label>(HeaderPath);
		_situationLabel = GetNode<RichTextLabel>(SituationLabelPath);
		_illustrationPlaceholder = GetNode<Label>(IllustrationPlaceholderPath);
		_illustrationCaption = GetNode<Label>(IllustrationCaptionPath);
		_prompt = GetNode<Label>(PromptPath);
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
			TickTransitionGuard(delta);
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
	}

	public void ShowWithTransition()
	{
		// Экран мог пролежать скрытым всю смену, пока игрок менял размер окна:
		// сигнал Resized до скрытого узла не доходит, и размер остаётся старым.
		FitScreenContentToWindow();
		Show();
		_transitionGroup.Show();
		_transitionGroup.Modulate = Colors.White;
		_inputBlocker.Show();
		_previousScreenBlur.Show();
		_transitionPlayer.Stop();
		_transitionPlayer.Show();
		_transitionPlayer.Play();
		_isTransitionPlaying = true;
		_transitionElapsed = 0.0;
		SetMaskTexture();
	}

	/// <summary>Открывает экран по рации и временно останавливает симуляцию.</summary>
	public void ShowRadioDecision(
		string incidentId,
		string missionTitle,
		IReadOnlyList<RadioOptionOffer> options,
		string missionEventId)
	{
		_incidentId = incidentId;
		_contentId = missionEventId ?? string.Empty;
		_options = options ?? Array.Empty<RadioOptionOffer>();
		_awaitingOutcome = false;
		_showingOutcome = false;
		_header.Text = $"К.О.Н.Т.У.Р.-Д  /  РАДИО: {missionTitle}";
		_situationLabel.Text = ContentSpanFormatter.ResolveEntryBbcode(
			_contentId,
			string.Empty,
			ContentSpanFormatter.DefaultHighlight);
		RequestSituationFit();

		for (int index = 0; index < _optionButtons.Count; index++)
		{
			bool hasOption = index < _options.Count;
			_optionButtons[index].Visible = hasOption;
			_optionButtons[index].Disabled = !hasOption || !_options[index].IsAvailable;
			if (hasOption)
			{
				string optionText = ContentTextResolver.ResolveOptionName(_contentId, _options[index].Id, string.Empty);
				_optionButtons[index].Text = $"[ {index + 1} ] {optionText}";
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
		_transitionPlayer.Stop();
		_transitionPlayer.Hide();
		_previousScreenBlur.Hide();
		_inputBlocker.Hide();
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
		_header.Text = outcome.IsSuccess
			? "\u041a.\u041e.\u041d.\u0422.\u0423.\u0420.-\u0414  /  \u0418\u0422\u041e\u0413 \u041e\u041f\u0415\u0420\u0410\u0426\u0418\u0418: \u0423\u0421\u041f\u0415\u0425"
			: "\u041a.\u041e.\u041d.\u0422.\u0423.\u0420.-\u0414  /  \u0418\u0422\u041e\u0413 \u041e\u041f\u0415\u0420\u0410\u0426\u0418\u0418: \u0421\u0411\u041e\u0419";
		_situationLabel.Text = ContentSpanFormatter.ResolveEntryBbcode(
			_contentId,
			string.Empty,
			ContentSpanFormatter.DefaultHighlight);
		RequestSituationFit();
		_prompt.Text = "\u0421\u0412\u042f\u0417\u042c \u0417\u0410\u0412\u0415\u0420\u0428\u0415\u041d\u0410. \u0417\u0410\u0424\u0418\u041a\u0421\u0418\u0420\u0423\u0419\u0422\u0415 \u0418\u0422\u041e\u0413:";
		_illustrationPlaceholder.Text = string.IsNullOrWhiteSpace(outcome.CreatureId)
			? "[ \u0418\u041b\u041b\u042e\u0421\u0422\u0420\u0410\u0426\u0418\u042f\\n  \u041d\u0415\u0414\u041e\u0421\u0422\u0423\u041f\u041d\u0410 ]"
			: $"[ \u0418\u041b\u041b\u042e\u0421\u0422\u0420\u0410\u0426\u0418\u042f\\n  {outcome.CreatureId} ]";
		_illustrationCaption.Text = outcome.IsSuccess
			? "\u041f\u041e\u0421\u041b\u0415\u0414\u0421\u0422\u0412\u0418\u0415 \u041e\u041f\u0415\u0420\u0410\u0426\u0418\u0418"
			: "\u041f\u041e\u0421\u041b\u0415\u0414\u0421\u0422\u0412\u0418\u0415 \u0421\u0411\u041e\u042f";

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
		_optionButtons[0].Text = locked
			? $"\u041f\u0420\u0418\u0401\u041c \u0417\u0410\u041f\u0418\u0421\u0418\u2026  {System.Math.Ceiling(_outcomeLock):0}"
			: "[ ENTER ] \u041f\u041e\u0414\u0422\u0412\u0415\u0420\u0414\u0418\u0422\u042c \u0418\u0422\u041e\u0413";
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

		_awaitingOutcome = true;
		_prompt.Text = "\u0423\u041a\u0410\u0417\u0410\u041d\u0418\u0415 \u041f\u0415\u0420\u0415\u0414\u0410\u041d\u041e. \u041e\u0416\u0418\u0414\u0410\u0415\u041c \u0414\u041e\u041a\u041b\u0410\u0414 \u0413\u0420\u0423\u041f\u041f\u042b...";
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

		StopTransition();
		Hide();
		_incidentId = string.Empty;
		_contentId = string.Empty;
		_options = Array.Empty<RadioOptionOffer>();
		_awaitingOutcome = false;
		_showingOutcome = false;
		CursorMode.Hide(this);
	}

	private void OnTransitionFinished()
	{
		_isTransitionPlaying = false;
		_transitionPlayer.Hide();
		_previousScreenBlur.Hide();
		_inputBlocker.Hide();
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

	/// <summary>Крупнее этого текст не станет: размер из сцены.</summary>
	private const int SituationFontMax = 22;

	/// <summary>Мельче этого читать уже нельзя, дальше включается прокрутка.</summary>
	private const int SituationFontMin = 14;

	/// <summary>Сколько кадров ждать, пока рамке назначат размер.</summary>
	private const int SituationFitAttempts = 8;

	private int _situationFitAttempts;

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
