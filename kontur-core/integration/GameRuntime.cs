// ЭСКИЗ ДЛЯ ИНТЕГРАЦИИ. В Godot-проект пока не копируется — см. docs/INTEGRATION.md.
//
// После мержа ветки: scripts/kontur/GameRuntime.cs + autoload под именем "GameRuntime".

using System;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Content;
using Kontur.Core.Events;

namespace Kontur.Integration
{
	/// <summary>
	/// Мост между симуляционным ядром и сценами. Единственное место, где движок
	/// встречается с ядром. Сцены подписываются на события этого узла и вызывают
	/// команды через Session — напрямую в системы ядра никто не лезет.
	/// </summary>
	public partial class GameRuntime : Node
	{
		[Export] public string ContentRoot { get; set; } = "res://data/";

		[Export] public int Seed { get; set; } = 41;

		/// <summary>Пауза симуляции — для меню, роликов и отладки.</summary>
		[Export] public bool IsPaused { get; set; }

		private IDisposable? _logSubscription;

		public GameSession Session { get; private set; } = null!;

		public override void _Ready()
		{
			ContentDatabase content = ContentLoader.Load(new GodotContentSource(ContentRoot));
			Session = new GameSession(content, Seed);

			// Единый лог всех сигналов ядра в Output — первое, что стоит смотреть при отладке.
			_logSubscription = Session.Events.SubscribeAll(e => GD.Print("[KONTUR] ", e.GetType().Name, " ", e));

			Session.Events.Subscribe<ShiftEnded>(OnShiftEnded);
			Session.Events.Subscribe<GameOverTriggered>(OnGameOver);
		}

		public override void _Process(double delta)
		{
			if (IsPaused)
			{
				return;
			}

			Session.Tick(delta);
		}

		public override void _ExitTree()
		{
			_logSubscription?.Dispose();
			_logSubscription = null;
		}

		private void OnShiftEnded(ShiftEnded e)
		{
			// Здесь — переход к пререндеренному ролику по e.OutroCutsceneId.
			GD.Print($"Смена {e.Day} завершена. Ролик: {e.OutroCutsceneId}");
		}

		private void OnGameOver(GameOverTriggered e)
		{
			// Здесь — финальный экран под конкретную причину проигрыша.
			GD.Print($"Game over: {e.Reason}");
		}
	}
}
