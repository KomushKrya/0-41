extends SceneTree

## Рендерит точный прямоугольник служебной геометрии карты: полигоны зданий и
## линии дорог из MapUI.tscn, обрезанные ровно по их общему bounding box.
## Рисунок карты (MapBackdrop) при этом прячется.
##
## Прямоугольник — объединение всех точек Polygon2D из BuildingLayer и всех точек
## Line2D из RoadLayer в системе координат корня сцены, поэтому картинка,
## нарисованная поверх этого рендера, ложится на технический слой один в один.
##
## Запуск из корня репозитория:
##   godot --path . --script tools/editor/_render_map_geometry.gd

const GEOMETRY_SCENE := "res://scenes/ui/map/MapUI.tscn"
const OUTPUT_DIR := "res://temp/map_geometry"
const TARGET_WIDTH := 4096

## Здание штаба. Должно совпадать с HeadquartersBuildingName в MapUI.tscn —
## именно по имени узла MapBuildingEditor находит штаб в рантайме.
const HEADQUARTERS_NODE_NAME := "ImportedBuilding_569"
const HEADQUARTERS_BUILDING_ID := "building_block_r04_c03"
const HEADQUARTERS_OUTLINE_PIXELS := 10.0

## Зона легенды в долях от размера картинки (x, y, ширина, высота), снята с
## разметки художника: горизонтальная полоса вдоль нижнего края, правая половина.
const LEGEND_ZONE := Rect2(0.489, 0.937, 0.503, 0.061)
const LEGEND_ZONE_COLOR := Color(0.0, 0.9, 0.15, 1.0)

## Варианты рендера: имя файла, фон, цвета зданий, дорог и штаба.
const VARIANTS := [
	{
		"file": "map_template_bw.png",
		"background": Color(1, 1, 1, 1),
		"buildings": Color(0.1, 0.1, 0.1, 1),
		"roads": Color(0.1, 0.1, 0.1, 1),
		# В чёрно-белом варианте штаб выворачивается: белая заливка в жирной обводке.
		"headquarters": Color(1, 1, 1, 1),
		"headquarters_outline": Color(0.1, 0.1, 0.1, 1),
	},
	{
		"file": "map_template_color.png",
		"background": Color(1, 1, 1, 1),
		"buildings": Color(0.16, 0.17, 0.19, 1),
		"roads": Color(0.77, 0.24, 0.18, 1),
		"headquarters": Color(0.11, 0.42, 0.66, 1),
		"headquarters_outline": Color(0.04, 0.16, 0.29, 1),
	},
	{
		"file": "map_overlay_alpha.png",
		"background": Color(0, 0, 0, 0),
		"buildings": Color(0.16, 0.17, 0.19, 1),
		"roads": Color(0.77, 0.24, 0.18, 1),
		"headquarters": Color(0.11, 0.42, 0.66, 1),
		"headquarters_outline": Color(0.04, 0.16, 0.29, 1),
	},
	{
		"file": "map_template_legend.png",
		"background": Color(1, 1, 1, 1),
		"buildings": Color(0.16, 0.17, 0.19, 1),
		"roads": Color(0.77, 0.24, 0.18, 1),
		"headquarters": Color(0.11, 0.42, 0.66, 1),
		"headquarters_outline": Color(0.04, 0.16, 0.29, 1),
		"legend_zone": true,
	},
]

var _viewport: SubViewport = null
var _geometry: Control = null
var _background: ColorRect = null
var _buildings: Array[Polygon2D] = []
var _roads: Array[Line2D] = []
var _headquarters: Polygon2D = null
var _headquarters_outline: Line2D = null
var _legend_zone: ColorRect = null
var _bounds := Rect2()
var _scale := 1.0
var _variant_index := 0
var _frames := 0

# Значения по умолчанию берутся из констант, но их можно переопределить
# аргументами после `--`, чтобы прогнать скрипт по свежесобранной геометрии:
#   godot --path . --script tools/editor/_render_map_geometry.gd -- \
#       --scene res://scenes/ui/map/MapUI.tscn --out res://temp/map_geometry
var _scene_path := GEOMETRY_SCENE
var _output_dir := OUTPUT_DIR
var _headquarters_name := HEADQUARTERS_NODE_NAME

func _read_user_arguments() -> void:
	var arguments := OS.get_cmdline_user_args()
	for index in range(arguments.size() - 1):
		match arguments[index]:
			"--scene":
				_scene_path = arguments[index + 1]
			"--out":
				_output_dir = arguments[index + 1]
			"--hq":
				_headquarters_name = arguments[index + 1]

