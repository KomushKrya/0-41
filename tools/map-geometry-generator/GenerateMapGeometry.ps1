param(
	[string]$BuildingsPath = "",
	[string]$RoadsPath = "",
	[string]$OutputPath = ""
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($BuildingsPath)) {
	$BuildingsPath = Join-Path $PSScriptRoot "input\buildings.geojson"
}

if ([string]::IsNullOrWhiteSpace($RoadsPath)) {
	$RoadsPath = Join-Path $PSScriptRoot "input\roads.geojson"
}

if ([string]::IsNullOrWhiteSpace($OutputPath)) {
	$OutputPath = Join-Path $PSScriptRoot "output\GeneratedMapGeometry.tscn"
}

$culture = [System.Globalization.CultureInfo]::InvariantCulture
$mapWidth = 928.0
$mapHeight = 672.0
$padding = 20.0
$buildings = [System.Collections.Generic.List[object]]::new()
$roads = [System.Collections.Generic.List[object]]::new()
$geometryEpsilon = 0.0001
$maskPolygon = @(
	[PSCustomObject]@{ X = 234.12764; Y = 281.68823 },
	[PSCustomObject]@{ X = 402.17236; Y = 211.79565 },
	[PSCustomObject]@{ X = 585.6765; Y = 145.22069 },
	[PSCustomObject]@{ X = 664.77734; Y = 348.42517 },
	[PSCustomObject]@{ X = 651.3115; Y = 355.10886 },
	[PSCustomObject]@{ X = 520.6821; Y = 376.9484 },
	[PSCustomObject]@{ X = 498.35052; Y = 414.39566 },
	[PSCustomObject]@{ X = 402.6355; Y = 449.87292 },
	[PSCustomObject]@{ X = 313.25342; Y = 445.8927 },
	[PSCustomObject]@{ X = 280.3659; Y = 390.2563 },
	[PSCustomObject]@{ X = 246.1059; Y = 315.6959 }
)

function Get-Points([object]$coordinates) {
	$points = [System.Collections.Generic.List[object]]::new()
	foreach ($coordinate in @($coordinates)) {
		if ($coordinate.Count -lt 2) {
			continue
		}

		[void]$points.Add([PSCustomObject]@{
			X = [double]$coordinate[0]
			Y = [double]$coordinate[1]
		})
	}

	if ($points.Count -gt 1 -and
		[math]::Abs($points[0].X - $points[$points.Count - 1].X) -lt 0.0000001 -and
		[math]::Abs($points[0].Y - $points[$points.Count - 1].Y) -lt 0.0000001) {
		$points.RemoveAt($points.Count - 1)
	}

	return $points.ToArray()
}

function Add-Polygon([object]$polygonCoordinates) {
	$rings = @($polygonCoordinates)
	if ($rings.Count -eq 0) {
		return
	}

	$points = Get-Points $rings[0]
	if ($points.Count -ge 3) {
		[void]$buildings.Add([PSCustomObject]@{ Points = $points })
	}
}

function Add-Road([object]$roadCoordinates) {
	$points = Get-Points $roadCoordinates
	if ($points.Count -ge 2) {
		[void]$roads.Add([PSCustomObject]@{ Points = $points })
	}
}

$buildingData = Get-Content -Raw -LiteralPath $BuildingsPath | ConvertFrom-Json
foreach ($feature in $buildingData.features) {
	switch ($feature.geometry.type) {
		"Polygon" { Add-Polygon $feature.geometry.coordinates }
		"MultiPolygon" {
			foreach ($polygon in $feature.geometry.coordinates) {
				Add-Polygon $polygon
			}
		}
	}
}

$roadData = Get-Content -Raw -LiteralPath $RoadsPath | ConvertFrom-Json
foreach ($feature in $roadData.features) {
	switch ($feature.geometry.type) {
		"LineString" { Add-Road $feature.geometry.coordinates }
		"MultiLineString" {
			foreach ($line in $feature.geometry.coordinates) {
				Add-Road $line
			}
		}
	}
}

if ($buildings.Count -eq 0 -or $roads.Count -eq 0) {
	throw "GeoJSON must contain at least one building polygon and one road line."
}

