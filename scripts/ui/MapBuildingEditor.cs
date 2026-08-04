using Godot;
using System.Collections.Generic;

[Tool]
public partial class MapBuildingEditor : Control
{
	[Export] public NodePath BuildingLayerPath { get; set; } = new("MapLayers/BuildingLayer");
	[Export] public NodePath RoadLayerPath { get; set; } = new("MapLayers/RoadLayer");
	[Export] public NodePath SelectionOutlinePath { get; set; } = new("OverlayLayer/SelectionOutline");
	[Export] public NodePath RouteRendererPath { get; set; } = new("MapLayers/RouteLayer/RouteDashRenderer");
	[Export] public NodePath MovingMarkerPath { get; set; } = new("MapLayers/RouteLayer/MovingMarker");
	[Export] public NodePath HeadquartersMarkerPath { get; set; } = new("MapLayers/MarkerLayer/HeadquartersMarker");
	[Export] public NodePath EffectLayerPath { get; set; } = new("MapLayers/EffectLayer");
	[Export] public PackedScene DispatchRouteScene { get; set; } = null!;
	[Export] public NodePath PaperFramePath { get; set; } = new("OverlayLayer/PaperFrame");
	[Export] public string HeadquartersBuildingName { get; set; } = string.Empty;
	[Export] public string HeadquartersBuildingId { get; set; } = string.Empty;
	[Export] public bool ShowHeadquartersLabel { get; set; } = true;
	[Export] public float MarkerSpeed { get; set; } = 30.0f;
	[Export] public float MissionCountdownRingRadius { get; set; } = 15.0f;
	[Export] public float MissionCountdownRingLineWidth { get; set; } = 10.0f;
	[Export] public Color MissionCountdownRingTrackColor { get; set; } = new(0.0f, 0.0f, 0.0f, 0.0f);
	[Export] public Color MissionCountdownRingProgressColor { get; set; } = new(0.38f, 0.38f, 0.38f, 1.0f);
	[Export] public Color MissionTravellingRingColor { get; set; } = new(0.36f, 0.68f, 0.44f, 1.0f);
	[Export] public UiPreviewData PreviewData { get; set; }
	[Export] public NodePath EditorPreviewPanelPath { get; set; } = new("OverlayLayer/EditorPreviewPanel");

	private const string StartAttachmentNode = "route_start";
	private const string TargetAttachmentNode = "route_target";
	private const float RoadNodeMergeDistance = 1.0f;

	private readonly Dictionary<Polygon2D, MapBuildingRole> _roles = new();
	private readonly Dictionary<Polygon2D, Color> _baseColors = new();
	private readonly Dictionary<string, Vector2> _roadNodes = new();
	private readonly Dictionary<string, List<RoadEdge>> _roadEdges = new();
	private readonly List<RoadSegment> _roadSegments = new();
	private readonly List<Vector2> _routePoints = new();
	private readonly List<float> _routeSegmentLengths = new();
	private readonly Dictionary<string, MapMarkerCountdownRing> _missionCountdowns = new();
	private readonly Dictionary<string, MapDispatchRoute> _dispatchRoutes = new();
	private Control _buildingLayer = null!;
	private Line2D _selectionOutline = null!;
	private readonly Label _selectedLabel = new();
	private readonly Label _roleLabel = new();
	private readonly Label _routeStatusLabel = new();
	private DashedRouteRenderer _routeRenderer = null!;
	private Control _routeLayer = null!;
	private Polygon2D _movingMarker = null!;
	private Label _headquartersMarker = null!;
	private Control _effectLayer = null!;
	private Control _roadLayer = null!;
	private Control _mapLayers = null!;
	private Control _mapGeometry = null!;
	private Line2D _paperFrame = null!;
	private Control _editorPreviewPanel = null!;
	private Polygon2D _selectedBuilding = null!;
	private float _routeLength;
	private float _activeMarkerSpeed;
	private float _markerTravelDistance;
	private string _coreRouteIncidentId = string.Empty;
	private double _coreRouteTravelSeconds;
	private int _lastDisplayedEtaSeconds = -1;
	private bool _isMarkerMoving;
	private bool _isRouteFading;
	private bool _isLayoutDebugEnabled;

	public override void _Ready()
	{
		_buildingLayer = GetNode<Control>(BuildingLayerPath);
		_roadLayer = GetNode<Control>(RoadLayerPath);
		_selectionOutline = GetNode<Line2D>(SelectionOutlinePath);
		_routeRenderer = GetNode<DashedRouteRenderer>(RouteRendererPath);
		_routeLayer = _routeRenderer.GetParent<Control>();
		_movingMarker = GetNode<Polygon2D>(MovingMarkerPath);
		_headquartersMarker = GetNode<Label>(HeadquartersMarkerPath);
		_effectLayer = GetNode<Control>(EffectLayerPath);
		_mapGeometry = _buildingLayer.GetParent<Control>();
		_mapLayers = _mapGeometry.GetParent<Control>();
		_paperFrame = GetNode<Line2D>(PaperFramePath);
		_editorPreviewPanel = GetNode<Control>(EditorPreviewPanelPath);

		FitMapViewportToPaperFrame();
		BuildRoadGraph();
		RegisterBuildings();


		_routeRenderer.ClearRoute();
		_movingMarker.Visible = false;
		ApplyEditorPreview();
		SetLayoutDebugEnabled(true);
		UpdateSelectionUi();
		UpdateRouteStatus("МАРШРУТ: назначь штаб и объект");
	}

	private void ApplyEditorPreview()
	{
		_editorPreviewPanel.Visible = Engine.IsEditorHint() && PreviewData != null;
		if (!Engine.IsEditorHint() || PreviewData == null)
		{
			return;
		}

		_editorPreviewPanel.GetNode<Label>("Title").Text = PreviewData.Title;
		_editorPreviewPanel.GetNode<Label>("Subtitle").Text = PreviewData.Subtitle;
		_editorPreviewPanel.GetNode<Label>("Details").Text = $"{PreviewData.Parameters}\n{PreviewData.Status}";
	}

	public void SetLayoutDebugEnabled(bool isEnabled)
	{
		_isLayoutDebugEnabled = isEnabled;
		_buildingLayer.Visible = isEnabled;
		_roadLayer.Visible = isEnabled;
		_selectionOutline.Visible = isEnabled && _selectedBuilding != null;
	}