func _initialize() -> void:
	_read_user_arguments()

	_viewport = SubViewport.new()
	_viewport.disable_3d = true
	_viewport.transparent_bg = true
	_viewport.msaa_2d = Viewport.MSAA_4X
	_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	_viewport.size = Vector2i(64, 64)
	get_root().add_child(_viewport)

	_geometry = (load(_scene_path) as PackedScene).instantiate() as Control
	_viewport.add_child(_geometry)

	# Рисунок карты не входит в геометрию: рендерим только служебные слои,
	# фон задаём своим прямоугольником поверх всего вьюпорта.
	var backdrop := _geometry.get_node_or_null("MapLayers/MapBackdrop")
	if backdrop != null:
		backdrop.visible = false

	_collect_buildings(_geometry.get_node("MapLayers/BuildingLayer"))
	_collect_roads(_geometry.get_node("MapLayers/RoadLayer"))
	if _buildings.is_empty() and _roads.is_empty():
		push_error("Не найдено ни зданий, ни дорог в %s" % _scene_path)
		quit(1)
		return

	_headquarters = _geometry.get_node_or_null("MapLayers/BuildingLayer/%s" % _headquarters_name) as Polygon2D
	if _headquarters == null:
		push_warning("Штаб '%s' не найден — рендер будет без выделения штаба." % _headquarters_name)

	_bounds = _calculate_bounds()
	_scale = float(TARGET_WIDTH) / _bounds.size.x
	# Округляем высоту вверх, чтобы картинка гарантированно накрывала весь bounds;
	# лишние доли пикселя раскидываем поровну сверху и снизу.
	var height_px := int(ceil(_bounds.size.y * _scale))
	var vertical_padding := (float(height_px) - _bounds.size.y * _scale) * 0.5

	_viewport.size = Vector2i(TARGET_WIDTH, height_px)
	_geometry.scale = Vector2(_scale, _scale)
	_geometry.position = (-_bounds.position * _scale) + Vector2(0.0, vertical_padding)

	_create_headquarters_outline()

	# Плашка легенды лежит поверх геометрии, чтобы было видно, что именно она накрывает.
	_legend_zone = ColorRect.new()
	_legend_zone.z_index = 100
	_legend_zone.color = LEGEND_ZONE_COLOR
	_legend_zone.position = _legend_zone_pixels().position
	_legend_zone.size = _legend_zone_pixels().size
	_legend_zone.visible = false
	_viewport.add_child(_legend_zone)

	_background = ColorRect.new()
	_background.z_index = -100
	_background.position = Vector2.ZERO
	_background.size = Vector2(_viewport.size)
	_viewport.add_child(_background)
	_viewport.move_child(_background, 0)

	DirAccess.make_dir_recursive_absolute(ProjectSettings.globalize_path(_output_dir))
	_apply_variant(VARIANTS[0])

func _process(_delta: float) -> bool:
	# Первые кадры уходят на применение трансформов и перерисовку Polygon2D/Line2D.
	_frames += 1
	if _frames < 4:
		return false

	var variant: Dictionary = VARIANTS[_variant_index]
	var image := _viewport.get_texture().get_image()
	var path := "%s/%s" % [_output_dir, variant["file"]]
	var result := image.save_png(path)
	if result != OK:
		push_error("Не удалось сохранить %s: %s" % [path, result])
		quit(1)
		return true

	print("saved %s %dx%d" % [path, image.get_width(), image.get_height()])

	_variant_index += 1
	if _variant_index < VARIANTS.size():
		_apply_variant(VARIANTS[_variant_index])
		_frames = 0
		return false

	_write_metadata()
	return true

func _apply_variant(variant: Dictionary) -> void:
	_background.color = variant["background"]
	for building in _buildings:
		building.color = variant["buildings"]
	for road in _roads:
		road.default_color = variant["roads"]

	if _headquarters != null:
		_headquarters.color = variant["headquarters"]
	if _headquarters_outline != null:
		_headquarters_outline.default_color = variant["headquarters_outline"]

	_legend_zone.visible = variant.get("legend_zone", false)

## Зона легенды в пикселях итоговой картинки.
func _legend_zone_pixels() -> Rect2:
	var image_size := Vector2(_viewport.size)
	return Rect2(LEGEND_ZONE.position * image_size, LEGEND_ZONE.size * image_size)

## Замкнутая обводка вокруг штаба: она читается в любом варианте, в том числе
## в чёрно-белом, где заливка штаба вывернута в белый.
func _create_headquarters_outline() -> void:
	if _headquarters == null or _headquarters.polygon.is_empty():
		return

	var outline := Line2D.new()
	outline.name = "HeadquartersOutline"
	outline.z_index = 1
	outline.width = HEADQUARTERS_OUTLINE_PIXELS / _scale
	outline.antialiased = true
	outline.joint_mode = Line2D.LINE_JOINT_ROUND
	outline.closed = true
	outline.points = _headquarters.polygon
	_headquarters.add_child(outline)
	_headquarters_outline = outline

