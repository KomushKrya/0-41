using System;
using Godot;

/// <summary>
/// Полноэкранный экран радио-решения. Чёрно-белое переходное видео управляет
/// прозрачностью всей сцены: чёрное оставляет прошлый экран, белое открывает UI.
/// </summary>
public partial class RadioDecisionUI : Control
{
	[Export] public NodePath TransitionPlayerPath { get; set; } = new("TransitionPlayer");
	[Export] public NodePath TransitionGroupPath { get; set; } = new("TransitionGroup");
	[Export] public NodePath ScreenContentPath { get; set; } = new("TransitionGroup/ScreenContent");
	[Export] public NodePath PreviousScreenBlurPath { get; set; } = new("PreviousScreenBlur");
	[Export] public NodePath InputBlockerPath { get; set; } = new("InputBlocker");

	private VideoStreamPlayer _transitionPlayer = null!;
	private CanvasGroup _transitionGroup = null!;
	private Control _screenContent = null!;
	private ColorRect _previousScreenBlur = null!;
	private ColorRect _inputBlocker = null!;
	private ShaderMaterial _transitionMaterial = null!;
	private ShaderMaterial _previousScreenBlurMaterial = null!;
	private bool _isTransitionPlaying;
	private bool _layoutInitialized;

	public override void _Ready()
	{
		_transitionPlayer = GetNode<VideoStreamPlayer>(TransitionPlayerPath);
		_transitionGroup = GetNode<CanvasGroup>(TransitionGroupPath);
		_screenContent = GetNode<Control>(ScreenContentPath);
		_previousScreenBlur = GetNode<ColorRect>(PreviousScreenBlurPath);
		_inputBlocker = GetNode<ColorRect>(InputBlockerPath);
		_transitionMaterial = _transitionGroup.Material as ShaderMaterial
			?? throw new InvalidOperationException("RadioDecisionUI: TransitionGroup requires a ShaderMaterial.");
		_previousScreenBlurMaterial = _previousScreenBlur.Material as ShaderMaterial
			?? throw new InvalidOperationException("RadioDecisionUI: PreviousScreenBlur requires a ShaderMaterial.");
		_transitionPlayer.Finished += OnTransitionFinished;
		Resized += FitScreenContentToWindow;
		FitScreenContentToWindow();
	}

	public override void _Process(double delta)
	{
		if (!_layoutInitialized)
		{
			FitScreenContentToWindow();
			_layoutInitialized = _screenContent.Size.X > 0f && _screenContent.Size.Y > 0f;
		}

		if (_isTransitionPlaying)
		{
			SetMaskTexture();
		}
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
		Show();
		_transitionGroup.Show();
		_transitionGroup.Modulate = Colors.White;
		_inputBlocker.Show();
		_previousScreenBlur.Show();
		_transitionPlayer.Stop();
		_transitionPlayer.Show();
		_transitionPlayer.Play();
		_isTransitionPlaying = true;
		SetMaskTexture();
	}

	public void StopTransition()
	{
		_isTransitionPlaying = false;
		_transitionPlayer.Stop();
		_transitionPlayer.Hide();
		_previousScreenBlur.Hide();
		_inputBlocker.Hide();
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
}
