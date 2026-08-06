using Godot;
using System;

/// <summary>
/// Окно входящего вызова поверх кабинета.
///
/// Устроено так же, как экран рации: чёрно-белое видео-маска вырезает окно
/// из кадра, под ним размывается комната, а заголовок, текст и кнопка лежат
/// отдельным слоем поверх маски — буквы и рамки не должны рваться по краям
/// кляксы. Закрывается тем же роликом, проигранным заново: он работает
/// ластиком и съедает окно тем же рисунком, каким его открывал.
///
/// Отличий от рации два: свой набор масок (клякса у них меньше, поэтому
/// и окно выходит меньше) и одна кнопка вместо трёх.
///
/// Логика перехода здесь повторяет RadioDecisionUI почти строка в строку.
/// Это осознанный дубль на время: пока оба экрана дозреют, вынесение общего
/// куска в отдельный класс будет дешевле и безопаснее, чем сейчас.
/// </summary>
public partial class PhoneCallAcceptanceUI : Control
{
	[Export] public NodePath TransitionPlayerPath { get; set; } = new("TransitionPlayer");
	[Export] public NodePath TransitionGroupPath { get; set; } = new("TransitionGroup");
	[Export] public NodePath ScreenContentPath { get; set; } = new("TransitionGroup/ScreenContent");
	[Export] public NodePath IllustrationPath { get; set; } = new("TransitionGroup/ScreenContent/Illustration");
	[Export] public NodePath ScreenOverlayPath { get; set; } = new("ScreenOverlay");
	[Export] public NodePath PreviousScreenBlurPath { get; set; } = new("PreviousScreenBlur");
	[Export] public NodePath InputBlockerPath { get; set; } = new("InputBlocker");
	[Export] public NodePath TitlePath { get; set; } = new("ScreenOverlay/CallTitle");
	[Export] public NodePath DescriptionPath { get; set; } = new("ScreenOverlay/Description");
	[Export] public NodePath ConfirmButtonPath { get; set; } = new("ScreenOverlay/ConfirmButton");

	/// <summary>Маски перехода, из которых окно выбирает случайную.</summary>
	[Export] public Godot.Collections.Array<VideoStream> TransitionMasks { get; set; } = new();

	/// <summary>Дольше этого переход не живёт, чем бы ни кончилось видео.</summary>
	[Export] public double TransitionTimeoutSeconds { get; set; } = 4.0;

	private VideoStreamPlayer _transitionPlayer = null!;
	private CanvasGroup _transitionGroup = null!;
	private Control _screenContent = null!;
	private Control _screenOverlay = null!;
	private ColorRect _previousScreenBlur = null!;
	private ColorRect _inputBlocker = null!;
	private Label _title = null!;
	/// <summary>Стенограмма приходит с разметкой движка, поэтому не Label.</summary>
	private RichTextLabel _description = null!;
	private TextureRect _illustration = null!;
	private ShaderMaterial _illustrationMaterial;
	private ShaderMaterial _transitionMaterial = null!;
	private ShaderMaterial _previousScreenBlurMaterial = null!;
	private Button _confirmButton = null!;
	private Action _confirmedCallback;
	private bool _pausedRuntime;

	private readonly RandomNumberGenerator _rng = new();

	private bool _isTransitionPlaying;
	private double _transitionElapsed;
	private bool _isClosing;
	private double _closeElapsed;
	private double _closeDuration;

