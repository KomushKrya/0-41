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
	/// Варианты решения для mission_event: ключ, тип диалога и числа сложности.
	/// Формулировки ядру не отдаются — их разворачивает текстовый бокс по тому же id.
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
				ParseQuality(option.Quality),
				option.RequirementModifier,
				ToStatBlock(option.Requirements)));
		}

		return result;
	}

	/// <summary>
	/// Неизвестный тип диалога — нейтральный. Ругаться здесь незачем: конвертер
	/// уже проверил допустимые значения на сборке текста и без этого не собрался бы.
	/// </summary>
	private static MissionEventQuality ParseQuality(string value)
	{
		return System.Enum.TryParse(value, true, out MissionEventQuality parsed)
			? parsed
			: MissionEventQuality.Neutral;
	}

	/// <summary>Пороги из текста в вид, понятный ядру. Незнакомый ключ пропускается.</summary>
	private static StatBlock ToStatBlock(IReadOnlyDictionary<string, int> requirements)
	{
		StatBlock result = StatBlock.Zero;
		if (requirements == null)
		{
			return result;
		}

		foreach (KeyValuePair<string, int> pair in requirements)
		{
			if (StatKinds.TryParse(pair.Key, out StatKind kind))
			{
				result = result.With(kind, pair.Value);
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
