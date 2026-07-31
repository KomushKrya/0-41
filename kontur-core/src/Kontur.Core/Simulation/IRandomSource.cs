using System.Collections.Generic;

namespace Kontur.Core.Simulation
{
	/// <summary>
	/// Единственный источник случайности в ядре. Подменяется в тестах на детерминированную заглушку,
	/// чтобы баланс можно было проверять воспроизводимо.
	/// </summary>
	public interface IRandomSource
	{
		/// <summary>Значение в [0;1).</summary>
		double NextDouble();

		/// <summary>Целое в [minInclusive; maxExclusive).</summary>
		int NextInt(int minInclusive, int maxExclusive);
	}

	public static class RandomSourceExtensions
	{
		public static bool Chance(this IRandomSource random, double probability)
		{
			if (probability <= 0.0)
			{
				return false;
			}

			if (probability >= 1.0)
			{
				return true;
			}

			return random.NextDouble() < probability;
		}

		public static T Pick<T>(this IRandomSource random, IReadOnlyList<T> items)
		{
			return items[random.NextInt(0, items.Count)];
		}

		/// <summary>Взвешенный выбор индекса для систем симуляции.</summary>
		public static int PickWeightedIndex(this IRandomSource random, IReadOnlyList<double> weights)
		{
			double total = 0.0;
			for (int i = 0; i < weights.Count; i++)
			{
				total += weights[i] > 0.0 ? weights[i] : 0.0;
			}

			if (total <= 0.0)
			{
				return random.NextInt(0, weights.Count);
			}

			double roll = random.NextDouble() * total;
			double accumulated = 0.0;
			for (int i = 0; i < weights.Count; i++)
			{
				accumulated += weights[i] > 0.0 ? weights[i] : 0.0;
				if (roll < accumulated)
				{
					return i;
				}
			}

			return weights.Count - 1;
		}
	}
}
