#nullable enable

using Godot;
using Kontur.Core.Api;

/// <summary>
/// Плоский предмет со стола (записка, блокнот), который по клику подлетает к
/// камере и даёт поработать со своим интерфейсом.
///
/// Повторяет настольный режим досье: камера никуда не едет — двигается только
/// предмет, симуляция стоит на паузе, пока игрок читает, выход — esc. Ввод мыши
/// пробрасывается в SubViewport, поэтому кнопки на странице работают.
/// </summary>
public partial class InspectableItemController : Node3D
{
	[Export] public NodePath ViewportInputPath { get; set; } = new("ViewportInput");

	[Export(PropertyHint.Range, "0.1,2.0,0.05")] public float TransitionDuration { get; set; } = 0.45f;

	/// <summary>Расстояние от камеры до страницы в режиме просмотра, метры.</summary>
	[Export(PropertyHint.Range, "0.05,1.5,0.01")] public float ViewDistance { get; set; } = 0.28f;

	/// <summary>Сдвиг предмета в плоскости кадра, метры: X вправо, Y вверх.</summary>
	[Export] public Vector2 ViewOffset { get; set; } = Vector2.Zero;

	/// <summary>
	/// Доворот предмета в плоскости кадра, градусы по часовой стрелке. Нужен
	/// предметам, у которых верх страницы в модели смотрит не по -Z: например у
	/// блокнота пружина идёт по короткой кромке -X, и без доворота он подлетает
	/// лёжа на боку.
	/// </summary>
	[Export(PropertyHint.Range, "-180,180,90")] public float ViewRoll { get; set; }

	/// <summary>Пока предмет читают, симуляция стоит. Досье ведёт себя так же.</summary>
	[Export] public bool PausesSimulation { get; set; } = true;

	private SubViewportInputController _viewportInput = null!;
	private Transform3D _restingTransform;
	private Tween? _transitionTween;
	private FlyPlayer? _viewingPlayer;
	private bool _isViewActive;
	private bool _pausedRuntime;

	public bool IsViewActive => _isViewActive;

	public override void _Ready()
	{
		_viewportInput = GetNode<SubViewportInputController>(ViewportInputPath);
		_restingTransform = GlobalTransform;
	}

	public override void _ExitTree()
	{
		// Сцену могли выгрузить с открытым предметом — иначе симуляция так и
		// осталась бы на паузе.
		ResumeSimulationIfOwned();
	}

	public override void _Input(InputEvent @event)
	{
		if (!_isViewActive)
		{
			return;
		}

		if (@event.IsActionPressed("ui_cancel"))
		{
			CloseView();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (_viewportInput.HandleInput(@event))
		{
			GetViewport().SetInputAsHandled();
		}
	}

	public void OpenView(FlyPlayer player)
	{
		if (_isViewActive)
		{
			return;
		}

		_isViewActive = true;
		_viewingPlayer = player;
		PauseSimulation();
		PresentAtCamera(player.GlobalTransform);
		_viewportInput.BeginInteraction();
		// Фиксируем взгляд там, где он есть: предмет прилетает в текущий кадр, и
		// вертеть головой, пока читаешь, нельзя — страница уедет за край экрана.
		// На выходе взгляд остаётся там же, откуда предмет взяли.
		player.FocusViewAt(player.GlobalTransform, player.Fov, returnsToViewOrigin: true);
	}

	public void CloseView()
	{
		if (!_isViewActive)
		{
			return;
		}

		_isViewActive = false;
		_viewportInput.EndInteraction();
		StartTransition();
		_transitionTween!.TweenProperty(this, "global_transform", _restingTransform, TransitionDuration);
		ResumeSimulationIfOwned();
		_viewingPlayer?.ExitFocusedView();
		_viewingPlayer = null;
	}

	/// <summary>
	/// Ставит предмет перед камерой. Вынесено отдельно от <see cref="OpenView"/>,
	/// чтобы кадр можно было проверять без игрока.
	/// </summary>
	public void PresentAtCamera(Transform3D cameraTransform)
	{
		StartTransition();
		_transitionTween!.TweenProperty(this, "global_transform", GetPresentationTransform(cameraTransform), TransitionDuration);
	}

	/// <summary>
	/// Поза страницы перед камерой.
	///
	/// Страница лежит в плоскости XZ предмета: нормаль — его +Y, верх страницы —
	/// его -Z. Значит, чтобы страница смотрела в камеру и не легла боком, +Y
	/// предмета должен совпасть с «назад» камеры, а -Z — с её «вверх».
	///
	/// <see cref="ViewRoll"/> доворачивает эту рамку вокруг оси взгляда, если
	/// верх страницы в модели смотрит не по -Z.
	/// </summary>
	public Transform3D GetPresentationTransform(Transform3D cameraTransform)
	{
		Basis cameraBasis = cameraTransform.Basis.Orthonormalized();
		// Ось взгляда — +Z камеры (смотрит на игрока), поэтому по часовой стрелке
		// для игрока — это отрицательный угол по правилу правой руки.
		Basis frameBasis = cameraBasis.Rotated(cameraBasis.Z, Mathf.DegToRad(-ViewRoll));
		Vector3 scale = _restingTransform.Basis.Scale;
		var pageBasis = new Basis(
			frameBasis.X * scale.X,
			frameBasis.Z * scale.Y,
			-frameBasis.Y * scale.Z);

		Vector3 origin = cameraTransform.Origin
			- (cameraBasis.Z * ViewDistance)
			+ (cameraBasis.X * ViewOffset.X)
			+ (cameraBasis.Y * ViewOffset.Y);
		return new Transform3D(pageBasis, origin);
	}

	private void PauseSimulation()
	{
		if (!PausesSimulation)
		{
			return;
		}

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

	private void StartTransition()
	{
		_transitionTween?.Kill();
		_transitionTween = CreateTween()
			.SetTrans(Tween.TransitionType.Cubic)
			.SetEase(Tween.EaseType.InOut)
			.SetParallel();
	}
}
