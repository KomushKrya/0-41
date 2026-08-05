using Godot;

/// <summary>
/// Главное меню. Вёрстка лежит в MainMenu.tscn и правится в редакторе: скрипт
/// не создаёт узлов, а находит готовые по путям и подписывается на кнопки.
///
/// Тексты всё же ставятся отсюда — они приходят из текстового движка, а не
/// из сцены. В сцене написаны те же подписи заглушкой, чтобы экран читался
/// в редакторе, но в игре их перекрывает Content.
///
/// Всё, что меню нужно от игры, идёт через GameFlow.
/// </summary>
public partial class MainMenu : Control
{
	[Export] public NodePath TitlePath { get; set; } = new("Column/Title");
	[Export] public NodePath SubtitlePath { get; set; } = new("Column/Subtitle");
	[Export] public NodePath ContinueButtonPath { get; set; } = new("Column/ContinueButton");
	[Export] public NodePath NewGameButtonPath { get; set; } = new("Column/NewGameButton");
	[Export] public NodePath SettingsButtonPath { get; set; } = new("Column/SettingsButton");
	[Export] public NodePath QuitButtonPath { get; set; } = new("Column/QuitButton");
	[Export] public NodePath HintPath { get; set; } = new("Column/Hint");
	[Export] public NodePath SettingsScreenPath { get; set; } = new("Settings");

	private Button _continueButton;
	private SettingsScreen _settings;
	private Label _hint;

	public override void _Ready()
	{
		// Меню может открыться после кабинета, где курсор захвачен камерой.
		// Сам экран отвечает за свой режим ввода, а не рассчитывает на путь перехода.
		CursorMode.Show(this);

		BindUi();
		RefreshContinueButton();
	}

	private void BindUi()
	{
		GetNode<Label>(TitlePath).Text = Content.Label("ui_menu_title");
		GetNode<Label>(SubtitlePath).Text = Content.Label("ui_menu_subtitle");

		_hint = GetNode<Label>(HintPath);
		_hint.Text = string.Empty;

		_continueButton = BindButton(ContinueButtonPath, "ui_menu_continue", OnContinue);
		BindButton(NewGameButtonPath, "ui_menu_new_game", OnNewGame);
		BindButton(SettingsButtonPath, "ui_menu_settings", OnToggleSettings);
		BindButton(QuitButtonPath, "ui_menu_quit", OnQuit);

		// Экран настроек один на всю игру: тот же самый открывается из паузы.
		// Держать в меню свою урезанную копию значило бы разойтись с ней на первой правке.
		_settings = GetNode<SettingsScreen>(SettingsScreenPath);
		_settings.Visible = false;
	}

	private Button BindButton(NodePath path, string labelId, System.Action onPressed)
	{
		var button = GetNode<Button>(path);
		button.Text = Content.Label(labelId);
		button.Pressed += () => onPressed();
		return button;
	}

	/// <summary>
	/// «Продолжить» гасится, если сохранения нет. Кнопка, которая ничего не делает,
	/// хуже отсутствующей: игрок жмёт её и решает, что игра сломана.
	/// </summary>
	private void RefreshContinueButton()
	{
		bool hasSave = GameFlow.Instance != null
			&& GameFlow.Instance.Runtime != null
			&& GameFlow.Instance.Runtime.HasSlot(GameFlow.QuickSlot);

		_continueButton.Disabled = !hasSave;
		_continueButton.TooltipText = hasSave ? string.Empty : Content.Label("ui_hint_no_saves");
	}

	// ------------------------------------------------------------------ действия

	private void OnNewGame()
	{
		if (GameFlow.Instance == null)
		{
			ShowHint(Content.Label("ui_hint_no_flow_autoload"));
			return;
		}

		GameFlow.Instance.StartNewGame();
	}

	private void OnContinue()
	{
		if (GameFlow.Instance == null)
		{
			ShowHint(Content.Label("ui_hint_no_flow"));
			return;
		}

		if (!GameFlow.Instance.ContinueGame())
		{
			ShowHint(Content.Label("ui_hint_load_failed"));
			RefreshContinueButton();
		}
	}

	private void OnToggleSettings()
	{
		if (_settings.Visible)
		{
			_settings.Close();
		}
		else
		{
			_settings.Open();
		}
	}

	private void OnQuit()
	{
		GetTree().Quit();
	}

	private void ShowHint(string text)
	{
		_hint.Text = text;
	}
}
