shader_type spatial;
render_mode unshaded, cull_disabled;

uniform sampler2D screen_texture : source_color, filter_nearest;
uniform float emission_energy = 0.65;

void fragment() {
	// PCNew.glb reserves only the central U segment for the monitor and rotates it.
	float normalized_u = clamp((UV.x - 0.17662379) / 0.67052755, 0.0, 1.0);
	vec2 screen_uv = vec2(UV.y, 1.0 - normalized_u);
	vec3 screen_color = texture(screen_texture, screen_uv).rgb;
	ALBEDO = screen_color;
	EMISSION = screen_color * emission_energy;
}
