extends SceneTree

# Генератор рамки шкалы «от руки»: прямоугольный контур с дрожанием линии и
# неровным нажимом, под NinePatchRect.
#
# Волны заданы периодами, кратными центральным секциям 9-патча (32 px по
# горизонтали, 16 px по вертикали), иначе на стыке плиток будет излом.
# Запуск: godot --headless --path <проект> --script tools/make_bar_frame_texture.gd

const OUT := "res://assets/textures/bar_frame_pencil.png"
const W := 64
const H := 48
const MARGIN := 16.0	# патч-марджин, должен совпадать с настройкой узла
const INSET := 5.0		# отступ линии от края текстуры
const CORE := 0.75		# полуширина плотной части штриха, px
const EDGE := 1.75		# полуширина размытия, px
const INK := 0.7
const GRAPHITE := Color(0.16, 0.15, 0.14)

func _process(_delta: float) -> bool:
	var image := Image.create(W, H, false, Image.FORMAT_RGBA8)
	var cx := float(W) * 0.5
	var cy := float(H) * 0.5
	var hw := cx - INSET
	var hh := cy - INSET
	for y in H:
		for x in W:
			var fx := float(x) + 0.5
			var fy := float(y) + 0.5
			# дрожание руки: периоды кратны центральным секциям патча
			var wobble := 1.15 * sin(TAU * fx / 32.0 + 0.7) \
				+ 0.85 * sin(TAU * fy / 16.0 + 2.1) \
				+ 0.45 * sin(TAU * fx / 16.0 + TAU * fy / 16.0)
			var dx: float = abs(fx - cx) - hw
			var dy: float = abs(fy - cy) - hh
			var outside := Vector2(max(dx, 0.0), max(dy, 0.0)).length()
			var sdf: float = outside + min(max(dx, dy), 0.0)
			var d: float = abs(sdf + wobble * 0.55)
			var ink: float = smoothstep(EDGE, CORE, d)
			# нажим гуляет вдоль линии, местами карандаш почти отрывается
			var press := 0.86 + 0.14 * sin(TAU * fx / 32.0 + TAU * fy / 16.0 * 0.5 + 1.9)
			image.set_pixel(x, y, Color(GRAPHITE.r, GRAPHITE.g, GRAPHITE.b, ink * press * INK))
	image.save_png(OUT)
	print("saved ", OUT)
	return true
