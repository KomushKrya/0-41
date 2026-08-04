using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;

[Tool]
public partial class MissionDispatchUI : Control, IComputerScreen
{
	[Export] public NodePath CallTitlePath { get; set; } = new("CallSheet/CallTitle");
	[Export] public NodePath CallTranscriptPath { get; set; } = new("CallSheet/CallTranscript");
	[Export] public NodePath DispatchButtonPath { get; set; } = new("DispatchButton");
	[Export] public NodePath FeedbackPath { get; set; } = new("Feedback");
	[Export] public UiPreviewData PreviewData { get; set; }

	private readonly Control[] _employeeSlots = new Control[3];
	private readonly Label[] _employeeNames = new Label[3];
	private Button _dispatchButton = null!;
	private Label _feedback = null!;

	public event Action<int>? EmployeeSlotRequested;
	public event Action? DispatchRequested;

	public override void _Ready()
	{
		_employeeSlots[0] = GetNode<Control>("EmployeeSlotOne");
		_employeeSlots[1] = GetNode<Control>("EmployeeSlotTwo");
		_employeeSlots[2] = GetNode<Control>("EmployeeSlotThree");
		_employeeNames[0] = GetNode<Label>("EmployeeSlotOne/Empty");
		_employeeNames[1] = GetNode<Label>("EmployeeSlotTwo/Empty");
		_employeeNames[2] = GetNode<Label>("EmployeeSlotThree/Empty");
		_dispatchButton = GetNode<Button>(DispatchButtonPath);
		_feedback = GetNode<Label>(FeedbackPath);
		if (Engine.IsEditorHint())
		{
			ApplyEditorPreview();
			return;
		}

		for (int index = 0; index < _employeeSlots.Length; index++)
		{
			int capturedIndex = index;
			_employeeSlots[index].GuiInput += input =>
			{
				if (input is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left })
				{
					EmployeeSlotRequested?.Invoke(capturedIndex);
				}
			};
		}

		_dispatchButton.Pressed += () => DispatchRequested?.Invoke();
	}

	public void ApplyEditorPreview(UiPreviewData preview = null)
	{
		preview ??= PreviewData;
		if (preview == null)
		{
			return;
		}

		GetNode<Label>(CallTitlePath).Text = preview.Title;
		GetNode<Label>(CallTranscriptPath).Text = preview.Description;
		SetEmployeeNames(preview.GetListItems());
		_feedback.Text = preview.Status;
		_dispatchButton.Disabled = true;
	}

	public void OnScreenOpened()
	{
		_feedback.Text = string.Empty;
	}

	public void SetCallDetails(string title, string transcript)
	{
		GetNode<Label>(CallTitlePath).Text = title;
		GetNode<Label>(CallTranscriptPath).Text = transcript;
	}

	public void SetEmployeeNames(IReadOnlyList<string> employeeNames)
	{
		for (int index = 0; index < _employeeNames.Length; index++)
		{
			_employeeNames[index].Text = index < employeeNames.Count && !string.IsNullOrEmpty(employeeNames[index])
				? employeeNames[index]
				: "СЛОТ СВОБОДЕН";
		}
	}

	public void SetFeedback(string message)
	{
		_feedback.Text = message;
	}
}
