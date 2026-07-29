using System.Collections.Generic;
using Godot;

public partial class Content : Node
{
	[Export] public string Locale { get; set; } = "ru";

	private const string LocalisationRoot = "res://content/localisation";

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

		LoadDirectory(localeRoot);
	}

	private void LoadDirectory(string path)
	{
		foreach (string fileName in DirAccess.GetFilesAt(path))
		{
			if (fileName.EndsWith(".json"))
			{
				LoadFile($"{path}/{fileName}");
			}
		}

		foreach (string directoryName in DirAccess.GetDirectoriesAt(path))
		{
			LoadDirectory($"{path}/{directoryName}");
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
			Day = source.TryGetValue("day", out Variant day) ? day.AsInt32() : 0,
			Requirements = ReadStringList(source, "requirements"),
			Properties = ReadStringList(source, "properties"),
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
				Name = ReadString(optionData, "name"),
				Canon = ReadString(optionData, "canon"),
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
				Reveal = reveal.VariantType == Variant.Type.String ? reveal.AsString() : string.Empty
			});
		}

		return chunks;
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
