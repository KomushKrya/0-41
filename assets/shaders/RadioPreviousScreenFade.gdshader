shader_type canvas_item;
render_mode unshaded;

uniform sampler2D mask_texture : repeat_disable, filter_linear;
uniform float darkness : hint_range(0.0, 1.0) = 0.72;

void fragment() {
	vec3 mask_color = texture(mask_texture, SCREEN_UV).rgb;
	float reveal = dot(mask_color, vec3(0.299, 0.587, 0.114));
	// The old screen darkens immediately, then clears wherever the new screen opens.
	COLOR = vec4(0.0, 0.0, 0.0, darkness * (1.0 - reveal));
}
