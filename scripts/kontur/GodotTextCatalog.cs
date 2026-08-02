using System.Collections.Generic;
using Godot;
using Kontur.Core.Content;
using Kontur.Core.Model;

/// <summary>
/// Реализация порта <see cref="ITextCatalog"/> поверх автозагрузки Content.
///
/// Нужна только для сверки при загрузке: ядро спрашивает, есть ли статья под существо и
/// есть ли в ней условный блок под каждое объявленное свойство. Опечатка в id тогда
/// падает при старте с внятным сообщением, а не превращается в пустой абзац на смене.
/// Сам текст ядру не отдаётся — его разворачивают текстовые боксы.
/// </summary>
public sealed class GodotTextCatalog : ITextCatalog
{
	public bool HasEntry(string entryId)
	{
		Content content = Content.Instance;
		return content != null && content.TryGetEntry(entryId, out _);
	}

	/// <summary>
	/// Кусочки досье в слоте, в порядке загрузки. Ядро берёт из них id,
	/// а разворачивает фразы уже интерфейс — как и с остальными текстами.
	/// </summary>
	public IReadOnlyList<string> GetBioLines(string slot)
	{
		var result = new List<string>();
		if (string.IsNullOrEmpty(slot))
		{
			return result;
		}

		foreach (KeyValuePair<string, ContentEntry> pair in Content.Instance.Entries)
		{
			if (string.Equals(pair.Value.Slot, slot, System.StringComparison.OrdinalIgnoreCase))
			{
				result.Add(pair.Key);
			}
		}

		return result;
	}

	/// <summary>
	/// Варианты решения: ключ, порядок и характеристики, по которым идёт проверка.
	/// Формулировки ядру не отдаются — их разворачивает текстовый бокс по тому же id.
	///
	/// Чисел текст не несёт вовсе: тип диалога и надбавка живут в radio.json,
	/// а порог по каждой названной характеристике ядро подставляет из требований миссии.
	/// Поэтому сюда уходят Neutral и null, а список характеристик — как есть.
	/// </summary>
	public IReadOnlyList<TextOption> GetOptions(string entryId)
	{
		var result = new List<TextOption>();

		Content content = Content.Instance;
		if (content == null || !content.TryGetEntry(entryId, out ContentEntry entry))
		{
			return result;
		}

		foreach (ContentOption option in entry.Options)
		{
			result.Add(new TextOption(
				option.Id,
				MissionEventQuality.Neutral,
				null,
				ToStatKinds(option.Requirements)));
		}

		return result;
	}

	/// <summary>
	/// Названия характеристик из текста в вид, понятный ядру. Незнакомый ключ пропускается:
	/// допустимые значения конвертер уже проверил на сборке текста.
	/// </summary>
	private static List<StatKind> ToStatKinds(IReadOnlyList<string> names)
	{
		var result = new List<StatKind>();
		if (names == null)
		{
			return result;
		}

		for (int i = 0; i < names.Count; i++)
		{
			if (StatKinds.TryParse(names[i], out StatKind kind))
			{
				result.Add(kind);
			}
		}

		return result;
	}

	public bool HasProperty(string entryId, string propertyId)
	{
		Content content = Content.Instance;
		if (content == null || !content.TryGetEntry(entryId, out ContentEntry entry))
		{
			return false;
		}

		// Смотрим объявленный список, а не перебираем куски в поисках %% reveal %%: для существ
		// конвертер сверяет одно с другим на сборке, и расхождение — ошибка билда. Перебор дал
		// бы второй ответ на тот же вопрос, который держался бы синхронным по договорённости.
		// Гарантия распространяется только на creature — у остальных типов properties пока
		// пустой задел, и на них этот метод рассчитывать не может.
		foreach (string property in entry.Properties)
		{
			if (property.Equals(propertyId, System.StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}
		}

		return false;
	}
}
