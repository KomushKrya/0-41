using Godot;
using System;

/// <summary>Central phone-call window over a blurred current frame.</summary>
public partial class PhoneCallAcceptanceUI : Control
{
	[Export] public NodePath PreviousScreenBlurPath { get; set; } = new("PreviousScreenBlur");
	[Export] public NodePath TitlePath { get; set; } = new("ContentFrame/CallTitle");
	[Export] public NodePath DescriptionPath { get; set; } = new("ContentFrame/Description");
	[Export] public NodePath IllustrationPath { get; set; } = new("ContentFrame/IllustrationPanel/Illustration");
	[Export] public NodePath IllustrationPlaceholderPath { get; set; } = new("ContentFrame/IllustrationPanel/Placeholder");
	[Export] public NodePath ConfirmButtonPath { get; set; } = new("ContentFrame/ConfirmButton");

	private ColorRect _previousScreenBlur = null!;
	private Label _title = null!;
	/// <summary>Стенограмма приходит с разметкой движка, поэтому не Label.</summary>
	private RichTextLabel _description = null!;
	private TextureRect _illustration = null!;
	private Label _illustrationPlaceholder = null!;
	private Button _confirmButton = null!;
	private Action? _confirmedCallback;
	private bool _pausedRuntime;

	public override void _Ready()
	{
		_previousScreenBlur = GetNode<ColorRect>(PreviousScreenBlurPath);
		_title = GetNode<Label>(TitlePath);
		_description = GetNode<RichTextLabel>(DescriptionPath);
		_illustration = GetNode<TextureRect>(IllustrationPath);
		_illustrationPlaceholder = GetNode<Label>(IllustrationPlaceholderPath);
		_confirmButton = GetNode<Button>(ConfirmButtonPath);
		_confirmButton.Pressed += CloseCall;
	}

	public override void _ExitTree()
	{
		_confirmButton.Pressed -= CloseCall;
	}

	public void ShowCallAcceptance(
		string title,
		string description,
		Texture2D illustration = null,
		Action? confirmedCallback = null)
	{
		_title.Text = title;
		FitCallTitle();
		_description.Text = description;
		RequestDescriptionFit();
		_illustration.Texture = illustration;
		_illustration.Visible = illustration != null;
		_illustrationPlaceholder.Visible = illustration == null;
		_confirmedCallback = confirmedCallback;

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null && runtime.IsReady && !runtime.IsPaused)
		{
			runtime.IsPaused = true;
			_pausedRuntime = true;
		}

		CursorMode.Show(this);
		ShowModal();
	}

	public void ShowTestWindow()
	{
		ShowCallAcceptance(
			"ВХОДЯЩИЙ ВЫЗОВ",
			"Диспетчерская 041. Поступило сообщение из жилого сектора: жильцы слышат шум в закрытой квартире. Требуется подтвердить принятие вызова.");
	}

	private void ShowModal()
	{
		Show();
		_previousScreenBlur.Show();
	}

	private void CloseCall()
	{
		Action? confirmedCallback = _confirmedCallback;
		_confirmedCallback = null;
		_previousScreenBlur.Hide();
		Hide();
		if (_pausedRuntime)
		{
			GameRuntime runtime = GameRuntime.Get(this);
			if (runtime != null)
			{
				runtime.IsPaused = false;
			}

			_pausedRuntime = false;
		}

		CursorMode.Hide(this);
		confirmedCallback?.Invoke();
	}

	// ------------------------------------------------------------------ подгонка текста

	private const int DescriptionFontMax = 15;
	private const int DescriptionFontMin = 11;
	private const int DescriptionFitAttempts = 8;

	private int _descriptionFitAttempts;

	/// <summary>Крупнее этого заголовок вызова не станет: размер из сцены.</summary>
	private const int TitleFontMax = 23;

	/// <summary>Мельче — заголовок перестаёт быть заголовком.</summary>
	private const int TitleFontMin = 15;

	/// <summary>
	/// Подгоняет заголовок вызова под его рамку.
	///
	/// Заголовок переносится по словам, но вниз расти ему некуда: следом сразу
	/// идёт описание. «Звонок от местных жителей. Несколько голосов, перебивают
	/// друг друга» разворачивался в четыре строки и наезжал на текст вызова.
	///
	/// Меряем шрифтом напрямую, а не через GetLineCount: счётчик строк обновится
	/// только после перерисовки, а нам нужен ответ здесь и сейчас, внутри цикла.
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
	/// То же, что у рации: рамка фиксированная, длину текста пишут авторы.
	/// Здесь цена ошибки выше — по описанию вызова игрок решает, принимать его
	/// или нет, и обрезанная последняя строка может стоить ему смены.
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
