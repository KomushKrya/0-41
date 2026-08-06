shader_type canvas_item;
render_mode unshaded;

// CanvasGroup renders its children into SCREEN_TEXTURE before drawing this shader.
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;

// hint_default_white: пока маску не назначили — в редакторе и до старта
// перехода — экран должен быть виден целиком, а не пропадать в чёрное.
uniform sampler2D mask_texture : hint_default_white, repeat_disable, filter_linear;

// Маска закрытия. Та же клякса, проигранная заново: где она разрослась,
// окно уже стёрто. hint_default_black — пустая маска ничего не стирает.
uniform sampler2D close_mask : hint_default_black, repeat_disable, filter_linear;

// 0 — окно открывается или просто висит, 1 — закрывается.
uniform float closing : hint_range(0.0, 1.0) = 0.0;

float luma(vec3 c) {
	return dot(c, vec3(0.299, 0.587, 0.114));
}

void fragment() {
	vec4 radio_screen = textureLod(screen_texture, SCREEN_UV, 0.0);
	if (radio_screen.a > 0.0001) {
		radio_screen.rgb /= radio_screen.a;
	}

	// Black leaves the previous 3D screen visible; white reveals the radio UI.
	float reveal = luma(texture(mask_texture, SCREEN_UV).rgb);

	// На закрытии показываем то, что было в окне И ещё не закрашено новой
	// кляксой. Обратного видео для этого не нужно: та же маска, проигранная
	// вперёд, съедает окно тем же рисунком, каким его открывала.
	float erased = min(reveal, 1.0 - luma(texture(close_mask, SCREEN_UV).rgb));
	reveal = mix(reveal, erased, closing);

	COLOR = vec4(radio_screen.rgb, radio_screen.a * reveal);
}
