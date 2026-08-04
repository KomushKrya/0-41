#nullable enable

using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Model;

/// <summary>
/// Разворот досье: две страницы, один список сотрудников.
///
/// Обе половины разворота — <see cref="DossierDispatchUI"/>, но список штата и
/// номер текущего разворота живут здесь. Листание идёт парами, поэтому слева
/// всегда чётный индекс: иначе один и тот же сотрудник появлялся бы дважды —
/// сначала справа, потом слева.
/// </summary>
public partial class DossierSpread : Node
{
	private const int PagesPerSpread = 2;

	[Export] public NodePath LeftPagePath { get; set; } = new("../DossierViewport/DossierUI");
	[Export] public NodePath RightPagePath { get; set; } = new("../DossierViewport/DossierUIRight");

	private readonly List<EmployeeView> _roster = new();
	private readonly List<DossierDispatchUI> _pages = new();
	private ComputerUI? _dispatchComputer;
	private int _firstOnSpread;

	/// <summary>Игрок выбрал сотрудника для отправки. В настольном режиме не поднимается.</summary>
	public event Action<EmployeeView>? EmployeeConfirmed;

	public override void _Ready()
	{
		DossierDispatchUI left = GetNode<DossierDispatchUI>(LeftPagePath);
		DossierDispatchUI right = GetNode<DossierDispatchUI>(RightPagePath);
		_pages.Add(left);
		_pages.Add(right);

		left.ConfigureSide(showPrevious: true, showNext: false, showCursor: true);
		right.ConfigureSide(showPrevious: false, showNext: true, showCursor: false);

		for (int i = 0; i < _pages.Count; i++)
		{
			int offsetOnSpread = i;
			DossierDispatchUI page = _pages[i];
			page.EmployeeChosen += () => ConfirmEmployeeAt(_firstOnSpread + offsetOnSpread);
			page.SkillPointRequested += stat => SpendSkillPoint(_firstOnSpread + offsetOnSpread, stat);
			page.PreviousPageRequested += TurnBack;
			page.NextPageRequested += TurnForward;
		}

		ReloadRoster();
		Refresh();
	}

	/// <summary>Досье открыто с компьютера: портреты выбирают сотрудника на задание.</summary>
	public void OpenForDispatch(ComputerUI computerUi)
	{
		_dispatchComputer = computerUi;
		ReloadRoster();
		_firstOnSpread = AlignToSpread(FindFirstSelectableIndex());
		Refresh();
	}

	/// <summary>Досье открыто со стола: только чтение штата, выбирать некуда.</summary>
	public void OpenForBrowsing()
	{
		_dispatchComputer = null;
		ReloadRoster();
		_firstOnSpread = AlignToSpread(_firstOnSpread);
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

	private void TurnBack()
	{
		if (_firstOnSpread < PagesPerSpread)
		{
			return;
		}

		_firstOnSpread -= PagesPerSpread;
		Refresh();
	}

	private void TurnForward()
	{
		if (_firstOnSpread + PagesPerSpread >= _roster.Count)
		{
			return;
		}

		_firstOnSpread += PagesPerSpread;
		Refresh();
	}

	private void Refresh()
	{
		if (_firstOnSpread >= _roster.Count)
		{
			_firstOnSpread = 0;
		}

		int maxStat = ResolveMaxStatValue();
		// Очки тратят, когда досье читают со стола. В режиме отправки игрока ждёт
		// выбор сотрудника, и плюсики там только отвлекали бы от него.
		bool spendingAllowed = _dispatchComputer == null;

		for (int i = 0; i < _pages.Count; i++)
		{
			int rosterIndex = _firstOnSpread + i;
			if (_roster.Count == 0)
			{
				// «Досье пусто» пишем один раз, на левой половине.
				if (i == 0)
				{
					_pages[i].ShowEmptyRoster();
				}
				else
				{
					_pages[i].ShowBlank();
				}

				continue;
			}

			if (rosterIndex >= _roster.Count)
			{
				_pages[i].ShowBlank();
				continue;
			}

			EmployeeView employee = _roster[rosterIndex];
			_pages[i].ShowEmployee(
				employee,
				IsSelectable(employee),
				spendingAllowed,
				maxStat,
				$"{rosterIndex + 1} / {_roster.Count}");
		}

		bool canTurnBack = _firstOnSpread >= PagesPerSpread;
		bool canTurnForward = _firstOnSpread + PagesPerSpread < _roster.Count;
		for (int i = 0; i < _pages.Count; i++)
		{
			_pages[i].SetNavigation(canTurnBack, canTurnForward);
		}
	}

	private void ConfirmEmployeeAt(int rosterIndex)
	{
		if (rosterIndex < 0 || rosterIndex >= _roster.Count || !IsSelectable(_roster[rosterIndex]))
		{
			return;
		}

		EmployeeConfirmed?.Invoke(_roster[rosterIndex]);
	}

	private void SpendSkillPoint(int rosterIndex, StatKind stat)
	{
		if (rosterIndex < 0 || rosterIndex >= _roster.Count)
		{
			return;
		}

		GameRuntime runtime = GameRuntime.Get(this);
		if (runtime == null || !runtime.IsReady)
		{
			return;
		}

		CommandResult result = runtime.Simulation.SpendSkillPoint(_roster[rosterIndex].Id, stat);
		if (!result.IsSuccess)
		{
			GD.PushWarning($"Dossier: {result.Error}");
			return;
		}

		// Очко потрачено — поменялись и характеристика, и остаток очков, поэтому
		// перечитываем штат целиком, а не правим одну подпись.
		ReloadRoster();
		Refresh();
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

	private static int AlignToSpread(int rosterIndex)
	{
		return rosterIndex - (rosterIndex % PagesPerSpread);
	}

	private bool IsSelectable(EmployeeView employee)
	{
		return _dispatchComputer != null
			&& employee.Status == EmployeeStatus.Available
			&& !_dispatchComputer.IsEmployeeSelectedForDispatch(employee.Id);
	}

	private int ResolveMaxStatValue()
	{
		GameRuntime runtime = GameRuntime.Get(this);
		return runtime != null && runtime.IsReady
			? runtime.Simulation.Config.Employees.MaxStatValue
			: int.MaxValue;
	}
}
