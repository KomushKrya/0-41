shader_type canvas_item;
render_mode unshaded;

// Маска иллюстрации: белое — картинка видна, чёрное — прозрачно.
// hint_default_white: пока маску не назначили, картинка видна целиком,
// а не пропадает молча.
uniform sampler2D mask_texture : hint_default_white, repeat_disable, filter_linear;

// Рамка узла в долях экрана: xy — левый верхний угол, zw — размер.
// Заполняет скрипт: считать её в шейдере не из чего.
uniform vec4 node_rect = vec4(0.0, 0.0, 1.0, 1.0);

// Насколько маска действует. 0 — картинка как есть, 1 — полностью по маске.
uniform float mask_strength : hint_range(0.0, 1.0) = 1.0;

void fragment() {
	// Берём положение внутри рамки узла, а не UV текстуры: у TextureRect
	// в режиме «обрезать по пропорции» UV бегает по вырезанному куску кадра,
	// и маска уехала бы вместе с ним. Маска нарисована под рамку на экране,
	// а не под конкретную картинку.
	vec2 rect_uv = (FRAGCOORD.xy * SCREEN_PIXEL_SIZE - node_rect.xy) / node_rect.zw;

	float reveal = dot(texture(mask_texture, rect_uv).rgb, vec3(0.299, 0.587, 0.114));
	COLOR.a *= mix(1.0, reveal, mask_strength);
}
