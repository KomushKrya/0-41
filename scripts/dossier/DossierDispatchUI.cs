#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Model;

[Tool]
public partial class DossierDispatchUI : Control
{
	[Export] public NodePath PageContainerPath { get; set; } = new("Page");
	[Export] public NodePath PageNumberPath { get; set; } = new("PageNumber");
	[Export] public NodePath PreviousPagePath { get; set; } = new("PreviousPage");
	[Export] public NodePath NextPagePath { get; set; } = new("NextPage");

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

	private readonly List<EmployeeView> _roster = new();
	private DossierPage _page = null!;
	private Label _pageNumber = null!;
	private BaseButton _previousPage = null!;
	private BaseButton _nextPage = null!;
	private ComputerUI? _dispatchComputer;
	private int _pageIndex;

	public event Action<EmployeeView>? EmployeeConfirmed;

	public override void _Ready()
	{
		_page = GetPage(PageContainerPath);
		_pageNumber = GetNode<Label>(PageNumberPath);
		_previousPage = GetNode<BaseButton>(PreviousPagePath);
		_nextPage = GetNode<BaseButton>(NextPagePath);
		if (Engine.IsEditorHint())
		{
			ApplyEditorPreview();
			return;
		}

		_page.PortraitButton.Pressed += () => ConfirmEmployeeAt(_pageIndex);
		_previousPage.Pressed += PreviousPage;
		_nextPage.Pressed += NextPage;
		BindUpgradeButton(_page.StrengthUpgradeButton, StatKind.Strength);
		BindUpgradeButton(_page.CombatUpgradeButton, StatKind.Combat);
		BindUpgradeButton(_page.AgilityUpgradeButton, StatKind.Agility);
		BindUpgradeButton(_page.CharismaUpgradeButton, StatKind.Charisma);
		BindUpgradeButton(_page.IntellectUpgradeButton, StatKind.Intellect);
		Refresh();
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
		SetUpgradeButtonsEnabled(false, null);
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

	public void OpenForDispatch(ComputerUI computerUi)
	{
		_dispatchComputer = computerUi;
		ReloadRoster();
		_pageIndex = FindFirstSelectableIndex();
		Refresh();
	}

	private void ReloadRoster()
	{
		_roster.Clear();
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return;
		}

		foreach (EmployeeView employee in runtime.Simulation.GetRoster())
		{
			_roster.Add(employee);
		}
	}

	private void PreviousPage()
	{
		if (_pageIndex <= 0)
		{
			return;
		}

		_pageIndex--;
		Refresh();
	}

	private void NextPage()
	{
		if (_pageIndex + 1 >= _roster.Count)
		{
			return;
		}

		_pageIndex++;
		Refresh();
	}

	private void ConfirmEmployeeAt(int employeeIndex)
	{
		if (employeeIndex < 0 || employeeIndex >= _roster.Count || !IsSelectable(_roster[employeeIndex]))
		{
			return;
		}

		EmployeeConfirmed?.Invoke(_roster[employeeIndex]);
	}

	private int FindFirstSelectableIndex()
	{
		for (int index = 0; index < _roster.Count; index++)
		{
			if (IsSelectable(_roster[index]))
			{
				return index;
			}
		}

		return 0;
	}

	private bool IsSelectable(EmployeeView employee)
	{
		return employee.Status == EmployeeStatus.Available
			&& (_dispatchComputer == null || !_dispatchComputer.IsEmployeeSelectedForDispatch(employee.Id));
	}

	private void BindUpgradeButton(Button button, StatKind stat)
	{
		button.Pressed += () => SpendSkillPoint(stat);
	}

	private void SpendSkillPoint(StatKind stat)
	{
		if (_pageIndex < 0 || _pageIndex >= _roster.Count)
		{
			return;
		}

		string employeeId = _roster[_pageIndex].Id;
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return;
		}

		CommandResult result = runtime.Simulation.SpendSkillPoint(employeeId, stat);
		if (!result.IsSuccess)
		{
			GD.PushWarning($"Dossier: {result.Error}");
			return;
		}

