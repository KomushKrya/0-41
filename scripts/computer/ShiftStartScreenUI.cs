#nullable enable

using Godot;
using Kontur.Core.Api;

/// <summary>
/// Заставка терминала до начала смены: пустой экран с одной кнопкой.
///
/// Пока смена не начата, остальные разделы бессмысленны — сотрудников некуда
/// отправлять, вызовов нет, — поэтому терминал не даёт по ним ходить, а вкладки
/// внизу горят приглушённым цветом: видно, что они есть и что они появятся.
///
/// Экран ничего не решает сам: нажатие уходит наверх, в <see cref="ComputerUI"/>,
/// а смену начинает и выводит игрока из-за монитора тот, кто знает про кабинет.
/// </summary>
public partial class ShiftStartScreenUI : Control, IComputerScreen
{
	private Label _day = null!;
	private Button _start = null!;

	/// <summary>Игрок нажал «Приступить к смене».</summary>
	public event System.Action? Confirmed;

	public override void _Ready()
	{
		VBoxContainer column = DosTerminal.CreateFramedColumn(string.Empty, out PanelContainer frame);
		frame.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		AddChild(frame);

		// Кнопка стоит по центру пустого поля: на этом экране больше нечего
		// читать, и искать её по углам игроку незачем.
		var center = new CenterContainer { SizeFlagsVertical = SizeFlags.ExpandFill };
		column.AddChild(center);

		var stack = new VBoxContainer();
		stack.AddThemeConstantOverride("separation", 12);
		center.AddChild(stack);

		_day = DosTerminal.CreateLine(string.Empty, DosTerminal.TextDim);
		_day.HorizontalAlignment = HorizontalAlignment.Center;
		stack.AddChild(_day);

		_start = DosTerminal.CreateRow(StartCaption);
		_start.Alignment = HorizontalAlignment.Center;
		_start.SizeFlagsHorizontal = SizeFlags.ShrinkCenter;
		_start.CustomMinimumSize = new Vector2(280.0f, 0.0f);
		_start.Pressed += () => Confirmed?.Invoke();

		// Кнопка на пустом экране одна, и в текстовом режиме единственный пункт
		// показывают выбранным — инверсией, как строку списка под курсором.
		DosTerminal.SetRowSelected(_start, true);
		stack.AddChild(_start);

		Refresh();
	}

	public void OnScreenOpened() => Refresh();

	private const string StartCaption = "Приступить к смене";
	private const string ResumeCaption = "Продолжить смену";

	private void Refresh()
	{
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			_day.Text = "ЯДРО НЕДОСТУПНО";
			return;
		}

		// Партия из сохранения возвращается в середину смены: день там уже стоит
		// свой, и кнопка не начинает смену, а отпускает остановленное время.
		bool isResume = GameFlow.Instance != null && GameFlow.Instance.PendingShiftIsResume;
		if (isResume)
		{
			_day.Text = $"СМЕНА {runtime.Session.Day}   ПРИОСТАНОВЛЕНА";
			_start.Text = ResumeCaption;
			return;
		}

		// Смена ещё не начата, поэтому в ядре стоит день прошлой: показываем
		// следующий — тот, который откроет кнопка.
		int day = GameFlow.Instance != null && GameFlow.Instance.PendingShiftDay > 0
			? GameFlow.Instance.PendingShiftDay
			: runtime.Session.Day + 1;

		_day.Text = $"СМЕНА {day}   НЕ НАЧАТА";
		_start.Text = StartCaption;
	}
}
