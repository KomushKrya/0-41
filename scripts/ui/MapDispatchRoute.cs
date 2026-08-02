using System.Collections.Generic;
using Godot;

/// <summary>Самостоятельное отображение маршрута одной группы на MapUI.</summary>
public partial class MapDispatchRoute : Control
{
	[Export] public NodePath RouteRendererPath { get; set; } = new("RouteDashRenderer");
	[Export] public NodePath MovingMarkerPath { get; set; } = new("MovingMarker");

	private readonly List<Vector2> _points = new();
	private readonly List<float> _segmentLengths = new();
	private DashedRouteRenderer _routeRenderer = null!;
	private Polygon2D _movingMarker = null!;
	private float _length;
	private double _travelSeconds;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		_routeRenderer = GetNode<DashedRouteRenderer>(RouteRendererPath);
		_movingMarker = GetNode<Polygon2D>(MovingMarkerPath);
		_movingMarker.Visible = false;
	}

	public void Initialize(IReadOnlyList<Vector2> points, double travelSeconds)
	{
		_points.Clear();
		_segmentLengths.Clear();
		_length = 0.0f;
		_travelSeconds = travelSeconds;

		foreach (Vector2 point in points)
		{
			_points.Add(point);
		}

		for (int index = 0; index < _points.Count - 1; index++)
		{
			float segmentLength = _points[index].DistanceTo(_points[index + 1]);
			_segmentLengths.Add(segmentLength);
			_length += segmentLength;
		}

		_routeRenderer.SetRoute(_points);
		_routeRenderer.SetHeadDistance(0.0f);
		_movingMarker.Visible = _length > 0.0f;
		if (_points.Count > 0)
		{
			_movingMarker.Position = _points[0];
		}
	}

	public void UpdateFromCore(double remainingSeconds)
	{
		if (_length <= 0.0f || _travelSeconds <= 0.0)
		{
			return;
		}

		float progress = Mathf.Clamp((float)(1.0 - remainingSeconds / _travelSeconds), 0.0f, 1.0f);
		float distance = _length * progress;
		_movingMarker.Position = GetPointAtDistance(distance);
		_routeRenderer.SetHeadDistance(distance);
	}

	public void Complete()
	{
		if (_length > 0.0f)
		{
			_movingMarker.Position = GetPointAtDistance(_length);
			_routeRenderer.SetHeadDistance(_length);
		}

		_movingMarker.Visible = false;
		QueueFree();
	}

	private Vector2 GetPointAtDistance(float distance)
	{
		float remaining = Mathf.Clamp(distance, 0.0f, _length);
		for (int index = 0; index < _segmentLengths.Count; index++)
		{
			float segmentLength = _segmentLengths[index];
			if (segmentLength > 0.0f && remaining <= segmentLength)
			{
				return _points[index].Lerp(_points[index + 1], remaining / segmentLength);
			}

			remaining -= segmentLength;
		}

		return _points.Count > 0 ? _points[^1] : Vector2.Zero;
	}
}
