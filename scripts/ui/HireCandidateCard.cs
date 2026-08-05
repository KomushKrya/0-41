using Godot;
using Kontur.Core.Api;
using Kontur.Core.Model;

/// <summary>
/// Карточка кандидата на экране найма. Вёрстка целиком в HireCandidateCard.tscn:
/// скрипт только раскладывает по готовым узлам данные из ядра.
///
/// Строк характеристик и способностей заранее неизвестно сколько, поэтому в сцене
/// лежат образцы — скрытые узлы StatRow и PerkLabel. Скрипт их размножает
/// через Duplicate, а не собирает по месту: так вид строки правится в редакторе
/// вместе с остальной карточкой, а не в двух местах разом.
/// </summary>
public partial class HireCandidateCard : Panel
{
	[Export] public NodePath NameLabelPath { get; set; } = new("Column/Name");
	[Export] public NodePath RankLabelPath { get; set; } = new("Column/Rank");
	[Export] public NodePath StatsPath { get; set; } = new("Column/Stats");
	[Export] public NodePath StatRowTemplatePath { get; set; } = new("Column/Stats/StatRow");
	[Export] public NodePath AbilitiesPath { get; set; } = new("Column/Abilities");
	[Export] public NodePath NoAbilitiesLabelPath { get; set; } = new("Column/Abilities/NoAbilities");
	[Export] public NodePath PerkLabelTemplatePath { get; set; } = new("Column/Abilities/PerkLabel");
	[Export] public NodePath PickButtonPath { get; set; } = new("Column/PickButton");

	/// <summary>Имя узла с подписью характеристики внутри образца строки.</summary>
	[Export] public string StatNameNode { get; set; } = "StatName";

	/// <summary>Имя узла со значением характеристики внутри образца строки.</summary>
	[Export] public string StatValueNode { get; set; } = "StatValue";

	/// <summary>Кого показывает карточка. Пусто — Setup ещё не звали.</summary>
	public string CandidateId { get; private set; } = string.Empty;

	/// <summary>Игрок нажал «взять» или снял отметку. Решает экран найма, а не карточка.</summary>
	public event System.Action<HireCandidateCard, bool> PickToggled;

	private Label _name;
	private Label _rank;
	private Control _stats;
	private Control _statRowTemplate;
	private Control _abilities;
	private Label _noAbilities;
	private Label _perkTemplate;
	private Button _pick;

	private bool _bound;

	public override void _Ready()
	{
		Bind();
	}

	/// <summary>
	/// Заполняет карточку. Зовётся сразу после AddChild, когда _Ready уже прошёл,
	/// но на случай другого порядка узлы находятся лениво.
	/// </summary>
	public void Setup(HireCandidateView candidate)
	{
		Bind();

		CandidateId = candidate.Id;

		_name.Text = candidate.Name;
		_rank.Text = Content.Label("ui_hiring_candidate_rank",
			"rank", candidate.RankTitle,
			"level", candidate.Level.ToString());

		FillStats(candidate);
		FillAbilities(candidate);
	}

	/// <summary>Снять или поставить отметку, не поднимая событие: правит экран найма.</summary>
	public void SetPickedNoSignal(bool picked)
	{
		Bind();
		_pick.SetPressedNoSignal(picked);
	}

	private void Bind()
	{
		if (_bound)
		{
			return;
		}

		_bound = true;

		_name = GetNode<Label>(NameLabelPath);
		_rank = GetNode<Label>(RankLabelPath);
		_stats = GetNode<Control>(StatsPath);
		_statRowTemplate = GetNode<Control>(StatRowTemplatePath);
		_abilities = GetNode<Control>(AbilitiesPath);
		_noAbilities = GetNode<Label>(NoAbilitiesLabelPath);
		_perkTemplate = GetNode<Label>(PerkLabelTemplatePath);
		_pick = GetNode<Button>(PickButtonPath);

		_statRowTemplate.Visible = false;
		_perkTemplate.Visible = false;

		_pick.Text = Content.Label("ui_hiring_take");
		_pick.Toggled += OnToggled;
	}

	private void OnToggled(bool pressed)
	{
		PickToggled?.Invoke(this, pressed);
	}

	// ------------------------------------------------------------------ содержимое

	/// <summary>
	/// Подпись характеристики берётся из текстового движка, а не пишется здесь
	/// строкой: id записи characteristic совпадает с именем StatKind, поэтому
	/// переименование правится в одном месте — в content/raw.
	/// </summary>
	private void FillStats(HireCandidateView candidate)
	{
		ClearCopies(_stats, _statRowTemplate);

		for (int i = 0; i < StatKinds.All.Length; i++)
		{
			StatKind kind = StatKinds.All[i];

			var row = (Control)_statRowTemplate.Duplicate();
			row.Visible = true;
			row.GetNode<Label>(StatNameNode).Text = Content.NameOf(kind.ToString().ToLowerInvariant());
			row.GetNode<Label>(StatValueNode).Text = candidate.Stats[kind].ToString();
			_stats.AddChild(row);
		}
	}

	private void FillAbilities(HireCandidateView candidate)
	{
		ClearCopies(_abilities, _perkTemplate, _noAbilities);

		_noAbilities.Text = Content.Label("ui_hiring_abilities_none");
		_noAbilities.Visible = candidate.AbilityIds.Count == 0;

		for (int i = 0; i < candidate.AbilityIds.Count; i++)
		{
			Label perk = BuildPerkLabel(candidate.AbilityIds[i]);
			_abilities.AddChild(perk);
		}
	}

	/// <summary>
	/// Название перка и пояснение берутся из текстового движка по тому же id,
	/// что лежит в данных способности. Нет записи — показываем сам id: так
	/// пропущенный текст виден сразу, а не выглядит пустым местом.
	/// </summary>
	private Label BuildPerkLabel(string abilityId)
	{
		string caption = abilityId;
		string tooltip = string.Empty;

		Content content = Content.Instance;
		if (content != null && content.TryGetEntry(abilityId, out ContentEntry entry))
		{
			if (!string.IsNullOrEmpty(entry.Name))
			{
				caption = entry.Name;
			}

			var builder = new System.Text.StringBuilder();
			for (int i = 0; i < entry.Chunks.Count; i++)
			{
				if (i > 0)
				{
					builder.Append('\n');
				}

				builder.Append(entry.Chunks[i].Text);
			}

			tooltip = builder.ToString();
		}

		var label = (Label)_perkTemplate.Duplicate();
		label.Visible = true;
		label.Text = "· " + caption;
		label.TooltipText = tooltip;
		return label;
	}

	/// <summary>Убирает то, что нарисовал прошлый вызов, не трогая узлы из сцены.</summary>
	private static void ClearCopies(Node parent, params Node[] keep)
	{
		foreach (Node child in parent.GetChildren())
		{
			bool isTemplate = false;
			for (int i = 0; i < keep.Length; i++)
			{
				if (child == keep[i])
				{
					isTemplate = true;
					break;
				}
			}

			if (!isTemplate)
			{
				parent.RemoveChild(child);
				child.QueueFree();
			}
		}
	}
}
