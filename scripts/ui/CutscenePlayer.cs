using Godot;

/// <summary>
/// Ролик между сменами. Сначала ищет видеофайл, не находит — показывает текст.
///
/// Два режима, а не два разных экрана, потому что вызывающему всё равно: он
/// говорит «покажи cutscene_before_shift_2» и ждёт, когда это кончится. Пока
/// видео не отрисовано, играть можно уже сейчас; когда файл появится, подкладывать
/// его нужно будет в папку, а не в код.
/// </summary>
public partial class CutscenePlayer : Control
{
	/// <summary>
	/// Где искать видео: имя файла совпадает с id записи катсцены.
	/// Все ролики лежат в одной папке с интро; прежний путь assets/cutscenes/
	/// в проекте не существовал, так что видео не нашлось бы при всём желании.
	/// </summary>
	private const string VideoFolder = "res://assets/video/cutscenes/";

	private static readonly string[] VideoExtensions = { ".ogv" };

	private VideoStreamPlayer _video;
	private bool _finished;

	public override void _Ready()
	{
		AnchorRight = 1.0f;
		AnchorBottom = 1.0f;

		CursorMode.Show(this);

		BuildUi();

		// Кабинет тяжёлый и грузится секундами. Пока игрок читает или смотрит
		// ролик, движок успевает поднять сцену в фоне — и переход после катсцены
		// перестаёт выглядеть зависанием.
		GameFlow.Instance?.PreloadOffice();

		string cutsceneId = GameFlow.Instance != null
			? GameFlow.Instance.PendingCutsceneId
			: string.Empty;

		if (string.IsNullOrEmpty(cutsceneId))
		{
			Finish();
			return;
		}

		if (TryPlayVideo(cutsceneId))
		{
			return;
		}

		// Ролики отрисованы, текстовой заглушки больше нет: если видео не нашлось,
		// это ошибка контента — сообщаем и идём дальше, а не показываем текст.
		GD.PushWarning($"[КАТСЦЕНА] Видео для '{cutsceneId}' не найдено в {VideoFolder}");
		Finish();
	}

	private void BuildUi()
	{
		var background = new ColorRect
		{
			Color = new Color(0.02f, 0.02f, 0.03f),
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f
		};
		AddChild(background);

		_video = new VideoStreamPlayer
		{
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			Expand = true,
			Visible = false
		};
		_video.Finished += OnVideoFinished;
		AddChild(_video);

	}

	// ------------------------------------------------------------------ видео

	private bool TryPlayVideo(string cutsceneId)
	{
		for (int i = 0; i < VideoExtensions.Length; i++)
		{
			string path = VideoFolder + cutsceneId + VideoExtensions[i];
			if (!ResourceLoader.Exists(path))
			{
				continue;
			}

			var stream = GD.Load<VideoStream>(path);
			if (stream == null)
			{
				continue;
			}

			_video.Stream = stream;
			_video.Visible = true;
			_video.Play();
			return true;
		}

		return false;
	}

	private void OnVideoFinished()
	{
		Finish();
	}

	// ------------------------------------------------------------------ ввод

	/// <summary>
	/// Пока идёт ролик, Escape — это «пропустить», а не «меню паузы».
	/// Обрабатываем в _Input и помечаем событие обработанным: PauseMenu слушает
	/// _UnhandledInput, который идёт после всех _Input. Иначе одно нажатие делало
	/// две вещи сразу — заканчивало ролик и открывало паузу, а
	/// <c>GetTree().Paused</c> переживает смену сцены и замораживал уже кабинет.
	/// </summary>
	public override void _Input(InputEvent @event)
	{
		if (_finished || @event is not InputEventKey key || !key.Pressed || key.Echo)
		{
			return;
		}

		if (key.Keycode != Key.Escape && key.Keycode != Key.Space && key.Keycode != Key.Enter)
		{
			return;
		}

		GetViewport().SetInputAsHandled();

		// Листать нечего: ролик либо смотрят, либо пропускают.
		Finish();
	}

	/// <summary>
	/// Завершение ровно одно на все пути: конец видео, конец текста, пропуск.
	/// Флаг нужен, чтобы нажатие в момент перехода не увело сцену дважды.
	/// </summary>
	private void Finish()
	{
		if (_finished)
		{
			return;
		}

		_finished = true;

		if (_video != null && _video.IsPlaying())
		{
			_video.Stop();
		}

		if (GameFlow.Instance != null)
		{
			GameFlow.Instance.OnCutsceneFinished();
		}
	}
}
