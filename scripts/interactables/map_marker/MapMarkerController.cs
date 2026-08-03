using System;
using System.Collections.Generic;
using Godot;
using Kontur.Core.Api;
using Kontur.Core.Events;
using Kontur.Core.Model;

/// <summary>
/// Отображает события карты из core физическими кнопками на настенной карте.
/// Контроллер не меняет симуляцию: он только подписывается на её события.
/// </summary>
public partial class MapMarkerController : Node
{
	[Export] public NodePath MapUiPath { get; set; } = new("../MapViewport/MapUI");
	[Export] public NodePath MapSurfacePath { get; set; } = new("../VisualRoot/ViewportSurface");
	[Export] public NodePath MarkerContainerPath { get; set; } = new("../VisualRoot/MapMarkers");
	[Export] public PackedScene MapMissionMarkerScene { get; set; } = null!;
	[Export] public float SurfaceOffset { get; set; } = 0.003f;

	private readonly Dictionary<string, MapMissionMarker> _pins = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, string> _pinBuildingIds = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, int> _buildingPinCounts = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, double> _markerDurations = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, double> _executionDurations = new(StringComparer.OrdinalIgnoreCase);
	private readonly Dictionary<string, double> _radioDurations = new(StringComparer.OrdinalIgnoreCase);
	private IDisposable _markerSpawnedSubscription = null!;
	private IDisposable _markerExpiredSubscription = null!;
	private IDisposable _squadDispatchedSubscription = null!;
	private IDisposable _squadArrivedSubscription = null!;
	private IDisposable _missionExecutionStartedSubscription = null!;
	private IDisposable _radioTriggeredSubscription = null!;
	private IDisposable _missionResolvedSubscription = null!;
	private IDisposable _squadReturningSubscription = null!;
	private IDisposable _squadReturnedSubscription = null!;
	private GameRuntime _runtime = null!;
	private MapBuildingEditor _mapUi = null!;
	private MeshInstance3D _mapSurface = null!;
	private Node3D _markerContainer = null!;

	public override void _Ready()
	{
		_mapUi = GetNode<MapBuildingEditor>(MapUiPath);
		_mapSurface = GetNode<MeshInstance3D>(MapSurfacePath);
		_markerContainer = GetNode<Node3D>(MarkerContainerPath);

		_runtime = GameRuntime.Get(this);
		if (_runtime == null || !_runtime.IsReady)
		{
			GD.PushWarning("MapMarkerController: GameRuntime is not ready; map pins are disabled.");
			return;
		}

		_markerSpawnedSubscription = _runtime.Session.Events.Subscribe<MapMarkerSpawned>(OnMarkerSpawned);
		_markerExpiredSubscription = _runtime.Session.Events.Subscribe<MapMarkerExpired>(marker => RemovePin(marker.IncidentId));
		_squadDispatchedSubscription = _runtime.Session.Events.Subscribe<SquadDispatched>(OnSquadDispatched);
		_squadArrivedSubscription = _runtime.Session.Events.Subscribe<SquadArrived>(arrival => _mapUi.FinishDispatchRoute(arrival.IncidentId));
		_missionExecutionStartedSubscription = _runtime.Session.Events.Subscribe<MissionExecutionStarted>(OnMissionExecutionStarted);
		_radioTriggeredSubscription = _runtime.Session.Events.Subscribe<RadioTriggered>(OnRadioTriggered);
		_missionResolvedSubscription = _runtime.Session.Events.Subscribe<MissionResolved>(OnMissionResolved);
		_squadReturningSubscription = _runtime.Session.Events.Subscribe<SquadReturning>(OnSquadReturning);
		_squadReturnedSubscription = _runtime.Session.Events.Subscribe<SquadReturned>(returned => _mapUi.FinishDispatchRoute(returned.IncidentId));

		foreach (IncidentView incident in _runtime.Session.GetActiveIncidents())
		{
			SyncPinState(incident);
		}
	}

	public override void _Process(double delta)
	{
		if (_runtime == null || !_runtime.IsReady)
		{
			return;
		}

		foreach (IncidentView incident in _runtime.Session.GetActiveIncidents())
		{
			SyncPinState(incident);
			if (incident.Phase is IncidentPhase.Travelling or IncidentPhase.Returning)
			{
				_mapUi.UpdateDispatchRoute(incident.Id, incident.RemainingSeconds);
			}
		}
	}

