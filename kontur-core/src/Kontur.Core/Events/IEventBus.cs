using System;

namespace Kontur.Core.Events
{
	/// <summary>Маркер игрового сигнала. Все сигналы из раздела 13 ДД реализуют этот интерфейс.</summary>
	public interface IGameEvent
	{
	}

	public interface IEventBus
	{
		/// <summary>Подписка на конкретный тип сигнала. Возврат — токен отписки.</summary>
		IDisposable Subscribe<T>(Action<T> handler) where T : IGameEvent;

		/// <summary>Подписка на все сигналы сразу (логи, отладочный оверлей, аналитика).</summary>
		IDisposable SubscribeAll(Action<IGameEvent> handler);

		/// <summary>Публикация сигнала. Безопасна при вызове из обработчика (события ставятся в очередь).</summary>
		void Publish<T>(T gameEvent) where T : IGameEvent;
	}
}
