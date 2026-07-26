using Godot;
using System.Collections.Generic;

public partial class MapBuildingEditor : Control
{
	[Export] public NodePath BuildingLayerPath { get; set; } = new("MapLayers/BuildingLayer");
	[Export] public NodePath RoadLayerPath { get; set; } = new("MapLayers/RoadLayer");
	[Export] public NodePath SelectionOutlinePath { get; set; } = new("OverlayLayer/SelectionOutline");
	[Export] public NodePath SelectedLabelPath { get; set; } = new("OverlayLayer/EditorPanel/MarginContainer/VBoxContainer/SelectedLabel");
	[Export] public NodePath RoleLabelPath { get; set; } = new("OverlayLayer/EditorPanel/MarginContainer/VBoxContainer/RoleLabel");
	[Export] public NodePath RouteStatusLabelPath { get; set; } = new("OverlayLayer/EditorPanel/MarginContainer/VBoxContainer/RouteStatusLabel");
	[Export] public NodePath ClearButtonPath { get; set; } = new("OverlayLayer/EditorPanel/MarginContainer/VBoxContainer/RoleButtonRow/ClearButton");
	[Export] public NodePath HeadquartersButtonPath { get; set; } = new("OverlayLayer/EditorPanel/MarginContainer/VBoxContainer/RoleButtonRow/HeadquartersButton");
	[Export] public NodePath ObjectButtonPath { get; set; } = new("OverlayLayer/EditorPanel/MarginContainer/VBoxContainer/RoleButtonRow/ObjectButton");
	[Export] public NodePath BuildRouteButtonPath { get; set; } = new("OverlayLayer/EditorPanel/MarginContainer/VBoxContainer/RouteButtonRow/BuildRouteButton");
	[Export] public NodePath RouteRendererPath { get; set; } = new("MapLayers/RouteLayer/RouteDashRenderer");
	[Export] public NodePath MovingMarkerPath { get; set; } = new("MapLayers/RouteLayer/MovingMarker");
	[Export] public float MarkerSpeed { get; set; } = 120.0f;

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
	private Control _buildingLayer = null!;
	private Line2D _selectionOutline = null!;
	private Label _selectedLabel = null!;
	private Label _roleLabel = null!;
	private Label _routeStatusLabel = null!;
	private DashedRouteRenderer _routeRenderer = null!;
	private Polygon2D _movingMarker = null!;
	private Control _roadLayer = null!;
	private Polygon2D _selectedBuilding = null!;
	private float _routeLength;
	private float _markerTravelDistance;
	private bool _isMarkerMoving;
	private bool _isRouteFading;
	private bool _isLayoutDebugEnabled;

	public override void _Ready()
	{
		_buildingLayer = GetNode<Control>(BuildingLayerPath);
		_roadLayer = GetNode<Control>(RoadLayerPath);
		_selectionOutline = GetNode<Line2D>(SelectionOutlinePath);
		_selectedLabel = GetNode<Label>(SelectedLabelPath);
		_roleLabel = GetNode<Label>(RoleLabelPath);
		_routeStatusLabel = GetNode<Label>(RouteStatusLabelPath);
		_routeRenderer = GetNode<DashedRouteRenderer>(RouteRendererPath);
		_movingMarker = GetNode<Polygon2D>(MovingMarkerPath);

		BuildRoadGraph();
		RegisterBuildings();

		GetNode<Button>(ClearButtonPath).Pressed += () => AssignSelectedRole(MapBuildingRole.Empty);
		GetNode<Button>(HeadquartersButtonPath).Pressed += () => AssignSelectedRole(MapBuildingRole.Headquarters);
		GetNode<Button>(ObjectButtonPath).Pressed += () => AssignSelectedRole(MapBuildingRole.Object);
		GetNode<Button>(BuildRouteButtonPath).Pressed += BuildRouteFromHeadquartersToObject;

		_routeRenderer.ClearRoute();
		_movingMarker.Visible = false;
		SetLayoutDebugEnabled(false);
		UpdateSelectionUi();
		UpdateRouteStatus("МАРШРУТ: назначь штаб и объект");
	}

	public void SetLayoutDebugEnabled(bool isEnabled)
	{
		_isLayoutDebugEnabled = isEnabled;
		_buildingLayer.Visible = isEnabled;
		_roadLayer.Visible = isEnabled;
		_selectionOutline.Visible = isEnabled && _selectedBuilding != null;
	}

	public override void _Process(double delta)
	{
		if (_isRouteFading)
		{
			_markerTravelDistance += MarkerSpeed * (float)delta;
			_routeRenderer.SetHeadDistance(_markerTravelDistance);

			if (_markerTravelDistance >= _routeLength + _routeRenderer.TailLength)
			{
				ClearRoute();
			}

			return;
		}

		if (!_isMarkerMoving || _routePoints.Count < 2)
		{
			return;
		}

		_markerTravelDistance += MarkerSpeed * (float)delta;
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
			_roles[building] = MapBuildingRole.Empty;
			_baseColors[building] = building.Color;
		}
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

		Vector2 startBuildingCenter = GetBuildingCenter(headquarters);
		Vector2 targetBuildingCenter = GetBuildingCenter(targetObject);
		RoadAttachment startAttachment = FindRoadAttachment(startBuildingCenter, targetBuildingCenter);
		RoadAttachment targetAttachment = FindRoadAttachment(targetBuildingCenter, startBuildingCenter);
		List<string> nodePath = FindRoadPath(startAttachment, targetAttachment);

