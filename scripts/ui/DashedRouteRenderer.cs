using Godot;
using System.Collections.Generic;

public partial class DashedRouteRenderer : Control
{
	[Export] public float DashLength { get; set; } = 12.0f;
	[Export] public float GapLength { get; set; } = 7.0f;
	[Export] public int VisibleDashCount { get; set; } = 5;
	[Export] public float LineWidth { get; set; } = 4.0f;
	[Export] public float ShadowWidth { get; set; } = 7.0f;
	[Export] public Color RouteColor { get; set; } = new(0.1f, 0.12f, 0.14f, 0.92f);
	[Export] public Color ShadowColor { get; set; } = new(0.12f, 0.08f, 0.04f, 0.18f);

	private readonly List<Vector2> _points = new();
	private readonly List<float> _segmentLengths = new();
	private float _headDistance;
	private float _totalLength;
	private bool _hasRoute;

	public float TailLength => Mathf.Max(DashLength, (DashLength + GapLength) * Mathf.Max(1, VisibleDashCount));

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		Visible = false;
	}

	public void SetRoute(IReadOnlyList<Vector2> points)
	{
		_points.Clear();
		_segmentLengths.Clear();
		_totalLength = 0.0f;
		_headDistance = 0.0f;

		foreach (Vector2 point in points)
		{
			_points.Add(point);
		}

		for (int i = 0; i < _points.Count - 1; i++)
		{
			float segmentLength = _points[i].DistanceTo(_points[i + 1]);
			_segmentLengths.Add(segmentLength);
			_totalLength += segmentLength;
		}

		_hasRoute = _points.Count >= 2 && _totalLength > 0.0f;
		Visible = _hasRoute;
		QueueRedraw();
	}

	public void SetHeadDistance(float distance)
	{
		_headDistance = Mathf.Max(0.0f, distance);
		QueueRedraw();
	}

	public void ClearRoute()
	{
		_points.Clear();
		_segmentLengths.Clear();
		_headDistance = 0.0f;
		_totalLength = 0.0f;
		_hasRoute = false;
		Visible = false;
		QueueRedraw();
	}

	public override void _Draw()
	{
		if (!_hasRoute)
		{
			return;
		}

		float patternLength = Mathf.Max(1.0f, DashLength + GapLength);
		float tailLength = TailLength;
		float tailStart = _headDistance - tailLength;
		float drawStart = Mathf.Max(0.0f, tailStart);
		float drawEnd = Mathf.Min(_headDistance, _totalLength);

		if (drawEnd <= drawStart)
		{
			return;
		}

		float firstDashStart = Mathf.Floor(drawStart / patternLength) * patternLength;
		for (float dashStart = firstDashStart; dashStart < drawEnd; dashStart += patternLength)
		{
			float sectionStart = Mathf.Max(dashStart, drawStart);
			float sectionEnd = Mathf.Min(dashStart + DashLength, drawEnd);
			if (sectionEnd <= sectionStart)
			{
				continue;
			}

			float sectionMid = (sectionStart + sectionEnd) * 0.5f;
			float fade = Mathf.Clamp((sectionMid - tailStart) / tailLength, 0.0f, 1.0f);
			DrawRouteSection(sectionStart, sectionEnd, fade * fade);
		}
	}

	private void DrawRouteSection(float startDistance, float endDistance, float alpha)
	{
		var sectionPoints = new List<Vector2>
		{
			GetRoutePointAtDistance(startDistance)
		};

		float distance = startDistance;
		while (distance < endDistance - 0.01f)
		{
			int segmentIndex = FindSegmentIndex(distance);
			float segmentEndDistance = GetSegmentEndDistance(segmentIndex);
			float nextDistance = Mathf.Min(endDistance, segmentEndDistance);

			if (nextDistance <= distance + 0.01f)
			{
				distance += 0.01f;
				continue;
			}

			sectionPoints.Add(GetRoutePointAtDistance(nextDistance));
			distance = nextDistance;
		}

		if (sectionPoints.Count >= 2)
		{
			DrawRouteLine(sectionPoints.ToArray(), alpha);
		}
	}

	private void DrawRouteLine(Vector2[] points, float alpha)
	{
		Color shadow = ShadowColor;
		Color route = RouteColor;
		shadow.A *= alpha;
		route.A *= alpha;

		DrawPolyline(points, shadow, ShadowWidth, true);
		DrawPolyline(points, route, LineWidth, true);
	}

	private int FindSegmentIndex(float distance)
	{
		float accumulatedDistance = 0.0f;
		for (int i = 0; i < _segmentLengths.Count; i++)
		{
			accumulatedDistance += _segmentLengths[i];
			if (distance < accumulatedDistance - 0.01f)
			{
				return i;
			}
		}

		return Mathf.Max(0, _segmentLengths.Count - 1);
	}

	private float GetSegmentEndDistance(int segmentIndex)
	{
		float distance = 0.0f;
		for (int i = 0; i <= segmentIndex && i < _segmentLengths.Count; i++)
		{
			distance += _segmentLengths[i];
		}

		return distance;
	}

	private Vector2 GetRoutePointAtDistance(float distance)
	{
		float remainingDistance = Mathf.Clamp(distance, 0.0f, _totalLength);

		for (int i = 0; i < _segmentLengths.Count; i++)
		{
			float segmentLength = _segmentLengths[i];
			if (segmentLength <= 0.0f)
			{
				continue;
			}

			if (remainingDistance <= segmentLength)
			{
				float t = remainingDistance / segmentLength;
				return _points[i].Lerp(_points[i + 1], t);
			}

			remainingDistance -= segmentLength;
		}

		return _points[^1];
	}
}
