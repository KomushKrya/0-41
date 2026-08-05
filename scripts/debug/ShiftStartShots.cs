#nullable enable

using Godot;

/// <summary>
/// Проверка и снимки экрана начала смены.
///
/// Прогон идёт по настоящему пути: GameFlow приводит игрока в кабинет со сменой
/// наготове, терминал встречает заставкой, кнопка начинает смену и откидывает
/// игрока от монитора. Поэтому узел-водитель живёт не в сцене, а рядом с ней:
/// смену сцены на кабинет он обязан пережить.
/// </summary>
public partial class ShiftStartShots : Node
{
	private const string ComputerPath = "NewOffice/Interactable/DeskComputer";
	private const string ComputerUiPath = ComputerPath + "/ComputerViewport/ComputerUI";
	private const string PlayerPath = "NewOffice/PlayerCameraRig/CameraPitch/PlayerCamera";
	private const string OutputFolder = "res://temp/";

	private const int WarmupFrames = 180;
	private const int FocusFrames = 90;

	public override void _Ready()
	{
		// Экземпляр из сцены только поднимает водителя: сама сцена сейчас
		// сменится на кабинет и вместе с ней исчезнет.
		if (GetTree().CurrentScene == this)
		{
			GetTree().Root.CallDeferred(Node.MethodName.AddChild, new ShiftStartShots());
			return;
		}

		Drive();
	}

	private async void Drive()
	{
		if (GameFlow.Instance == null)
		{
			GD.PushError("[SHOT] Нет автозагрузки GameFlow.");
			GetTree().Quit(1);
			return;
		}

		GameFlow.Instance.StartNewGame();
		if (!GameFlow.Instance.HasPendingShift)
		{
			// Стартовый выбор увёл бы в найм — для проверки терминала он лишний.
			GameFlow.Instance.BeginShift(1);
		}

		await Settle(WarmupFrames);

		Node? scene = GetTree().CurrentScene;
		var computer = scene?.GetNodeOrNull<DeskComputerInteraction>(ComputerPath);
		var computerUi = scene?.GetNodeOrNull<ComputerUI>(ComputerUiPath);
		var player = scene?.GetNodeOrNull<FlyPlayer>(PlayerPath);
		if (computer == null || computerUi == null || player == null)
		{
			GD.PushError("[SHOT] Кабинет не собрался: нет компьютера, терминала или игрока.");
			GetTree().Quit(1);
			return;
		}

		// Кадр от стола: заставка обязана быть на мониторе до всякой интеракции.
		GD.Print($"[SHOT] заставка до подхода к столу: {computerUi.IsShiftStartModeActive}");
		Save("shift_start_desk");

		computer.EnterComputerMode(player);
		await Settle(FocusFrames);

		GD.Print($"[SHOT] заставка активна: {computerUi.IsShiftStartModeActive}");
		Save("shift_start_screen");

		Button? start = FindStartButton(computerUi);
		if (start == null)
		{
			GD.PushError("[SHOT] Кнопка начала смены не найдена.");
			GetTree().Quit(1);
			return;
		}

		start.EmitSignal(BaseButton.SignalName.Pressed);
		await Settle(FocusFrames);

		GameRuntime? runtime = GameRuntime.Get(this);
		GD.Print($"[SHOT] смена идёт: {runtime?.Session.IsShiftActive}, пауза: {runtime?.IsPaused}, "
			+ $"заставка: {computerUi.IsShiftStartModeActive}, фокус камеры: {player.IsViewFocused}");
		Save("shift_start_after");

		if (runtime != null)
		{
			await DriveResume(runtime);
		}

		GetTree().Quit();
	}