	/// <summary>
	/// Возвращает центр здания в координатах корневого MapUI. Явные привязки
	/// MapBuildingPolygon имеют приоритет; остальные служебные building_block ID
	/// стабильно раскладываются по сетке реальной геометрии карты.
	/// </summary>
	public bool TryGetBuildingCenter(string buildingId, out Vector2 center)
	{
		center = default;
		if (string.IsNullOrWhiteSpace(buildingId))
		{
			return false;
		}

		foreach (MapBuildingPolygon building in FindNodesOfType<MapBuildingPolygon>(_buildingLayer))
		{
			if (string.Equals(building.BuildingId, buildingId, System.StringComparison.OrdinalIgnoreCase))
			{
				return TryGetBuildingCenterInMapSpace(building, out center);
			}
		}

		if (!string.IsNullOrWhiteSpace(HeadquartersBuildingId)
			&& string.Equals(HeadquartersBuildingId, buildingId, System.StringComparison.OrdinalIgnoreCase))
		{
			Polygon2D headquarters = _buildingLayer.GetNodeOrNull<Polygon2D>(HeadquartersBuildingName);
			if (headquarters != null)
			{
				return TryGetBuildingCenterInMapSpace(headquarters, out center);
			}
		}

		return TryGetGridBuildingCenter(buildingId, out center);
	}

	/// <summary>
	/// Помечает контур, соответствующий инциденту core, как объект задания.
	/// </summary>
	public bool MarkMissionBuilding(string buildingId)
	{
		if (!TryFindBuildingForId(buildingId, out Polygon2D building)
			|| !_roles.TryGetValue(building, out MapBuildingRole role)
			|| role == MapBuildingRole.Headquarters)
		{
			return false;
		}

		_roles[building] = MapBuildingRole.Object;
		building.Color = GetRoleColor(building, MapBuildingRole.Object);
		if (_selectedBuilding == building)
		{
			UpdateSelectionUi();
		}

		return true;
	}

	/// <summary>
	/// Возвращает объект задания к базовому цвету после исчезновения метки.
	/// </summary>
	public void ClearMissionBuilding(string buildingId)
	{
		if (!TryFindBuildingForId(buildingId, out Polygon2D building)
			|| !_roles.TryGetValue(building, out MapBuildingRole role)
			|| role != MapBuildingRole.Object)
		{
			return;
		}

		_roles[building] = MapBuildingRole.Empty;
		building.Color = GetRoleColor(building, MapBuildingRole.Empty);
		if (_selectedBuilding == building)
		{
			UpdateSelectionUi();
		}
	}

	/// <summary>Показывает обратный отсчёт, пока группу ещё можно отправить.</summary>

	/// <summary>Показывает обратный отсчёт, пока группу ещё можно отправить.</summary>
	public void StartMissionCountdown(string incidentId, string buildingId, double lifetimeSeconds)
	{
		StopMissionCountdown(incidentId);
		if (lifetimeSeconds <= 0.0 || !TryGetBuildingCenter(buildingId, out Vector2 mapPosition))
		{
			return;
		}

		const float diameter = 56.0f;
		Transform2D mapToEffect = _effectLayer.GetGlobalTransform().AffineInverse() * GetGlobalTransform();
		Vector2 effectPosition = mapToEffect * mapPosition;
		var ring = new MapMarkerCountdownRing
		{
			Position = effectPosition - new Vector2(diameter, diameter) * 0.5f,
			Size = new Vector2(diameter, diameter),
			MouseFilter = MouseFilterEnum.Ignore,
			ZIndex = 5,
			Radius = MissionCountdownRingRadius,
			LineWidth = MissionCountdownRingLineWidth,
			TrackColor = MissionCountdownRingTrackColor,
			ProgressColor = MissionCountdownRingProgressColor,
		};

		_effectLayer.AddChild(ring);
		ring.Start(lifetimeSeconds);
		_missionCountdowns[incidentId] = ring;
	}

	public void StopMissionCountdown(string incidentId)
	{
		if (!_missionCountdowns.Remove(incidentId, out MapMarkerCountdownRing ring))
		{
			return;
		}

		if (IsInstanceValid(ring))
		{
			ring.QueueFree();
		}
	}

	/// <summary>Время пути равно длительности движения маркера по дорожному маршруту.</summary>
	/// <summary>Синхронизирует кольцо с оставшимся временем инцидента из core.</summary>
	public void UpdateMissionCountdown(string incidentId, double remainingSeconds)
	{
		if (_missionCountdowns.TryGetValue(incidentId, out MapMarkerCountdownRing ring)
			&& IsInstanceValid(ring))
		{
			ring.SetRemaining(remainingSeconds);
		}
	}

	public void ShowTravellingMissionRing(string incidentId, string buildingId)
	{
		if (!_missionCountdowns.TryGetValue(incidentId, out MapMarkerCountdownRing ring)
			|| !IsInstanceValid(ring))
		{
			StartMissionCountdown(incidentId, buildingId, 1.0);
			if (!_missionCountdowns.TryGetValue(incidentId, out ring) || !IsInstanceValid(ring))
			{
				return;
			}
		}

		ring.ShowFull(MissionTravellingRingColor);
	}

	public bool TryGetTravelSeconds(string buildingId, out double travelSeconds)
	{
		travelSeconds = 0.0;
		if (MarkerSpeed <= 0.0f
			|| !TryFindBuildingForId(buildingId, out Polygon2D targetBuilding)
			|| !TryBuildRoute(targetBuilding, out List<Vector2> routePoints))
		{
			return false;
		}

		travelSeconds = CalculateRouteLength(routePoints) / MarkerSpeed;
		return travelSeconds > 0.0;
	}

	/// <summary>Запускает движение маркера с длительностью, переданной в core при отправке группы.</summary>
	public bool StartDispatchRoute(string incidentId, string buildingId, double travelSeconds)
	{
		if (travelSeconds <= 0.0
			|| !TryFindBuildingForId(buildingId, out Polygon2D targetBuilding)
			|| !TryBuildRoute(targetBuilding, out List<Vector2> routePoints))
		{
			return false;
		}

		RemoveDispatchRoute(incidentId);
		if (DispatchRouteScene == null)
		{
			return false;
		}

		MapDispatchRoute route = DispatchRouteScene.Instantiate<MapDispatchRoute>();
		_routeLayer.AddChild(route);
		route.Initialize(routePoints, travelSeconds);
		_dispatchRoutes[incidentId] = route;
		return true;
	}

