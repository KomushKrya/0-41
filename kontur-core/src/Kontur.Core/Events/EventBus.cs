using System;
using System.Collections.Generic;

namespace Kontur.Core.Events
{
	/// <summary>
	/// Синхронная шина событий без зависимостей от движка.
	/// Публикация во время обработки другого события не приводит к рекурсии:
	/// вложенные события складываются в очередь и разбираются последовательно.
	/// </summary>
	public sealed class EventBus : IEventBus
	{
		private readonly Dictionary<Type, List<Action<IGameEvent>>> _handlers =
			new Dictionary<Type, List<Action<IGameEvent>>>();

		private readonly List<Action<IGameEvent>> _globalHandlers = new List<Action<IGameEvent>>();
		private readonly Queue<IGameEvent> _queue = new Queue<IGameEvent>();
		private bool _isDraining;

		public IDisposable Subscribe<T>(Action<T> handler) where T : IGameEvent
		{
			if (handler == null)
			{
				throw new ArgumentNullException(nameof(handler));
			}

			Type key = typeof(T);
			if (!_handlers.TryGetValue(key, out List<Action<IGameEvent>>? list))
			{
				list = new List<Action<IGameEvent>>();
				_handlers[key] = list;
			}

			Action<IGameEvent> wrapper = e => handler((T)e);
			list.Add(wrapper);
			return new Subscription(() => list.Remove(wrapper));
		}

		public IDisposable SubscribeAll(Action<IGameEvent> handler)
		{
			if (handler == null)
			{
				throw new ArgumentNullException(nameof(handler));
			}

			_globalHandlers.Add(handler);
			return new Subscription(() => _globalHandlers.Remove(handler));
		}

		public void Publish<T>(T gameEvent) where T : IGameEvent
		{
			if (gameEvent == null)
			{
				return;
			}

			_queue.Enqueue(gameEvent);

			if (_isDraining)
			{
				return;
			}

			_isDraining = true;
			try
			{
				while (_queue.Count > 0)
				{
					Dispatch(_queue.Dequeue());
				}
			}
			finally
			{
				_isDraining = false;
			}
		}

		public void Clear()
		{
			_handlers.Clear();
			_globalHandlers.Clear();
			_queue.Clear();
		}

		private void Dispatch(IGameEvent gameEvent)
		{
			Type actualType = gameEvent.GetType();
			if (_handlers.TryGetValue(actualType, out List<Action<IGameEvent>>? list) && list.Count > 0)
			{
				// Копия — обработчик имеет право отписаться прямо во время вызова.
				Action<IGameEvent>[] snapshot = list.ToArray();
				for (int i = 0; i < snapshot.Length; i++)
				{
					snapshot[i](gameEvent);
				}
			}

			if (_globalHandlers.Count > 0)
			{
				Action<IGameEvent>[] globals = _globalHandlers.ToArray();
				for (int i = 0; i < globals.Length; i++)
				{
					globals[i](gameEvent);
				}
			}
		}

		private sealed class Subscription : IDisposable
		{
			private Action? _unsubscribe;

			public Subscription(Action unsubscribe)
			{
				_unsubscribe = unsubscribe;
			}

			public void Dispose()
			{
				_unsubscribe?.Invoke();
				_unsubscribe = null;
			}
		}
	}
}
