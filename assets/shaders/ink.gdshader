// Hand-inked strokes batched by game/InkMesh.cs: UV.x = the stroke's half-width, UV.y = signed distance across it
// (both in world px); UV.x < -5 marks a round dot (UV + (10, 0) = offset / radius). The edge is anti-aliased over one screen pixel at any zoom; a stroke thinner than a pixel keeps
// its coverage (it greys out) instead of fattening; the paper's tooth breaks the ink a little.
shader_type canvas_item;

varying vec2 wp;

void vertex() {
	wp = VERTEX;
}

float h12(vec2 p) {
	uvec2 v = uvec2(ivec2(floor(p)) + 32768);
	v = v * 1664525u + 1013904223u;
	v.x += v.y * 1664525u; v.y += v.x * 1664525u;
	v ^= v >> 16u;
	v.x += v.y * 1664525u;
	return float(v.x) * (1.0 / 4294967295.0);
}

void fragment() {
	if (UV.x < -5.0) {
		// a round pen dot: UV = local offset / radius, shifted by -10 on x
		vec2 q = UV - vec2(-10.0, 0.0);
		float pxd = max(length(vec2(dFdx(q.x), dFdy(q.x))), 1e-4);
		COLOR.a *= clamp((1.0 - length(q)) / pxd + 0.5, 0.0, 1.0);
	} else {
	float across = UV.y;
	float px = max(length(vec2(dFdx(across), dFdy(across))), 1e-4);
	float hw = UV.x;
	float cover;
	if (hw * 2.0 >= px) {
		cover = clamp((hw - abs(across)) / px + 0.5, 0.0, 1.0);
	} else {
		cover = clamp((px * 0.5 - abs(across)) / px + 0.5, 0.0, 1.0) * (hw * 2.0 / px);
	}
	// the paper's tooth: a faint, world-locked speckle where the pen skipped
	float tooth = h12(wp * 0.7);
	cover *= 0.9 + 0.1 * smoothstep(0.08, 0.3, tooth);
	COLOR.a *= cover;
	}
}
