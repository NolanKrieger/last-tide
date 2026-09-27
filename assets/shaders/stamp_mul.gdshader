// A stamp printed INTO the paper rather than pasted on it: multiply blending, so the art's pale ground takes the
// paper's own tone and texture and only its ink and washes darken it (the portolan wind roses).
shader_type canvas_item;
render_mode blend_mul;

void fragment() {
	vec4 t = texture(TEXTURE, UV) * COLOR;
	COLOR = vec4(mix(vec3(1.0), t.rgb * 1.06, t.a), 1.0);
}
