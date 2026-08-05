shader_type canvas_item;

// Портрет погибшего сотрудника. Чёрно-белое фото — единственный признак гибели,
// который виден прямо в папке: о травмах игрок узнаёт только с терминала.
uniform float amount : hint_range(0.0, 1.0) = 1.0;

void fragment() {
	vec4 source = texture(TEXTURE, UV);
	float gray = dot(source.rgb, vec3(0.299, 0.587, 0.114));
	COLOR = vec4(mix(source.rgb, vec3(gray), amount), source.a);
}
