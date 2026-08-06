using Godot;

/// <summary>
/// Медленный проезд камеры по кабинету за подписями главного меню.
///
/// Сам кабинет — обычная сцена <c>MenuOffice.tscn</c>, положенная сюда инстансом:
/// её видно в редакторе, предметы стоят там же, где их поставили в игре, и кадр
/// ставится мышью, а не правкой чисел. Раньше кабинет собирался здесь кодом из
/// игровой сцены, чтобы на ходу выкинуть скрипты и экраны предметов; предметы от
/// этой пересадки вставали на бок, и код убран целиком.
///
/// <c>MenuOffice.tscn</c> — форк поддерева кабинета из <c>main.tscn</c> без
/// камеры игрока. Девять предметов там ссылаются не на игровые сцены, а на копии
/// в <c>scenes/ui/menu/office/</c>: у игровых внутри скрипты, зоны взаимодействия
/// и свои SubViewport'ы, а выпотрошить инстанс из родительской сцены нельзя.
/// Сверх игры в кабинете меню добавлены кадр <c>MenuCamera</c>, луна за окном
/// и <c>Environment</c> — в игре у WorldEnvironment он пустой.
///
/// Копия живёт своей жизнью: подвинете предмет в игре — в меню он останется на
/// старом месте. Ровно так разошёлся и был выброшен отдельный <c>NewOffice.tscn</c>,
/// где лампа и блокнот стояли не там. Меняете расстановку в игре — перенесите её и сюда.
///
/// Свет за окном (<c>MoonBehindWindow</c>, <c>MoonRim</c>, <c>WindowLight</c>) стоит
/// без теней намеренно: стена между ним и комнатой сплошная, и с тенями лунный свет
/// не дошёл бы внутрь. Тёплый ключ и единственные тени даёт настольная лампа.
/// </summary>
public partial class MenuBackdrop : Node3D
{
	/// <summary>Имя камеры внутри кабинета. Ищется в поддереве, а не путём: кабинет — инстанс.</summary>
	[Export] public string CameraName { get; set; } = "MenuCamera";

	/// <summary>
	/// Выключатель движения. Со снятой галкой камера стоит на месте, а SubViewport
	/// перерисовывается один раз: фон превращается в статичный кадр и перестаёт
	/// стоить чего-либо каждый кадр. Запас на слабые машины.
	/// </summary>
	[Export] public bool Animated { get; set; } = true;

	/// <summary>Размах смещения камеры в метрах, по её собственным осям: вбок, вверх, вперёд.</summary>
	[Export] public Vector3 DriftMeters { get; set; } = new(0.11f, 0.035f, 0.07f);

	/// <summary>Размах доворота в градусах: наклон, поворот, крен.</summary>
	[Export] public Vector3 DriftDegrees { get; set; } = new(0.5f, 0.9f, 0.0f);

	/// <summary>Период основной волны. Медленно: движение должно читаться как дыхание, а не как проезд.</summary>
	[Export] public float DriftSeconds { get; set; } = 48.0f;

	/// <summary>На сколько градусов «дышит» угол обзора.</summary>
	[Export] public float FovDrift { get; set; } = 0.7f;

	private Camera3D _camera;
	private Transform3D _rest;
	private float _restFov;
	private double _time;

	public override void _Ready()
	{
		_camera = FindChild(CameraName, true, false) as Camera3D;
		if (_camera == null)
		{
			GD.PushWarning($"[МЕНЮ] Камера «{CameraName}» не найдена в кабинете — фон останется неподвижным.");
		}
		else
		{
			_rest = _camera.Transform;
			_restFov = _camera.Fov;
		}

		if (!Animated)
		{
			FreezeViewport();
		}

		SetProcess(Animated && _camera != null);
	}

	public override void _Process(double delta)
	{
		_time += delta;

		// Две волны с несоизмеримыми периодами: их сумма не повторяется на глазок,
		// поэтому проезд не читается как зацикленный.
		float slow = Mathf.Tau * (float)_time / DriftSeconds;
		float slower = Mathf.Tau * (float)_time / (DriftSeconds * 1.618f);

		var offset = new Vector3(
			Mathf.Sin(slow) * DriftMeters.X,
			Mathf.Sin(slower) * DriftMeters.Y,
			Mathf.Cos(slower) * DriftMeters.Z);

		var turn = new Vector3(
			Mathf.Sin(slower) * Mathf.DegToRad(DriftDegrees.X),
			Mathf.Sin(slow) * Mathf.DegToRad(DriftDegrees.Y),
			Mathf.Sin(slower) * Mathf.DegToRad(DriftDegrees.Z));

		// Смещение — в осях камеры, а не мира: вбок значит вбок от кадра.
		_camera.Transform = new Transform3D(
			_rest.Basis * Basis.FromEuler(turn),
			_rest.Origin + _rest.Basis * offset);

		_camera.Fov = _restFov + Mathf.Sin(slower) * FovDrift;
	}

	/// <summary>Статичный фон: один кадр вместо непрерывной перерисовки.</summary>
	private void FreezeViewport()
	{
		if (GetViewport() is SubViewport viewport)
		{
			viewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
		}
	}
}
