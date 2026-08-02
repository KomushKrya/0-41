shader_type spatial;
render_mode unshaded, cull_disabled;

uniform sampler2D dossier_texture : source_color, filter_nearest;
uniform float page_offset = 0.0;
uniform float emission_energy = 0.08;

void fragment() {
	vec2 dossier_uv = vec2(UV.x * 0.5 + page_offset, UV.y);
	vec4 page = texture(dossier_texture, dossier_uv);
	// Until the SubViewport has produced its first UI frame, keep the paper readable.
	float has_page_pixels = step(0.01, max(max(page.r, page.g), page.b));
	vec3 paper = vec3(0.72, 0.65, 0.48);
	vec3 page_color = mix(paper, page.rgb, has_page_pixels);
	ALBEDO = page_color;
	EMISSION = page_color * emission_energy;
	ALPHA = 1.0;
}