	public override void _Ready()
	{
		// Окно живёт поверх кабинета и обязано пережить паузу: меню паузы
		// останавливает дерево, и без этого кнопка «принять вызов» переставала
		// жать, а переход замирал вместе с невидимым блокировщиком ввода.
		ProcessMode = ProcessModeEnum.Always;

		_transitionPlayer = GetNode<VideoStreamPlayer>(TransitionPlayerPath);
		_transitionGroup = GetNode<CanvasGroup>(TransitionGroupPath);
		_screenContent = GetNode<Control>(ScreenContentPath);
		_screenOverlay = GetNode<Control>(ScreenOverlayPath);
		_previousScreenBlur = GetNode<ColorRect>(PreviousScreenBlurPath);
		_inputBlocker = GetNode<ColorRect>(InputBlockerPath);
		_title = GetNode<Label>(TitlePath);
		_description = GetNode<RichTextLabel>(DescriptionPath);
		_illustration = GetNode<TextureRect>(IllustrationPath);
		_illustrationMaterial = _illustration.Material as ShaderMaterial;
		_confirmButton = GetNode<Button>(ConfirmButtonPath);

		_transitionMaterial = _transitionGroup.Material as ShaderMaterial
			?? throw new InvalidOperationException("PhoneCallAcceptanceUI: TransitionGroup requires a ShaderMaterial.");
		_previousScreenBlurMaterial = _previousScreenBlur.Material as ShaderMaterial
			?? throw new InvalidOperationException("PhoneCallAcceptanceUI: PreviousScreenBlur requires a ShaderMaterial.");

		_confirmButton.Pressed += CloseCall;
		_illustration.Resized += UpdateIllustrationRect;
		Resized += FitScreenContentToWindow;
		FitScreenContentToWindow();
	}

	public override void _ExitTree()
	{
		_confirmButton.Pressed -= CloseCall;
		if (_illustration != null)
		{
			_illustration.Resized -= UpdateIllustrationRect;
		}

		Resized -= FitScreenContentToWindow;
	}

	public override void _Process(double delta)
	{
		if (_isTransitionPlaying)
		{
			SetMaskTexture();

			double openLength = _transitionPlayer.GetStreamLength();
			SetOverlayFade((float)(_transitionElapsed / (openLength > 0.0 ? openLength : TransitionTimeoutSeconds)));

			TickTransitionGuard(delta);
		}

		if (_isClosing)
		{
			TickCloseTransition(delta);
		}
	}

	public void ShowCallAcceptance(
		string title,
		string description,
		Texture2D illustration = null,
		Action confirmedCallback = null)
	{
		_title.Text = title;
		RequestTitleFit();
		_description.Text = $"[p align=fill]{description}[/p]";
		RequestDescriptionFit();
		_illustration.Texture = illustration;
		_confirmedCallback = confirmedCallback;

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null && runtime.IsReady && !runtime.IsPaused)
		{
			runtime.IsPaused = true;
			_pausedRuntime = true;
		}

