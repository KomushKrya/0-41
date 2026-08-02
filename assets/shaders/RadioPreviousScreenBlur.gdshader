shader_type canvas_item;
render_mode unshaded;

uniform sampler2D screen_texture : hint_screen_texture, repeat_disable, filter_linear;
uniform sampler2D mask_texture : repeat_disable, filter_linear;
uniform float blur_radius_pixels = 7.0;

void fragment() {
	vec2 offset = SCREEN_PIXEL_SIZE * blur_radius_pixels;
	vec4 blurred = texture(screen_texture, SCREEN_UV) * 0.227027;
	blurred += texture(screen_texture, SCREEN_UV + vec2(offset.x, 0.0)) * 0.158971;
	blurred += texture(screen_texture, SCREEN_UV - vec2(offset.x, 0.0)) * 0.158971;
	blurred += texture(screen_texture, SCREEN_UV + vec2(0.0, offset.y)) * 0.158971;
	blurred += texture(screen_texture, SCREEN_UV - vec2(0.0, offset.y)) * 0.158971;
	blurred += texture(screen_texture, SCREEN_UV + offset) * 0.034;
	blurred += texture(screen_texture, SCREEN_UV - offset) * 0.034;
	blurred += texture(screen_texture, SCREEN_UV + vec2(offset.x, -offset.y)) * 0.034;
	blurred += texture(screen_texture, SCREEN_UV + vec2(-offset.x, offset.y)) * 0.034;

	float reveal = dot(texture(mask_texture, SCREEN_UV).rgb, vec3(0.299, 0.587, 0.114));
	// Black mask = blurred previous screen; white mask = transparent overlay.
	COLOR = vec4(blurred.rgb, 1.0 - reveal);
}