		if (nodePath.Count == 0)
		{
			ClearRoute();
			UpdateRouteStatus("МАРШРУТ: путь не найден");
			return;
		}

		_routePoints.Clear();

		foreach (string nodeId in nodePath)
		{
			_routePoints.Add(GetRouteNodePosition(nodeId, startAttachment, targetAttachment));
		}

		ShowRoute();
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

	private Vector2 GetBuildingCenter(Polygon2D building)
	{
		Vector2 sum = Vector2.Zero;
		Vector2[] polygon = building.Polygon;

		foreach (Vector2 point in polygon)
		{
			sum += building.GetGlobalTransform() * point;
		}

		Vector2 globalCenter = sum / polygon.Length;
		return GetRouteLayer().GetGlobalTransform().AffineInverse() * globalCenter;
	}

	private Control GetRouteLayer()
	{
		return _routeRenderer.GetParent<Control>();
	}

	private RoadAttachment FindRoadAttachment(Vector2 buildingCenter, Vector2 otherBuildingCenter)
	{
		RoadAttachment bestRayAttachment = default;
		float bestRayDistance = float.MaxValue;
		float bestFallbackDistance = float.MaxValue;
		RoadAttachment bestFallbackAttachment = default;
		Vector2 directionToTarget = otherBuildingCenter - buildingCenter;

		if (directionToTarget.LengthSquared() <= 0.001f)
		{
			directionToTarget = Vector2.Right;
		}

		directionToTarget = directionToTarget.Normalized();

		foreach (RoadSegment segment in _roadSegments)
		{
			Vector2 projectedPoint = ProjectPointOnSegment(buildingCenter, segment.FromPosition, segment.ToPosition);
			float distanceToRoad = buildingCenter.DistanceTo(projectedPoint);

			if (distanceToRoad < bestFallbackDistance)
			{
				float fallbackDistanceFromFrom = segment.FromPosition.DistanceTo(projectedPoint);
				float fallbackDistanceFromTo = segment.ToPosition.DistanceTo(projectedPoint);
				bestFallbackAttachment = new RoadAttachment(projectedPoint, segment, fallbackDistanceFromFrom, fallbackDistanceFromTo);
				bestFallbackDistance = distanceToRoad;
			}

			if (!TryGetRaySegmentIntersection(
				buildingCenter,
				directionToTarget,
				segment.FromPosition,
				segment.ToPosition,
				out Vector2 rayIntersection,
				out float rayDistance))
			{
				continue;
			}

			if (rayDistance >= bestRayDistance)
			{
				continue;
			}

			float distanceFromFrom = segment.FromPosition.DistanceTo(rayIntersection);
			float distanceFromTo = segment.ToPosition.DistanceTo(rayIntersection);
			bestRayAttachment = new RoadAttachment(rayIntersection, segment, distanceFromFrom, distanceFromTo);
			bestRayDistance = rayDistance;
		}

		return bestRayDistance < float.MaxValue ? bestRayAttachment : bestFallbackAttachment;
	}

	private static bool TryGetRaySegmentIntersection(
		Vector2 rayOrigin,
		Vector2 rayDirection,
		Vector2 segmentStart,
		Vector2 segmentEnd,
		out Vector2 intersection,
		out float rayDistance)
	{
		intersection = Vector2.Zero;
		rayDistance = 0.0f;

		Vector2 segment = segmentEnd - segmentStart;
		float denominator = Cross(rayDirection, segment);

		if (Mathf.Abs(denominator) <= 0.001f)
		{
			return false;
		}

		Vector2 originToSegmentStart = segmentStart - rayOrigin;
		float rayT = Cross(originToSegmentStart, segment) / denominator;
		float segmentT = Cross(originToSegmentStart, rayDirection) / denominator;

		if (rayT <= 0.001f || segmentT < 0.0f || segmentT > 1.0f)
		{
			return false;
		}

		intersection = rayOrigin + (rayDirection * rayT);
		rayDistance = rayT;
		return true;
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

	private void ShowRoute()
	{
		_routeRenderer.SetRoute(_routePoints);
		_routeRenderer.SetHeadDistance(0.0f);
		_movingMarker.Visible = true;
		_markerTravelDistance = 0.0f;
		_routeLength = CalculateRouteLength();
		_movingMarker.Position = _routePoints[0];
		_isMarkerMoving = _routeLength > 0.0f;
		_isRouteFading = false;
		if (!_isMarkerMoving)
		{
			ClearRoute();
			UpdateRouteStatus("МАРШРУТ: точка прибыла");
			return;
		}

		UpdateRouteStatus("МАРШРУТ: движение");
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
		_routeLength = 0.0f;
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
			MapBuildingRole.Headquarters => new Color(0.42f, 0.58f, 0.58f, 1.0f),
			MapBuildingRole.Object => new Color(0.66f, 0.38f, 0.31f, 1.0f),
			_ => _baseColors[building]
		};
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

	private readonly record struct RoadSourceSegment(
		string RoadName,
		int SegmentIndex,
		Vector2 From,
		Vector2 To);
}
