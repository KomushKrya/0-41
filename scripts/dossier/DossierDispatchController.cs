#nullable enable

using System;
using Godot;
using Kontur.Core.Api;

public partial class DossierDispatchController : Node3D
{
	[Export] public NodePath DossierUiPath { get; set; } = new("DossierViewport/DossierUI");
	[Export] public NodePath VisualRootPath { get; set; } = new("VisualRoot");
	[Export] public NodePath LeftPagePath { get; set; } = new("VisualRoot/CoverPivot");
	[Export] public NodePath DispatchComputerPath { get; set; } = new("../DeskComputer");
	[Export] public DossierPresentationLayout PresentationLayout { get; set; } = null!;
	[Export] public Transform3D OpenLeftPageTransform { get; set; } = Transform3D.Identity;
	[Export(PropertyHint.Range, "0.1,2.0,0.05")] public float TransitionDuration { get; set; } = 0.45f;
	[Export(PropertyHint.Range, "0.0,0.1,0.001")] public float ClosedCoverLift { get; set; } = 0.026f;

	private DossierDispatchUI _dossierUi = null!;
	private ComputerUI? _computerUi;
	private int _slotIndex;
	private Node3D _dispatchComputer = null!;
	private Node3D _visualRoot = null!;
	private Node3D _leftPage = null!;
	private Transform3D _restingTransform;
	private Transform3D _openLeftPageTransform;
	private Tween? _transitionTween;

	public event Action? SelectionConfirmed;

	public override void _Ready()
	{
		_dossierUi = GetNode<DossierDispatchUI>(DossierUiPath);
		_dispatchComputer = GetNode<Node3D>(DispatchComputerPath);
		if (PresentationLayout == null)
		{
			throw new InvalidOperationException("DossierDispatchController: presentation layout is not assigned.");
		}

		_visualRoot = GetNode<Node3D>(VisualRootPath);
		_leftPage = GetNode<Node3D>(LeftPagePath);
		_openLeftPageTransform = OpenLeftPageTransform;
		_restingTransform = GlobalTransform;
		_dossierUi.EmployeeConfirmed += ConfirmEmployee;
		Show();
		SetClosedImmediately();
	}

	public override void _ExitTree()
	{
		if (_dossierUi != null)
		{
			_dossierUi.EmployeeConfirmed -= ConfirmEmployee;
		}
	}

	public void OpenForDispatch(ComputerUI computerUi, int slotIndex)
	{
		_computerUi = computerUi;
		_slotIndex = slotIndex;
		Show();
		AnimateToDispatchPose();
		_dossierUi.OpenForDispatch(computerUi);
	}

	public Transform3D GetDispatchPresentationTransform()
	{
		return GetDispatchTargetTransform();
	}

	public void CloseDispatch()
	{
		AnimateToRestingPose();
		_computerUi = null;
	}

	public void OpenFromDesk()
	{
		if (_computerUi != null)
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
		_transitionTween!.TweenProperty(this, "global_transform", GetDispatchTargetTransform(), TransitionDuration);
		// The presentation layout already stores the exact open-dossier orientation.
		// Applying an extra 90° here made the runtime shot differ from DossierWorkbench.
		_transitionTween.Parallel().TweenProperty(_visualRoot, "rotation", Vector3.Zero, TransitionDuration);
		_transitionTween.Parallel().TweenProperty(_leftPage, "transform", _openLeftPageTransform, TransitionDuration);
	}

	private void AnimateToRestingPose()
	{
		StartTransition();
		_transitionTween!.TweenProperty(this, "global_transform", _restingTransform, TransitionDuration);
		_transitionTween.Parallel().TweenProperty(_visualRoot, "rotation", Vector3.Zero, TransitionDuration);
		_transitionTween.Parallel().TweenProperty(_leftPage, "transform", GetClosedLeftPageTransform(), TransitionDuration);
	}

	private Transform3D GetDispatchTargetTransform()
	{
		return _dispatchComputer.GlobalTransform * PresentationLayout.DossierTransform;
	}

	private void SetClosedImmediately()
	{
		_visualRoot.Rotation = Vector3.Zero;
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