	public bool StartReturnRoute(string incidentId, string buildingId, double returnSeconds)
	{
		if (returnSeconds <= 0.0
			|| !TryFindBuildingForId(buildingId, out Polygon2D targetBuilding)
			|| !TryBuildRoute(targetBuilding, out List<Vector2> routePoints))
		{
			return false;
		}

		routePoints.Reverse();
		RemoveDispatchRoute(incidentId);
		if (DispatchRouteScene == null)
		{
			return false;
		}

		MapDispatchRoute route = DispatchRouteScene.Instantiate<MapDispatchRoute>();
		_routeLayer.AddChild(route);
		route.Initialize(routePoints, returnSeconds);
		_dispatchRoutes[incidentId] = route;
		return true;
	}

	public void UpdateDispatchRoute(string incidentId, double remainingSeconds)
	{
		if (!_dispatchRoutes.TryGetValue(incidentId, out MapDispatchRoute route)
			|| !IsInstanceValid(route))
		{
			return;
		}

		route.UpdateFromCore(remainingSeconds);
	}

	public void FinishDispatchRoute(string incidentId)
	{
		if (!_dispatchRoutes.Remove(incidentId, out MapDispatchRoute route)
			|| !IsInstanceValid(route))
		{
			return;
		}

		route.Complete();
	}

	private void RemoveDispatchRoute(string incidentId)
	{
		if (_dispatchRoutes.Remove(incidentId, out MapDispatchRoute route) && IsInstanceValid(route))
		{
			route.QueueFree();
		}
	}

	private bool TryFindBuildingForId(string buildingId, out Polygon2D resolvedBuilding)
	{
		resolvedBuilding = null!;
		if (!TryGetBuildingCenter(buildingId, out Vector2 targetCenter))
		{
			return false;
		}

		float bestDistanceSquared = float.MaxValue;
		foreach (Polygon2D building in FindNodesOfType<Polygon2D>(_buildingLayer))
		{
			if (!TryGetBuildingCenterInMapSpace(building, out Vector2 buildingCenter))
			{
				continue;
			}

			float distanceSquared = buildingCenter.DistanceSquaredTo(targetCenter);
			if (distanceSquared < bestDistanceSquared)
			{
				bestDistanceSquared = distanceSquared;
				resolvedBuilding = building;
			}
		}

		return resolvedBuilding != null;
	}

	private bool TryGetGridBuildingCenter(string buildingId, out Vector2 center)
	{
		center = default;
		if (!TryParseBlockCoordinates(buildingId, out int row, out int column))
		{
			return false;
		}

		var buildingCenters = new List<Vector2>();
		foreach (Polygon2D building in FindNodesOfType<Polygon2D>(_buildingLayer))
		{
			if (TryGetBuildingCenterInMapSpace(building, out Vector2 buildingCenter))
			{
				buildingCenters.Add(buildingCenter);
			}
		}

		if (buildingCenters.Count == 0)
		{
			return false;
		}

		Vector2 minimum = buildingCenters[0];
		Vector2 maximum = buildingCenters[0];
		foreach (Vector2 buildingCenter in buildingCenters)
		{
			minimum = minimum.Min(buildingCenter);
			maximum = maximum.Max(buildingCenter);
		}

		// Сетка контента состоит из шести строк и пяти столбцов. Координаты
		// выбирают ближайший реальный контур здания, а не пустую точку на карте.
		Vector2 desiredCenter = new(
			Mathf.Lerp(minimum.X, maximum.X, (column - 0.5f) / 5.0f),
			Mathf.Lerp(minimum.Y, maximum.Y, (row - 0.5f) / 6.0f));

		float bestDistanceSquared = float.MaxValue;
		foreach (Vector2 buildingCenter in buildingCenters)
		{
			float distanceSquared = buildingCenter.DistanceSquaredTo(desiredCenter);
			if (distanceSquared < bestDistanceSquared)
			{
				bestDistanceSquared = distanceSquared;
				center = buildingCenter;
			}
		}

		return true;
	}

	private bool TryGetBuildingCenterInMapSpace(Polygon2D building, out Vector2 center)
	{
		center = default;
		if (building.Polygon.Length == 0)
		{
			return false;
		}

		Transform2D buildingToMap = GetGlobalTransform().AffineInverse() * building.GetGlobalTransform();
		foreach (Vector2 point in building.Polygon)
		{
			center += buildingToMap * point;
		}

		center /= building.Polygon.Length;
		return true;
	}

