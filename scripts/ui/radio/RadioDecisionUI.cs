using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;

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
	[Export] public NodePath HeaderPath { get; set; } = new("TransitionGroup/ScreenContent/Header");
	[Export] public NodePath SituationLabelPath { get; set; } = new("TransitionGroup/ScreenContent/ContentFrame/SituationLabel");
	[Export] public NodePath OptionOneButtonPath { get; set; } = new("TransitionGroup/ScreenContent/ContentFrame/OptionOneButton");
	[Export] public NodePath OptionTwoButtonPath { get; set; } = new("TransitionGroup/ScreenContent/ContentFrame/OptionTwoButton");
	[Export] public NodePath OptionThreeButtonPath { get; set; } = new("TransitionGroup/ScreenContent/ContentFrame/OptionThreeButton");

	private VideoStreamPlayer _transitionPlayer = null!;
	private CanvasGroup _transitionGroup = null!;
	private Control _screenContent = null!;
	private ColorRect _previousScreenBlur = null!;
	private ColorRect _inputBlocker = null!;
	private Label _header = null!;
	private Label _situationLabel = null!;
	private readonly List<Button> _optionButtons = new();
	private ShaderMaterial _transitionMaterial = null!;
	private ShaderMaterial _previousScreenBlurMaterial = null!;
	private bool _isTransitionPlaying;
	private bool _layoutInitialized;
	private string _incidentId = string.Empty;
	private IReadOnlyList<RadioOptionView> _options = Array.Empty<RadioOptionView>();
	private bool _pausedRuntime;
	private Input.MouseModeEnum _previousMouseMode;

	public override void _Ready()
	{
		_transitionPlayer = GetNode<VideoStreamPlayer>(TransitionPlayerPath);
		_transitionGroup = GetNode<CanvasGroup>(TransitionGroupPath);
		_screenContent = GetNode<Control>(ScreenContentPath);
		_previousScreenBlur = GetNode<ColorRect>(PreviousScreenBlurPath);
		_inputBlocker = GetNode<ColorRect>(InputBlockerPath);
		_header = GetNode<Label>(HeaderPath);
		_situationLabel = GetNode<Label>(SituationLabelPath);
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

	/// <summary>Открывает экран по рации и временно останавливает симуляцию.</summary>
	public void ShowRadioDecision(string incidentId, string missionTitle, string situationText, IReadOnlyList<RadioOptionView> options)
	{
		_incidentId = incidentId;
		_options = options ?? Array.Empty<RadioOptionView>();
		_header.Text = $"К.О.Н.Т.У.Р.-Д  /  РАДИО: {missionTitle}";
		_situationLabel.Text = situationText;

		for (int index = 0; index < _optionButtons.Count; index++)
		{
			bool hasOption = index < _options.Count;
			_optionButtons[index].Visible = hasOption;
			_optionButtons[index].Disabled = !hasOption;
			if (hasOption)
			{
				_optionButtons[index].Text = $"[ {index + 1} ] {_options[index].Text}";
			}
		}

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null && runtime.IsReady && !runtime.IsPaused)
		{
			runtime.IsPaused = true;
			_pausedRuntime = true;
		}

		_previousMouseMode = Input.MouseMode;
		Input.MouseMode = Input.MouseModeEnum.Visible;
		ShowWithTransition();
	}

	public void StopTransition()
	{
		_isTransitionPlaying = false;
		_transitionPlayer.Stop();
		_transitionPlayer.Hide();
		_previousScreenBlur.Hide();
		_inputBlocker.Hide();
	}

	private void ChooseOption(int optionIndex)
	{
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

		CloseDecision();
	}

	private void CloseDecision()
	{
		StopTransition();
		Hide();
		_incidentId = string.Empty;
		_options = Array.Empty<RadioOptionView>();
		if (_pausedRuntime)
		{
			GameRuntime runtime = GameRuntime.Get(this);
			if (runtime != null)
			{
				runtime.IsPaused = false;
			}

			_pausedRuntime = false;
		}

		Input.MouseMode = _previousMouseMode;
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
