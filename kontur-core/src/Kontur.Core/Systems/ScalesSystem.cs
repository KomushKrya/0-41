using Kontur.Core.Config;
using Kontur.Core.Events;
using Kontur.Core.Model;

namespace Kontur.Core.Systems
{
	/// <summary>
	/// Шкалы «Заражение / Гласность / Лояльность» и условия мгновенного проигрыша (ДД, раздел 11).
	/// Единственное место, где эти значения меняются.
	/// </summary>
	public sealed class ScalesSystem
	{
		private readonly GameState _state;
		private readonly ScalesConfig _config;
		private readonly IEventBus _bus;

		public ScalesSystem(GameState state, ScalesConfig config, IEventBus bus)
		{
			_state = state;
			_config = config;
			_bus = bus;
		}

		public void Reset()
		{
			_state.Scales = new ScaleValues(_config.StartInfection, _config.StartPublicity, _config.StartLoyalty);
			_state.IsGameOver = false;
			_state.GameOverReason = null;
		}

		public void Apply(ScaleDelta delta, string reason)
		{
			if (delta.IsZero || _state.IsGameOver)
			{
				return;
			}

			_state.Scales = _state.Scales.Apply(delta, _config.Min, _config.Max);
			_bus.Publish(new ScalesChanged(_state.Scales, delta, reason));

			CheckGameOver();
		}

		/// <summary>
		/// Роняет лояльность до проигрышного порога одним движением.
		///
		/// Нужно там, где дело не в накопленных промахах, а в одном событии, после
		/// которого работать некому: погиб последний оперативник. Считать для этого
		/// «сколько отнять» на стороне вызывающего — значит продублировать знание
		/// о пороге, которое живёт здесь.
		/// </summary>
		public void DepleteLoyalty(string reason)
		{
			if (_state.IsGameOver || _state.Scales.Loyalty <= _config.LoyaltyLoseAt)
			{
				return;
			}

			Apply(new ScaleDelta(0.0, 0.0, _config.LoyaltyLoseAt - _state.Scales.Loyalty), reason);
		}

		private void CheckGameOver()
		{
			if (_state.IsGameOver)
			{
				return;
			}

			GameOverReason? reason = null;

			if (_state.Scales.Infection >= _config.InfectionLoseAt)
			{
				reason = Model.GameOverReason.InfectionMaxed;
			}
			else if (_state.Scales.Publicity >= _config.PublicityLoseAt)
			{
				reason = Model.GameOverReason.PublicityMaxed;
			}
			else if (_state.Scales.Loyalty <= _config.LoyaltyLoseAt)
			{
				reason = Model.GameOverReason.LoyaltyDepleted;
			}

			if (reason == null)
			{
				return;
			}

			_state.IsGameOver = true;
			_state.GameOverReason = reason;
			_bus.Publish(new GameOverTriggered(reason.Value, _state.Scales, _state.Day));
		}
	}
}
