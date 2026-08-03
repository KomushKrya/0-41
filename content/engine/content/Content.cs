using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Godot;

public partial class Content : Node
{
	[Export] public string Locale { get; set; } = "ru";

	/// <summary>Корень собранного текста. Публичный: на него смотрит и горячая перезагрузка.</summary>
	public const string LocalisationRoot = "res://content/localisation";

	/// <summary>Должно совпадать с VARIABLE_RE в content/engine/converter/build.py.</summary>
	private static readonly Regex VariablePattern = new(@"\{\{([^{}]*)\}\}");

	private readonly Dictionary<string, ContentEntry> _entries = new();

	public static Content Instance { get; private set; } = null!;

	public IReadOnlyDictionary<string, ContentEntry> Entries => _entries;

	public override void _Ready()
	{
		Instance = this;
		Load(Locale);
	}

	public void Load(string locale)
	{
		_entries.Clear();
		Locale = locale;

		string localeRoot = $"{LocalisationRoot}/{locale}";
		if (!DirAccess.DirExistsAbsolute(localeRoot))
		{
			GD.PushWarning($"Content: нет папки локали {localeRoot}");
			return;
		}

		foreach (string path in EnumerateJsonFiles(localeRoot))
		{
			LoadFile(path);
		}
	}

	/// <summary>
	/// Все .json под указанной папкой, вложенные тоже (UI/perks лежит на два уровня вниз).
	///
	/// Единственное место, где решается, что считать файлом собранного текста: на нём сидят
	/// и загрузка, и слежение горячей перезагрузки. Иначе правило пришлось бы менять в двух
	/// местах, и второе однажды забыли бы.
	/// </summary>
	public static IEnumerable<string> EnumerateJsonFiles(string root)
	{
		if (!DirAccess.DirExistsAbsolute(root))
		{
			yield break;
		}

		foreach (string fileName in DirAccess.GetFilesAt(root))
		{
			if (fileName.EndsWith(".json"))
			{
				yield return $"{root}/{fileName}";
			}
		}

		foreach (string directoryName in DirAccess.GetDirectoriesAt(root))
		{
			foreach (string nested in EnumerateJsonFiles($"{root}/{directoryName}"))
			{
				yield return nested;
			}
		}
	}

	public ContentEntry GetEntry(string id)
	{
		return _entries.TryGetValue(id, out ContentEntry entry) ? entry : null;
	}

	public bool TryGetEntry(string id, out ContentEntry entry)
	{
		return _entries.TryGetValue(id, out entry);
	}

	public IReadOnlyList<ContentChunk> GetChunks(string id)
	{
		ContentEntry entry = GetEntry(id);
		return entry != null ? entry.Chunks : new List<ContentChunk>();
	}

	/// <summary>
	/// Подставляет значения вместо {{имя}}. Чистая функция: движок значений не знает —
	/// числа живут в геймплейных данных (data/abilities.json и т.п.) и приходят через
	/// resolve. Неразрешённая подстановка остаётся в тексте как есть — пустое место
	/// прочиталось бы как опечатка автора, а видимое {{имя}} сразу называет причину.
	/// Ругаться на пропажу — дело вызывающего: только он знает, из какой записи текст.
	/// </summary>
	public static string Fill(string text, Func<string, string> resolve)
	{
		if (string.IsNullOrEmpty(text) || !HasVariables(text))
		{
			return text;
		}

		return VariablePattern.Replace(text, match =>
		{
			string value = resolve != null ? resolve(match.Groups[1].Value) : null;
			return string.IsNullOrEmpty(value) ? match.Value : value;
		});
	}

	public static string Fill(string text, IReadOnlyDictionary<string, string> values)
	{
		return Fill(text, name => values != null && values.TryGetValue(name, out string value) ? value : null);
	}

	/// <summary>Есть ли в тексте {{имя}} — дешёвая проверка перед регуляркой.</summary>
	public static bool HasVariables(string text)
	{
		return !string.IsNullOrEmpty(text) && text.Contains("{{");
	}

	private void LoadFile(string path)
	{
		using FileAccess file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			GD.PushWarning($"Content: не удалось открыть {path}");
			return;
		}

