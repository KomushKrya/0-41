#nullable enable

using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Model;

/// <summary>
/// Описание особой способности словами: что даёт и когда работает.
///
/// Отдельным классом, потому что тех же слов ждут разные экраны — набор и
/// досье, — а решение по ним игрок принимает одно и то же. Разойдись
/// формулировки, и «Тяжёлая рука» при найме означала бы не то же, что в досье.
///
/// Числа берутся у ядра, а не из текстов. В content/raw описания написаны
/// с подстановками вроде «+{{bonus.charisma}} к харизме»; тянуть их строкой
/// значило бы повторять здесь разбор шаблонов, а при правке data/abilities.json
/// описание разошлось бы с механикой и никто бы не заметил.
/// </summary>
public static class AbilityText
{
	/// <summary>
	/// «боевая подготовка +2, ловкость +2. Всегда.»
	/// Пусто, если ядро не поднялось или способность неизвестна.
	/// </summary>
	public static string Describe(Node context, string abilityId)
	{
		GameRuntime runtime = GameRuntime.Get(context);
		if (runtime == null || !runtime.IsReady)
		{
			return string.Empty;
		}

		Ability? ability = runtime.Session.Content.FindAbility(abilityId);
		if (ability == null)
		{
			return string.Empty;
		}

		var parts = new List<string>();

		// Бонус ко всем сразу пишем одной строкой, а не пятью одинаковыми:
		// «все характеристики +1» читается за секунду, список из пяти — нет.
		if (ability.AllStatsBonus != 0)
		{
			parts.Add("все характеристики " + Signed(ability.AllStatsBonus));
		}

		StatBlock bonus = ability.Bonus;
		for (int i = 0; i < StatKinds.All.Length; i++)
		{
			StatKind kind = StatKinds.All[i];
			int value = bonus[kind];
			if (value == 0)
			{
				continue;
			}

			// Пишем «ловкость +2», а не «+2 к ловкости». Предлог требует дательного
			// падежа, а названия в текстовом движке лежат в именительном: без
			// таблицы склонений выходило «+2 к боевая подготовка». Порядок
			// «характеристика — число» падежа не требует вовсе.
			string statName = Content.NameOf(kind.ToString().ToLowerInvariant()).ToLowerInvariant();
			parts.Add($"{statName} {Signed(value)}");
		}

		if (parts.Count == 0)
		{
			return string.Empty;
		}

		return string.Join(", ", parts) + ". " + DescribeCondition(context, ability);
	}

	/// <summary>
	/// Когда бонус работает. Условие важнее самого числа: «+1 ко всем» звучит
	/// сильнее, чем «+2 к силе», пока не выяснится, что первое — только против
	/// одного вида существ, а второе — всегда.
	/// </summary>
	private static string DescribeCondition(Node context, Ability ability)
	{
		switch (ability.Condition)
		{
			case AbilityConditionKind.AgainstCreatureTag:
				// Пока отдел не завёл карточку на это существо, название — спойлер.
				// Многоточие честнее пустоты: игрок видит, что условие есть,
				// и что оно узкое, — просто ещё не знает, к чему относится.
				return IsCreatureTagKnown(context, ability.ConditionValue)
					? $"Только против «{ability.ConditionValue}»."
					: "Только против «…».";

			case AbilityConditionKind.WithEquipment:
				// Снаряжение лежит на складе с самого начала, тайной оно не является.
				return $"Только со снаряжением «{ResolveName(ability.ConditionValue)}».";

			default:
				return "Всегда.";
		}
	}

	/// <summary>
	/// Знает ли отдел о существах с таким тегом.
	///
	/// Тег — не существо: «мимик» может стоять у нескольких карточек. Достаточно
	/// одной открытой, чтобы слово перестало быть тайной.
	/// </summary>
	private static bool IsCreatureTagKnown(Node context, string tag)
	{
		if (string.IsNullOrWhiteSpace(tag))
		{
			return false;
		}

		GameRuntime runtime = GameRuntime.Get(context);
		if (runtime == null || !runtime.IsReady)
		{
			return false;
		}

		IReadOnlyList<EncyclopediaEntryView> known = runtime.Session.GetEncyclopedia();
		for (int i = 0; i < known.Count; i++)
		{
			CreatureDefinition? creature = runtime.Session.Content.FindCreature(known[i].CreatureId);
			if (creature == null)
			{
				continue;
			}

			for (int t = 0; t < creature.Tags.Count; t++)
			{
				if (string.Equals(creature.Tags[t], tag, System.StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}
		}

		return false;
	}

	private static string Signed(int value)
	{
		return (value > 0 ? "+" : "−") + System.Math.Abs(value);
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
}
