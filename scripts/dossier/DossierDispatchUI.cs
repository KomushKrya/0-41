#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Model;

public partial class DossierDispatchUI : Control
{
	[Export] public NodePath LeftPagePath { get; set; } = new("LeftPage");
	[Export] public NodePath RightPagePath { get; set; } = new("RightPage");
	[Export] public NodePath PagePath { get; set; } = new("PageNumber");
	[Export] public NodePath PreviousPagePath { get; set; } = new("PreviousPage");
	[Export] public NodePath NextPagePath { get; set; } = new("NextPage");

	private readonly List<EmployeeView> _roster = new();
	private DossierPage _leftPage = null!;
	private DossierPage _rightPage = null!;
	private Label _page = null!;
	private Button _previousPage = null!;
	private Button _nextPage = null!;
	private ComputerUI? _dispatchComputer;
	private int _spreadStartIndex;

	public event Action<EmployeeView>? EmployeeConfirmed;

	public override void _Ready()
	{
		_leftPage = GetPage(LeftPagePath);
		_rightPage = GetPage(RightPagePath);
		_page = GetNode<Label>(PagePath);
		_previousPage = GetNode<Button>(PreviousPagePath);
		_nextPage = GetNode<Button>(NextPagePath);
		_leftPage.PortraitButton.Pressed += () => ConfirmEmployeeAt(_spreadStartIndex);
		_rightPage.PortraitButton.Pressed += () => ConfirmEmployeeAt(_spreadStartIndex + 1);
		_previousPage.Pressed += PreviousSpread;
		_nextPage.Pressed += NextSpread;
		Refresh();
	}

	public void OpenForDispatch(ComputerUI computerUi)
	{
		_dispatchComputer = computerUi;
		_roster.Clear();
		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime != null && runtime.IsReady)
		{
			foreach (EmployeeView employee in runtime.Session.GetRoster())
			{
				_roster.Add(employee);
			}
		}

		_spreadStartIndex = FindFirstSelectableIndex() / 2 * 2;
		Refresh();
	}

	private void PreviousSpread()
	{
		if (_spreadStartIndex <= 0)
		{
			return;
		}

		_spreadStartIndex -= 2;
		Refresh();
	}

	private void NextSpread()
	{
		if (_spreadStartIndex + 2 >= _roster.Count)
		{
			return;
		}

		_spreadStartIndex += 2;
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

	private void Refresh()
	{
		RefreshPage(_leftPage, _spreadStartIndex);
		RefreshPage(_rightPage, _spreadStartIndex + 1);

		if (_roster.Count == 0)
		{
			_page.Text = "- / -";
			_previousPage.Disabled = true;
			_nextPage.Disabled = true;
			return;
		}

		int spreadCount = Mathf.CeilToInt(_roster.Count / 2.0f);
		_page.Text = $"{_spreadStartIndex / 2 + 1} / {spreadCount}";
		_previousPage.Disabled = _spreadStartIndex == 0;
		_nextPage.Disabled = _spreadStartIndex + 2 >= _roster.Count;
	}

	private void RefreshPage(DossierPage page, int employeeIndex)
	{
		if (employeeIndex < 0 || employeeIndex >= _roster.Count)
		{
			page.Name.Text = "\u0414\u041e\u0421\u042c\u0415 \u041f\u0423\u0421\u0422\u041e";
			page.Level.Text = string.Empty;
			page.Stats.Text = string.Empty;
			page.Traits.Text = string.Empty;
			page.Status.Text = "\u041d\u0410 \u042d\u0422\u041e\u0419 \u0421\u0422\u0420\u0410\u041d\u0418\u0426\u0415 \u041d\u0415\u0422 \u0421\u041e\u0422\u0420\u0423\u0414\u041d\u0418\u041a\u0410";
			page.PortraitButton.Text = "-";
			page.PortraitButton.Disabled = true;
			page.PortraitButton.Modulate = new Color(0.5f, 0.5f, 0.5f, 1.0f);
			return;
		}

		EmployeeView employee = _roster[employeeIndex];
		bool isSelectable = IsSelectable(employee);
		page.Name.Text = employee.Name.ToUpperInvariant();
		page.Level.Text = $"\u0417\u0412\u0410\u041d\u0418\u0415: {employee.RankTitle}\n\u0423\u0420\u041e\u0412\u0415\u041d\u042c: {employee.Level}";
		page.Stats.Text = BuildStats(employee);
		page.Traits.Text = BuildTraits(employee);
		page.Status.Text = isSelectable
			? "\u041d\u0410\u0416\u041c\u0418\u0422\u0415 \u041d\u0410 \u0424\u041e\u0422\u041e, \u0427\u0422\u041e\u0411\u042b \u041d\u0410\u0417\u041d\u0410\u0427\u0418\u0422\u042c"
			: employee.Status == EmployeeStatus.Available
				? "\u0423\u0416\u0415 \u0412\u042b\u0411\u0420\u0410\u041d \u0414\u041b\u042f \u042d\u0422\u041e\u0419 \u0413\u0420\u0423\u041f\u041f\u042b"
				: "\u0417\u0410\u041d\u042f\u0422 \u041d\u0410 \u0414\u0420\u0423\u0413\u041e\u0419 \u041c\u0418\u0421\u0421\u0418\u0418";
		page.PortraitButton.Text = $"\u0424\u041e\u0422\u041e\n{employee.PortraitId}";
		page.PortraitButton.Disabled = !isSelectable;
		page.PortraitButton.Modulate = isSelectable ? Colors.White : new Color(0.5f, 0.5f, 0.5f, 1.0f);
	}

	private DossierPage GetPage(NodePath pagePath)
	{
		Node page = GetNode(pagePath);
		return new DossierPage(
			page.GetNode<Label>("EmployeeName"),
			page.GetNode<Label>("Level"),
			page.GetNode<Label>("Stats"),
			page.GetNode<Label>("Traits"),
			page.GetNode<Label>("Status"),
			page.GetNode<Button>("PortraitButton"));
	}

	private static string BuildStats(EmployeeView employee)
	{
		return "\u0425\u0410\u0420\u0410\u041a\u0422\u0415\u0420\u0418\u0421\u0422\u0418\u041a\u0418\n"
			+ $"\u0421\u0418\u041b {employee.Stats.Strength}  \u0412\u041e\u0421\u041f {employee.Stats.Intellect}\n"
			+ $"\u0412\u042b\u041d {employee.Stats.Combat}  \u0425\u0410\u0420 {employee.Stats.Charisma}\n"
			+ $"\u0421\u0410\u041c {employee.Stats.Agility}";
	}

	private static string BuildTraits(EmployeeView employee)
	{
		return employee.AbilityIds.Count == 0
			? "\u041e\u0421\u041e\u0411\u042b\u0415 \u0427\u0415\u0420\u0422\u042b: \u041d\u0415\u0422"
			: "\u041e\u0421\u041e\u0411\u042b\u0415 \u0427\u0415\u0420\u0422\u042b: " + string.Join(", ", employee.AbilityIds);
	}

	private sealed class DossierPage
	{
		public DossierPage(Label name, Label level, Label stats, Label traits, Label status, Button portraitButton)
		{
			Name = name;
			Level = level;
			Stats = stats;
			Traits = traits;
			Status = status;
			PortraitButton = portraitButton;
		}

		public Label Name { get; }
		public Label Level { get; }
		public Label Stats { get; }
		public Label Traits { get; }
		public Label Status { get; }
		public Button PortraitButton { get; }
	}
}
