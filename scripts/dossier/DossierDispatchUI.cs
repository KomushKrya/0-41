#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Model;

/// <summary>
/// Одна страница разворота досье.
///
/// Страница ничего не знает ни о штате, ни о том, кого показывает соседняя
/// половина: и то и другое держит <see cref="DossierSpread"/>. Иначе две копии
/// этого класса вели бы каждая свой список и разъехались бы на первом листании.
/// </summary>
[Tool]
public partial class DossierDispatchUI : Control
{
	[Export] public NodePath PageContainerPath { get; set; } = new("Page");
	[Export] public NodePath PageNumberPath { get; set; } = new("PageNumber");
	[Export] public NodePath PreviousPagePath { get; set; } = new("PreviousPage");
	[Export] public NodePath NextPagePath { get; set; } = new("NextPage");
	[Export] public NodePath CursorPath { get; set; } = new("DossierCursor");

	/// <summary>Подложка страницы: единственное, что остаётся на пустой половине разворота.</summary>
	[Export] public NodePath PageBackgroundPath { get; set; } = new("Page/задний фон");

	private UiPreviewData? _previewData;

	[Export]
	public UiPreviewData? PreviewData
	{
		get => _previewData;
		set
		{
			if (_previewData == value)
			{
				return;
			}

			if (_previewData != null)
			{
				_previewData.Changed -= OnPreviewDataChanged;
			}

			_previewData = value;
			if (_previewData != null)
			{
				_previewData.Changed += OnPreviewDataChanged;
			}

			RequestEditorPreviewRefresh();
		}
	}

	/// <summary>Насколько уголок светлеет под нажатием.</summary>
	[Export] public Color PressedCornerTint { get; set; } = new(1.12f, 1.12f, 1.12f);

	/// <summary>Подсветка портрета под курсором: кнопка прозрачная, светлеет только наведением.</summary>
	[Export] public Color PortraitHoverTint { get; set; } = new(1.0f, 1.0f, 1.0f, 0.16f);

	private const string DesaturateShaderPath = "res://assets/shaders/PortraitDesaturate.gdshader";

	/// <summary>Пропорция рамки портрета из DossierUI.tscn: 146.8 на 186.5.</summary>
	private const float DefaultPortraitAspect = 0.787f;

	private DossierPage _page = null!;
	private Label _pageNumber = null!;
	private BaseButton _previousPage = null!;
	private BaseButton _nextPage = null!;
	private bool _ownsPreviousCorner;
	private bool _ownsNextCorner;
	private readonly List<CanvasItem> _pageContent = new();

	/// <summary>Игрок выбрал сотрудника с этой страницы для отправки.</summary>
	public event Action? EmployeeChosen;

	/// <summary>Игрок нажал плюсик у характеристики на этой странице.</summary>
	public event Action<StatKind>? SkillPointRequested;

	public event Action? PreviousPageRequested;
	public event Action? NextPageRequested;

	public override void _Ready()
	{
		_page = GetPage(PageContainerPath);
		_pageNumber = GetNode<Label>(PageNumberPath);
		_previousPage = GetNode<BaseButton>(PreviousPagePath);
		_nextPage = GetNode<BaseButton>(NextPagePath);
		CollectPageContent();
		if (Engine.IsEditorHint())
		{
			ApplyEditorPreview();
			return;
		}

		_page.PortraitButton.Pressed += () => EmployeeChosen?.Invoke();
		ApplyPortraitButtonLook();
		_previousPage.Pressed += () => PreviousPageRequested?.Invoke();
		_nextPage.Pressed += () => NextPageRequested?.Invoke();
		BindCornerPressFeedback(_previousPage);
		BindCornerPressFeedback(_nextPage);
		BindUpgradeButton(_page.StrengthUpgradeButton, StatKind.Strength);
		BindUpgradeButton(_page.CombatUpgradeButton, StatKind.Combat);
		BindUpgradeButton(_page.AgilityUpgradeButton, StatKind.Agility);
		BindUpgradeButton(_page.CharismaUpgradeButton, StatKind.Charisma);
		BindUpgradeButton(_page.IntellectUpgradeButton, StatKind.Intellect);
	}

