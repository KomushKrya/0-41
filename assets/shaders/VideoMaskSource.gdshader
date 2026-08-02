shader_type canvas_item;
render_mode unshaded;

// The VideoStreamPlayer remains active as a dynamic texture source, but its
// pixels are only consumed by RadioLumaMask and must not be drawn directly.
void fragment() {
	COLOR = vec4(0.0);
}
