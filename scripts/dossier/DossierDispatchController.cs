#nullable enable
using System;
using Godot;
using Kontur.Core.Api;

[Tool]
public partial class DossierDispatchController : Node3D
{
	[Export] public NodePath DossierUiPath { get; set; } = new("DossierViewport/DossierUI");
	[Export] public NodePath VisualRootPath { get; set; } = new("VisualRoot");
	[Export] public NodePath LeftPagePath { get; set; } = new("VisualRoot/LeftPage");
	[Export] public NodePath DispatchDisplayPosePath { get; set; } = new("../DossierDisplayPose");
	[Export(PropertyHint.Range, "0.1,2.0,0.05")] public float TransitionDuration { get; set; } = 0.45f;
	[Export(PropertyHint.Range, "0.0,0.1,0.001")] public float ClosedCoverLift { get; set; } = 0.026f;
	[Export]
	public bool PreviewDispatchPose
	{
		get => _previewDispatchPose;
		set
		{
			if (_previewDispatchPose == value)
			{
				return;
			}

			_previewDispatchPose = value;
			if (IsInsideTree() && Engine.IsEditorHint())
			{
				CallDeferred(nameof(RefreshEditorPreview));
			}
		}
	}

	private DossierDispatchUI _dossierUi = null!;
	private ComputerUI? _computerUi;
	private int _slotIndex;
	private Node3D _dispatchDisplayPose = null!;
	private Node3D _visualRoot = null!;
	private Node3D _leftPage = null!;
	private Transform3D _restingTransform;
	private Transform3D _editorRestingTransform;
	private Transform3D _openLeftPageTransform;
	private bool _previewDispatchPose;
	private bool _runtimeReady;
	private Tween? _transitionTween;

	public event Action? SelectionConfirmed;

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
		{
			_visualRoot = GetNode<Node3D>(VisualRootPath);
			_leftPage = GetNode<Node3D>(LeftPagePath);
			_openLeftPageTransform = _leftPage.Transform;
			_editorRestingTransform = GlobalTransform;
			Show();
			CallDeferred(nameof(RefreshEditorPreview));
			return;
		}

		_dossierUi = GetNode<DossierDispatchUI>(DossierUiPath);
		_dispatchDisplayPose = GetDispatchDisplayPose()
			?? throw new System.InvalidOperationException("DossierDispatchController: DossierDisplayPose is not available.");
		_visualRoot = GetNode<Node3D>(VisualRootPath);
		_leftPage = GetNode<Node3D>(LeftPagePath);
		_openLeftPageTransform = _leftPage.Transform;
		_restingTransform = GlobalTransform;
		_dossierUi.EmployeeConfirmed += ConfirmEmployee;
		_runtimeReady = true;
		Show();
		SetClosedImmediately();
	}

	public override void _ExitTree()
	{
		if (_runtimeReady)
		{
			_dossierUi.EmployeeConfirmed -= ConfirmEmployee;
		}
	}

	/// <summary>Shows the currently selected A/B placement directly in the Godot editor.</summary>
	public void RefreshEditorPreview()
	{
		if (!Engine.IsEditorHint())
		{
			return;
		}

		_visualRoot ??= GetNodeOrNull<Node3D>(VisualRootPath);
		if (_visualRoot == null)
		{
			return;
		}

		if (!_previewDispatchPose)
		{
			GlobalTransform = _editorRestingTransform;
			_visualRoot.Rotation = Vector3.Zero;
			// Keep the authored open transform in the editor. Applying the runtime closed
			// transform here dirtied the scene and accumulated ClosedCoverLift on reload.
			_leftPage.Transform = _openLeftPageTransform;
			Show();
			return;
		}

		_editorRestingTransform = GlobalTransform;
		Node3D? pose = GetDispatchDisplayPose();
		if (pose == null)
		{
			return;
		}

		GlobalTransform = pose.GlobalTransform;
		_visualRoot.Rotation = new Vector3(Mathf.Pi * 0.5f, 0.0f, 0.0f);
		_leftPage.Transform = _openLeftPageTransform;
		Show();
	}

	public void OpenForDispatch(ComputerUI computerUi, int slotIndex)
	{
		_computerUi = computerUi;
		_slotIndex = slotIndex;
		Show();
		AnimateToDispatchPose();
		_dossierUi.OpenForDispatch(computerUi);
	}

	public void CloseDispatch()
	{
		AnimateToRestingPose();
		_computerUi = null;
	}

	public void OpenFromDesk()
	{
		if (!_runtimeReady || _computerUi != null)
		{
			return;
		}

		Show();
		StartTransition();
		_transitionTween!.TweenProperty(_leftPage, "transform", _openLeftPageTransform, TransitionDuration);
	}

	private void AnimateToDispatchPose()
	{
		StartTransition();
		_transitionTween!.TweenProperty(this, "global_transform", _dispatchDisplayPose.GlobalTransform, TransitionDuration);
		_transitionTween.Parallel().TweenProperty(
			_visualRoot,
			"rotation",
			new Vector3(Mathf.Pi * 0.5f, 0.0f, 0.0f),
			TransitionDuration);
		_transitionTween.Parallel().TweenProperty(_leftPage, "transform", _openLeftPageTransform, TransitionDuration);
	}

	private void AnimateToRestingPose()
	{
		StartTransition();
		_transitionTween!.TweenProperty(this, "global_transform", _restingTransform, TransitionDuration);
		_transitionTween.Parallel().TweenProperty(_visualRoot, "rotation", Vector3.Zero, TransitionDuration);
		_transitionTween.Parallel().TweenProperty(_leftPage, "transform", GetClosedLeftPageTransform(), TransitionDuration);
	}

	private void SetClosedImmediately()
	{
		_visualRoot.Rotation = Vector3.Zero;
		SetClosedPageTransform();
	}

	private void SetClosedPageTransform()
	{
		_leftPage.Transform = GetClosedLeftPageTransform();
	}

	private Transform3D GetClosedLeftPageTransform()
	{
		Transform3D closedTransform = _openLeftPageTransform;
		closedTransform.Origin += Vector3.Up * ClosedCoverLift;
		closedTransform.Basis = new Basis(Vector3.Back, Mathf.Pi) * closedTransform.Basis;
		return closedTransform;
	}

	private void StartTransition()
	{
		_transitionTween?.Kill();
		_transitionTween = CreateTween()
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.InOut)
			.SetParallel();
	}

	private Node3D? GetDispatchDisplayPose()
	{
		return GetNodeOrNull<Node3D>(DispatchDisplayPosePath);
	}

	private void ConfirmEmployee(EmployeeView employee)
	{
		if (_computerUi == null || !_computerUi.AssignEmployeeToDispatchSlot(_slotIndex, employee))
		{
			return;
		}

		CloseDispatch();
		SelectionConfirmed?.Invoke();
	}
}