	/// <summary>
	/// Уголки страницы — это и есть кнопки листания, поэтому на левой половине
	/// разворота живёт только «назад», а на правой только «вперёд». Курсор на
	/// разворот нужен один, и он ездит в координатах всего вьюпорта.
	/// </summary>
	public void ConfigureSide(bool showPrevious, bool showNext, bool showCursor)
	{
		_ownsPreviousCorner = showPrevious;
		_ownsNextCorner = showNext;
		_previousPage.Visible = showPrevious;
		_nextPage.Visible = showNext;

		if (!showCursor)
		{
			Control? cursor = GetNodeOrNull<Control>(CursorPath);
			if (cursor != null)
			{
				cursor.Visible = false;
			}
		}
	}

	public void ShowEmployee(
		EmployeeView employee,
		bool selectable,
		bool spendingAllowed,
		int maxStat,
		string pageNumber)
	{
		SetPageContentVisible(true);
		_pageNumber.Visible = true;
		_page.Name.Text = employee.Name.ToUpperInvariant();
		_page.Level.Text = employee.Level.ToString();
		_page.Strength.Text = employee.Stats.Strength.ToString();
		_page.Combat.Text = employee.Stats.Combat.ToString();
		_page.Agility.Text = employee.Stats.Agility.ToString();
		_page.Charisma.Text = employee.Stats.Charisma.ToString();
		_page.Intellect.Text = employee.Stats.Intellect.ToString();
		_page.TraitsText.Text = BuildTraitsText(employee);
		_page.BioText.Text = BuildBioText(employee);
		_page.Experience.Text = $"{employee.Experience} / {employee.ExperienceToNextLevel}";
		_page.SkillPoints.Text = employee.UnspentSkillPoints.ToString();
		_page.Portrait.Texture = LoadPortrait(employee.PortraitId);
		_page.PortraitButton.Text = string.Empty;
		_page.PortraitButton.Disabled = !selectable;

		// Гибель читается прямо с фотографии: погибший — чёрно-белый снимок.
		// Травма по фото не видна намеренно, о ней узнают с терминала.
		SetPortraitDesaturated(employee.Status == EmployeeStatus.Dead);
		_pageNumber.Text = pageNumber;
		SetUpgradeButtons(
			spendingAllowed && employee.UnspentSkillPoints > 0 && employee.Status != EmployeeStatus.Dead,
			employee,
			maxStat);
	}

	/// <summary>
	/// Половина разворота, за которой сотрудника уже нет: чистая бумага.
	///
	/// Прочерки вместо цифр и пустые заголовки читались как сломанная страница,
	/// поэтому от страницы остаётся только подложка.
	/// </summary>
	public void ShowBlank()
	{
		SetPageContentVisible(false);
		_pageNumber.Visible = false;
	}

	/// <summary>Штата нет вовсе — это стоит сказать словами, а не пустой страницей.</summary>
	public void ShowEmptyRoster()
	{
		SetPageContentVisible(false);
		_pageNumber.Visible = false;
		_page.Name.Visible = true;
		_page.Name.Text = "ДОСЬЕ ПУСТО";
	}

	/// <summary>
	/// Уголок, которым некуда листать, не гасится, а исчезает: у первой страницы
	/// досье левого уголка физически нет, как и правого у последней.
	/// </summary>
	public void SetNavigation(bool canTurnBack, bool canTurnForward)
	{
		_previousPage.Visible = _ownsPreviousCorner && canTurnBack;
		_nextPage.Visible = _ownsNextCorner && canTurnForward;
		_previousPage.Disabled = !canTurnBack;
		_nextPage.Disabled = !canTurnForward;
	}

