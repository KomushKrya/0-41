using System.Collections.Generic;
using System.Text;

public sealed class ContentEntry
{
	public string Id = string.Empty;
	public string Type = string.Empty;
	public string Name = string.Empty;
	public string Outcome = string.Empty;

	/// <summary>Чем заканчивается вызов: "radio" — вмешательство по рации, "filler" — только проверка. Только у call.</summary>
	public string MissionType = string.Empty;

	public int Day;
	public IReadOnlyList<string> Requirements = new List<string>();
	public IReadOnlyList<string> Properties = new List<string>();

	/// <summary>Имена подстановок {{имя}}, встреченные в тексте: что игра должна заполнить.</summary>
	public IReadOnlyList<string> Variables = new List<string>();

	/// <summary>Характеристики, подсвеченные в тексте: какой навык вызов намекает потребовать.</summary>
	public IReadOnlyList<string> Stats = new List<string>();

	public IReadOnlyList<ContentChunk> Chunks = new List<ContentChunk>();
	public IReadOnlyList<ContentOption> Options = new List<ContentOption>();
}

public sealed class ContentChunk
{
	/// <summary>Обычная реплика или абзац.</summary>
	public const string KindText = "text";

	/// <summary>Служебная шапка звонка — [ЗВОНОК ПЕРЕНАПРАВЛЕН ...], рендерится отдельно.</summary>
	public const string KindCallMeta = "call_meta";

	/// <summary>Цвет подсветки ключевых слов по умолчанию. Один на все характеристики.</summary>
	public const string HighlightColor = "#d27253";

	public string Text = string.Empty;
	public string Kind = KindText;
	public string Reveal = string.Empty;

	/// <summary>
	/// Текст, разбитый на отрезки: обычные и ключевые слова (<c>Highlight</c>).
	/// Пустой список — в куске подсвечивать нечего, весь текст обычный.
	/// </summary>
	public IReadOnlyList<ContentSpan> Spans = new List<ContentSpan>();

	public bool IsCallMeta => Kind == KindCallMeta;

	public bool HasHighlights => Spans.Count > 0;

	/// <summary>
	/// Текст с подсветкой для RichTextLabel. Квадратные скобки самого текста экранируются:
	/// без этого «[1985]» в реплике разобралось бы как разметка и абзац потерял бы кусок.
	/// </summary>
	public string ToBbcode(string color = HighlightColor)
	{
		if (!HasHighlights)
		{
			return Escape(Text);
		}

		var builder = new StringBuilder();
		foreach (ContentSpan span in Spans)
		{
			if (span.Highlight)
			{
				builder.Append("[color=").Append(color).Append(']')
					.Append(Escape(span.Text))
					.Append("[/color]");
			}
			else
			{
				builder.Append(Escape(span.Text));
			}
		}

		return builder.ToString();
	}

	private static string Escape(string text)
	{
		return text.Replace("[", "[lb]");
	}
}

/// <summary>
/// Отрезок куска: текст и надо ли его красить. Какую характеристику ключевое слово
/// подсказывает, здесь не хранится — игрок должен додуматься сам, а перечень слов
/// для авторов лежит в content/raw/_system/Keywords.md.
/// </summary>
public sealed class ContentSpan
{
	public string Text = string.Empty;
	public bool Highlight;
}

public sealed class ContentOption
{
	public string Name = string.Empty;
	public int RequirementModifier;
	public IReadOnlyList<ContentChunk> Chunks = new List<ContentChunk>();
}
