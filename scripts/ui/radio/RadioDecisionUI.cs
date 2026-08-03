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
	private Label _situationLabel = null!;
	private Label _illustrationPlaceholder = null!;
	private Label _illustrationCaption = null!;
	private Label _prompt = null!;
	private readonly List<Button> _optionButtons = new();
	private ShaderMaterial _transitionMaterial = null!;
	private ShaderMaterial _previousScreenBlurMaterial = null!;
	private bool _isTransitionPlaying;
	private bool _layoutInitialized;
	private string _incidentId = string.Empty;
	private string _contentId = string.Empty;
	private IReadOnlyList<RadioOptionOffer> _options = Array.Empty<RadioOptionOffer>();
	private bool _awaitingOutcome;
	private bool _showingOutcome;
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
		_situationLabel.Text = ContentTextResolver.ResolveEntryText(_contentId, string.Empty);

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

	/// <summary>Reuses the radio screen as the result confirmation screen.</summary>
	public void ShowOutcome(MissionOutcomeReady outcome)
	{
		if (!Visible || !string.Equals(_incidentId, outcome.IncidentId, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		_awaitingOutcome = false;
		_showingOutcome = true;
		_contentId = outcome.SummaryContentId ?? string.Empty;
		_header.Text = outcome.IsSuccess
			? "\u041a.\u041e.\u041d.\u0422.\u0423.\u0420.-\u0414  /  \u0418\u0422\u041e\u0413 \u041e\u041f\u0415\u0420\u0410\u0426\u0418\u0418: \u0423\u0421\u041f\u0415\u0425"
			: "\u041a.\u041e.\u041d.\u0422.\u0423.\u0420.-\u0414  /  \u0418\u0422\u041e\u0413 \u041e\u041f\u0415\u0420\u0410\u0426\u0418\u0418: \u0421\u0411\u041e\u0419";
		_situationLabel.Text = ContentTextResolver.ResolveEntryText(_contentId, string.Empty);
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
			if (isConfirm)
			{
				_optionButtons[index].Text = "[ ENTER ] \u041f\u041e\u0414\u0422\u0412\u0415\u0420\u0414\u0418\u0422\u042c \u0418\u0422\u041e\u0413";
			}
		}
	}

	private void ChooseOption(int optionIndex)
	{
		if (_showingOutcome)
		{
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
