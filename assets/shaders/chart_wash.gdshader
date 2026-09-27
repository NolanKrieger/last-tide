// Chart screen: the sea the player has charted takes a pale hand-tinted wash; the pigment pools darker along the edge
// of the charted water, like watercolour drying. Blank parchment stays blank.
shader_type canvas_item;
uniform sampler2D reveal : filter_linear, repeat_disable;
uniform vec4 wash : source_color = vec4(0.42, 0.56, 0.64, 1.0);
uniform vec2 texel = vec2(0.002, 0.0027);
void fragment() {
	float r = texture(reveal, UV).r;
	float blur = 0.0;
	for (int i = 0; i < 8; i++) {
		float a = float(i) * 0.785398;
		blur += texture(reveal, UV + vec2(cos(a), sin(a)) * texel * 3.0).r;
	}
	blur /= 8.0;
	float rim = clamp((r - blur) * 3.0, 0.0, 1.0);
	float speck = fract(sin(dot(floor(UV / texel), vec2(12.9898, 78.233))) * 43758.5453);
	float a = r * (0.13 + 0.03 * speck) + rim * 0.32;
	COLOR = vec4(wash.rgb, a);
}
