#nullable enable

using System;
using Godot;
using Kontur.Core.Api;

public partial class DossierDispatchController : Node3D
{
	[Export] public NodePath SpreadPath { get; set; } = new("DossierSpread");
	[Export] public NodePath ViewportInputPath { get; set; } = new("DossierViewportInput");
	[Export] public NodePath VisualRootPath { get; set; } = new("VisualRoot");
	[Export] public NodePath LeftPagePath { get; set; } = new("VisualRoot/CoverPivot");
	[Export] public NodePath DispatchComputerPath { get; set; } = new("../DeskComputer");
	[Export] public DossierPresentationLayout PresentationLayout { get; set; } = null!;
	[Export] public Transform3D OpenLeftPageTransform { get; set; } = Transform3D.Identity;
	[Export(PropertyHint.Range, "0.1,2.0,0.05")] public float TransitionDuration { get; set; } = 0.45f;
	[Export(PropertyHint.Range, "0.0,0.1,0.001")] public float ClosedCoverLift { get; set; } = 0.026f;

	/// <summary>Расстояние от камеры до разворота в настольном режиме, метры.</summary>
	[Export(PropertyHint.Range, "0.1,1.5,0.01")] public float DeskViewDistance { get; set; } = 0.19f;

	/// <summary>
	/// Сдвиг папки в плоскости кадра, метры: X вправо, Y вверх.
	///
	/// Разворот не центрирован относительно корня папки — обе страницы уходят
	/// от него влево, — поэтому без сдвига в кадре он оказывается смещённым.
	/// </summary>
	[Export] public Vector2 DeskViewOffset { get; set; } = new(0.095f, 0.0f);

	private DossierSpread _spread = null!;
	private SubViewportInputController _viewportInput = null!;
	private ComputerUI? _computerUi;
	private int _slotIndex;
	private Node3D _dispatchComputer = null!;
	private Node3D _visualRoot = null!;
	private Node3D _leftPage = null!;
	private Transform3D _restingTransform;
	private Transform3D _openLeftPageTransform;
	private Tween? _transitionTween;

	private FlyPlayer? _deskPlayer;
	private bool _isDeskModeActive;
	private bool _pausedRuntime;

	public event Action? SelectionConfirmed;

	public override void _Ready()
	{
		_spread = GetNode<DossierSpread>(SpreadPath);
		_viewportInput = GetNode<SubViewportInputController>(ViewportInputPath);
		_dispatchComputer = GetNode<Node3D>(DispatchComputerPath);
		if (PresentationLayout == null)
		{
			throw new InvalidOperationException("DossierDispatchController: presentation layout is not assigned.");
		}

		_visualRoot = GetNode<Node3D>(VisualRootPath);
		_leftPage = GetNode<Node3D>(LeftPagePath);
		_openLeftPageTransform = OpenLeftPageTransform;
		_restingTransform = GlobalTransform;
		_spread.EmployeeConfirmed += ConfirmEmployee;
		Show();
		SetClosedImmediately();
	}

	public override void _ExitTree()
	{
		if (_spread != null)
		{
			_spread.EmployeeConfirmed -= ConfirmEmployee;
		}

		// Мод мог остаться активным, если сцену выгрузили при открытом досье:
		// без этого симуляция так и осталась бы на паузе.
		ResumeSimulationIfOwned();
	}

	public override void _Input(InputEvent @event)
	{
		if (!_isDeskModeActive)
		{
			return;
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			CloseFromDesk();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_viewportInput.HandleInput(@event))
		{
			GetViewport().SetInputAsHandled();
		}
	}

	public void OpenForDispatch(ComputerUI computerUi, int slotIndex)
	{
		_computerUi = computerUi;
		_slotIndex = slotIndex;
		Show();
		AnimateToDispatchPose();
		_spread.OpenForDispatch(computerUi);
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

	/// <summary>
	/// Досье открыто прямо со стола: папка сама подлетает к камере, встаёт
	/// развором перпендикулярно взгляду и держит симуляцию на паузе, пока игрок
	/// читает. Камера при этом не едет никуда — двигается только папка. Выход — esc.
	/// </summary>
	public void OpenFromDesk(FlyPlayer player)
	{
		// Режим отправки главнее: он ждёт от игрока выбора сотрудника, и
		// перехватывать у него камеру на полпути нельзя.
		if (_computerUi != null || _isDeskModeActive)
		{
			return;
		}

		_isDeskModeActive = true;
		_deskPlayer = player;
		PauseSimulation();
		PresentAtCamera(player.GlobalTransform);
		_viewportInput.BeginInteraction();
		// Фиксируем взгляд там, где он есть: папка прилетает в текущий кадр, и
		// уводить камеру некуда — но и вертеть головой, пока читаешь, нельзя,
		// иначе разворот уедет за край экрана. На выходе взгляд остаётся там же,
		// откуда открывали.
		player.FocusViewAt(player.GlobalTransform, player.Fov, returnsToViewOrigin: true);
	}

	/// <summary>
	/// Ставит раскрытый разворот перед камерой. Вынесено отдельно от
	/// <see cref="OpenFromDesk"/>, чтобы кадр можно было проверять без игрока.
	/// </summary>
	public void PresentAtCamera(Transform3D cameraTransform)
	{
		Show();
		_spread.OpenForBrowsing();
		StartTransition();
		_transitionTween!.TweenProperty(this, "global_transform", GetDeskPresentationTransform(cameraTransform), TransitionDuration);
		_transitionTween.Parallel().TweenProperty(_visualRoot, "rotation", Vector3.Zero, TransitionDuration);
		_transitionTween.Parallel().TweenProperty(_leftPage, "transform", _openLeftPageTransform, TransitionDuration);
	}

	/// <summary>
	/// Поза папки перед камерой, независимая от того, где стоит компьютер.
	///
	/// Страницы лежат в плоскости XZ папки: нормаль — её +Y, верх страницы —
	/// её -Z. Значит, чтобы разворот смотрел в камеру и не лежал боком, +Y папки
	/// должен совпасть с «назад» камеры, а -Z — с её «вверх».
	/// </summary>
	public Transform3D GetDeskPresentationTransform(Transform3D cameraTransform)
	{
		Basis cameraBasis = cameraTransform.Basis.Orthonormalized();
		Vector3 scale = _restingTransform.Basis.Scale;
		var pageBasis = new Basis(
			cameraBasis.X * scale.X,
			cameraBasis.Z * scale.Y,
			-cameraBasis.Y * scale.Z);

		Vector3 origin = cameraTransform.Origin
			- (cameraBasis.Z * DeskViewDistance)
			+ (cameraBasis.X * DeskViewOffset.X)
			+ (cameraBasis.Y * DeskViewOffset.Y);
		return new Transform3D(pageBasis, origin);
	}

	public void CloseFromDesk()
	{
		if (!_isDeskModeActive)
		{
			return;
		}

		_isDeskModeActive = false;
		_viewportInput.EndInteraction();
		AnimateToRestingPose();
		ResumeSimulationIfOwned();
		_deskPlayer?.ExitFocusedView();
		_deskPlayer = null;
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

	private void PauseSimulation()
	{
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null && runtime.IsReady && !runtime.IsPaused)
		{
			runtime.IsPaused = true;
			_pausedRuntime = true;
		}
	}

	private void ResumeSimulationIfOwned()
	{
		if (!_pausedRuntime)
		{
			return;
		}

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null)
		{
			runtime.IsPaused = false;
		}

		_pausedRuntime = false;
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
