using System;
using System.Collections.Generic;
using Kontur.Core.Model;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Склад снаряжения. Слоты — на группу, не на сотрудника (ДД, раздел 6).
	/// Единица считается занятой с момента отправки группы и возвращается на склад
	/// только по правилам своего вида.
	/// </summary>
	public sealed class EquipmentInventory
	{
		private readonly Dictionary<string, EquipmentStack> _stacks =
			new Dictionary<string, EquipmentStack>(StringComparer.OrdinalIgnoreCase);

		public IReadOnlyDictionary<string, EquipmentStack> Stacks
		{
			get { return _stacks; }
		}

		public int GetQuantity(string definitionId)
		{
			EquipmentStack? stack;
			return _stacks.TryGetValue(definitionId, out stack) ? stack.Quantity : 0;
		}

		public bool IsShiftOnly(string definitionId)
		{
			EquipmentStack? stack;
			return _stacks.TryGetValue(definitionId, out stack) && stack.IsShiftOnly;
		}

		public void Add(string definitionId, int quantity, bool isShiftOnly)
		{
			if (quantity <= 0)
			{
				return;
			}

			EquipmentStack? stack;
			if (_stacks.TryGetValue(definitionId, out stack))
			{
				stack.Quantity += quantity;
				return;
			}

			_stacks[definitionId] = new EquipmentStack(definitionId, quantity, isShiftOnly);
		}

		public bool TryTake(string definitionId)
		{
			EquipmentStack? stack;
			if (!_stacks.TryGetValue(definitionId, out stack) || stack.Quantity <= 0)
			{
				return false;
			}

			stack.Quantity--;
			return true;
		}

		public void Return(string definitionId)
		{
			EquipmentStack? stack;
			if (_stacks.TryGetValue(definitionId, out stack))
			{
				stack.Quantity++;
				return;
			}

			_stacks[definitionId] = new EquipmentStack(definitionId, 1, false);
		}

		/// <summary>Снаряжение, найденное в удачной миссии, живёт только текущую смену (ДД, раздел 6).</summary>
		public List<string> RemoveShiftOnlyItems()
		{
			var removed = new List<string>();
			foreach (KeyValuePair<string, EquipmentStack> pair in _stacks)
			{
				if (pair.Value.IsShiftOnly && pair.Value.Quantity > 0)
				{
					removed.Add(pair.Key);
				}
			}

			for (int i = 0; i < removed.Count; i++)
			{
				_stacks.Remove(removed[i]);
			}

			return removed;
		}

		public void RemoveAllOfKind(IReadOnlyDictionary<string, EquipmentDefinition> definitions, EquipmentKind kind)
		{
			var toRemove = new List<string>();
			foreach (KeyValuePair<string, EquipmentStack> pair in _stacks)
			{
				EquipmentDefinition? definition;
				if (definitions.TryGetValue(pair.Key, out definition) && definition.Kind == kind)
				{
					toRemove.Add(pair.Key);
				}
			}

			for (int i = 0; i < toRemove.Count; i++)
			{
				_stacks.Remove(toRemove[i]);
			}
		}

		public void Remove(string definitionId)
		{
			_stacks.Remove(definitionId);
		}

		public void Clear()
		{
			_stacks.Clear();
		}
	}
}