	/// <summary>
	/// Второй заход: партия сохраняется, поднимается «Продолжить» — терминал
	/// обязан встретить той же заставкой, но с кнопкой продолжения.
	/// </summary>
	private async System.Threading.Tasks.Task DriveResume(GameRuntime runtime)
	{
		// Быстрый слот — живой файл игрока. Забираем его содержимое и кладём
		// обратно, чтобы проверка не стёрла чужое сохранение.
		string backup = ReadSlot(runtime);
		runtime.SaveToSlot(GameFlow.QuickSlot, "проверка заставки");

		if (GameFlow.Instance == null || !GameFlow.Instance.ContinueGame())
		{
			GD.PushError("[SHOT] Загрузка быстрого слота не удалась.");
			RestoreSlot(backup);
			return;
		}

		await Settle(WarmupFrames);

		Node? scene = GetTree().CurrentScene;
		var computer = scene?.GetNodeOrNull<DeskComputerInteraction>(ComputerPath);
		var computerUi = scene?.GetNodeOrNull<ComputerUI>(ComputerUiPath);
		var player = scene?.GetNodeOrNull<FlyPlayer>(PlayerPath);
		if (computer == null || computerUi == null || player == null)
		{
			GD.PushError("[SHOT] Кабинет после загрузки не собрался.");
			RestoreSlot(backup);
			return;
		}

		GD.Print($"[SHOT] после загрузки заставка до подхода к столу: {computerUi.IsShiftStartModeActive}");
		Save("shift_resume_desk");

		computer.EnterComputerMode(player);
		await Settle(FocusFrames);

		GD.Print($"[SHOT] после загрузки — заставка: {computerUi.IsShiftStartModeActive}, "
			+ $"время стоит: {runtime.Session.IsTimeFrozen}");
		Save("shift_resume_screen");

		Button? resume = FindStartButton(computerUi);
		if (resume == null)
		{
			GD.PushError("[SHOT] Кнопка продолжения не найдена.");
			RestoreSlot(backup);
			return;
		}

		GD.Print($"[SHOT] подпись кнопки: «{resume.Text}»");
		resume.EmitSignal(BaseButton.SignalName.Pressed);
		await Settle(FocusFrames);

		GD.Print($"[SHOT] после нажатия — время стоит: {runtime.Session.IsTimeFrozen}, "
			+ $"владельцы паузы: {string.Join(",", runtime.Session.TimeFreezeOwners)}, "
			+ $"фокус камеры: {player.IsViewFocused}");
		Save("shift_resume_after");

		RestoreSlot(backup);
	}

	private static string ReadSlot(GameRuntime runtime)
	{
		if (!runtime.HasSlot(GameFlow.QuickSlot))
		{
			return string.Empty;
		}

		using FileAccess file = FileAccess.Open(GameRuntime.GetSlotPath(GameFlow.QuickSlot), FileAccess.ModeFlags.Read);
		return file == null ? string.Empty : file.GetAsText();
	}

	private static void RestoreSlot(string backup)
	{
		string path = GameRuntime.GetSlotPath(GameFlow.QuickSlot);
		if (string.IsNullOrEmpty(backup))
		{
			if (FileAccess.FileExists(path))
			{
				DirAccess.RemoveAbsolute(path);
			}

			return;
		}

		using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
		file?.StoreString(backup);
	}

	private void Save(string name)
	{
		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputFolder));
		string path = ProjectSettings.GlobalizePath($"{OutputFolder}{name}.png");
		Image image = GetTree().Root.GetTexture().GetImage();
		if (image.SavePng(path) == Error.Ok)
		{
			GD.Print($"[SHOT] saved {path}");
		}
		else
		{
			GD.PushError($"[SHOT] Не удалось сохранить {path}.");
		}
	}

	private async System.Threading.Tasks.Task Settle(int frames)
	{
		for (int i = 0; i < frames; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	private static Button? FindStartButton(Node node)
	{
		if (node is ShiftStartScreenUI screen)
		{
			return FindButton(screen);
		}

		foreach (Node child in node.GetChildren())
		{
			Button? found = FindStartButton(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	private static Button? FindButton(Node node)
	{
		if (node is Button button)
		{
			return button;
		}

		foreach (Node child in node.GetChildren())
		{
			Button? found = FindButton(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}
}
