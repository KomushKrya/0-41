#nullable enable

using System.Collections.Generic;
using System.Text;
using Godot;

/// <summary>
/// Превращает отрезки разметки текстового движка в BBCode для RichTextLabel.
///
/// Конвертер отдаёт абзац разрезанным на отрезки `{текст, подсветка, жирность}`
/// вместо позиций в строке — потому что после подстановки {{переменных}} позиции
/// уехали бы. Здесь отрезки собираются обратно, уже с разметкой.
///
/// Экранирование обязательно: в текстах встречаются квадратные скобки
/// («[ДАННЫЕ НЕ ПОДТВЕРЖДЕНЫ]»), и без него RichTextLabel съел бы их как тег.
/// </summary>
public static class ContentSpanFormatter
{
	/// <summary>Цвет слов-маркеров по умолчанию — тёплый янтарный терминала.</summary>
	public static readonly Color DefaultHighlight = new(1.0f, 0.82f, 0.40f);

	/// <summary>Абзац с разметкой. Если отрезков нет, вернётся экранированный текст.</summary>
	public static string ChunkToBbcode(ContentChunk chunk, Color highlight)
	{
		if (chunk == null)
		{
			return string.Empty;
		}

		if (chunk.Spans == null || chunk.Spans.Count == 0)
		{
			return Escape(chunk.Text);
		}

		return SpansToBbcode(chunk.Spans, highlight);
	}

	public static string SpansToBbcode(IReadOnlyList<ContentSpan> spans, Color highlight)
	{
		var text = new StringBuilder();
		string color = highlight.ToHtml(false);

		for (int i = 0; i < spans.Count; i++)
		{
			ContentSpan span = spans[i];
			string body = Escape(span.Text);
			if (span.Bold)
			{
				body = "[b]" + body + "[/b]";
			}

			if (span.Highlight)
			{
				body = "[color=#" + color + "]" + body + "[/color]";
			}

			text.Append(body);
		}

		return text.ToString();
	}

	/// <summary>
	/// Вся запись целиком, абзац за абзацем. Куски-подписи (call_meta) пропускаются:
	/// они идут в заголовок, а не в тело — так же, как в ContentTextResolver.
	/// </summary>
	public static string ResolveEntryBbcode(string contentId, string fallback, Color highlight)
	{
		ContentEntry? entry = string.IsNullOrWhiteSpace(contentId) || Content.Instance == null
			? null
			: Content.Instance.GetEntry(contentId);
		if (entry == null || entry.Chunks.Count == 0)
		{
			return Escape(fallback ?? string.Empty);
		}

		var text = new StringBuilder();
		foreach (ContentChunk chunk in entry.Chunks)
		{
			if (chunk.IsCallMeta || string.IsNullOrWhiteSpace(chunk.Text))
			{
				continue;
			}

			if (text.Length > 0)
			{
				text.Append("\n\n");
			}

			text.Append(ChunkToBbcode(chunk, highlight));
		}

		return text.Length > 0 ? text.ToString() : Escape(fallback ?? string.Empty);
	}

	/// <summary>Текст из контента может содержать «[», и для BBCode это открытие тега.</summary>
	public static string Escape(string text)
	{
		return string.IsNullOrEmpty(text) ? string.Empty : text.Replace("[", "[lb]");
	}
}
