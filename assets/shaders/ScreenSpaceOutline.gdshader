shader_type canvas_item;

uniform vec4 outline_color : source_color = vec4(0.85, 0.95, 0.75, 1.0);
uniform float outline_width = 3.0;

void fragment()
{
	float silhouette = texture(TEXTURE, UV).a;
	float expanded_silhouette = 0.0;

	for (int x = -4; x <= 4; x++)
	{
		for (int y = -4; y <= 4; y++)
		{
			vec2 offset = vec2(float(x), float(y));
			if (length(offset) > outline_width)
			{
				continue;
			}

			expanded_silhouette = max(
				expanded_silhouette,
				texture(TEXTURE, UV + (offset * TEXTURE_PIXEL_SIZE)).a
			);
		}
	}

	float outline = max(expanded_silhouette - silhouette, 0.0);
	COLOR = vec4(outline_color.rgb, outline * outline_color.a);
}