function Convert-ToWebMercator([object]$point) {
	$earthRadius = 6378137.0
	$longitudeRadians = $point.X * [math]::PI / 180.0
	$latitude = [math]::Max(-85.05112878, [math]::Min(85.05112878, $point.Y))
	$latitudeRadians = $latitude * [math]::PI / 180.0

	return [PSCustomObject]@{
		X = $earthRadius * $longitudeRadians
		Y = $earthRadius * [math]::Log([math]::Tan(([math]::PI / 4.0) + ($latitudeRadians / 2.0)))
	}
}

$allPoints = [System.Collections.Generic.List[object]]::new()
foreach ($building in $buildings) {
	foreach ($point in $building.Points) {
		[void]$allPoints.Add((Convert-ToWebMercator $point))
	}
}
foreach ($road in $roads) {
	foreach ($point in $road.Points) {
		[void]$allPoints.Add((Convert-ToWebMercator $point))
	}
}

$minX = ($allPoints | Measure-Object -Property X -Minimum).Minimum
$maxX = ($allPoints | Measure-Object -Property X -Maximum).Maximum
$minY = ($allPoints | Measure-Object -Property Y -Minimum).Minimum
$maxY = ($allPoints | Measure-Object -Property Y -Maximum).Maximum
$sourceWidth = $maxX - $minX
$sourceHeight = $maxY - $minY
$scale = [math]::Min(($mapWidth - ($padding * 2.0)) / $sourceWidth, ($mapHeight - ($padding * 2.0)) / $sourceHeight)
$mappedWidth = $sourceWidth * $scale
$mappedHeight = $sourceHeight * $scale
$offsetX = ($mapWidth - $mappedWidth) / 2.0
$offsetY = ($mapHeight - $mappedHeight) / 2.0

function Format-Number([double]$value) {
	return $value.ToString("0.###", $culture)
}

function Convert-ToMapPoint([object]$point) {
	$projected = Convert-ToWebMercator $point
	return [PSCustomObject]@{
		X = $offsetX + (($projected.X - $minX) * $scale)
		Y = $offsetY + (($maxY - $projected.Y) * $scale)
	}
}

function Convert-ToMapPoints([object[]]$points) {
	$mappedPoints = [System.Collections.Generic.List[object]]::new()
	foreach ($point in $points) {
		[void]$mappedPoints.Add((Convert-ToMapPoint $point))
	}

	return $mappedPoints.ToArray()
}

function Format-MappedPoints([object[]]$points) {
	$values = [System.Collections.Generic.List[string]]::new()
	foreach ($point in $points) {
		[void]$values.Add((Format-Number $point.X))
		[void]$values.Add((Format-Number $point.Y))
	}

	return [string]::Join(", ", $values)
}

function Get-CrossProduct([object]$from, [object]$to, [object]$point) {
	return (($to.X - $from.X) * ($point.Y - $from.Y)) - (($to.Y - $from.Y) * ($point.X - $from.X))
}

function Test-PointOnSegment([object]$point, [object]$from, [object]$to) {
	if ([math]::Abs((Get-CrossProduct $from $to $point)) -gt $geometryEpsilon) {
		return $false
	}

	$dot = (($point.X - $from.X) * ($point.X - $to.X)) + (($point.Y - $from.Y) * ($point.Y - $to.Y))
	return $dot -le $geometryEpsilon
}

function Test-PointInMask([object]$point) {
	$inside = $false
	for ($i = 0; $i -lt $maskPolygon.Count; $i++) {
		$from = $maskPolygon[$i]
		$to = $maskPolygon[($i + 1) % $maskPolygon.Count]

		if (Test-PointOnSegment $point $from $to) {
			return $true
		}

		if (($from.Y -gt $point.Y) -ne ($to.Y -gt $point.Y)) {
			$intersectionX = $from.X + (($point.Y - $from.Y) * ($to.X - $from.X) / ($to.Y - $from.Y))
			if ($point.X -lt $intersectionX) {
				$inside = -not $inside
			}
		}
	}

	return $inside
}

function Test-PolygonInsideMask([object[]]$points) {
	foreach ($point in $points) {
		if (-not (Test-PointInMask $point)) {
			return $false
		}
	}

	return $true
}