	/// <summary>
	/// Кнопка лежит поверх фотографии и сама ничего не рисует: рамку и снимок
	/// даёт страница, а кнопка только ловит нажатие и светлеет под курсором.
	/// </summary>
	private void ApplyPortraitButtonLook()
	{
		var transparent = new StyleBoxEmpty();
		_page.PortraitButton.AddThemeStyleboxOverride("normal", transparent);
		_page.PortraitButton.AddThemeStyleboxOverride("pressed", transparent);
		_page.PortraitButton.AddThemeStyleboxOverride("focus", transparent);
		_page.PortraitButton.AddThemeStyleboxOverride("disabled", transparent);
		_page.PortraitButton.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = PortraitHoverTint });
	}

	private void SetPortraitDesaturated(bool isDesaturated)
	{
		if (!isDesaturated)
		{
			_page.Portrait.Material = null;
			return;
		}

		var shader = GD.Load<Shader>(DesaturateShaderPath);
		if (shader == null)
		{
			GD.PushWarning($"{nameof(DossierDispatchUI)}: шейдер {DesaturateShaderPath} не найден.");
			return;
		}

		var material = new ShaderMaterial { Shader = shader };

		// Рамка портрета выше, чем шире, и без пропорции лента пошла бы не под 45°.
		// До первой раскладки размер ещё нулевой, поэтому запасной вариант —
		// пропорция из сцены, а не единица: иначе первый кадр рисуется с браком.
		Vector2 frame = _page.Portrait.Size;
		material.SetShaderParameter(
			"aspect",
			frame.X > 0.0f && frame.Y > 0.0f ? frame.X / frame.Y : DefaultPortraitAspect);
		_page.Portrait.Material = material;
	}

	private void BindCornerPressFeedback(BaseButton corner)
	{
		corner.ButtonDown += () => corner.SelfModulate = PressedCornerTint;
		corner.ButtonUp += () => corner.SelfModulate = Colors.White;
	}

	private void ApplyEditorPreview()
	{
		UiPreviewData? preview = PreviewData;
		if (preview == null)
		{
			return;
		}

		_page.Name.Text = preview.PrimaryName;
		_page.Level.Text = "1";
		_page.Strength.Text = "3";
		_page.Combat.Text = "3";
		_page.Agility.Text = "3";
		_page.Charisma.Text = "3";
		_page.Intellect.Text = "3";
		_page.TraitsText.Text = preview.Description;
		_page.BioText.Text = preview.Status;
		_page.Experience.Text = "0 / 100";
		_page.SkillPoints.Text = "0";
		_page.PortraitButton.Disabled = true;
		SetUpgradeButtons(false, null, int.MaxValue);
		_pageNumber.Text = "1 / 1";
		_previousPage.Disabled = true;
		_nextPage.Disabled = true;
	}

	private void OnPreviewDataChanged() => RequestEditorPreviewRefresh();

	private void RequestEditorPreviewRefresh()
	{
		if (Engine.IsEditorHint() && IsInsideTree())
		{
			CallDeferred(nameof(ApplyEditorPreview));
		}
	}

	private void BindUpgradeButton(Button button, StatKind stat)
	{
		button.Pressed += () => SkillPointRequested?.Invoke(stat);
	}

	/// <summary>
	/// Собирает всё содержимое страницы, кроме подложки: его целиком гасят
	/// <see cref="ShowBlank"/> и <see cref="ShowEmptyRoster"/>.
	/// </summary>
	private void CollectPageContent()
	{
		_pageContent.Clear();
		Node pageNode = GetNode(PageContainerPath);
		Node? background = GetNodeOrNull(PageBackgroundPath);
		if (background == null)
		{
			GD.PushWarning($"{nameof(DossierDispatchUI)}: подложка страницы не найдена по пути {PageBackgroundPath}.");
		}

		foreach (Node child in pageNode.GetChildren())
		{
			if (child != background && child is CanvasItem item)
			{
				_pageContent.Add(item);
			}
		}
	}

	private void SetPageContentVisible(bool isVisible)
	{
		for (int i = 0; i < _pageContent.Count; i++)
		{
			_pageContent[i].Visible = isVisible;
		}
	}

	/// <summary>
	/// Плюсики не просто гаснут, а исчезают: кнопка, которая никогда не сработает,
	/// на бумажной анкете выглядит опечаткой, а не отключённым элементом.
	/// </summary>
	private void SetUpgradeButtons(bool canSpend, EmployeeView? employee, int maxStat)
	{
		SetUpgradeButton(_page.StrengthUpgradeButton, canSpend, employee, StatKind.Strength, maxStat);
		SetUpgradeButton(_page.CombatUpgradeButton, canSpend, employee, StatKind.Combat, maxStat);
		SetUpgradeButton(_page.AgilityUpgradeButton, canSpend, employee, StatKind.Agility, maxStat);
		SetUpgradeButton(_page.CharismaUpgradeButton, canSpend, employee, StatKind.Charisma, maxStat);
		SetUpgradeButton(_page.IntellectUpgradeButton, canSpend, employee, StatKind.Intellect, maxStat);
	}

	private static void SetUpgradeButton(Button button, bool canSpend, EmployeeView? employee, StatKind stat, int maxStat)
	{
		bool isAvailable = canSpend && employee != null && employee.Stats[stat] < maxStat;
		button.Visible = isAvailable;
		button.Disabled = !isAvailable;
	}

	private static Texture2D? LoadPortrait(string portraitId)
	{
		if (!string.IsNullOrWhiteSpace(portraitId))
		{
			Texture2D? portrait = GD.Load<Texture2D>($"res://assets/portraits/{portraitId}.png");
			if (portrait != null)
			{
				return portrait;
			}

			portrait = GD.Load<Texture2D>($"res://assets/textures/{portraitId}.png");
			if (portrait != null)
			{
				return portrait;
			}
		}

		return GD.Load<Texture2D>("res://assets/textures/test man.png");
	}

	private static string BuildTraitsText(EmployeeView employee)
	{
		if (employee.AbilityIds.Count == 0)
		{
			return "Нет";
		}

		var traits = new List<string>();
		foreach (string abilityId in employee.AbilityIds)
		{
			traits.Add("• " + ResolveName(abilityId));
		}

		return string.Join("\n", traits);
	}

	/// <summary>
	/// Собирает досье одним абзацем, а не столбиком строк.
	///
	/// Слотов у генератора четыре, плюс возраст и пометки о состоянии — столбиком
	/// это всегда упиралось в низ страницы. Сплошным текстом запись ещё и больше
	/// похожа на то, что её писал живой человек, а не заполнил бланк.
	/// </summary>
	private static string BuildBioText(EmployeeView employee)
	{
		var sentences = new List<string>();
		if (employee.Age > 0)
		{
			sentences.Add($"Возраст: {employee.Age}");
		}

		foreach (string bioId in employee.BioIds)
		{
			string text = ResolveText(bioId);
			if (!string.IsNullOrWhiteSpace(text))
			{
				sentences.Add(text);
			}
		}

		if (employee.IsInjured)
		{
			sentences.Add("Травмирован");
		}

		if (!string.IsNullOrWhiteSpace(employee.CurrentIncidentId))
		{
			sentences.Add("На задании: " + employee.CurrentIncidentId);
		}

		if (employee.Status == EmployeeStatus.Dead)
		{
			sentences.Add("Погиб");
		}

		for (int i = 0; i < sentences.Count; i++)
		{
			sentences[i] = EndSentence(sentences[i]);
		}

		return string.Join(" ", sentences);
	}

	/// <summary>
	/// Закрывает фразу точкой, если автор её не поставил: в сплошном абзаце
	/// строки без знака слипаются в одно предложение.
	/// </summary>
	private static string EndSentence(string text)
	{
		string trimmed = text.TrimEnd();
		if (trimmed.Length == 0)
		{
			return trimmed;
		}

		char last = trimmed[trimmed.Length - 1];
		return last == '.' || last == '!' || last == '?' || last == '…' || last == ';'
			? trimmed
			: trimmed + '.';
	}

	private static string ResolveName(string entryId)
	{
		if (Content.Instance == null)
		{
			return entryId;
		}

		ContentEntry? entry = Content.Instance.GetEntry(entryId);
		return entry != null && !string.IsNullOrWhiteSpace(entry.Name) ? entry.Name : entryId;
	}

	private static string ResolveText(string entryId)
	{
		if (Content.Instance == null)
		{
			return string.Empty;
		}

		ContentEntry? entry = Content.Instance.GetEntry(entryId);
		return entry == null || entry.Chunks.Count == 0 ? string.Empty : entry.Chunks[0].Text;
	}

	private DossierPage GetPage(NodePath pagePath)
	{
		Node page = GetNode(pagePath);
		return new DossierPage(
			page.GetNode<Label>("EmployeeName"),
			page.GetNode<Label>("LevelText/LabelID"),
			page.GetNode<Button>("PortraitButton"),
			page.GetNode<TextureRect>("Portrait"),
			page.GetNode<Label>("strength/strengthID"),
			page.GetNode<Label>("combat/combatID"),
			page.GetNode<Label>("agility/agilityID"),
			page.GetNode<Label>("charisma/charismaID"),
			page.GetNode<Label>("intellect/strengthID"),
			page.GetNode<Button>("strength/StrengthUpgradeButton"),
			page.GetNode<Button>("combat/CombatUpgradeButton"),
			page.GetNode<Button>("agility/AgilityUpgradeButton"),
			page.GetNode<Button>("charisma/CharismaUpgradeButton"),
			page.GetNode<Button>("intellect/IntellectUpgradeButton"),
			page.GetNode<RichTextLabel>("Traits/RichTextLabel"),
			page.GetNode<RichTextLabel>("Bio/RichTextLabel"),
			page.GetNode<Label>("LabelXP/CountXP"),
			page.GetNode<Label>("LabelXP2/CountXP"));
	}

	private sealed class DossierPage
	{
		public DossierPage(
			Label name,
			Label level,
			Button portraitButton,
			TextureRect portrait,
			Label strength,
			Label combat,
			Label agility,
			Label charisma,
			Label intellect,
			Button strengthUpgradeButton,
			Button combatUpgradeButton,
			Button agilityUpgradeButton,
			Button charismaUpgradeButton,
			Button intellectUpgradeButton,
			RichTextLabel traitsText,
			RichTextLabel bioText,
			Label experience,
			Label skillPoints)
		{
			Name = name;
			Level = level;
			PortraitButton = portraitButton;
			Portrait = portrait;
			Strength = strength;
			Combat = combat;
			Agility = agility;
			Charisma = charisma;
			Intellect = intellect;
			StrengthUpgradeButton = strengthUpgradeButton;
			CombatUpgradeButton = combatUpgradeButton;
			AgilityUpgradeButton = agilityUpgradeButton;
			CharismaUpgradeButton = charismaUpgradeButton;
			IntellectUpgradeButton = intellectUpgradeButton;
			TraitsText = traitsText;
			BioText = bioText;
			Experience = experience;
			SkillPoints = skillPoints;
		}

		public Label Name { get; }
		public Label Level { get; }
		public Button PortraitButton { get; }
		public TextureRect Portrait { get; }
		public Label Strength { get; }
		public Label Combat { get; }
		public Label Agility { get; }
		public Label Charisma { get; }
		public Label Intellect { get; }
		public Button StrengthUpgradeButton { get; }
		public Button CombatUpgradeButton { get; }
		public Button AgilityUpgradeButton { get; }
		public Button CharismaUpgradeButton { get; }
		public Button IntellectUpgradeButton { get; }
		public RichTextLabel TraitsText { get; }
		public RichTextLabel BioText { get; }
		public Label Experience { get; }
		public Label SkillPoints { get; }
	}
}
