shader_type canvas_item;
render_mode unshaded;

// CanvasGroup renders its children into SCREEN_TEXTURE before drawing this shader.
uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_nearest;
// hint_default_white: пока маску не назначили — в редакторе и до старта
// перехода — экран должен быть виден целиком, а не пропадать в чёрное.
uniform sampler2D mask_texture : hint_default_white, repeat_disable, filter_linear;

void fragment() {
	vec4 radio_screen = textureLod(screen_texture, SCREEN_UV, 0.0);
	if (radio_screen.a > 0.0001) {
		radio_screen.rgb /= radio_screen.a;
	}

	// Black leaves the previous 3D screen visible; white reveals the radio UI.
	vec3 mask_color = texture(mask_texture, SCREEN_UV).rgb;
	float reveal = dot(mask_color, vec3(0.299, 0.587, 0.114));
	COLOR = vec4(radio_screen.rgb, radio_screen.a * reveal);
}