func _write_metadata() -> void:
	# Прямоугольник, который реально покрывает картинка: по ширине он совпадает
	# с bounds точно, по высоте чуть больше из-за округления до целого пикселя.
	var covered_size := Vector2(float(_viewport.size.x), float(_viewport.size.y)) / _scale
	var covered := Rect2(
		_bounds.position - Vector2(0.0, (covered_size.y - _bounds.size.y) * 0.5),
		covered_size)
	var metadata := {
		"source_scene": _scene_path,
		"geometry_bounds": {
			"x": _bounds.position.x,
			"y": _bounds.position.y,
			"width": _bounds.size.x,
			"height": _bounds.size.y,
			"aspect": _bounds.size.x / _bounds.size.y,
		},
		"covered_rect": {
			"x": covered.position.x,
			"y": covered.position.y,
			"width": covered.size.x,
			"height": covered.size.y,
			"aspect": covered.size.x / covered.size.y,
		},
		"image_size_px": {"width": _viewport.size.x, "height": _viewport.size.y},
		"pixels_per_unit": _scale,
		"headquarters": _build_headquarters_metadata(covered),
		"legend_zone": _build_legend_zone_metadata(covered),
		"building_count": _buildings.size(),
		"road_count": _roads.size(),
		"variants": VARIANTS.map(func(variant: Dictionary) -> String: return variant["file"]),
	}

	var path := "%s/bounds.json" % _output_dir
	var file := FileAccess.open(path, FileAccess.WRITE)
	if file == null:
		push_error("Не удалось записать %s" % path)
		return

	file.store_string(JSON.stringify(metadata, "\t"))
	file.close()
	print("saved %s" % path)
	print("bounds %s, aspect %f, ppu %f" % [_bounds, _bounds.size.x / _bounds.size.y, _scale])

## Зона легенды в трёх системах координат плюс список зданий, которые она
## накрывает: их метки заданий окажутся под плашкой.
func _build_legend_zone_metadata(covered: Rect2) -> Dictionary:
	var zone_px := _legend_zone_pixels()
	var zone_map := Rect2(covered.position + zone_px.position / _scale, zone_px.size / _scale)

	var covered_buildings: Array[String] = []
	for building in _buildings:
		var to_root := _geometry.get_global_transform().affine_inverse() * building.get_global_transform()
		for point in building.polygon:
			if zone_map.has_point(to_root * point):
				covered_buildings.append(String(building.name))
				break

	return {
		"normalized": {
			"x": LEGEND_ZONE.position.x,
			"y": LEGEND_ZONE.position.y,
			"width": LEGEND_ZONE.size.x,
			"height": LEGEND_ZONE.size.y,
		},
		"pixels": {
			"x": zone_px.position.x,
			"y": zone_px.position.y,
			"width": zone_px.size.x,
			"height": zone_px.size.y,
		},
		"map_units": {
			"x": zone_map.position.x,
			"y": zone_map.position.y,
			"width": zone_map.size.x,
			"height": zone_map.size.y,
		},
		"covered_buildings": covered_buildings,
	}

## Центр штаба в единицах карты и в пикселях картинки — чтобы художник знал,
## куда ставить символ управления, а не искал квартал на глаз.
func _build_headquarters_metadata(covered: Rect2) -> Dictionary:
	if _headquarters == null or _headquarters.polygon.is_empty():
		return {"found": false, "node_name": _headquarters_name}

	var to_root := _geometry.get_global_transform().affine_inverse() * _headquarters.get_global_transform()
	var centre := Vector2.ZERO
	var minimum := Vector2(INF, INF)
	var maximum := Vector2(-INF, -INF)
	for point in _headquarters.polygon:
		var mapped: Vector2 = to_root * point
		centre += mapped
		minimum = minimum.min(mapped)
		maximum = maximum.max(mapped)

	centre /= float(_headquarters.polygon.size())
	var centre_px := (centre - covered.position) * _scale

	return {
		"found": true,
		"node_name": _headquarters_name,
		"building_id": HEADQUARTERS_BUILDING_ID,
		"centre": {"x": centre.x, "y": centre.y},
		"centre_px": {"x": centre_px.x, "y": centre_px.y},
		"size_px": {"x": (maximum.x - minimum.x) * _scale, "y": (maximum.y - minimum.y) * _scale},
	}

## Повторяет TryGetMapGeometryBounds из MapBuildingEditor: объединение точек
## зданий и дорог в системе координат корня геометрии.
func _calculate_bounds() -> Rect2:
	var minimum := Vector2(INF, INF)
	var maximum := Vector2(-INF, -INF)

	for building in _buildings:
		var to_root := _geometry.get_global_transform().affine_inverse() * building.get_global_transform()
		for point in building.polygon:
			var mapped: Vector2 = to_root * point
			minimum = minimum.min(mapped)
			maximum = maximum.max(mapped)

	for road in _roads:
		var to_root := _geometry.get_global_transform().affine_inverse() * road.get_global_transform()
		for point in road.points:
			var mapped: Vector2 = to_root * point
			minimum = minimum.min(mapped)
			maximum = maximum.max(mapped)

	return Rect2(minimum, maximum - minimum)

func _collect_buildings(root: Node) -> void:
	for child in root.get_children():
		if child is Polygon2D:
			_buildings.append(child)

		_collect_buildings(child)

func _collect_roads(root: Node) -> void:
	for child in root.get_children():
		if child is Line2D:
			_roads.append(child)

		_collect_roads(child)
