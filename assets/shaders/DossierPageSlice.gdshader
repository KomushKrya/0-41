shader_type spatial;
render_mode unshaded, cull_disabled;

uniform sampler2D dossier_texture : source_color, filter_nearest;
uniform float emission_energy = 0.08;

void fragment() {
	vec4 page = texture(dossier_texture, UV);
	// Until the SubViewport has produced its first UI frame, keep the paper readable.
	float has_page_pixels = step(0.01, max(max(page.r, page.g), page.b));
	vec3 paper = vec3(0.72, 0.65, 0.48);
	vec3 page_color = mix(paper, page.rgb, has_page_pixels);
	ALBEDO = page_color;
	EMISSION = page_color * emission_energy;
	ALPHA = 1.0;
}
