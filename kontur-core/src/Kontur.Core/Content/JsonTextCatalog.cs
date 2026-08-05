using System;
using System.Collections.Generic;
using System.Text.Json;
using Kontur.Core.Model;

namespace Kontur.Core.Content
{
	/// <summary>Текстовый каталог для headless: читает те же собранные JSON, что и Godot.</summary>
	public sealed class JsonTextCatalog : ITextCatalog
	{
		private static readonly string[] TypeFiles = { "missions/calls/call.json", "missions/mission_ids/mission_id.json", "missions/radio/radio.json", "missions/reports/report.json", "creatures/creature.json", "cutscenes/cutscene.json", "equipment/equipment.json", "shift_notes/shift_note.json", "personnel/bio/bio_line.json", "UI/hover_footnote/perks/perk.json", "UI/hover_footnote/characteristics/characteristic.json", "UI/hover_footnote/equipment_kinds/equipment_kind.json", "UI/hover_footnote/scales/scale.json" };
		private readonly Dictionary<string, HashSet<string>> _properties = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, List<TextOption>> _options = new Dictionary<string, List<TextOption>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, List<string>> _bioLines = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, List<string>> _requirements = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
		private static readonly IReadOnlyList<TextOption> NoOptions = Array.Empty<TextOption>();
		private static readonly IReadOnlyList<string> NoBioLines = Array.Empty<string>();

		public static JsonTextCatalog Load(IContentSource localeSource)
		{
			var catalog = new JsonTextCatalog();
			foreach (string file in TypeFiles) if (localeSource.Exists(file)) catalog.ReadFile(file, localeSource.ReadAllText(file));
			return catalog;
		}
		public bool HasEntry(string entryId) => !string.IsNullOrEmpty(entryId) && _properties.ContainsKey(entryId);
		public bool HasProperty(string entryId, string propertyId) => _properties.TryGetValue(entryId, out HashSet<string>? properties) && properties.Contains(propertyId);
		public IReadOnlyList<TextOption> GetOptions(string entryId) => _options.TryGetValue(entryId, out List<TextOption>? options) ? options : NoOptions;
		public IReadOnlyList<string> GetBioLines(string slot) => _bioLines.TryGetValue(slot ?? string.Empty, out List<string>? lines) ? lines : NoBioLines;
		public IReadOnlyList<string> GetRequirements(string entryId) => entryId != null && _requirements.TryGetValue(entryId, out List<string>? flags) ? flags : NoBioLines;

		private void ReadFile(string fileName, string json)
		{
			try
			{
				using JsonDocument document = JsonDocument.Parse(json);
				if (document.RootElement.ValueKind != JsonValueKind.Object) throw new ContentException($"Каталог текстов: '{fileName}' должен быть объектом id -> запись.");
				foreach (JsonProperty entry in document.RootElement.EnumerateObject()) ReadEntry(entry.Name, entry.Value);
			}
			catch (JsonException exception) { throw new ContentException($"Каталог текстов, ошибка разбора '{fileName}': {exception.Message}"); }
		}
		private void ReadEntry(string id, JsonElement entry)
		{
			var properties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (entry.TryGetProperty("properties", out JsonElement list) && list.ValueKind == JsonValueKind.Array)
				foreach (JsonElement property in list.EnumerateArray()) if (property.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(property.GetString())) properties.Add(property.GetString()!);
			_properties[id] = properties;
			if (entry.TryGetProperty("requirements", out JsonElement flags) && flags.ValueKind == JsonValueKind.Array)
			{
				var required = new List<string>();
				foreach (JsonElement flag in flags.EnumerateArray()) if (flag.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(flag.GetString())) required.Add(flag.GetString()!);
				if (required.Count > 0) _requirements[id] = required;
			}
			if (entry.TryGetProperty("slot", out JsonElement slotValue) && slotValue.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(slotValue.GetString()))
			{
				string slot = slotValue.GetString()!; if (!_bioLines.TryGetValue(slot, out List<string>? lines)) { lines = new List<string>(); _bioLines[slot] = lines; } lines.Add(id);
			}
			if (!entry.TryGetProperty("options", out JsonElement options) || options.ValueKind != JsonValueKind.Array) return;
			var parsed = new List<TextOption>();
			foreach (JsonElement option in options.EnumerateArray()) parsed.Add(new TextOption(ReadString(option, "id"), ReadStats(option)));
			_options[id] = parsed;
		}
		private static string ReadString(JsonElement element, string name) => element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
		private static List<StatKind> ReadStats(JsonElement option)
		{
			var result = new List<StatKind>();
			if (option.TryGetProperty("requires", out JsonElement required) && required.ValueKind == JsonValueKind.Array)
				foreach (JsonElement item in required.EnumerateArray()) if (item.ValueKind == JsonValueKind.String && StatKinds.TryParse(item.GetString() ?? string.Empty, out StatKind stat)) result.Add(stat);
			return result;
		}
	}
}
