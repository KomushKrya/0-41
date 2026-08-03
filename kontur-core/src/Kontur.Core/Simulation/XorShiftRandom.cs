namespace Kontur.Core.Simulation
{
	/// <summary>
	/// Детерминированный ГПСЧ (xorshift64*). Один и тот же seed => один и тот же прогон смены.
	/// Намеренно не System.Random: его реализация менялась между версиями .NET,
	/// а нам нужна воспроизводимость баланса между машинами.
	/// </summary>
	public sealed class XorShiftRandom : IRandomSource
	{
		private ulong _state;

		public XorShiftRandom(int seed)
		{
			Seed = seed;
			_state = seed == 0 ? 0x9E3779B97F4A7C15UL : (ulong)seed * 0x9E3779B97F4A7C15UL;
			if (_state == 0UL)
			{
				_state = 0x9E3779B97F4A7C15UL;
			}
		}

		public int Seed { get; }

		public ulong State
		{
			get { return _state; }
		}

		public void RestoreState(ulong state)
		{
			_state = state == 0UL ? 0x9E3779B97F4A7C15UL : state;
		}

		public double NextDouble()
		{
			ulong value = NextUInt64();
			// 53 значащих бита — стандартный способ получить double в [0;1).
			return (value >> 11) * (1.0 / 9007199254740992.0);
		}

		public int NextInt(int minInclusive, int maxExclusive)
		{
			if (maxExclusive <= minInclusive)
			{
				return minInclusive;
			}

			long range = (long)maxExclusive - minInclusive;
			return (int)(minInclusive + (long)(NextDouble() * range));
		}

		private ulong NextUInt64()
		{
			ulong x = _state;
			x ^= x >> 12;
			x ^= x << 25;
			x ^= x >> 27;
			_state = x;
			return unchecked(x * 0x2545F4914F6CDD1DUL);
		}
	}
}