	private static bool TryParseBlockCoordinates(string buildingId, out int row, out int column)
	{
		row = 0;
		column = 0;
		const string prefix = "building_block_r";
		if (!buildingId.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		int columnMarker = buildingId.IndexOf("_c", prefix.Length, System.StringComparison.OrdinalIgnoreCase);
		if (columnMarker < 0
			|| !int.TryParse(buildingId.Substring(prefix.Length, columnMarker - prefix.Length), out row)
			|| !int.TryParse(buildingId.Substring(columnMarker + 2), out column))
		{
			return false;
		}

		return row is >= 1 and <= 6 && column is >= 1 and <= 5;
	}

	private void FitMapViewportToPaperFrame()
	{
		if (!TryGetBoundsInTargetSpace(_paperFrame, _paperFrame.Points, this, out Rect2 frameBounds)
			|| !TryGetMapGeometryBounds(out Rect2 geometryBounds))
		{
			return;
		}

		_mapLayers.AnchorLeft = 0.0f;
		_mapLayers.AnchorTop = 0.0f;
		_mapLayers.AnchorRight = 0.0f;
		_mapLayers.AnchorBottom = 0.0f;
		_mapLayers.Position = frameBounds.Position;
		_mapLayers.Size = frameBounds.Size;

		float scale = Mathf.Min(
			frameBounds.Size.X / geometryBounds.Size.X,
			frameBounds.Size.Y / geometryBounds.Size.Y);
		Vector2 scaledGeometrySize = geometryBounds.Size * scale;
		_mapGeometry.Scale = Vector2.One * scale;
		_mapGeometry.Position = ((frameBounds.Size - scaledGeometrySize) * 0.5f)
			- (geometryBounds.Position * scale);
		GD.Print($"Map layout: frame={frameBounds}, geometry={geometryBounds}, scale={scale}, position={_mapGeometry.Position}.");
	}

	private bool TryGetMapGeometryBounds(out Rect2 bounds)
	{
		Vector2 minimum = new(float.MaxValue, float.MaxValue);
		Vector2 maximum = new(float.MinValue, float.MinValue);
		bool hasPoints = false;

		foreach (Polygon2D building in FindNodesOfType<Polygon2D>(_buildingLayer))
		{
			ExpandBoundsInTargetSpace(building, building.Polygon, _mapGeometry, ref minimum, ref maximum, ref hasPoints);
		}

		foreach (Line2D road in FindNodesOfType<Line2D>(_roadLayer))
		{
			ExpandBoundsInTargetSpace(road, road.Points, _mapGeometry, ref minimum, ref maximum, ref hasPoints);
		}

		bounds = hasPoints ? new Rect2(minimum, maximum - minimum) : default;
		return hasPoints && bounds.Size.X > 0.0f && bounds.Size.Y > 0.0f;
	}

	private static bool TryGetBoundsInTargetSpace(
		CanvasItem source,
		IEnumerable<Vector2> points,
		CanvasItem target,
		out Rect2 bounds)
	{
		Vector2 minimum = new(float.MaxValue, float.MaxValue);
		Vector2 maximum = new(float.MinValue, float.MinValue);
		bool hasPoints = false;
		ExpandBoundsInTargetSpace(source, points, target, ref minimum, ref maximum, ref hasPoints);
		bounds = hasPoints ? new Rect2(minimum, maximum - minimum) : default;
		return hasPoints && bounds.Size.X > 0.0f && bounds.Size.Y > 0.0f;
	}

	private static void ExpandBoundsInTargetSpace(
		CanvasItem source,
		IEnumerable<Vector2> points,
		CanvasItem target,
		ref Vector2 minimum,
		ref Vector2 maximum,
		ref bool hasPoints)
	{
		Transform2D sourceToTarget = target.GetGlobalTransform().AffineInverse() * source.GetGlobalTransform();
		foreach (Vector2 point in points)
		{
			Vector2 targetPoint = sourceToTarget * point;
			minimum = new Vector2(Mathf.Min(minimum.X, targetPoint.X), Mathf.Min(minimum.Y, targetPoint.Y));
			maximum = new Vector2(Mathf.Max(maximum.X, targetPoint.X), Mathf.Max(maximum.Y, targetPoint.Y));
			hasPoints = true;
		}
	}

	public override void _Process(double delta)
	{
		if (_isRouteFading)
		{
			_markerTravelDistance += _activeMarkerSpeed * (float)delta;
			_routeRenderer.SetHeadDistance(_markerTravelDistance);

			if (_markerTravelDistance >= _routeLength + _routeRenderer.TailLength)
			{
				ClearRoute();
			}

			return;
		}

		if (!string.IsNullOrEmpty(_coreRouteIncidentId))
		{
			return;
		}

		if (!_isMarkerMoving || _routePoints.Count < 2)
		{
			return;
		}

		_markerTravelDistance += _activeMarkerSpeed * (float)delta;
		if (_markerTravelDistance >= _routeLength)
		{
			_markerTravelDistance = _routeLength;
			_movingMarker.Position = GetRoutePointAtDistance(_markerTravelDistance);
			_routeRenderer.SetHeadDistance(_markerTravelDistance);
			CompleteRoute();
			return;
		}

		_movingMarker.Position = GetRoutePointAtDistance(_markerTravelDistance);
		_routeRenderer.SetHeadDistance(_markerTravelDistance);
		UpdateRouteEta();
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (!_isLayoutDebugEnabled)
		{
			return;
		}

		if (@event is not InputEventMouseButton button || !button.Pressed || button.ButtonIndex != MouseButton.Left)
		{
			return;
		}

		if (TrySelectBuilding(button.Position))
		{
			AcceptEvent();
		}
	}

	private void RegisterBuildings()
	{
		foreach (Polygon2D building in FindNodesOfType<Polygon2D>(_buildingLayer))
		{
			_baseColors[building] = building.Color;
			MapBuildingRole role = IsConfiguredHeadquarters(building)
				|| building is MapBuildingPolygon { IsHeadquarters: true }
				? MapBuildingRole.Headquarters
				: MapBuildingRole.Empty;
			_roles[building] = role;
			building.Color = GetRoleColor(building, role);
		}

		AssignDefaultHeadquarters();
		UpdateHeadquartersMarker();
	}

	private bool IsConfiguredHeadquarters(Polygon2D building)
	{
		return !string.IsNullOrWhiteSpace(HeadquartersBuildingName)
			&& building.Name.ToString() == HeadquartersBuildingName;
	}

	private void AssignDefaultHeadquarters()
	{
		if (FindBuildingByRole(MapBuildingRole.Headquarters) != null || _roles.Count == 0)
		{
			return;
		}

		Vector2 minimum = new(float.MaxValue, float.MaxValue);
		Vector2 maximum = new(float.MinValue, float.MinValue);
		var centers = new Dictionary<Polygon2D, Vector2>();
		Transform2D layerTransformInverse = _buildingLayer.GetGlobalTransform().AffineInverse();

		foreach (Polygon2D building in _roles.Keys)
		{
			if (building.Polygon.Length == 0)
			{
				continue;
			}

			Vector2 center = Vector2.Zero;
			Transform2D buildingTransform = building.GetGlobalTransform();
			foreach (Vector2 point in building.Polygon)
			{
				Vector2 layerPoint = layerTransformInverse * (buildingTransform * point);
				minimum = new Vector2(Mathf.Min(minimum.X, layerPoint.X), Mathf.Min(minimum.Y, layerPoint.Y));
				maximum = new Vector2(Mathf.Max(maximum.X, layerPoint.X), Mathf.Max(maximum.Y, layerPoint.Y));
				center += layerPoint;
			}

			centers[building] = center / building.Polygon.Length;
		}

		if (centers.Count == 0)
		{
			return;
		}

		Vector2 mapCenter = (minimum + maximum) * 0.5f;
		Polygon2D headquarters = null;
		float bestDistanceSquared = float.MaxValue;
		foreach ((Polygon2D building, Vector2 center) in centers)
		{
			float distanceSquared = center.DistanceSquaredTo(mapCenter);
			if (distanceSquared >= bestDistanceSquared)
			{
				continue;
			}

			headquarters = building;
			bestDistanceSquared = distanceSquared;
		}

		if (headquarters != null)
		{
			_roles[headquarters] = MapBuildingRole.Headquarters;
			headquarters.Color = GetRoleColor(headquarters, MapBuildingRole.Headquarters);
		}
	}

	private Polygon2D AssignRandomObject(Polygon2D excludedBuilding = null)
	{
		var candidates = new List<Polygon2D>();
		foreach ((Polygon2D building, MapBuildingRole role) in _roles)
		{
			if (role != MapBuildingRole.Empty
				|| building == excludedBuilding
				|| building is MapBuildingPolygon { IsDispatchTarget: false })
			{
				continue;
			}

			candidates.Add(building);
		}

		if (candidates.Count == 0)
		{
			return null;
		}

		Polygon2D selectedObject = candidates[GD.RandRange(0, candidates.Count - 1)];
		_roles[selectedObject] = MapBuildingRole.Object;
		selectedObject.Color = GetRoleColor(selectedObject, MapBuildingRole.Object);
		return selectedObject;
	}

	private void GenerateRandomObject()
	{
		Polygon2D previousObject = FindBuildingByRole(MapBuildingRole.Object);
		ClearRole(MapBuildingRole.Object);

		Polygon2D generatedObject = AssignRandomObject(previousObject);
		if (generatedObject == null)
		{
			generatedObject = AssignRandomObject();
		}

		ClearRoute();
		if (generatedObject == null)
		{
			UpdateRouteStatus("МАРШРУТ: нет доступных зданий");
			return;
		}

		_selectedBuilding = generatedObject;
		UpdateSelectionUi();
		UpdateRouteStatus("МАРШРУТ: назначен новый случайный объект");
	}

	private void BuildRoadGraph()
	{
		_roadNodes.Clear();
		_roadEdges.Clear();
		_roadSegments.Clear();

		var sourceSegments = CollectRoadSourceSegments();
		foreach (RoadSourceSegment sourceSegment in sourceSegments)
		{
			var splitPoints = new List<Vector2> { sourceSegment.From, sourceSegment.To };

			foreach (RoadSourceSegment otherSegment in sourceSegments)
			{
				if (sourceSegment.Equals(otherSegment))
				{
					continue;
				}

				if (TryGetSegmentIntersection(sourceSegment.From, sourceSegment.To, otherSegment.From, otherSegment.To, out Vector2 intersection))
				{
					splitPoints.Add(intersection);
				}
			}

			AddSplitRoadSegments(sourceSegment, splitPoints);
		}
	}

	private List<RoadSourceSegment> CollectRoadSourceSegments()
	{
		var sourceSegments = new List<RoadSourceSegment>();

		foreach (Line2D road in FindNodesOfType<Line2D>(_roadLayer))
		{
			if (road.Points.Length < 2)
			{
				continue;
			}

			for (int i = 0; i < road.Points.Length - 1; i++)
			{
				Vector2 from = ConvertRoadPointToRouteSpace(road, road.Points[i]);
				Vector2 to = ConvertRoadPointToRouteSpace(road, road.Points[i + 1]);
				sourceSegments.Add(new RoadSourceSegment(road.Name, i, from, to));
			}
		}

		return sourceSegments;
	}

	private static List<T> FindNodesOfType<T>(Node parent) where T : Node
	{
		var nodes = new List<T>();
		CollectNodesOfType(parent, nodes);
		return nodes;
	}

	private static void CollectNodesOfType<T>(Node parent, List<T> nodes) where T : Node
	{
		foreach (Node child in parent.GetChildren())
		{
			if (child is T node)
			{
				nodes.Add(node);
			}

			CollectNodesOfType(child, nodes);
		}
	}

	private Vector2 ConvertRoadPointToRouteSpace(Line2D road, Vector2 roadPoint)
	{
		Vector2 globalPoint = road.GetGlobalTransform() * roadPoint;
		return GetRouteLayer().GetGlobalTransform().AffineInverse() * globalPoint;
	}

	private static bool TryGetSegmentIntersection(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out Vector2 intersection)
	{
		intersection = Vector2.Zero;
		Vector2 r = b - a;
		Vector2 s = d - c;
		float denominator = Cross(r, s);

		if (Mathf.Abs(denominator) <= 0.001f)
		{
			return false;
		}

		float t = Cross(c - a, s) / denominator;
		float u = Cross(c - a, r) / denominator;
		if (t <= 0.001f || t >= 0.999f || u <= 0.001f || u >= 0.999f)
		{
			return false;
		}

		intersection = a + (r * t);
		return true;
	}

	private static float Cross(Vector2 a, Vector2 b)
	{
		return (a.X * b.Y) - (a.Y * b.X);
	}

	private void AddSplitRoadSegments(RoadSourceSegment sourceSegment, List<Vector2> splitPoints)
	{
		splitPoints.Sort((a, b) => sourceSegment.From.DistanceSquaredTo(a).CompareTo(sourceSegment.From.DistanceSquaredTo(b)));

		for (int i = 0; i < splitPoints.Count - 1; i++)
		{
			if (splitPoints[i].DistanceSquaredTo(splitPoints[i + 1]) <= 0.001f)
			{
				continue;
			}

			string from = GetOrCreateRoadNode(splitPoints[i]);
			string to = GetOrCreateRoadNode(splitPoints[i + 1]);
			AddRoadEdge(from, to);
		}
	}

	private string GetOrCreateRoadNode(Vector2 position)
	{
		foreach ((string nodeId, Vector2 nodePosition) in _roadNodes)
		{
			if (nodePosition.DistanceTo(position) <= RoadNodeMergeDistance)
			{
				return nodeId;
			}
		}

		string newNodeId = $"road_node_{_roadNodes.Count}";
		_roadNodes[newNodeId] = position;
		_roadEdges[newNodeId] = new List<RoadEdge>();
		return newNodeId;
	}

	private void AddRoadEdge(string from, string to)
	{
		if (from == to)
		{
			return;
		}

		Vector2 fromPosition = _roadNodes[from];
		Vector2 toPosition = _roadNodes[to];
		float cost = fromPosition.DistanceTo(toPosition);
		_roadEdges[from].Add(new RoadEdge(to, cost));
		_roadEdges[to].Add(new RoadEdge(from, cost));
		_roadSegments.Add(new RoadSegment(from, to, fromPosition, toPosition, cost));
	}

	private bool TrySelectBuilding(Vector2 localMousePosition)
	{
		foreach (Polygon2D building in _roles.Keys)
		{
			Vector2 buildingLocalPosition = building.GetGlobalTransform().AffineInverse() * localMousePosition;
			if (!Geometry2D.IsPointInPolygon(buildingLocalPosition, building.Polygon))
			{
				continue;
			}

			_selectedBuilding = building;
			UpdateSelectionUi();
			return true;
		}

		return false;
	}

	private void AssignSelectedRole(MapBuildingRole role)
	{
		if (_selectedBuilding == null)
		{
			return;
		}

		if (role is MapBuildingRole.Headquarters or MapBuildingRole.Object)
		{
			ClearRole(role);
		}

		_roles[_selectedBuilding] = role;
		_selectedBuilding.Color = GetRoleColor(_selectedBuilding, role);
		UpdateHeadquartersMarker();
		ClearRoute();
		UpdateSelectionUi();
		UpdateRouteStatus("МАРШРУТ: готов к построению");
	}

	private void ClearRole(MapBuildingRole role)
	{
		foreach ((Polygon2D building, MapBuildingRole existingRole) in _roles)
		{
			if (existingRole != role)
			{
				continue;
			}

			_roles[building] = MapBuildingRole.Empty;
			building.Color = GetRoleColor(building, MapBuildingRole.Empty);
		}

		UpdateHeadquartersMarker();
	}

	private void BuildRouteFromHeadquartersToObject()
	{
		Polygon2D headquarters = FindBuildingByRole(MapBuildingRole.Headquarters);
		Polygon2D targetObject = FindBuildingByRole(MapBuildingRole.Object);

		if (headquarters == null || targetObject == null)
		{
			ClearRoute();
			UpdateRouteStatus("МАРШРУТ: нужны штаб и объект");
			return;
		}

		BuildingRoadAnchor startAnchor = FindClosestRoadAnchor(headquarters, targetObject);
		BuildingRoadAnchor targetAnchor = FindClosestRoadAnchor(targetObject, headquarters);
		List<string> nodePath = FindRoadPath(startAnchor.Attachment, targetAnchor.Attachment);

		if (nodePath.Count == 0)
		{
			ClearRoute();
			UpdateRouteStatus("МАРШРУТ: путь не найден");
			return;
		}

		_routePoints.Clear();
		_routePoints.Add(startAnchor.BuildingPosition);

		foreach (string nodeId in nodePath)
		{
			_routePoints.Add(GetRouteNodePosition(nodeId, startAnchor.Attachment, targetAnchor.Attachment));
		}

		_routePoints.Add(targetAnchor.BuildingPosition);
		ShowRoute(new List<Vector2>(_routePoints), null);
	}

	private bool TryBuildRoute(Polygon2D targetObject, out List<Vector2> routePoints)
	{
		routePoints = new List<Vector2>();
		Polygon2D headquarters = FindBuildingByRole(MapBuildingRole.Headquarters);
		if (headquarters == null || targetObject == null)
		{
			return false;
		}

		BuildingRoadAnchor startAnchor = FindClosestRoadAnchor(headquarters, targetObject);
		BuildingRoadAnchor targetAnchor = FindClosestRoadAnchor(targetObject, headquarters);
		List<string> nodePath = FindRoadPath(startAnchor.Attachment, targetAnchor.Attachment);
		if (nodePath.Count == 0)
		{
			return false;
		}

		routePoints.Add(startAnchor.BuildingPosition);
		foreach (string nodeId in nodePath)
		{
			routePoints.Add(GetRouteNodePosition(nodeId, startAnchor.Attachment, targetAnchor.Attachment));
		}

		routePoints.Add(targetAnchor.BuildingPosition);
		return true;
	}

	private Polygon2D FindBuildingByRole(MapBuildingRole role)
	{
		foreach ((Polygon2D building, MapBuildingRole existingRole) in _roles)
		{
			if (existingRole == role)
			{
				return building;
			}
		}

		return null;
	}

	private Control GetRouteLayer()
	{
		return _routeRenderer.GetParent<Control>();
	}

	private BuildingRoadAnchor FindClosestRoadAnchor(Polygon2D building, Polygon2D otherBuilding)
	{
		Vector2[] buildingPoints = GetBuildingPointsInRouteSpace(building);
		Vector2[] otherBuildingPoints = GetBuildingPointsInRouteSpace(otherBuilding);
		Vector2 facingPosition = FindPerimeterPointFacingBuilding(buildingPoints, otherBuildingPoints);
		return FindClosestRoadAnchor(facingPosition);
	}

	private BuildingRoadAnchor FindClosestRoadAnchor(Vector2 buildingPosition)
	{
		BuildingRoadAnchor bestAnchor = default;
		float bestDistanceSquared = float.MaxValue;

		foreach (RoadSegment segment in _roadSegments)
		{
			Vector2 roadPosition = ProjectPointOnSegment(
				buildingPosition,
				segment.FromPosition,
				segment.ToPosition);
			ConsiderRoadAnchor(
				buildingPosition,
				roadPosition,
				segment,
				ref bestAnchor,
				ref bestDistanceSquared);
		}

		return bestAnchor;
	}

	private static Vector2 FindPerimeterPointFacingBuilding(Vector2[] buildingPoints, Vector2[] otherBuildingPoints)
	{
		Vector2 bestPoint = buildingPoints[0];
		float bestDistanceSquared = float.MaxValue;

		for (int buildingIndex = 0; buildingIndex < buildingPoints.Length; buildingIndex++)
		{
			Vector2 buildingFrom = buildingPoints[buildingIndex];
			Vector2 buildingTo = buildingPoints[(buildingIndex + 1) % buildingPoints.Length];

			for (int otherIndex = 0; otherIndex < otherBuildingPoints.Length; otherIndex++)
			{
				Vector2 otherFrom = otherBuildingPoints[otherIndex];
				Vector2 otherTo = otherBuildingPoints[(otherIndex + 1) % otherBuildingPoints.Length];

				ConsiderFacingPoint(buildingFrom, ProjectPointOnSegment(buildingFrom, otherFrom, otherTo), ref bestPoint, ref bestDistanceSquared);
				ConsiderFacingPoint(buildingTo, ProjectPointOnSegment(buildingTo, otherFrom, otherTo), ref bestPoint, ref bestDistanceSquared);
				ConsiderFacingPoint(ProjectPointOnSegment(otherFrom, buildingFrom, buildingTo), otherFrom, ref bestPoint, ref bestDistanceSquared);
				ConsiderFacingPoint(ProjectPointOnSegment(otherTo, buildingFrom, buildingTo), otherTo, ref bestPoint, ref bestDistanceSquared);
			}
		}

		return bestPoint;
	}

	private static void ConsiderFacingPoint(
		Vector2 buildingPosition,
		Vector2 otherBuildingPosition,
		ref Vector2 bestPoint,
		ref float bestDistanceSquared)
	{
		float distanceSquared = buildingPosition.DistanceSquaredTo(otherBuildingPosition);
		if (distanceSquared < bestDistanceSquared)
		{
			bestPoint = buildingPosition;
			bestDistanceSquared = distanceSquared;
		}
	}

	private Vector2[] GetBuildingPointsInRouteSpace(Polygon2D building)
	{
		var points = new Vector2[building.Polygon.Length];
		Transform2D buildingTransform = building.GetGlobalTransform();
		Transform2D routeTransformInverse = GetRouteLayer().GetGlobalTransform().AffineInverse();
		for (int index = 0; index < building.Polygon.Length; index++)
		{
			points[index] = routeTransformInverse * (buildingTransform * building.Polygon[index]);
		}

		return points;
	}

	private static void ConsiderRoadAnchor(
		Vector2 buildingPosition,
		Vector2 roadPosition,
		RoadSegment segment,
		ref BuildingRoadAnchor bestAnchor,
		ref float bestDistanceSquared)
	{
		float distanceSquared = buildingPosition.DistanceSquaredTo(roadPosition);
		if (distanceSquared >= bestDistanceSquared)
		{
			return;
		}

		float distanceFromFrom = segment.FromPosition.DistanceTo(roadPosition);
		float distanceFromTo = segment.ToPosition.DistanceTo(roadPosition);
		bestAnchor = new BuildingRoadAnchor(
			buildingPosition,
			new RoadAttachment(roadPosition, segment, distanceFromFrom, distanceFromTo));
		bestDistanceSquared = distanceSquared;
	}

	private static Vector2 ProjectPointOnSegment(Vector2 point, Vector2 segmentStart, Vector2 segmentEnd)
	{
		Vector2 segment = segmentEnd - segmentStart;
		float segmentLengthSquared = segment.LengthSquared();
		if (segmentLengthSquared <= 0.0f)
		{
			return segmentStart;
		}

		float t = Mathf.Clamp((point - segmentStart).Dot(segment) / segmentLengthSquared, 0.0f, 1.0f);
		return segmentStart + (segment * t);
	}

	private List<string> FindRoadPath(RoadAttachment startAttachment, RoadAttachment targetAttachment)
	{
		var graph = CloneRoadGraph();
		graph[StartAttachmentNode] = new List<RoadEdge>();
		graph[TargetAttachmentNode] = new List<RoadEdge>();

		ConnectAttachmentToGraph(graph, StartAttachmentNode, startAttachment);
		ConnectAttachmentToGraph(graph, TargetAttachmentNode, targetAttachment);

		if (startAttachment.Segment.Equals(targetAttachment.Segment))
		{
			float directCost = startAttachment.Position.DistanceTo(targetAttachment.Position);
			graph[StartAttachmentNode].Add(new RoadEdge(TargetAttachmentNode, directCost));
			graph[TargetAttachmentNode].Add(new RoadEdge(StartAttachmentNode, directCost));
		}

		return FindRoadPath(StartAttachmentNode, TargetAttachmentNode, graph);
	}

	private Dictionary<string, List<RoadEdge>> CloneRoadGraph()
	{
		var graph = new Dictionary<string, List<RoadEdge>>();

		foreach ((string nodeId, List<RoadEdge> edges) in _roadEdges)
		{
			graph[nodeId] = new List<RoadEdge>(edges);
		}

		return graph;
	}

	private void ConnectAttachmentToGraph(Dictionary<string, List<RoadEdge>> graph, string attachmentNodeId, RoadAttachment attachment)
	{
		graph[attachmentNodeId].Add(new RoadEdge(attachment.Segment.From, attachment.DistanceFromFrom));
		graph[attachmentNodeId].Add(new RoadEdge(attachment.Segment.To, attachment.DistanceFromTo));
		graph[attachment.Segment.From].Add(new RoadEdge(attachmentNodeId, attachment.DistanceFromFrom));
		graph[attachment.Segment.To].Add(new RoadEdge(attachmentNodeId, attachment.DistanceFromTo));
	}

	private List<string> FindRoadPath(string startNode, string targetNode, Dictionary<string, List<RoadEdge>> graph)
	{
		var distances = new Dictionary<string, float>();
		var previous = new Dictionary<string, string>();
		var queue = new PriorityQueue<string, float>();

		foreach (string nodeId in graph.Keys)
		{
			distances[nodeId] = float.PositiveInfinity;
		}

		distances[startNode] = 0.0f;
		queue.Enqueue(startNode, 0.0f);

		while (queue.Count > 0)
		{
			string current = queue.Dequeue();
			if (current == targetNode)
			{
				break;
			}

			foreach (RoadEdge edge in graph[current])
			{
				float nextDistance = distances[current] + edge.Cost;
				if (nextDistance >= distances[edge.To])
				{
					continue;
				}

				distances[edge.To] = nextDistance;
				previous[edge.To] = current;
				queue.Enqueue(edge.To, nextDistance);
			}
		}

		if (startNode != targetNode && !previous.ContainsKey(targetNode))
		{
			return new List<string>();
		}

		var path = new List<string> { targetNode };
		string pathNode = targetNode;

		while (pathNode != startNode)
		{
			pathNode = previous[pathNode];
			path.Add(pathNode);
		}

		path.Reverse();
		return path;
	}

	private Vector2 GetRouteNodePosition(string nodeId, RoadAttachment startAttachment, RoadAttachment targetAttachment)
	{
		return nodeId switch
		{
			StartAttachmentNode => startAttachment.Position,
			TargetAttachmentNode => targetAttachment.Position,
			_ => _roadNodes[nodeId]
		};
	}

	private void ShowRoute(IReadOnlyList<Vector2> routePoints, double? travelSeconds)
	{
		_coreRouteIncidentId = string.Empty;
		_coreRouteTravelSeconds = 0.0;
		_routePoints.Clear();
		foreach (Vector2 routePoint in routePoints)
		{
			_routePoints.Add(routePoint);
		}

		_routeRenderer.SetRoute(_routePoints);
		_routeRenderer.SetHeadDistance(0.0f);
		_movingMarker.Visible = true;
		_markerTravelDistance = 0.0f;
		_routeLength = CalculateRouteLength();
		_activeMarkerSpeed = travelSeconds is > 0.0
			? _routeLength / (float)travelSeconds.Value
			: MarkerSpeed;
		_movingMarker.Position = _routePoints[0];
		_isMarkerMoving = _routeLength > 0.0f;
		_isRouteFading = false;
		if (!_isMarkerMoving)
		{
			ClearRoute();
			UpdateRouteStatus("МАРШРУТ: точка прибыла");
			return;
		}

		UpdateRouteEta();
	}

	private void UpdateRouteEta()
	{
		if (_activeMarkerSpeed <= 0.0f)
		{
			UpdateRouteStatus("МАРШРУТ: группа ожидает отправки");
			return;
		}

		float remainingDistance = Mathf.Max(_routeLength - _markerTravelDistance, 0.0f);
		int remainingSeconds = Mathf.CeilToInt(remainingDistance / _activeMarkerSpeed);
		if (remainingSeconds == _lastDisplayedEtaSeconds)
		{
			return;
		}

		_lastDisplayedEtaSeconds = remainingSeconds;
		UpdateRouteStatus($"МАРШРУТ: прибытие через {remainingSeconds} с");
	}

	private float CalculateRouteLength()
	{
		_routeSegmentLengths.Clear();
		float totalLength = 0.0f;

		for (int i = 0; i < _routePoints.Count - 1; i++)
		{
			float segmentLength = _routePoints[i].DistanceTo(_routePoints[i + 1]);
			_routeSegmentLengths.Add(segmentLength);
			totalLength += segmentLength;
		}

		return totalLength;
	}

	private static float CalculateRouteLength(IReadOnlyList<Vector2> routePoints)
	{
		float totalLength = 0.0f;
		for (int i = 0; i < routePoints.Count - 1; i++)
		{
			totalLength += routePoints[i].DistanceTo(routePoints[i + 1]);
		}

		return totalLength;
	}

	private Vector2 GetRoutePointAtDistance(float distance)
	{
		float remainingDistance = distance;

		for (int i = 0; i < _routeSegmentLengths.Count; i++)
		{
			float segmentLength = _routeSegmentLengths[i];
			if (segmentLength <= 0.0f)
			{
				continue;
			}

			if (remainingDistance <= segmentLength)
			{
				float t = remainingDistance / segmentLength;
				return _routePoints[i].Lerp(_routePoints[i + 1], t);
			}

			remainingDistance -= segmentLength;
		}

		return _routePoints[^1];
	}

	private void ClearRoute()
	{
		_routePoints.Clear();
		_routeSegmentLengths.Clear();
		_routeRenderer.ClearRoute();
		_movingMarker.Visible = false;
		_isMarkerMoving = false;
		_isRouteFading = false;
		_markerTravelDistance = 0.0f;
		_lastDisplayedEtaSeconds = -1;
		_routeLength = 0.0f;
		_activeMarkerSpeed = 0.0f;
		_coreRouteIncidentId = string.Empty;
		_coreRouteTravelSeconds = 0.0;
	}

	private void CompleteRoute()
	{
		_isMarkerMoving = false;
		_isRouteFading = true;
		_movingMarker.Visible = false;
		UpdateRouteStatus("МАРШРУТ: точка прибыла");
	}

	private Color GetRoleColor(Polygon2D building, MapBuildingRole role)
	{
		return role switch
		{
			MapBuildingRole.Headquarters => new Color(0.22f, 0.48f, 0.52f, 1.0f),
			MapBuildingRole.Object => new Color(0.66f, 0.38f, 0.31f, 1.0f),
			_ => _baseColors[building]
		};
	}

	private void UpdateHeadquartersMarker()
	{
		if (!ShowHeadquartersLabel)
		{
			_headquartersMarker.Visible = false;
			return;
		}

		Polygon2D headquarters = FindBuildingByRole(MapBuildingRole.Headquarters);
		if (headquarters == null || headquarters.Polygon.Length == 0)
		{
			_headquartersMarker.Visible = false;
			return;
		}

		Vector2 polygonCenter = Vector2.Zero;
		foreach (Vector2 point in headquarters.Polygon)
		{
			polygonCenter += point;
		}

		polygonCenter /= headquarters.Polygon.Length;
		Vector2 globalCenter = headquarters.GetGlobalTransform() * polygonCenter;
		Control markerLayer = _headquartersMarker.GetParent<Control>();
		Vector2 markerCenter = markerLayer.GetGlobalTransform().AffineInverse() * globalCenter;

		_headquartersMarker.Position = markerCenter - (_headquartersMarker.Size * 0.5f);
		_headquartersMarker.Visible = true;
	}

	private void UpdateSelectionUi()
	{
		if (_selectedBuilding == null)
		{
			_selectionOutline.Visible = false;
			_selectedLabel.Text = "ЗДАНИЕ: не выбрано";
			_roleLabel.Text = "ТИП: -";
			return;
		}

		MapBuildingRole role = _roles[_selectedBuilding];
		_selectedLabel.Text = $"ЗДАНИЕ: {_selectedBuilding.Name}";
		_roleLabel.Text = $"ТИП: {GetRoleLabel(role)}";
		UpdateSelectionOutline();
	}

	private void UpdateSelectionOutline()
	{
		Vector2[] selectedPolygon = _selectedBuilding.Polygon;
		var outlinePoints = new Vector2[selectedPolygon.Length + 1];
		Transform2D selectedBuildingTransform = _selectedBuilding.GetGlobalTransform();
		Transform2D outlineTransform = _selectionOutline.GetGlobalTransform().AffineInverse();

		for (int i = 0; i < selectedPolygon.Length; i++)
		{
			outlinePoints[i] = outlineTransform * (selectedBuildingTransform * selectedPolygon[i]);
		}

		if (selectedPolygon.Length > 0)
		{
			outlinePoints[^1] = outlineTransform * (selectedBuildingTransform * selectedPolygon[0]);
		}

		_selectionOutline.Points = outlinePoints;
		_selectionOutline.Visible = _isLayoutDebugEnabled;
	}

	private void UpdateRouteStatus(string status)
	{
		_routeStatusLabel.Text = status;
	}

	private static string GetRoleLabel(MapBuildingRole role)
	{
		return role switch
		{
			MapBuildingRole.Headquarters => "ШТАБ",
			MapBuildingRole.Object => "ОБЪЕКТ",
			_ => "ПУСТО"
		};
	}

	private enum MapBuildingRole
	{
		Empty,
		Headquarters,
		Object
	}

	private readonly record struct RoadEdge(string To, float Cost);

	private readonly record struct RoadSegment(
		string From,
		string To,
		Vector2 FromPosition,
		Vector2 ToPosition,
		float Cost);

	private readonly record struct RoadAttachment(
		Vector2 Position,
		RoadSegment Segment,
		float DistanceFromFrom,
		float DistanceFromTo);

	private readonly record struct BuildingRoadAnchor(
		Vector2 BuildingPosition,
		RoadAttachment Attachment);

	private readonly record struct RoadSourceSegment(
		string RoadName,
		int SegmentIndex,
		Vector2 From,
		Vector2 To);
}
