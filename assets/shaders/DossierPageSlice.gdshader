shader_type spatial;
render_mode unshaded, cull_disabled;

uniform sampler2D dossier_texture : source_color, filter_nearest;
uniform float emission_energy = 0.08;
// Разворот целиком лежит в одном вьюпорте: левая страница берёт его левую
// половину, правая — правую. Одна текстура на обе половины, а не два вьюпорта,
// потому что курсор в режиме отправки должен ездить по развороту непрерывно.
uniform vec2 uv_offset = vec2(0.0, 0.0);
uniform vec2 uv_scale = vec2(1.0, 1.0);

void fragment() {
	vec4 page = texture(dossier_texture, uv_offset + UV * uv_scale);
	// Until the SubViewport has produced its first UI frame, keep the paper readable.
	float has_page_pixels = step(0.01, max(max(page.r, page.g), page.b));
	vec3 paper = vec3(0.72, 0.65, 0.48);
	vec3 page_color = mix(paper, page.rgb, has_page_pixels);
	ALBEDO = page_color;
	EMISSION = page_color * emission_energy;
	ALPHA = 1.0;
}
