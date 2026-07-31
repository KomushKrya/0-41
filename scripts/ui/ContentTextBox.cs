using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Базовый текстовый бокс: берёт запись по id, отбрасывает условные абзацы, которые игра
/// ещё не открыла, и отдаёт наследнику готовый список кусков. Сам не рисует и не листает —
/// наследнику достаточно переопределить Render(). Пример — docs/ТЕКСТОВЫЕ-БОКСЫ.md.
/// </summary>
public abstract partial class ContentTextBox : Control
{
	/// <summary>Группа, по которой горячая перезагрузка находит все боксы в сцене.</summary>
	public const string GroupName = "content_text_box";

	/// <summary>Что показывать. Меняется и в рантайме — через Open().</summary>
	[Export] public string ContentId { get; set; } = string.Empty;

	/// <summary>Загрузить ContentId сразу в _Ready. Сними, если бокс наполняется по событию.</summary>
	[Export] public bool OpenOnReady { get; set; } = true;

	/// <summary>Показывать все условные абзацы, не спрашивая игру. Для превью и отладки.</summary>
	[Export] public bool RevealEverything { get; set; }

	/// <summary>Перечитывать текст, когда меняется флаг или открывается новый абзац энциклопедии.</summary>
	[Export] public bool RefreshOnReveal { get; set; } = true;

	private readonly List<ContentChunk> _chunks = new();

	/// <summary>Имена подстановок, о которых уже пожаловались: иначе Refresh() зальёт Output.</summary>
	private readonly HashSet<string> _reportedMissingValues = new();

	private IDisposable _revealSubscription;

	private IDisposable _flagSubscription;

	/// <summary>Загруженная запись. Null, пока Open() не позвали или если id не нашёлся.</summary>
	protected ContentEntry Entry { get; private set; }

	/// <summary>Куски в порядке файла, уже без скрытых. Пустой список, если записи нет.</summary>
	protected IReadOnlyList<ContentChunk> Chunks => _chunks;

	/// <summary>Шапка звонка (kind: call_meta) без квадратных скобок. Пустая строка, если её нет.</summary>
	protected string CallMeta { get; private set; } = string.Empty;

	public bool IsLoaded => Entry != null;

	public override void _Ready()
	{
		AddToGroup(GroupName);

		if (RefreshOnReveal)
		{
			GameRuntime runtime = GameRuntime.Get(this);
			if (runtime != null && runtime.IsReady)
			{
				_revealSubscription = runtime.Session.Events.Subscribe<CreatureRevealed>(_ => Refresh());
				_flagSubscription = runtime.Session.Events.Subscribe<FlagChanged>(_ => Refresh());
			}
		}

		if (OpenOnReady && ContentId.Length > 0)
		{
			Open(ContentId);
		}
	}

	public override void _ExitTree()
	{
		_revealSubscription?.Dispose();
		_revealSubscription = null;

		_flagSubscription?.Dispose();
		_flagSubscription = null;
	}

	/// <summary>Показать другую запись. Пустой id очищает бокс.</summary>
	public void Open(string contentId)
	{
		ContentId = contentId;
		_reportedMissingValues.Clear();

		if (contentId.Length == 0)
		{
			Entry = null;
			Rebuild();
			return;
		}

		Content content = Content.Instance;
		if (content == null)
		{
			GD.PushWarning($"{Name}: автозагрузка Content не найдена, показывать нечего");
			Entry = null;
			Rebuild();
			return;
		}

		Entry = content.GetEntry(contentId);
		if (Entry == null)
		{
			OnMissingContent(contentId);
		}

		Rebuild();
	}

	/// <summary>Перечитать флаги и перерисовать, ничего не перезагружая.</summary>
	public void Refresh()
	{
		Rebuild();
	}

	/// <summary>
	/// Достать запись из Content заново. Нужно после перезагрузки текста: бокс держит
	/// ссылку на прежний ContentEntry, и одного Refresh() мало.
	/// </summary>
	public void Reload()
	{
		Open(ContentId);
	}

	/// <summary>Нарисовать <see cref="Chunks"/>. Зовётся при каждом Open() и Refresh(), в том числе с пустым списком.</summary>
	protected abstract void Render();

	/// <summary>
	/// Открыт ли условный абзац: сюжетный флаг с именем свойства либо раскрытие
	/// энциклопедии. Единственная точка про условность — если флаги живут не в ядре,
	/// переопределяй здесь.
	/// </summary>
	protected virtual bool IsRevealed(string propertyId)
	{
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return false;
		}

