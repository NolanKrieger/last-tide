// Atlas sprites drawn through a MultiMesh (one draw call per atlas): each instance carries its atlas region in
// INSTANCE_CUSTOM (uv offset xy, uv size zw) and its tint and fade in the instance colour.
shader_type canvas_item;

void vertex() {
	UV = INSTANCE_CUSTOM.xy + UV * INSTANCE_CUSTOM.zw;
}

void fragment() {
	COLOR = texture(TEXTURE, UV) * COLOR;
}
