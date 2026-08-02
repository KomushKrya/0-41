using System.Collections.Generic;
using System.Text;

public sealed class ContentEntry
{
	public string Id = string.Empty;
	public string Type = string.Empty;
	public string Name = string.Empty;
	public string Outcome = string.Empty;

	/// <summary>Только у call: "radio" — вмешательство по рации, "filler" — одна проверка.</summary>
	public string MissionType = string.Empty;

	public int Day;
	public IReadOnlyList<string> Requirements = new List<string>();
	public IReadOnlyList<string> Properties = new List<string>();

	/// <summary>Имена подстановок {{имя}}, встреченные в тексте: что игра должна заполнить.</summary>
	public IReadOnlyList<string> Variables = new List<string>();

	public IReadOnlyList<ContentChunk> Chunks = new List<ContentChunk>();
	public IReadOnlyList<ContentOption> Options = new List<ContentOption>();
}

public sealed class ContentChunk
{
	/// <summary>Обычная реплика или абзац.</summary>
	public const string KindText = "text";

	/// <summary>Шапка звонка — [ЗВОНОК ПЕРЕНАПРАВЛЕН ...], рендерится отдельно.</summary>
	public const string KindCallMeta = "call_meta";

	public const string HighlightColor = "#d27253";

	public string Text = string.Empty;
	public string Kind = KindText;
	public string Reveal = string.Empty;

	/// <summary>Текст по отрезкам. Пустой список — размечать нечего.</summary>
	public IReadOnlyList<ContentSpan> Spans = new List<ContentSpan>();

	public bool IsCallMeta => Kind == KindCallMeta;

	public bool HasHighlights => Spans.Count > 0;

	/// <summary>
	/// Разметка для RichTextLabel. Скобки самого текста экранируются: иначе «[1985]»
	/// в реплике разобралось бы как bbcode.
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
			if (span.Bold)
			{
				builder.Append("[b]");
			}

			if (span.Highlight)
			{
				builder.Append("[color=").Append(color).Append(']');
			}

			builder.Append(Escape(span.Text));

			if (span.Highlight)
			{
				builder.Append("[/color]");
			}

			if (span.Bold)
			{
				builder.Append("[/b]");
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
/// Отрезок куска. <c>Highlight</c> — ключевое слово вызова; какую характеристику оно
/// подсказывает, не хранится нигде, кроме _system/Keywords.md. <c>Bold</c> — подписи.
/// </summary>
public sealed class ContentSpan
{
	public string Text = string.Empty;
	public bool Highlight;
	public bool Bold;
}

public sealed class ContentOption
{
	public string Name = string.Empty;
	public IReadOnlyList<ContentChunk> Chunks = new List<ContentChunk>();
}
