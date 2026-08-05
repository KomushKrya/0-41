#nullable enable

using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Model;

/// <summary>
/// Снимки картотеки на заполненной энциклопедии — по кадру на каждое существо.
///
/// Снимается кадр, который видит игрок: сцена main, игрок за столом, штатный
/// ракурс фокуса на мониторе. Отдельный вьюпорт с голой вкладкой не годится —
/// монитор со стеклом, полем по краям и наклоном к камере и есть то, как
/// картотека выглядит в игре.
///
/// Экран показывает только опознанных, поэтому перед съёмкой все существа
/// объявляются опознанными и все их свойства вскрытыми: так виден предельный
/// вид статьи, до которого игрок доходит к концу прогона.
/// </summary>
public partial class EncyclopediaShots : Node
{
	private const string ComputerPath = "NewOffice/Interactable/DeskComputer";
	private const string ComputerUiPath = ComputerPath + "/ComputerViewport/ComputerUI";
	private const string PlayerPath = "NewOffice/PlayerCameraRig/CameraPitch/PlayerCamera";
	private const string OutputFolder = "res://temp/";
	private const string OutputPrefix = "encyclopedia_";

	/// <summary>Кадры на прогрев сцены: свет, окружение и мебель встают не сразу.</summary>
	private const int WarmupFrames = 180;

	/// <summary>Кадры на доводку камеры до монитора: фокус едет плавно.</summary>
	private const int FocusFrames = 90;

	public override async void _Ready()
	{
		GameRuntime? runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			GD.PushError("[SHOT] Ядро не готово: " + (runtime == null ? "нет автозагрузки GameRuntime" : runtime.LoadError));
			GetTree().Quit(1);
			return;
		}

		RevealEverything(runtime.Session);

		Node scene = GD.Load<PackedScene>("res://scenes/main.tscn").Instantiate();
		AddChild(scene);
		await Settle(WarmupFrames);

		var computer = scene.GetNodeOrNull<DeskComputerInteraction>(ComputerPath);
		var player = scene.GetNodeOrNull<FlyPlayer>(PlayerPath);
		var computerUi = scene.GetNodeOrNull<ComputerUI>(ComputerUiPath);
		if (computer == null || player == null || computerUi == null)
		{
			GD.PushError("[SHOT] В сцене нет компьютера, игрока или терминала.");
			GetTree().Quit(1);
			return;
		}

		computer.EnterComputerMode(player);
		computerUi.OpenScreen(ComputerScreen.Encyclopedia);

		// Курсор терминала встаёт в середину экрана и закрывает собой букву
		// в тексте статьи. На снимке он лишний: указывать им некому.
		Control? cursor = computerUi.GetNodeOrNull<Control>("ComputerCursor");
		if (cursor != null)
		{
			cursor.Visible = false;
		}

		await Settle(FocusFrames);

		Control? screen = FindNode<EncyclopediaScreenUI>(computerUi);
		if (screen == null)
		{
			GD.PushError("[SHOT] Вкладка картотеки не найдена в дереве терминала.");
			GetTree().Quit(1);
			return;
		}

		List<Button> rows = CollectRowButtons(screen);
		IReadOnlyList<EncyclopediaEntryView> entries = runtime.Session.GetEncyclopedia();
		if (rows.Count != entries.Count)
		{
			GD.PushError($"[SHOT] Строк в списке {rows.Count}, записей в картотеке {entries.Count} — снимки не сопоставить.");
			GetTree().Quit(1);
			return;
		}

		DirAccess.MakeDirRecursiveAbsolute(ProjectSettings.GlobalizePath(OutputFolder));
		for (int i = 0; i < rows.Count; i++)
		{
			rows[i].EmitSignal(BaseButton.SignalName.Pressed);
			await Settle(8);

			string path = ProjectSettings.GlobalizePath($"{OutputFolder}{OutputPrefix}{entries[i].CreatureId}.png");
			Image image = GetTree().Root.GetTexture().GetImage();
			Error result = image.SavePng(path);
			if (result == Error.Ok)
			{
				GD.Print($"[SHOT] saved {path} {image.GetWidth()}x{image.GetHeight()}");
			}
			else
			{
				GD.PushError($"[SHOT] Не удалось сохранить {path}: {result}.");
			}
		}

		GetTree().Quit();
	}

	/// <summary>Опознать всех существ и вскрыть все свойства.</summary>
	private static void RevealEverything(KonturSimulation session)
	{
		foreach (KeyValuePair<string, CreatureDefinition> pair in session.Content.Creatures)
		{
			CreatureDefinition creature = pair.Value;
			session.DebugState.Encyclopedia.Identify(creature.Id);
			for (int i = 0; i < creature.Properties.Count; i++)
			{
				session.DebugState.Encyclopedia.RevealProperty(creature.Id, creature.Properties[i]);
			}
		}
	}

	private async System.Threading.Tasks.Task Settle(int frames)
	{
		for (int i = 0; i < frames; i++)
		{
			await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
		}
	}

	/// <summary>
	/// Кнопки строк списка. Кнопка действия и вкладки нижней панели живут вне
	/// прокрутки, поэтому берём только то, что внутри неё.
	/// </summary>
	private static List<Button> CollectRowButtons(Node screen)
	{
		var rows = new List<Button>();
		ScrollContainer? scroll = FindNode<ScrollContainer>(screen);
		if (scroll != null)
		{
			CollectButtons(scroll, rows);
		}

		return rows;
	}

	private static T? FindNode<T>(Node node) where T : Node
	{
		if (node is T match)
		{
			return match;
		}

		foreach (Node child in node.GetChildren())
		{
			T? found = FindNode<T>(child);
			if (found != null)
			{
				return found;
			}
		}

		return null;
	}

	private static void CollectButtons(Node node, List<Button> found)
	{
		if (node is Button button)
		{
			found.Add(button);
		}

		foreach (Node child in node.GetChildren())
		{
			CollectButtons(child, found);
		}
	}
}
