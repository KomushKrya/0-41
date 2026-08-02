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

function Format-Points([object[]]$points) {
	$values = [System.Collections.Generic.List[string]]::new()
	foreach ($point in $points) {
		$projected = Convert-ToWebMercator $point
		$x = $offsetX + (($projected.X - $minX) * $scale)
		$y = $offsetY + (($maxY - $projected.Y) * $scale)
		[void]$values.Add((Format-Number $x))
		[void]$values.Add((Format-Number $y))
	}

	return [string]::Join(", ", $values)
}

$scene = [System.Text.StringBuilder]::new()
[void]$scene.AppendLine("[gd_scene format=3]")
[void]$scene.AppendLine()
[void]$scene.AppendLine("[node name=`"GeneratedMapGeometry`" type=`"Control`"]")
[void]$scene.AppendLine("mouse_filter = 2")
[void]$scene.AppendLine()
[void]$scene.AppendLine("[node name=`"BuildingLayer`" type=`"Control`" parent=`".`"]")
[void]$scene.AppendLine("mouse_filter = 2")

$buildingIndex = 1
foreach ($building in $buildings) {
	[void]$scene.AppendLine()
	[void]$scene.AppendLine(("[node name=`"ImportedBuilding_{0}`" type=`"Polygon2D`" parent=`"BuildingLayer`"]" -f $buildingIndex))
	[void]$scene.AppendLine("color = Color(0.58, 0.54, 0.42, 1)")
	[void]$scene.AppendLine(("polygon = PackedVector2Array({0})" -f (Format-Points $building.Points)))
	$buildingIndex++
}

[void]$scene.AppendLine()
[void]$scene.AppendLine("[node name=`"RoadLayer`" type=`"Control`" parent=`".`"]")
[void]$scene.AppendLine("mouse_filter = 2")

$roadIndex = 1
foreach ($road in $roads) {
	[void]$scene.AppendLine()
	[void]$scene.AppendLine(("[node name=`"ImportedRoad_{0}`" type=`"Line2D`" parent=`"RoadLayer`"]" -f $roadIndex))
	[void]$scene.AppendLine(("points = PackedVector2Array({0})" -f (Format-Points $road.Points)))
	[void]$scene.AppendLine("width = 4.0")
	[void]$scene.AppendLine("default_color = Color(0.36, 0.31, 0.22, 0.82)")
	[void]$scene.AppendLine("antialiased = true")
	$roadIndex++
}

[System.IO.Directory]::CreateDirectory((Split-Path -Parent $OutputPath)) | Out-Null
[System.IO.File]::WriteAllText($OutputPath, $scene.ToString(), [System.Text.UTF8Encoding]::new($false))
Write-Host "Generated $OutputPath with $($buildings.Count) buildings and $($roads.Count) roads."
