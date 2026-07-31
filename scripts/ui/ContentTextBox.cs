using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Базовый текстовый бокс: берёт запись контента по id, отбрасывает условные абзацы,
/// которые игра ещё не открыла, и отдаёт наследнику готовый список кусков.
///
/// Сам ничего не рисует и не листает — это дело наследника. Нижний текст, энциклопедия,
/// отчёты на компьютере выглядят по-разному, общее у них только две вещи: откуда берётся
/// текст и какие куски сейчас видны. Наследнику достаточно переопределить Render().
///
/// <example>
/// <code>
/// public partial class ReportBox : ContentTextBox
/// {
///     [Export] public NodePath LabelPath { get; set; } = new("Label");
///
///     private RichTextLabel _label = null!;
///
///     public override void _Ready()
///     {
///         _label = GetNode&lt;RichTextLabel&gt;(LabelPath);
///         base._Ready();
///     }
///
///     protected override void Render()
///     {
///         var text = new System.Text.StringBuilder();
///         foreach (ContentChunk chunk in Chunks)
///         {
///             text.AppendLine(chunk.Text);
///         }
///
///         _label.Text = text.ToString();
///     }
/// }
/// </code>
/// </example>
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

	/// <summary>
	/// Перечитать флаги и перерисовать, ничего не перезагружая. Звать, когда игра
	/// открыла новое свойство, а запись осталась та же.
	/// </summary>
	public void Refresh()
	{
		Rebuild();
	}

	/// <summary>
	/// Достать запись из Content заново и перерисовать. Нужно после перезагрузки текста:
	/// бокс держит ссылку на прежний ContentEntry, и одного Refresh() мало — он покажет
	/// то же самое. Ставит на место и переименованный, и пропавший id.
	/// </summary>
	public void Reload()
	{
		Open(ContentId);
	}

	/// <summary>Нарисовать <see cref="Chunks"/>. Зовётся при каждом Open() и Refresh(), в том числе с пустым списком.</summary>
	protected abstract void Render();

	/// <summary>
	/// Открыт ли условный абзац. По умолчанию спрашивает ядро в два шага: сначала пак
	/// сюжетных флагов (id свойства = имя флага), затем раскрытия энциклопедии, где то же
	/// свойство может быть открыто отчётом с выезда. Id записи и id существа в
	/// геймплейных данных — один и тот же, поэтому переводить ничего не нужно.
	/// Если флаги живут где-то ещё — переопределяй здесь, это единственная точка.
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
	/// Виден ли кусок сейчас. Тот же вопрос, по которому собран <see cref="Chunks"/>, —
	/// нужен наследникам, которые идут по <c>Entry.Chunks</c> сами, чтобы поставить на
	/// месте скрытого абзаца заглушку.
	/// </summary>
	protected bool IsChunkVisible(ContentChunk chunk)
	{
		return chunk.Reveal.Length == 0 || RevealEverything || IsRevealed(chunk.Reveal);
	}

	/// <summary>
	/// Значение для подстановки {{имя}}. Пара к <see cref="IsRevealed"/>: тот решает, показывать
	/// ли абзац, этот — чем заполнить дырку в нём. По умолчанию ищет число в геймплейных данных
	/// записи с тем же id — сейчас это перки, где {{bonus.strength}} и {{allStatsBonus}} берутся
	/// из data/abilities.json, поэтому правка баланса меняет текст сама, без правки текста.
	/// Пустая строка значит «не знаю»: подстановка останется видимой в тексте.
	/// Числа других типов появятся здесь же — это единственная точка.
	/// </summary>
	protected virtual string ResolveValue(string name)
	{
		KonturRuntime runtime = KonturRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return string.Empty;
		}

		return runtime.Simulation.Content.Abilities.TryGetValue(ContentId, out Ability ability)
			? AbilityValue(ability, name)
			: string.Empty;
	}

	private static string AbilityValue(Ability ability, string name)
	{
		if (name.Equals("allStatsBonus", StringComparison.OrdinalIgnoreCase))
		{
			return ability.AllStatsBonus.ToString(CultureInfo.InvariantCulture);
		}

		const string bonusPrefix = "bonus.";
		if (name.StartsWith(bonusPrefix, StringComparison.OrdinalIgnoreCase)
			&& StatKinds.TryParse(name.Substring(bonusPrefix.Length), out StatKind kind))
		{
			return ability.Bonus[kind].ToString(CultureInfo.InvariantCulture);
		}

		return string.Empty;
	}

	/// <summary>
	/// Разворачивает {{имя}} в куске. Кусок лежит в кэше <see cref="Content"/> и общий для всех
	/// боксов, поэтому подстановка идёт в копию: иначе первый показ переписал бы текст всем
	/// остальным, а при смене значения подставлять было бы уже некуда.
	/// </summary>
	private ContentChunk Substitute(ContentChunk chunk)
	{
		if (!Content.HasVariables(chunk.Text))
		{
			return chunk;
		}

		string filled = Content.Fill(chunk.Text, name =>
		{
			string value = ResolveValue(name);
			if (string.IsNullOrEmpty(value) && _reportedMissingValues.Add(name))
			{
				GD.PushWarning($"{Name}: нечем заполнить {{{{{name}}}}} в записи {ContentId}");
			}

			return value;
		});

		return new ContentChunk { Text = filled, Kind = chunk.Kind, Reveal = chunk.Reveal };
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