		Variant parsed = Json.ParseString(file.GetAsText());
		if (parsed.VariantType != Variant.Type.Dictionary)
		{
			GD.PushWarning($"Content: {path} не является JSON-объектом");
			return;
		}

		foreach (KeyValuePair<Variant, Variant> pair in parsed.AsGodotDictionary())
		{
			string id = pair.Key.AsString();
			if (_entries.ContainsKey(id))
			{
				GD.PushWarning($"Content: дублирующийся id {id} в {path}");
				continue;
			}

			_entries[id] = ReadEntry(pair.Value.AsGodotDictionary());
		}
	}

	private static ContentEntry ReadEntry(Godot.Collections.Dictionary source)
	{
		ContentEntry entry = new()
		{
			Id = ReadString(source, "id"),
			Type = ReadString(source, "type"),
			Name = ReadString(source, "name"),
			Outcome = ReadString(source, "outcome"),
			MissionType = ReadString(source, "mission_type"),
			Slot = ReadString(source, "slot"),
			Day = source.TryGetValue("day", out Variant day) ? day.AsInt32() : 0,
			Requirements = ReadStringList(source, "requirements"),
			Properties = ReadStringList(source, "properties"),
			Variables = ReadStringList(source, "variables"),
			Stats = ReadStringList(source, "stats"),
			Chunks = ReadChunks(source, "chunks")
		};

		if (!source.TryGetValue("options", out Variant options))
		{
			return entry;
		}

		List<ContentOption> parsedOptions = new();
		foreach (Variant option in options.AsGodotArray())
		{
			Godot.Collections.Dictionary optionData = option.AsGodotDictionary();
			parsedOptions.Add(new ContentOption
			{
				Id = ReadString(optionData, "id"),
				Name = ReadString(optionData, "name"),
				Requirements = ReadStringList(optionData, "requires"),
				RequirementModifier = optionData.TryGetValue("requirement_modifier", out Variant modifier)
					? modifier.AsInt32()
					: 0,
				Chunks = ReadChunks(optionData, "chunks")
			});
		}

		entry.Options = parsedOptions;
		return entry;
	}

	private static List<ContentChunk> ReadChunks(Godot.Collections.Dictionary source, string key)
	{
		List<ContentChunk> chunks = new();
		if (!source.TryGetValue(key, out Variant value))
		{
			return chunks;
		}

		foreach (Variant item in value.AsGodotArray())
		{
			Godot.Collections.Dictionary chunkData = item.AsGodotDictionary();
			chunkData.TryGetValue("reveal", out Variant reveal);
			string kind = ReadString(chunkData, "kind");

			chunks.Add(new ContentChunk
			{
				Text = ReadString(chunkData, "text"),
				Kind = kind.Length > 0 ? kind : ContentChunk.KindText,
				Reveal = reveal.VariantType == Variant.Type.String ? reveal.AsString() : string.Empty,
				Spans = ReadSpans(chunkData)
			});
		}

		return chunks;
	}

	/// <summary>Отрезки подсветки куска. Пустой список — подсвечивать в нём нечего.</summary>
	private static List<ContentSpan> ReadSpans(Godot.Collections.Dictionary chunkData)
	{
		List<ContentSpan> spans = new();
		if (!chunkData.TryGetValue("spans", out Variant value))
		{
			return spans;
		}

		foreach (Variant item in value.AsGodotArray())
		{
			Godot.Collections.Dictionary spanData = item.AsGodotDictionary();
			spans.Add(new ContentSpan
			{
				Text = ReadString(spanData, "text"),
				Highlight = spanData.TryGetValue("highlight", out Variant highlight) && highlight.AsBool()
			});
		}

		return spans;
	}

	private static List<string> ReadStringList(Godot.Collections.Dictionary source, string key)
	{
		List<string> values = new();
		if (!source.TryGetValue(key, out Variant value))
		{
			return values;
		}

		foreach (Variant item in value.AsGodotArray())
		{
			values.Add(item.AsString());
		}

		return values;
	}

	private static string ReadString(Godot.Collections.Dictionary source, string key)
	{
		return source.TryGetValue(key, out Variant value) ? value.AsString() : string.Empty;
	}
}
