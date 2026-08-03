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
	private Label _description = null!;
	private TextureRect _illustration = null!;
	private Label _illustrationPlaceholder = null!;
	private Button _confirmButton = null!;
	private Action? _confirmedCallback;
	private bool _pausedRuntime;
	private Input.MouseModeEnum _previousMouseMode;

	public override void _Ready()
	{
		_previousScreenBlur = GetNode<ColorRect>(PreviousScreenBlurPath);
		_title = GetNode<Label>(TitlePath);
		_description = GetNode<Label>(DescriptionPath);
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
		_description.Text = description;
		_illustration.Texture = illustration;
		_illustration.Visible = illustration != null;
		_illustrationPlaceholder.Visible = illustration == null;
		_confirmedCallback = confirmedCallback;

		KonturRuntime runtime = KonturRuntime.Get(this);
		if (runtime != null && runtime.IsReady && !runtime.IsPaused)
		{
			runtime.IsPaused = true;
			_pausedRuntime = true;
		}

		_previousMouseMode = Input.MouseMode;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		ShowModal();
	}

	/// <summary>
	/// Проверка вёрстки окна без запущенной смены. Реплика выдумана и намеренно
	/// осталась строкой в коде: в каталоге текстов ей не место — это не игровой
	/// вызов, а образец для отладки.
	/// </summary>
	public void ShowTestWindow()
	{
		ShowCallAcceptance(
			Content.Label("ui_computer_incoming"),
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
			KonturRuntime runtime = KonturRuntime.Get(this);
			if (runtime != null)
			{
				runtime.IsPaused = false;
			}

			_pausedRuntime = false;
		}

		Input.MouseMode = _previousMouseMode;
		confirmedCallback?.Invoke();
	}
}
