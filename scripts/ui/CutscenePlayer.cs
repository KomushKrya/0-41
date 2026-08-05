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
	/// <summary>Где искать видео: имя файла совпадает с id записи катсцены.</summary>
	private const string VideoFolder = "res://assets/cutscenes/";

	private static readonly string[] VideoExtensions = { ".ogv" };

	private VideoStreamPlayer _video;
	private RichTextLabel _text;
	private Label _prompt;
	private Content _content;

	private ContentEntry _entry;
	private int _chunkIndex;
	private bool _finished;

	public override void _Ready()
	{
		AnchorRight = 1.0f;
		AnchorBottom = 1.0f;

		CursorMode.Show(this);

		BuildUi();

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

		ShowText(cutsceneId);
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

		_text = new RichTextLabel
		{
			BbcodeEnabled = true,
			FitContent = true,
			AnchorLeft = 0.5f,
			AnchorTop = 0.5f,
			AnchorRight = 0.5f,
			AnchorBottom = 0.5f,
			GrowHorizontal = GrowDirection.Both,
			GrowVertical = GrowDirection.Both,
			CustomMinimumSize = new Vector2(760.0f, 0.0f),
			Visible = false
		};
		AddChild(_text);

		_prompt = new Label
		{
			Text = Content.Label("ui_cutscene_hint_next"),
			HorizontalAlignment = HorizontalAlignment.Center,
			AnchorLeft = 0.0f,
			AnchorTop = 1.0f,
			AnchorRight = 1.0f,
			AnchorBottom = 1.0f,
			OffsetTop = -48.0f,
			Modulate = new Color(1.0f, 1.0f, 1.0f, 0.45f)
		};
		AddChild(_prompt);
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

	// ------------------------------------------------------------------ текст

	private void ShowText(string cutsceneId)
	{
		_content = Content.Instance;
		if (_content == null)
		{
			GD.PushWarning("[CUTSCENE] Автозагрузка Content не готова — ролик пропущен.");
			Finish();
			return;
		}

		_entry = _content.GetEntry(cutsceneId);
		if (_entry == null || _entry.Chunks.Count == 0)
		{
			GD.PushWarning($"[CUTSCENE] Ни видео, ни текста под '{cutsceneId}'.");
			Finish();
			return;
		}

		_text.Visible = true;
		_chunkIndex = 0;
		RenderChunk();
	}

	/// <summary>
	/// Куски показываются по одному, накопительно — как реплики в диалоге.
	/// Разметка ключевых слов идёт через ToBbcode: она же используется в звонках,
	/// и катсцена не должна выглядеть иначе.
	/// </summary>
	private void RenderChunk()
	{
		var builder = new System.Text.StringBuilder();

		for (int i = 0; i <= _chunkIndex && i < _entry.Chunks.Count; i++)
		{
			if (i > 0)
			{
				builder.Append("\n\n");
			}

			builder.Append(_entry.Chunks[i].ToBbcode());
		}

		_text.Text = builder.ToString();

		bool last = _chunkIndex >= _entry.Chunks.Count - 1;
		_prompt.Text = last
			? Content.Label("ui_cutscene_hint_last")
			: Content.Label("ui_cutscene_hint_next");
	}

	// ------------------------------------------------------------------ ввод

	public override void _UnhandledInput(InputEvent @event)
	{
		if (_finished || @event is not InputEventKey key || !key.Pressed || key.Echo)
		{
			return;
		}

		if (key.Keycode == Key.Escape)
		{
			Finish();
			return;
		}

		if (key.Keycode != Key.Space && key.Keycode != Key.Enter)
		{
			return;
		}

		// Видео листать нечего: пробел на нём означает «хватит».
		if (_video.Visible)
		{
			Finish();
			return;
		}

		if (_entry == null || _chunkIndex >= _entry.Chunks.Count - 1)
		{
			Finish();
			return;
		}

		_chunkIndex++;
		RenderChunk();
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