	private void SyncPinState(IncidentView incident)
	{
		if (incident.Phase is not (IncidentPhase.MarkerActive or IncidentPhase.Travelling or IncidentPhase.RadioPending or IncidentPhase.OnSite))
		{
			return;
		}

		if (!_pins.ContainsKey(incident.Id))
		{
			CreatePin(incident.Id, incident.BuildingId);
		}

		if (!_pins.TryGetValue(incident.Id, out MapMissionMarker marker))
		{
			return;
		}

		switch (incident.Phase)
		{
			case IncidentPhase.MarkerActive:
				if (!_markerDurations.TryGetValue(incident.Id, out double markerDuration))
				{
					markerDuration = incident.RemainingSeconds;
					_markerDurations[incident.Id] = markerDuration;
				}

				marker.ShowDispatchCountdown(incident.RemainingSeconds, markerDuration);
				marker.SetDispatchInteractive(true);
				break;
			case IncidentPhase.Travelling:
				marker.SetDispatchInteractive(false);
				marker.ShowTravelling();
				break;
			case IncidentPhase.RadioPending:
				marker.SetDispatchInteractive(false);
				if (!_radioDurations.TryGetValue(incident.Id, out double radioDuration))
				{
					radioDuration = incident.RemainingSeconds;
					_radioDurations[incident.Id] = radioDuration;
				}

				marker.ShowRadioCountdown(incident.RemainingSeconds, radioDuration);
				break;
			case IncidentPhase.OnSite:
				marker.SetDispatchInteractive(false);
				if (!_executionDurations.TryGetValue(incident.Id, out double executionDuration))
				{
					executionDuration = incident.RemainingSeconds;
					_executionDurations[incident.Id] = executionDuration;
				}

				marker.ShowMissionExecution(incident.RemainingSeconds, executionDuration);
				break;
		}
	}

	public override void _ExitTree()
	{
		_markerSpawnedSubscription?.Dispose();
		_markerExpiredSubscription?.Dispose();
		_squadDispatchedSubscription?.Dispose();
		_squadArrivedSubscription?.Dispose();
		_missionExecutionStartedSubscription?.Dispose();
		_radioTriggeredSubscription?.Dispose();
		_missionResolvedSubscription?.Dispose();
		_squadReturningSubscription?.Dispose();
		_squadReturnedSubscription?.Dispose();
	}

	private void OnMarkerSpawned(MapMarkerSpawned marker)
	{
		CreatePin(marker.IncidentId, marker.BuildingId);
		_markerDurations[marker.IncidentId] = marker.LifetimeSeconds;
		if (_pins.TryGetValue(marker.IncidentId, out MapMissionMarker pin))
		{
			pin.ShowDispatchCountdown(marker.LifetimeSeconds, marker.LifetimeSeconds);
			pin.SetDispatchInteractive(true);
		}
	}

	public CommandResult TryDispatchSquad(
		string incidentId,
		IReadOnlyList<string> employeeIds,
		IReadOnlyList<string> equipmentIds)
	{
		if (!_pinBuildingIds.TryGetValue(incidentId, out string buildingId)
			|| !_mapUi.TryGetTravelSeconds(buildingId, out double travelSeconds))
		{
			return CommandResult.Fail("Для этого задания не удалось построить маршрут на карте.");
		}

		GameRuntime runtime = GameRuntime.Get(this);
		return runtime == null || !runtime.IsReady
			? CommandResult.Fail("Симуляция ещё не готова.")
			: runtime.Session.DispatchSquad(incidentId, employeeIds, equipmentIds, travelSeconds);
	}

	private void OnSquadDispatched(SquadDispatched dispatch)
	{
		if (!_pinBuildingIds.TryGetValue(dispatch.IncidentId, out string buildingId))
		{
			GD.PushWarning($"MapMarkerController: no map marker for '{dispatch.IncidentId}'.");
			return;
		}

		if (_pins.TryGetValue(dispatch.IncidentId, out MapMissionMarker pin))
		{
			pin.SetDispatchInteractive(false);
			pin.ShowTravelling();
		}
		if (!_mapUi.StartDispatchRoute(dispatch.IncidentId, buildingId, dispatch.TravelSeconds))
		{
			GD.PushWarning($"MapMarkerController: could not start route for '{dispatch.IncidentId}'.");
		}
	}

