shader_type canvas_item;

// Портрет погибшего сотрудника: обесцвеченное фото и траурная лента наискось
// через правый нижний угол. Чёрно-белый снимок и лента — единственный признак
// гибели, который виден прямо в папке: о травмах игрок узнаёт только с терминала.
uniform float amount : hint_range(0.0, 1.0) = 1.0;

// Ширина рамки, делённая на высоту. Без этого лента шла бы не под 45°:
// UV нормализованы к прямоугольнику, а рамка портрета выше, чем шире.
uniform float aspect = 1.0;

uniform float ribbon_offset : hint_range(0.0, 1.0) = 0.12;
uniform float ribbon_width : hint_range(0.0, 0.5) = 0.16;
uniform vec4 ribbon_color : source_color = vec4(0.04, 0.04, 0.045, 1.0);

// Кромка вдоль ленты. Без неё чёрная полоса пропадает на тёмном участке
// снимка — здесь она легла на пиджак и была не видна вовсе.
uniform vec4 ribbon_edge_color : source_color = vec4(0.62, 0.60, 0.55, 1.0);
uniform float ribbon_edge_width : hint_range(0.0, 0.05) = 0.02;

/// Полоса между двумя границами с мягкими краями.
float Band(float value, float from, float to, float feather) {
	return smoothstep(from - feather, from + feather, value)
		* (1.0 - smoothstep(to - feather, to + feather, value));
}

void fragment() {
	vec4 source = texture(TEXTURE, UV);
	float gray = dot(source.rgb, vec3(0.299, 0.587, 0.114));
	vec3 photo = mix(source.rgb, vec3(gray), amount);

	// Расстояние до правого нижнего угла, посчитанное в долях высоты.
	float corner = (1.0 - UV.x) * aspect + (1.0 - UV.y);
	float feather = 0.004;
	float ribbon = Band(corner, ribbon_offset, ribbon_offset + ribbon_width, feather);

	vec3 result = mix(photo, ribbon_color.rgb, ribbon * ribbon_color.a);

	// Две тонкие светлые линии по краям полосы: они и делают ленту заметной.
	float inner = Band(corner, ribbon_offset - ribbon_edge_width, ribbon_offset, feather);
	float outer = Band(
		corner,
		ribbon_offset + ribbon_width,
		ribbon_offset + ribbon_width + ribbon_edge_width,
		feather);
	result = mix(result, ribbon_edge_color.rgb, max(inner, outer) * ribbon_edge_color.a);

	COLOR = vec4(result, source.a);
}
