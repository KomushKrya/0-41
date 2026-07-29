using System.Collections.Generic;
using Godot;

public partial class BottomTextRenderer : CanvasLayer
{
	[Export] public string StartupContentId { get; set; } = "cutscene_intro";
	[Export] public bool FreeWhenFinished { get; set; } = true;

	private Label _label = null!;
	private Control _panel = null!;
	private IReadOnlyList<ContentChunk> _chunks = new List<ContentChunk>();
	private string _contentId = string.Empty;
	private int _chunkIndex = -1;

	public bool IsPlaying => _chunkIndex >= 0;

	public override void _Ready()
	{
		_panel = GetNode<Control>("Panel");
		_label = GetNode<Label>("Panel/MarginContainer/Text");
		_panel.Visible = false;
	}

	public void Play(string contentId)
	{
		_chunks = Content.Instance.GetChunks(contentId);
		if (_chunks.Count == 0)
		{
			GD.PushWarning($"BottomTextRenderer: нет текста для id {contentId}");
			return;
		}

		_contentId = contentId;
		_chunkIndex = 0;
		_panel.Visible = true;
		_label.Text = _chunks[_chunkIndex].Text;
	}

	public void Advance()
	{
		if (!IsPlaying)
		{
			Play(StartupContentId);
			return;
		}

		_chunkIndex++;

		if (_chunkIndex < _chunks.Count)
		{
			_label.Text = _chunks[_chunkIndex].Text;
			return;
		}

		Finish();
	}

	private void Finish()
	{
		_chunks = new List<ContentChunk>();
		_contentId = string.Empty;
		_chunkIndex = -1;
		_panel.Visible = false;

		if (FreeWhenFinished)
		{
			QueueFree();
		}
	}
}
