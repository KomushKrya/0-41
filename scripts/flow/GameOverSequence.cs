using Godot;
using Kontur.Core.Model;

/// <summary>
/// Проводы игрока к блокноту перед концовкой.
///
/// Шкала упёрлась в критическое значение — но ролик сразу не запускается.
/// Сначала игрок обязан увидеть, какая именно строка в блокноте его добила:
/// камера поднимает блокнот и держит его две секунды. Без этой паузы концовка
/// приходит ниоткуда, и причина проигрыша остаётся за кадром.
///
/// Если игрок в этот момент разбирается с вызовом — отправляет группу, говорит
/// по телефону или по рации, — концовка ждёт молча и забирает его, только когда
/// он освободится. Обрывать разговор на полуслове нельзя: игрок не поймёт, что
/// произошло, и решит, что игра сломалась.
///
/// Узел живёт в кабинете, а не в автозагрузке: всё, чем он распоряжается —
/// блокнот, камера, экраны — принадлежит сцене кабинета и исчезает вместе с ней.
/// </summary>
public partial class GameOverSequence : Node
{
	[Export] public NodePath NotebookPath { get; set; } = new("../../NewOffice/Interactable/Notebook");
	[Export] public NodePath PlayerPath { get; set; } = new("../../NewOffice/PlayerCameraRig/CameraPitch/PlayerCamera");
	[Export] public NodePath ComputerPath { get; set; } = new("../../NewOffice/Interactable/DeskComputer");
	[Export] public NodePath ComputerUiPath { get; set; } = new("../../NewOffice/Interactable/DeskComputer/ComputerViewport/ComputerUI");

	/// <summary>Сколько секунд блокнот держится в кадре, прежде чем пойдёт ролик.</summary>
	[Export(PropertyHint.Range, "0.5,10.0,0.5")] public double HoldSeconds { get; set; } = 2.0;

	/// <summary>Тот, кто сейчас в кабинете. Пусто — кабинета нет, и вести некого.</summary>
	public static GameOverSequence Instance { get; private set; }

	private InspectableItemController _notebook;
	private FlyPlayer _player;
	private DeskComputerInteraction _computer;
	private ComputerUI _computerUi;
	private Control _radioUi;
	private Control _phoneUi;

	private GameOverReason _reason;
	private bool _running;
	private double _held;

	public override void _Ready()
	{
		Instance = this;

		_notebook = GetNodeOrNull<InspectableItemController>(NotebookPath);
		_player = GetNodeOrNull<FlyPlayer>(PlayerPath);
		_computer = GetNodeOrNull<DeskComputerInteraction>(ComputerPath);
		_computerUi = GetNodeOrNull<ComputerUI>(ComputerUiPath);

		// Экраны вызова и рации живут в общих слоях сцены и заведены в группы —
		// путём до них не ходим, чтобы перестановка слоёв не ломала концовку.
		_radioUi = GetTree().GetFirstNodeInGroup("radio_decision_ui") as Control;
		_phoneUi = GetTree().GetFirstNodeInGroup("phone_call_acceptance_ui") as Control;
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	/// <summary>
	/// Берёт концовку на себя. Ложь — вести игрока некому или нечем, и тогда
	/// вызывающий обязан показать ролик сам: остаться совсем без концовки хуже,
	/// чем показать её без проводов.
	/// </summary>
	public static bool TryRun(GameOverReason reason)
	{
		if (Instance == null || !IsInstanceValid(Instance))
		{
			return false;
		}

		return Instance.Begin(reason);
	}

	private bool Begin(GameOverReason reason)
	{
		if (_running)
		{
			return true;
		}

		if (_notebook == null || _player == null)
		{
			GD.PushWarning("[КОНЕЦ] Блокнот или камера не найдены — концовка пойдёт без проводов.");
			return false;
		}

		_reason = reason;
		_running = true;
		_held = 0.0;
		return true;
	}

	public override void _Process(double delta)
	{
		if (!_running)
		{
			return;
		}

		if (IsBusyScreenOpen())
		{
			// Отсчёт сбрасываем, а не приостанавливаем: две секунды игрок должен
			// смотреть на блокнот подряд, иначе он их попросту не заметит.
			_held = 0.0;
			return;
		}

		if (!_notebook.IsViewActive)
		{
			OpenNotebook();
			_held = 0.0;
			return;
		}

		_held += delta;
		if (_held < HoldSeconds)
		{
			return;
		}

		Finish();
	}

	/// <summary>
	/// Поднимает блокнот к камере. Если игрок успел закрыть его сам, следующий
	/// кадр поднимет снова: уйти от концовки нельзя, на то она и концовка.
	/// </summary>
	private void OpenNotebook()
	{
		if (_player.IsViewFocused)
		{
			// Камеру держит монитор или другой предмет со стола. Сначала отпускаем
			// её и ждём возврата: два фокуса разом подерутся за один и тот же кадр,
			// и блокнот окажется поверх чужой страницы.
			_computer?.ExitComputerMode();
			CloseOtherInspectables();
			return;
		}

		_notebook.OpenView(_player);
	}

	private void CloseOtherInspectables()
	{
		foreach (Node node in GetTree().GetNodesInGroup("interactive"))
		{
			if (node is InspectableItemController item && item != _notebook)
			{
				item.CloseView();
			}
		}
	}

	/// <summary>Экраны, с которых игрока не уводят: он занят живым вызовом.</summary>
	private bool IsBusyScreenOpen()
	{
		if (IsInstanceValid(_radioUi) && _radioUi.Visible)
		{
			return true;
		}

		if (IsInstanceValid(_phoneUi) && _phoneUi.Visible)
		{
			return true;
		}

		return IsInstanceValid(_computerUi) && _computerUi.IsDispatchSelectionActive;
	}

	private void Finish()
	{
		_running = false;

		if (GameFlow.Instance == null)
		{
			GD.PushError("[КОНЕЦ] Нет автозагрузки GameFlow — ролик концовки показать некому.");
			return;
		}

		GameFlow.Instance.PlayEnding(_reason);
	}
}
