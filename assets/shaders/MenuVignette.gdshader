shader_type canvas_item;

// Затемняющая накладка главного меню: виньетка по краям, тень слева под колонкой
// текста и мелкое зерно. Кладётся между живым фоном и подписями — фон приглушает,
// текст не трогает.
//
// Экранную текстуру шейдер не читает: в режиме gl_compatibility за этим тянется
// копия кадра. Здесь хватает полупрозрачной заливки поверх, а она стоит ровно
// один проход по пикселям.

// Насколько густо темнеют края.
uniform float vignette_strength : hint_range(0.0, 1.0) = 0.85;
// Радиус чистого центра: до него накладки нет.
uniform float vignette_radius : hint_range(0.0, 1.5) = 0.42;
// Ширина растушёвки от чистого центра до полной густоты.
uniform float vignette_softness : hint_range(0.01, 1.5) = 0.62;
// Отдельная тень с левого края: на ней стоит колонка меню.
uniform float column_shade : hint_range(0.0, 1.0) = 0.5;
// Докуда эта тень дотягивается по ширине экрана.
uniform float column_width : hint_range(0.0, 1.0) = 0.52;
// Ровная подложка по всему кадру: без неё зерно в светлой части пропадает.
uniform float base_shade : hint_range(0.0, 1.0) = 0.08;
// Цвет затемнения. Чуть синее чёрного — кадр в кабинете тёплый, контраст по температуре.
uniform vec4 shade_color : source_color = vec4(0.016, 0.018, 0.027, 1.0);
uniform float grain_strength : hint_range(0.0, 0.2) = 0.03;
// Кадров зерна в секунду: на 60 оно кипит, на 12 выглядит плёнкой.
uniform float grain_fps = 12.0;
// Кадр шире, чем выше: без поправки виньетка выходит овалом по горизонтали.
uniform float aspect = 1.6;

float hash(vec2 p)
{
	return fract(sin(dot(p, vec2(41.13, 289.77))) * 43758.5453);
}

void fragment()
{
	vec2 d = (UV - vec2(0.5)) * vec2(aspect, 1.0);
	float edge = smoothstep(vignette_radius, vignette_radius + vignette_softness, length(d));

	float column = column_shade * smoothstep(column_width, 0.0, UV.x);
	float alpha = base_shade + edge * vignette_strength + column;

	float grain = (hash(UV * vec2(1920.0, 1080.0) + floor(TIME * grain_fps)) - 0.5) * grain_strength;

	COLOR = vec4(shade_color.rgb, clamp(alpha + grain, 0.0, 1.0));
}
