using System.Text;
using Godot;

/// <summary>
/// Статья энциклопедии: имя существа, всегда видимый первый абзац и те условные абзацы,
/// которые игра уже открыла. На месте закрытых по умолчанию остаётся заглушка — так
/// видно, что у существа есть неизученные свойства, и статья не «прыгает» по высоте,
/// когда очередное свойство открывается.
///
/// Первый наследник <see cref="ContentTextBox"/> и заодно пример: вся работа — прочитать
/// готовый список кусков и сложить строку.
/// </summary>
public partial class EncyclopediaTextBox : ContentTextBox
{
	[Export] public NodePath TextPath { get; set; } = new("Text");

	/// <summary>Показывать заглушку вместо закрытого абзаца.</summary>
	[Export] public bool ShowRedacted { get; set; } = true;

	[Export] public string RedactedText { get; set; } = "[ДАННЫЕ НЕ ПОДТВЕРЖДЕНЫ]";

	private RichTextLabel _text;

	public override void _Ready()
	{
		// Ярлык нужен раньше базового _Ready: тот сразу вызовет Render().
		_text = GetNodeOrNull<RichTextLabel>(TextPath);
		if (_text == null)
		{
			GD.PushWarning($"{Name}: не найден RichTextLabel по пути {TextPath}");
		}

		base._Ready();
	}

	protected override void Render()
	{
		if (_text == null)
		{
			return;
		}

		if (!IsLoaded)
		{
			_text.Text = "[i]Статья не загружена.[/i]";
			return;
		}

		var builder = new StringBuilder();

		if (Entry.Name.Length > 0)
		{
			builder.Append("[b]").Append(Entry.Name.ToUpperInvariant()).Append("[/b]\n\n");
		}

		// Идём по всем кускам записи, а не по Chunks: на закрытых нужна заглушка,
		// а в Chunks их уже нет.
		foreach (ContentChunk chunk in Entry.Chunks)
		{
			if (IsChunkVisible(chunk))
			{
				builder.Append(chunk.Text).Append("\n\n");
			}
			else if (ShowRedacted)
			{
				builder.Append("[color=#5a6b5a]").Append(RedactedText).Append("[/color]\n\n");
			}
		}

		_text.Text = builder.ToString().TrimEnd('\n');
	}
}
