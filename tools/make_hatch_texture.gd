extends SceneTree

# Генератор бесшовной карандашной штриховки для шкал блокнота.
# Штрихи идут по диагонали x+y с шагом PERIOD; размер тайла кратен шагу,
# поэтому плитка стыкуется без шва по обеим осям.
# Запуск: godot --headless --path <проект> --script tools/make_hatch_texture.gd

const OUT := "res://assets/textures/hatch_pencil.png"
const SIZE := 56
const PERIOD := 7.0
const CORE := 0.32		# доля полушага, закрашенная полностью
const EDGE := 0.68		# доля полушага, где штрих сходит на нет
const INK := 0.52
const GRAPHITE := Color(0.16, 0.15, 0.14)

func _process(_delta: float) -> bool:
	var image := Image.create(SIZE, SIZE, false, Image.FORMAT_RGBA8)
	for y in SIZE:
		for x in SIZE:
			var line := float(x + y)
			var idx := fmod(floor(line / PERIOD), 12.0)
			# дрожание руки: сдвиг всей линии + лёгкая волна вдоль неё
			line += (_hash(idx) - 0.5) * 1.6 + sin(TAU * float(y) / float(SIZE)) * 1.1
			var d: float = abs(fposmod(line / PERIOD, 1.0) - 0.5) * 2.0
			var ink: float = smoothstep(EDGE, CORE, d) * (0.72 + 0.28 * _hash(idx + 3.0))
			image.set_pixel(x, y, Color(GRAPHITE.r, GRAPHITE.g, GRAPHITE.b, ink * INK))
	image.save_png(OUT)
	print("saved ", OUT)
	return true

func _hash(n: float) -> float:
	return fposmod(sin(n * 12.9898) * 43758.5453, 1.0)