function Get-SegmentIntersectionParameters([object]$from, [object]$to) {
	$parameters = [System.Collections.Generic.List[double]]::new()
	[void]$parameters.Add(0.0)
	[void]$parameters.Add(1.0)

	$roadX = $to.X - $from.X
	$roadY = $to.Y - $from.Y
	for ($i = 0; $i -lt $maskPolygon.Count; $i++) {
		$maskFrom = $maskPolygon[$i]
		$maskTo = $maskPolygon[($i + 1) % $maskPolygon.Count]
		$maskX = $maskTo.X - $maskFrom.X
		$maskY = $maskTo.Y - $maskFrom.Y
		$denominator = ($roadX * $maskY) - ($roadY * $maskX)
		if ([math]::Abs($denominator) -le $geometryEpsilon) {
			continue
		}

		$deltaX = $maskFrom.X - $from.X
		$deltaY = $maskFrom.Y - $from.Y
		$t = (($deltaX * $maskY) - ($deltaY * $maskX)) / $denominator
		$u = (($deltaX * $roadY) - ($deltaY * $roadX)) / $denominator
		if ($t -ge -$geometryEpsilon -and $t -le (1.0 + $geometryEpsilon) -and
			$u -ge -$geometryEpsilon -and $u -le (1.0 + $geometryEpsilon)) {
			[void]$parameters.Add([math]::Max(0.0, [math]::Min(1.0, $t)))
		}
	}

	$sorted = @($parameters | Sort-Object)
	$unique = [System.Collections.Generic.List[double]]::new()
	foreach ($parameter in $sorted) {
		if ($unique.Count -eq 0 -or [math]::Abs($parameter - $unique[$unique.Count - 1]) -gt $geometryEpsilon) {
			[void]$unique.Add($parameter)
		}
	}

	return $unique.ToArray()
}

function Get-PointOnSegment([object]$from, [object]$to, [double]$parameter) {
	return [PSCustomObject]@{
		X = $from.X + (($to.X - $from.X) * $parameter)
		Y = $from.Y + (($to.Y - $from.Y) * $parameter)
	}
}

function Get-DistanceSquared([object]$from, [object]$to) {
	$x = $to.X - $from.X
	$y = $to.Y - $from.Y
	return ($x * $x) + ($y * $y)
}

function Get-ClippedRoadParts([object[]]$points) {
	$parts = [System.Collections.Generic.List[object]]::new()
	$currentPart = $null

	for ($pointIndex = 0; $pointIndex -lt ($points.Count - 1); $pointIndex++) {
		$from = $points[$pointIndex]
		$to = $points[$pointIndex + 1]
		$parameters = @(Get-SegmentIntersectionParameters $from $to)

		for ($parameterIndex = 0; $parameterIndex -lt ($parameters.Count - 1); $parameterIndex++) {
			$startParameter = [double]$parameters[$parameterIndex]
			$endParameter = [double]$parameters[$parameterIndex + 1]
			if (($endParameter - $startParameter) -le $geometryEpsilon) {
				continue
			}

			$middle = Get-PointOnSegment $from $to (($startParameter + $endParameter) * 0.5)
			if (-not (Test-PointInMask $middle)) {
				if ($null -ne $currentPart -and $currentPart.Count -ge 2) {
					[void]$parts.Add([PSCustomObject]@{ Points = $currentPart.ToArray() })
				}
				$currentPart = $null
				continue
			}

			$clippedFrom = Get-PointOnSegment $from $to $startParameter
			$clippedTo = Get-PointOnSegment $from $to $endParameter
			if ($null -eq $currentPart) {
				$currentPart = [System.Collections.Generic.List[object]]::new()
				[void]$currentPart.Add($clippedFrom)
			}
			elseif ((Get-DistanceSquared $currentPart[$currentPart.Count - 1] $clippedFrom) -gt ($geometryEpsilon * $geometryEpsilon)) {
				if ($currentPart.Count -ge 2) {
					[void]$parts.Add([PSCustomObject]@{ Points = $currentPart.ToArray() })
				}
				$currentPart = [System.Collections.Generic.List[object]]::new()
				[void]$currentPart.Add($clippedFrom)
			}

			if ((Get-DistanceSquared $currentPart[$currentPart.Count - 1] $clippedTo) -gt ($geometryEpsilon * $geometryEpsilon)) {
				[void]$currentPart.Add($clippedTo)
			}
		}
	}

	if ($null -ne $currentPart -and $currentPart.Count -ge 2) {
		[void]$parts.Add([PSCustomObject]@{ Points = $currentPart.ToArray() })
	}

	return $parts.ToArray()
}

