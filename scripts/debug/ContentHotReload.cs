using Godot;

/// <summary>
/// Горячая перезагрузка текста, только в отладочной сборке. Следит за
/// content/localisation и перечитывает JSON сама; по F9 сначала гоняет конвертер.
/// Сцены, C# и числа из data/*.json так не обновляются.
/// </summary>
public partial class ContentHotReload : Node
{
	private const string ConverterPath = "res://content/engine/converter/build.py";

	/// <summary>Собрать текст на старте: поэтому content/localisation и не лежит в гите.</summary>
	[Export] public bool BuildOnStart { get; set; } = true;

	/// <summary>Следить за папкой локализации и перечитывать без нажатий.</summary>
	[Export] public bool WatchFiles { get; set; } = true;

	/// <summary>Прогнать конвертер и перечитать текст.</summary>
	[Export] public Key RebuildKey { get; set; } = Key.F9;

	/// <summary>Как часто проверять время правки файлов.</summary>
	[Export] public double PollSeconds { get; set; } = 0.5;

	/// <summary>Чем звать питон. Меняй, если в PATH он под другим именем.</summary>
	[Export] public string PythonExecutable { get; set; } = "python";

	private double _accumulator;
	private ulong _stamp;
	private ulong _pendingStamp;

	public override void _Ready()
	{
		if (!OS.IsDebugBuild())
		{
			SetProcess(false);
			SetProcessUnhandledInput(false);
			return;
		}

		if (BuildOnStart)
		{
			// Автозагрузка идёт раньше Content, поэтому здесь только сборка: его _Ready
			// прочитает уже свежие файлы.
			RunConverter();
		}

		_stamp = LocaleStamp();
		_pendingStamp = _stamp;
		GD.Print($"[текст] горячая перезагрузка включена: {OS.GetKeycodeString(RebuildKey)} — пересборка, папка локализации под наблюдением");
	}

	public override void _Process(double delta)
	{
		if (!WatchFiles)
		{
			return;
		}

		_accumulator += delta;
		if (_accumulator < PollSeconds)
		{
			return;
		}

		_accumulator = 0.0;

		ulong current = LocaleStamp();
		if (current == _stamp)
		{
			return;
		}

		// Конвертер пишет файлы подряд: ждём, пока время правки перестанет меняться,
		// иначе прочитаем обрезанный JSON.
		if (current != _pendingStamp)
		{
			_pendingStamp = current;
			return;
		}

		Reload();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == RebuildKey)
		{
			RebuildAndReload();
			GetViewport().SetInputAsHandled();
		}
	}

	/// <summary>Прогнать build.py и, если он не ругнулся, перечитать текст.</summary>
	public void RebuildAndReload()
	{
		// При ошибке не перечитываем: в игре остаётся последний собранный текст, а не пустота.
		if (RunConverter())
		{
			Reload();
		}
	}

	/// <summary>Собрать content/raw в JSON. Возвращает false, если конвертер ругнулся.</summary>
	private bool RunConverter()
	{
		string script = ProjectSettings.GlobalizePath(ConverterPath);
		var output = new Godot.Collections.Array();
		int code = OS.Execute(PythonExecutable, new[] { script, "--include-drafts" }, output, true);

		if (code == 0)
		{
			return true;
		}

		string reason = output.Count > 0
			? string.Join("\n", output)
			: $"не удалось запустить «{PythonExecutable}» — проверь, что питон есть в PATH "
				+ "(или поменяй PythonExecutable на автозагрузке ContentHotReload)";

		GD.PushError($"[текст] конвертер не отработал (код {code}):\n{reason}");
		return false;
	}

	/// <summary>Перечитать готовый JSON и заново открыть все текстовые боксы в сцене.</summary>
	public void Reload()
	{
		Content content = Content.Instance;
		if (content == null)
		{
			GD.PushWarning("[текст] автозагрузка Content не найдена, перечитывать нечего");
			return;
		}

		content.Load(content.Locale);

		// Боксы держат ссылку на прежний ContentEntry, поэтому переоткрываем, а не Refresh().
		int boxes = 0;
		foreach (Node node in GetTree().GetNodesInGroup(ContentTextBox.GroupName))
		{
			if (node is ContentTextBox box && box.ContentId.Length > 0)
			{
				box.Reload();
				boxes++;
			}
		}

		_stamp = LocaleStamp();
		_pendingStamp = _stamp;
		GD.Print($"[текст] перезагружено: записей {content.Entries.Count}, боксов на экране {boxes}");
	}

	/// <summary>
	/// Свёртка времени правки всех собранных файлов. Точное значение неважно — важно, что оно
	/// меняется, когда конвертер что-то переписал. Обход общий с загрузчиком, поэтому следим
	/// ровно за теми файлами, которые Content потом и прочитает.
	/// </summary>
	private static ulong LocaleStamp()
	{
		ulong stamp = 0;
		foreach (string path in Content.EnumerateJsonFiles(Content.LocalisationRoot))
		{
			unchecked
			{
				stamp = (stamp * 31) + FileAccess.GetModifiedTime(path);
			}
		}

		return stamp;
	}
}