		CursorMode.Show(this);
		ShowWithTransition();
	}

	public void ShowTestWindow()
	{
		ShowCallAcceptance(
			"ВХОДЯЩИЙ ВЫЗОВ",
			"Диспетчерская 041. Поступило сообщение из жилого сектора: жильцы слышат шум в закрытой квартире. Требуется подтвердить принятие вызова.");
	}

	// ------------------------------------------------------------------ переход

	public void ShowWithTransition()
	{
		FitScreenContentToWindow();
		CallDeferred(nameof(UpdateIllustrationRect));
		Show();
		_transitionGroup.Show();
		_transitionGroup.Modulate = Colors.White;
		_inputBlocker.Show();
		_previousScreenBlur.Show();
		_previousScreenBlur.Modulate = Colors.White;
		_transitionPlayer.Stop();
		PickTransitionMask();
		_transitionPlayer.Show();
		_transitionPlayer.Play();
		_isTransitionPlaying = true;
		_transitionElapsed = 0.0;
		SetOverlayFade(0f);
		SetMaskTexture();
	}

	/// <summary>Какой маской открылось окно: ей же оно и закроется.</summary>
	private int _maskIndex = -1;

	/// <summary>
	/// Берёт одну из масок наугад, чтобы вызовы не открывались под копирку.
	/// Форма кляксы у них разная, так что и окно каждый раз рвётся по-своему.
	/// </summary>
	private void PickTransitionMask()
	{
		if (TransitionMasks == null || TransitionMasks.Count == 0)
		{
			_maskIndex = -1;
			return;
		}

		_maskIndex = _rng.RandiRange(0, TransitionMasks.Count - 1);
		_transitionPlayer.Stream = TransitionMasks[_maskIndex];
	}

	/// <summary>
	/// Ставит ту же маску, которой окно открылось.
	///
	/// На закрытии брать случайную нельзя. Форма окна заморожена от маски
	/// открытия, а съедала бы его чужая клякса: окно уходило бы не по своим
	/// краям. Хуже того, кляксы разного размера — широкая накрыла бы узкую
	/// за первую секунду, а узкая не доела бы широкую до конца ролика, и
	/// остаток снимал бы предохранитель по таймеру, то есть рывком.
	/// </summary>
	private void ReplayTransitionMask()
	{
		if (TransitionMasks == null || _maskIndex < 0 || _maskIndex >= TransitionMasks.Count)
		{
			return;
		}

		_transitionPlayer.Stream = TransitionMasks[_maskIndex];
	}

	/// <summary>
	/// Аварийное завершение перехода.
	///
	/// InputBlocker — прозрачный прямоугольник во весь экран с mouse_filter=Stop.
	/// Если снимать его только по сигналу Finished, то не доигравшее видео —
	/// а оно не доигрывает, если поток не открылся, — запирает игрока в глухом
	/// экране навсегда. Секунда лишнего затемнения этого не стоит.
	/// </summary>
	private void TickTransitionGuard(double delta)
	{
		_transitionElapsed += delta;

		if (_transitionElapsed < TransitionTimeoutSeconds)
		{
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

		// Размытие комнаты не снимаем: сквозь рваные края её видно всё время,
		// пока окно открыто. Уйдёт вместе с окном, в FinishClose.
	}

	/// <summary>
	/// Оставляет на экране форму последнего кадра маски.
	///
	/// Клякса — это и есть форма окна, а не только способ его проявить. Держаться
	/// при этом за текстуру проигрывателя нельзя: она принадлежит ему, и что с ней
	/// станет после Stop — не наша забота. Поэтому снимаем копию.
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

	/// <summary>
	/// Убирает окно тем же рисунком, каким оно открывалось.
	///
	/// Обратного видео нет, и VideoStreamPlayer назад играть не умеет. Но оно
	/// и не нужно: та же маска, проигранная вперёд, работает ластиком — шейдер
	/// оставляет только то, что было в окне и ещё не закрашено новой кляксой.
	/// </summary>
	public void StartCloseTransition()
	{
		_isClosing = true;
		_closeElapsed = 0.0;
		_inputBlocker.Show();
		_transitionPlayer.Stop();
		ReplayTransitionMask();
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

		if (_closeElapsed >= _closeDuration + 0.5
			|| (_closeElapsed > 0.5 && !_transitionPlayer.IsPlaying()))
		{
			FinishClose();
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

		// Снимаем режим ластика и его кадр: следующее открытие начинается
		// с чистого листа, иначе окно проявится уже наполовину съеденным.
		_transitionMaterial.SetShaderParameter("closing", 0.0f);
		_transitionMaterial.SetShaderParameter("close_mask", default(Variant));

		Hide();

		// Картинку снимаем здесь: иначе она доживёт до следующего вызова
		// и мелькнёт чужим кадром, пока новый ещё не назначен.
		_illustration.Texture = null;

		CursorMode.Hide(this);

		Action confirmedCallback = _confirmedCallback;
		_confirmedCallback = null;
		confirmedCallback?.Invoke();
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

	private void SetCloseMaskTexture()
	{
		Texture2D videoTexture = _transitionPlayer.GetVideoTexture();
		if (videoTexture != null)
		{
			_transitionMaterial.SetShaderParameter("close_mask", videoTexture);
		}
	}

	/// <summary>Прозрачность верхнего слоя: 0 — его нет, 1 — виден целиком.</summary>
	private void SetOverlayFade(float alpha)
	{
		_screenOverlay.Modulate = new Color(1f, 1f, 1f, Math.Clamp(alpha, 0f, 1f));
	}

	private void CloseCall()
	{
		if (_isClosing)
		{
			return;
		}

		// Симуляцию отпускаем сразу — время идёт, пока окно доигрывает уход.
		// Обещанный вызывающему коллбэк уходит в FinishClose, когда окно ушло.
		if (_pausedRuntime)
		{
			GameRuntime runtime = GameRuntime.Get(this);
			if (runtime != null)
			{
				runtime.IsPaused = false;
			}

			_pausedRuntime = false;
		}

		StopTransition();
		StartCloseTransition();
	}

	/// <summary>
	/// Размер ScreenContent приходится выставлять руками: якоря Control считаются
	/// от прямоугольника родителя, а родитель здесь CanvasGroup, то есть Node2D.
	/// Прямоугольника у него нет, и «растянуть на весь экран» якорями не выйдет —
	/// получится 0x0, и всё содержимое схлопнется в начало координат.
	/// </summary>
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
	/// Сообщает шейдеру иллюстрации, какую часть экрана он занимает: маска
	/// картинки нарисована под рамку на экране, а из шейдера её не достать.
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

	// ------------------------------------------------------------------ подгонка текста

	/// <summary>
	/// Стенограмма набирается крупнее макетных 22 pt: на телефоне её читают
	/// вслух за собеседником, и мелкий кегль тут мешает больше, чем на рации.
	/// </summary>
	private const int DescriptionFontMax = 18;

	/// <summary>
	/// Ужимать почти некуда, и это сознательно: длинный вызов лучше прокрутить,
	/// чем читать его мельче. Ниже этого включается прокрутка.
	/// </summary>
	private const int DescriptionFontMin = 17;

	private const int DescriptionFitAttempts = 8;

	private int _descriptionFitAttempts;

	/// <summary>Крупнее этого заголовок не станет: 36 pt макета в координатах 1280x720.</summary>
	private const int TitleFontMax = 24;

	/// <summary>Мельче — заголовок перестаёт быть заголовком.</summary>
	private const int TitleFontMin = 16;

	private int _titleFitAttempts;

	/// <summary>
	/// Ставит подгонку заголовка в очередь.
	///
	/// Напрямую её звать нельзя: на первом показе рамке ещё не назначили размер,
	/// ширина нулевая, и подгонка молча уходит ни с чем — заголовок остаётся
	/// крупным и наезжает на текст вызова.
	/// </summary>
	private void RequestTitleFit()
	{
		_titleFitAttempts = DescriptionFitAttempts;
		CallDeferred(nameof(FitCallTitle));
	}

	/// <summary>
	/// Подгоняет заголовок вызова под его рамку.
	///
	/// Меряем шрифтом напрямую, а не через GetLineCount: счётчик строк обновится
	/// только после перерисовки, а ответ нужен здесь и сейчас, внутри цикла.
	/// </summary>
	private void FitCallTitle()
	{
		if (_title == null)
		{
			return;
		}

		Font font = _title.GetThemeFont("font");
		float available = _title.Size.Y;
		float width = _title.Size.X;

		if (font == null || available <= 1.0f || width <= 1.0f)
		{
			if (_titleFitAttempts-- > 0)
			{
				CallDeferred(nameof(FitCallTitle));
			}

			return;
		}

		for (int size = TitleFontMax; size >= TitleFontMin; size--)
		{
			_title.AddThemeFontSizeOverride("font_size", size);

			float height = font.GetMultilineStringSize(
				_title.Text,
				HorizontalAlignment.Left,
				width,
				size).Y;

			if (height <= available)
			{
				return;
			}
		}
	}

	private void RequestDescriptionFit()
	{
		_descriptionFitAttempts = DescriptionFitAttempts;
		CallDeferred(nameof(FitDescription));
	}

	/// <summary>
	/// Рамка у стенограммы фиксированная, а длину текста пишут авторы. Здесь цена
	/// ошибки высокая: по описанию вызова игрок решает, принимать его или нет,
	/// и обрезанная последняя строка может стоить ему смены.
	/// </summary>
	private void FitDescription()
	{
		if (_description == null)
		{
			return;
		}

		float available = _description.Size.Y;
		if (available <= 1.0f)
		{
			if (_descriptionFitAttempts-- > 0)
			{
				CallDeferred(nameof(FitDescription));
			}

			return;
		}

		for (int size = DescriptionFontMax; size >= DescriptionFontMin; size--)
		{
			_description.AddThemeFontSizeOverride("normal_font_size", size);
			if (_description.GetContentHeight() <= available)
			{
				_description.ScrollActive = false;
				return;
			}
		}

		_description.ScrollActive = true;
	}
}