$scene = [System.Text.StringBuilder]::new()
[void]$scene.AppendLine("[gd_scene format=3]")
[void]$scene.AppendLine()
[void]$scene.AppendLine("[node name=`"GeneratedMapGeometry`" type=`"Control`"]")
[void]$scene.AppendLine(("custom_minimum_size = Vector2({0}, {1})" -f (Format-Number $mapWidth), (Format-Number $mapHeight)))
[void]$scene.AppendLine("mouse_filter = 2")
[void]$scene.AppendLine()
[void]$scene.AppendLine("[node name=`"Background`" type=`"ColorRect`" parent=`".`"]")
[void]$scene.AppendLine("z_index = -1")
[void]$scene.AppendLine(("offset_right = {0}" -f (Format-Number $mapWidth)))
[void]$scene.AppendLine(("offset_bottom = {0}" -f (Format-Number $mapHeight)))
[void]$scene.AppendLine("mouse_filter = 2")
[void]$scene.AppendLine("color = Color(0.72, 0.67, 0.54, 1)")
[void]$scene.AppendLine()
[void]$scene.AppendLine("[node name=`"BuildingLayer`" type=`"Control`" parent=`".`"]")
[void]$scene.AppendLine("mouse_filter = 2")

$buildingIndex = 1
$discardedBuildingCount = 0
foreach ($building in $buildings) {
	$mappedPoints = Convert-ToMapPoints $building.Points
	if (-not (Test-PolygonInsideMask $mappedPoints)) {
		$discardedBuildingCount++
		continue
	}

	[void]$scene.AppendLine()
	[void]$scene.AppendLine(("[node name=`"ImportedBuilding_{0}`" type=`"Polygon2D`" parent=`"BuildingLayer`"]" -f $buildingIndex))
	[void]$scene.AppendLine("color = Color(0.58, 0.54, 0.42, 1)")
	[void]$scene.AppendLine(("polygon = PackedVector2Array({0})" -f (Format-MappedPoints $mappedPoints)))
	$buildingIndex++
}

[void]$scene.AppendLine()
[void]$scene.AppendLine("[node name=`"RoadLayer`" type=`"Control`" parent=`".`"]")
[void]$scene.AppendLine("mouse_filter = 2")

$roadIndex = 1
$discardedRoadCount = 0
foreach ($road in $roads) {
	$mappedPoints = Convert-ToMapPoints $road.Points
	$clippedParts = @(Get-ClippedRoadParts $mappedPoints)
	if ($clippedParts.Count -eq 0) {
		$discardedRoadCount++
		continue
	}

	foreach ($part in $clippedParts) {
		[void]$scene.AppendLine()
		[void]$scene.AppendLine(("[node name=`"ImportedRoad_{0}`" type=`"Line2D`" parent=`"RoadLayer`"]" -f $roadIndex))
		[void]$scene.AppendLine(("points = PackedVector2Array({0})" -f (Format-MappedPoints $part.Points)))
		[void]$scene.AppendLine("width = 0.5")
		[void]$scene.AppendLine("default_color = Color(0.36, 0.31, 0.22, 0.82)")
		[void]$scene.AppendLine("antialiased = true")
		$roadIndex++
	}
}

[void]$scene.AppendLine()
[void]$scene.AppendLine("[node name=`"Mask`" type=`"Polygon2D`" parent=`".`"]")
[void]$scene.AppendLine("visible = false")
[void]$scene.AppendLine(("polygon = PackedVector2Array({0})" -f (Format-MappedPoints $maskPolygon)))

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $OutputPath)) | Out-Null
[System.IO.File]::WriteAllText($OutputPath, $scene.ToString(), [System.Text.UTF8Encoding]::new($false))
$generatedBuildingCount = $buildingIndex - 1
$generatedRoadCount = $roadIndex - 1
Write-Host "Generated $OutputPath with $generatedBuildingCount buildings and $generatedRoadCount clipped road parts. Removed $discardedBuildingCount buildings and $discardedRoadCount roads outside Mask."
