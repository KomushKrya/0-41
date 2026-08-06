using Godot;

/// <summary>
/// Точка в центре экрана — куда смотрит камера.
///
/// Взаимодействие в кабинете идёт лучом из середины кадра, но целиться было
/// не по чему: пустой экран, и попадёшь ли по ручке двери, выясняется только
/// по подсветке. Точка убирает эту угадайку.
///
/// Видна, пока игрок крутит камерой, и уходит в двух случаях.
///
/// Первый — появился курсор: открылся любой экран поверх кабинета. Режим
/// спрашиваем у Input, а не храним свой флаг: владелец режима один,
/// <see cref="CursorMode"/>, и любой второй источник правды рано или поздно
/// с ним разойдётся.
///
/// Второй — игрок работает с предметом: сел за компьютер, раскрыл досье,
/// смотрит в блокнот. Курсор при этом остаётся захваченным, так что первой
/// проверки мало. Целиться там уже не во что, а точка повисает поверх экрана
/// компьютера и читается как грязь на стекле.
/// </summary>
public partial class Crosshair : Control
{
	/// <summary>Камера игрока: у неё спрашиваем, не занят ли он предметом.</summary>
	[Export] public NodePath PlayerPath { get; set; } =
		new("../../../NewOffice/PlayerCameraRig/CameraPitch/PlayerCamera");

	private FlyPlayer _player;

	/// <summary>Радиус самой точки в единицах холста.</summary>
	[Export] public float DotRadius { get; set; } = 1.5f;

	/// <summary>Тёмная кайма под точкой: без неё она теряется на светлом кадре.</summary>
	[Export] public float OutlineRadius { get; set; } = 2.6f;

	[Export] public Color DotColor { get; set; } = new(0.749f, 0.722f, 0.714f, 0.85f);

	[Export] public Color OutlineColor { get; set; } = new(0f, 0f, 0f, 0.45f);

	private bool _wasVisible;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		Resized += QueueRedraw;
		Visible = false;

		_player = GetNodeOrNull<FlyPlayer>(PlayerPath);
		if (_player == null)
		{
			// Не ошибка: точка нужна и в отладочных сценах, где игрока нет.
			// Там она просто следует за курсором, без второй проверки.
			GD.PushWarning("Crosshair: игрок не найден, точка не будет прятаться при работе с предметом.");
		}
	}

	public override void _ExitTree()
	{
		Resized -= QueueRedraw;
	}

	public override void _Process(double delta)
	{
		// Переход к предмету и обратно тоже прячем: камера едет сама,
		// целиться в этот момент нечем, а мигнувшая точка бросается в глаза.
		bool busyWithObject = _player != null
			&& (_player.IsViewFocused || _player.IsCameraTransitioning);

		bool shouldShow = Input.MouseMode == Input.MouseModeEnum.Captured && !busyWithObject;
		if (shouldShow == _wasVisible)
		{
			return;
		}

		_wasVisible = shouldShow;
		Visible = shouldShow;
	}

	public override void _Draw()
	{
		Vector2 centre = Size * 0.5f;
		DrawCircle(centre, OutlineRadius, OutlineColor);
		DrawCircle(centre, DotRadius, DotColor);
	}
}