		return runtime.Session.IsFlagSet(propertyId)
			|| runtime.Session.IsPropertyRevealed(ContentId, propertyId);
	}

	/// <summary>
	/// Виден ли кусок. Тот же вопрос, по которому собран <see cref="Chunks"/>, — для
	/// наследников, которые идут по <c>Entry.Chunks</c> сами и ставят заглушки.
	/// </summary>
	protected bool IsChunkVisible(ContentChunk chunk)
	{
		return chunk.Reveal.Length == 0 || RevealEverything || IsRevealed(chunk.Reveal);
	}

	/// <summary>
	/// Значение для {{имя}}. Ищет число в геймплейных данных записи с тем же id — перки
	/// в data/abilities.json, снаряжение в data/equipment.json, — поэтому правка баланса
	/// меняет текст сама. Пустая строка значит «не знаю»: подстановка останется видимой.
	/// </summary>
	protected virtual string ResolveValue(string name)
	{
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return string.Empty;
		}

		if (runtime.Session.Content.Abilities.TryGetValue(ContentId, out Ability ability))
		{
			return BonusValue(ability.Bonus, ability.AllStatsBonus, name);
		}

		EquipmentDefinition equipment = runtime.Session.Content.FindEquipment(ContentId);
		return equipment != null
			? BonusValue(equipment.Bonus, equipment.AllStatsBonus, name)
			: string.Empty;
	}

	/// <summary>{{allStatsBonus}} и {{bonus.<характеристика>}} — общая форма у перков и снаряжения.</summary>
	private static string BonusValue(StatBlock bonus, int allStatsBonus, string name)
	{
		if (name.Equals("allStatsBonus", StringComparison.OrdinalIgnoreCase))
		{
			return allStatsBonus.ToString(CultureInfo.InvariantCulture);
		}

		const string bonusPrefix = "bonus.";
		if (name.StartsWith(bonusPrefix, StringComparison.OrdinalIgnoreCase)
			&& StatKinds.TryParse(name.Substring(bonusPrefix.Length), out StatKind kind))
		{
			return bonus[kind].ToString(CultureInfo.InvariantCulture);
		}

		return string.Empty;
	}

	/// <summary>
	/// Разворачивает {{имя}} в копию куска: оригинал лежит в кэше <see cref="Content"/> и
	/// общий для всех боксов, первый же показ переписал бы текст остальным.
	/// </summary>
	private ContentChunk Substitute(ContentChunk chunk)
	{
		if (!Content.HasVariables(chunk.Text))
		{
			return chunk;
		}

		string Resolve(string name)
		{
			string value = ResolveValue(name);
			if (string.IsNullOrEmpty(value) && _reportedMissingValues.Add(name))
			{
				GD.PushWarning($"{Name}: нечем заполнить {{{{{name}}}}} в записи {ContentId}");
			}

			return value;
		}

		// Каждый отрезок подставляется сам по себе — потому конвертер и отдаёт отрезки,
		// а не позиции: позиции после подстановки уехали бы.
		List<ContentSpan> spans = new(chunk.Spans.Count);
		foreach (ContentSpan span in chunk.Spans)
		{
			spans.Add(new ContentSpan
			{
				Text = Content.Fill(span.Text, Resolve),
				Highlight = span.Highlight,
				Bold = span.Bold
			});
		}

		return new ContentChunk
		{
			Text = Content.Fill(chunk.Text, Resolve),
			Kind = chunk.Kind,
			Reveal = chunk.Reveal,
			Spans = spans
		};
	}

	/// <summary>Записи с таким id нет. По умолчанию предупреждение в Output.</summary>
	protected virtual void OnMissingContent(string contentId)
	{
		GD.PushWarning($"{Name}: нет записи контента с id {contentId}");
	}

	private void Rebuild()
	{
		_chunks.Clear();
		CallMeta = string.Empty;

		if (Entry != null)
		{
			foreach (ContentChunk chunk in Entry.Chunks)
			{
				if (!IsChunkVisible(chunk))
				{
					continue;
				}

				ContentChunk ready = Substitute(chunk);

				if (ready.IsCallMeta && CallMeta.Length == 0)
				{
					CallMeta = ready.Text;
				}

				_chunks.Add(ready);
			}
		}

		Render();
	}
}