		ReloadRoster();
		_pageIndex = FindEmployeeIndex(employeeId);
		Refresh();
	}

	private int FindEmployeeIndex(string employeeId)
	{
		for (int index = 0; index < _roster.Count; index++)
		{
			if (_roster[index].Id == employeeId)
			{
				return index;
			}
		}

		return 0;
	}

	private void Refresh()
	{
		if (_roster.Count == 0)
		{
			RefreshEmptyPage();
			return;
		}

		_pageIndex = Mathf.Clamp(_pageIndex, 0, _roster.Count - 1);
		RefreshPage(_roster[_pageIndex]);
		_pageNumber.Text = $"{_pageIndex + 1} / {_roster.Count}";
		_previousPage.Disabled = _pageIndex == 0;
		_nextPage.Disabled = _pageIndex + 1 >= _roster.Count;
	}

	private void RefreshEmptyPage()
	{
		_page.Name.Text = "ДОСЬЕ ПУСТО";
		_page.Level.Text = "-";
		_page.Strength.Text = "-";
		_page.Combat.Text = "-";
		_page.Agility.Text = "-";
		_page.Charisma.Text = "-";
		_page.Intellect.Text = "-";
		_page.TraitsText.Text = string.Empty;
		_page.BioText.Text = string.Empty;
		_page.Experience.Text = "- / -";
		_page.SkillPoints.Text = "0";
		_page.Portrait.Texture = LoadPortrait(string.Empty);
		_page.PortraitButton.Text = string.Empty;
		_page.PortraitButton.Disabled = true;
		SetUpgradeButtonsEnabled(false, null);
		_pageNumber.Text = "- / -";
		_previousPage.Disabled = true;
		_nextPage.Disabled = true;
	}

	private void RefreshPage(EmployeeView employee)
	{
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
		_page.PortraitButton.Disabled = !IsSelectable(employee);
		_page.PortraitButton.Modulate = IsSelectable(employee) ? Colors.White : new Color(0.65f, 0.65f, 0.65f, 1.0f);
		SetUpgradeButtonsEnabled(employee.UnspentSkillPoints > 0 && employee.Status != EmployeeStatus.Dead, employee);
	}

	private void SetUpgradeButtonsEnabled(bool canSpend, EmployeeView? employee)
	{
		int maxStat = int.MaxValue;
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null && runtime.IsReady)
		{
			maxStat = runtime.Simulation.Config.Employees.MaxStatValue;
		}

		SetUpgradeButton(_page.StrengthUpgradeButton, canSpend, employee, StatKind.Strength, maxStat);
		SetUpgradeButton(_page.CombatUpgradeButton, canSpend, employee, StatKind.Combat, maxStat);
		SetUpgradeButton(_page.AgilityUpgradeButton, canSpend, employee, StatKind.Agility, maxStat);
		SetUpgradeButton(_page.CharismaUpgradeButton, canSpend, employee, StatKind.Charisma, maxStat);
		SetUpgradeButton(_page.IntellectUpgradeButton, canSpend, employee, StatKind.Intellect, maxStat);
	}

	private static void SetUpgradeButton(Button button, bool canSpend, EmployeeView? employee, StatKind stat, int maxStat)
	{
		button.Disabled = !canSpend || employee == null || employee.Stats[stat] >= maxStat;
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

	private static string BuildBioText(EmployeeView employee)
	{
		var lines = new List<string>();
		if (employee.Age > 0)
		{
			lines.Add($"Возраст: {employee.Age}");
		}

		foreach (string bioId in employee.BioIds)
		{
			string text = ResolveText(bioId);
			if (!string.IsNullOrWhiteSpace(text))
			{
				lines.Add(text);
			}
		}

		if (employee.IsInjured)
		{
			lines.Add("Травмирован");
		}

		if (!string.IsNullOrWhiteSpace(employee.CurrentIncidentId))
		{
			lines.Add("На задании: " + employee.CurrentIncidentId);
		}

		if (employee.Status == EmployeeStatus.Dead)
		{
			lines.Add("Погиб");
		}

		return string.Join("\n", lines);
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
			page.GetNode<TextureRect>("PortraitButton/Portrait"),
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
