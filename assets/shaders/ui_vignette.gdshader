// Menu backdrops: a warm ink wash over whatever lies behind, heavier toward the edges like an old chart's
// darkened margins. With reach 2 and edge 0 it is instead a soft ellipse of colour that fades to nothing at the rect's
// edges (paper pooled behind the title's menu).
shader_type canvas_item;
uniform vec4 tint : source_color = vec4(0.16, 0.13, 0.10, 1.0);
uniform float centre = 0.25;   // strength at the centre
uniform float edge = 0.7;      // strength at the rim
uniform float power = 1.8;
uniform float reach = 1.5;     // 2.0 = the ellipse just touches the rect's sides
void fragment() {
	vec2 d = (UV - 0.5) * vec2(1.0, 0.9);
	float r = clamp(length(d) * reach, 0.0, 1.0);
	float a = mix(centre, edge, smoothstep(0.0, 1.0, pow(r, power)));
	COLOR = vec4(tint.rgb, a * tint.a);
}
