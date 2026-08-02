using System;
using System.Collections.Generic;
using System.Text.Json;
using Kontur.Core.Model;

namespace Kontur.Core.Content
{
	/// <summary>
	/// Каталог текстов поверх собранного конвертером JSON (content/localisation/&lt;локаль&gt;).
	///
	/// Нужен, чтобы ядро оставалось запускаемым без Godot: консольный прогон и самопроверки
	/// читают ровно тот же файл, что и игра, и падают на тех же опечатках в id.
	/// В игре эту роль исполняет GodotTextCatalog поверх автозагрузки Content — здесь
	/// сознательно продублирована только структура, но не разбор текста: куски не читаются.
	/// </summary>
	public sealed class JsonTextCatalog : ITextCatalog
	{
		/// <summary>Раскладка совпадает с FOLDER_TYPES в content/engine/converter/build.py.</summary>
		private static readonly string[] TypeFiles =
		{
			"missions/calls/call.json",
			"missions/mission_ids/mission_id.json",
			"missions/radio/radio.json",
			"missions/reports/report.json",
			"creatures/creature.json",
			"cutscenes/cutscene.json",
			"equipment/equipment.json",
			"shift_notes/shift_note.json",
			"UI/hover_footnote/perks/perk.json",
			"UI/hover_footnote/characteristics/characteristic.json",
			"UI/hover_footnote/equipment_kinds/equipment_kind.json",
			"UI/hover_footnote/scales/scale.json"
		};

		private static readonly IReadOnlyList<TextOption> NoOptions = new List<TextOption>();

		private readonly Dictionary<string, HashSet<string>> _properties =
			new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

		private readonly Dictionary<string, List<TextOption>> _options =
			new Dictionary<string, List<TextOption>>(StringComparer.OrdinalIgnoreCase);

		private JsonTextCatalog()
		{
		}

		public int EntryCount
		{
			get { return _properties.Count; }
		}

		/// <summary>
		/// Читает все файлы локали. Отсутствующий файл — не ошибка: конвертер создаёт
		/// его только когда в этой папке есть записи, а пустой тип контента вполне нормален.
		/// </summary>
		public static JsonTextCatalog Load(IContentSource localeSource)
		{
			if (localeSource == null)
			{
				throw new ArgumentNullException(nameof(localeSource));
			}

			var catalog = new JsonTextCatalog();

			for (int i = 0; i < TypeFiles.Length; i++)
			{
				string fileName = TypeFiles[i];
				if (!localeSource.Exists(fileName))
				{
					continue;
				}

				catalog.ReadFile(fileName, localeSource.ReadAllText(fileName));
			}

			return catalog;
		}

		public bool HasEntry(string entryId)
		{
			return !string.IsNullOrEmpty(entryId) && _properties.ContainsKey(entryId);
		}

		public bool HasProperty(string entryId, string propertyId)
		{
			HashSet<string>? properties;
			return _properties.TryGetValue(entryId, out properties) && properties.Contains(propertyId);
		}

		public IReadOnlyList<TextOption> GetOptions(string entryId)
		{
			List<TextOption>? options;
			return _options.TryGetValue(entryId, out options) ? options : NoOptions;
		}

		private void ReadFile(string fileName, string json)
		{
			JsonDocument document;
			try
			{
				document = JsonDocument.Parse(json);
			}
			catch (JsonException exception)
			{
				throw new ContentException($"Каталог текстов, ошибка разбора '{fileName}': {exception.Message}");
			}

			using (document)
			{
				if (document.RootElement.ValueKind != JsonValueKind.Object)
				{
					throw new ContentException($"Каталог текстов: '{fileName}' должен быть объектом id -> запись.");
				}

				foreach (JsonProperty entry in document.RootElement.EnumerateObject())
				{
					ReadEntry(entry.Name, entry.Value);
				}
			}
		}

		private void ReadEntry(string entryId, JsonElement entry)
		{
			var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

			JsonElement propertyList;
			if (entry.TryGetProperty("properties", out propertyList) && propertyList.ValueKind == JsonValueKind.Array)
			{
				foreach (JsonElement property in propertyList.EnumerateArray())
				{
					string? value = property.GetString();
					if (!string.IsNullOrWhiteSpace(value))
					{
						properties.Add(value!);
					}
				}
			}

			_properties[entryId] = properties;

			JsonElement optionList;
			if (!entry.TryGetProperty("options", out optionList) || optionList.ValueKind != JsonValueKind.Array)
			{
				return;
			}

			var options = new List<TextOption>();
			foreach (JsonElement option in optionList.EnumerateArray())
			{
				options.Add(new TextOption(
					ReadString(option, "id"),
					ParseQuality(ReadString(option, "quality")),
					ReadOptionalInt(option, "requirement_modifier"),
					ReadCheckedStats(option)));
			}

			_options[entryId] = options;
		}

		private static string ReadString(JsonElement element, string name)
		{
			JsonElement value;
			if (!element.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.String)
			{
				return string.Empty;
			}

			return value.GetString() ?? string.Empty;
		}

		private static MissionEventQuality ParseQuality(string value)
		{
			MissionEventQuality parsed;
			return Enum.TryParse<MissionEventQuality>(value, true, out parsed)
				? parsed
				: MissionEventQuality.Neutral;
		}

		/// <summary>
		/// `requires` в собранном JSON — список характеристик латиницей, без чисел:
		/// порог по каждой подставляется из требований миссии.
		/// </summary>
		private static List<StatKind> ReadCheckedStats(JsonElement option)
		{
			var result = new List<StatKind>();

			JsonElement requires;
			if (!option.TryGetProperty("requires", out requires) || requires.ValueKind != JsonValueKind.Array)
			{
				return result;
			}

			foreach (JsonElement entry in requires.EnumerateArray())
			{
				StatKind kind;
				string name = entry.ValueKind == JsonValueKind.String ? entry.GetString() ?? string.Empty : string.Empty;
				if (StatKinds.TryParse(name, out kind))
				{
					result.Add(kind);
				}
			}

			return result;
		}

		private static int? ReadOptionalInt(JsonElement element, string name)
		{
			JsonElement value;
			if (!element.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.Number)
			{
				return null;
			}

			return value.TryGetInt32(out int parsed) ? parsed : (int?)null;
		}

		private static int ReadInt(JsonElement element, string name)
		{
			JsonElement value;
			if (!element.TryGetProperty(name, out value) || value.ValueKind != JsonValueKind.Number)
			{
				return 0;
			}

			return value.TryGetInt32(out int parsed) ? parsed : 0;
		}
	}
}