	private void OnMissionExecutionStarted(MissionExecutionStarted execution)
	{
		_radioDurations.Remove(execution.IncidentId);
		_executionDurations[execution.IncidentId] = execution.DurationSeconds;
		if (_pins.TryGetValue(execution.IncidentId, out MapMissionMarker pin))
		{
			pin.ShowMissionExecution(execution.DurationSeconds, execution.DurationSeconds);
		}
	}

	private void OnRadioTriggered(RadioTriggered radio)
	{
		_radioDurations[radio.IncidentId] = radio.ResponseSeconds;
		if (_pins.TryGetValue(radio.IncidentId, out MapMissionMarker pin))
		{
			pin.ShowRadioCountdown(radio.ResponseSeconds, radio.ResponseSeconds);
		}
	}

	private void OnMissionResolved(MissionResolved resolved)
	{
		_executionDurations.Remove(resolved.Outcome.IncidentId);
		_radioDurations.Remove(resolved.Outcome.IncidentId);
		if (_pins.TryGetValue(resolved.Outcome.IncidentId, out MapMissionMarker pin))
		{
			pin.HideIndicator();
		}

		if (resolved.Outcome.SquadWiped)
		{
			RemovePin(resolved.Outcome.IncidentId);
		}
	}

	private void OnSquadReturning(SquadReturning returning)
	{
		RemovePin(returning.IncidentId);
		if (!_mapUi.StartReturnRoute(returning.IncidentId, returning.BuildingId, returning.ReturnSeconds))
		{
			GD.PushWarning($"MapMarkerController: could not start return route for '{returning.IncidentId}'.");
		}
	}

	private void CreatePin(string incidentId, string buildingId)
	{
		RemovePin(incidentId);
		if (MapMissionMarkerScene == null || !_mapUi.TryGetBuildingCenter(buildingId, out Vector2 mapPosition))
		{
			GD.PushWarning($"MapMarkerController: no map position for building '{buildingId}'.");
			return;
		}

		if (_mapSurface.Mesh is not QuadMesh mapQuad)
		{
			GD.PushError("MapMarkerController: ViewportSurface must use QuadMesh.");
			return;
		}

		Vector2 mapSize = _mapUi.Size;
		if (mapSize.X <= 0.0f || mapSize.Y <= 0.0f)
		{
			GD.PushError("MapMarkerController: MapUI has no size.");
			return;
		}

		Vector2 normalized = new(mapPosition.X / mapSize.X, mapPosition.Y / mapSize.Y);
		Vector2 localMapPosition = new(
			(normalized.X - 0.5f) * mapQuad.Size.X,
			(0.5f - normalized.Y) * mapQuad.Size.Y);

		MapMissionMarker pin = MapMissionMarkerScene.Instantiate<MapMissionMarker>();
		_markerContainer.AddChild(pin);
		pin.Position = _mapSurface.Position + new Vector3(localMapPosition.X, localMapPosition.Y, SurfaceOffset);
		pin.Initialize(incidentId);
		_pins[incidentId] = pin;
		_pinBuildingIds[incidentId] = buildingId;
		_buildingPinCounts.TryGetValue(buildingId, out int pinCount);
		_buildingPinCounts[buildingId] = pinCount + 1;

		if (!_mapUi.MarkMissionBuilding(buildingId))
		{
			GD.PushWarning($"MapMarkerController: could not highlight building '{buildingId}'.");
		}
	}

	private void RemovePin(string incidentId)
	{
		_markerDurations.Remove(incidentId);
		_executionDurations.Remove(incidentId);
		_radioDurations.Remove(incidentId);
		if (!_pins.Remove(incidentId, out MapMissionMarker pin))
		{
			return;
		}

		if (IsInstanceValid(pin))
		{
			pin.QueueFree();
		}

		if (!_pinBuildingIds.Remove(incidentId, out string buildingId))
		{
			return;
		}

		int remainingPins = _buildingPinCounts[buildingId] - 1;
		if (remainingPins > 0)
		{
			_buildingPinCounts[buildingId] = remainingPins;
			return;
		}

		_buildingPinCounts.Remove(buildingId);
		_mapUi.ClearMissionBuilding(buildingId);
	}
}
