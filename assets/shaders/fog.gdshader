// Unexplored sea is blank parchment (GDD §5); the chart inks itself in as she sails (pillar 4). This quad sits over the
// chart and under the ships. Where the reveal mask is empty it paints the same paper as the sea; at the edge of the
// known world the wash stops in a ragged watercolour tide line, and on the blank side the cartographer's first pencil
// hatching runs a little way out, as if the next piece of the chart were being sketched in.
// On top of the mask it clears a live circle of her sight every frame (`sight`), charted or not: without a cartographer
// nothing is inked, but she still sees the sea around her. The circle is analytic, so it works off the chart's
// rectangle too (uv outside 0..1), where the mask has nothing to say.
shader_type canvas_item;
render_mode unshaded;

uniform sampler2D reveal : filter_linear_mipmap, repeat_disable;
uniform vec2 map_half = vec2(3000.0, 2250.0);   // metres
uniform float px_per_m = 4.0;
uniform int seed = 0;
uniform vec3 sight = vec3(0.0);   // her live sight: centre (m, xy) and radius (m, z); z = 0 hides it

#include "res://assets/shaders/world.gdshaderinc"

varying vec2 wpos;

void vertex() {
	wpos = VERTEX;
}

void fragment() {
	vec2 w = wpos / px_per_m;                      // metres, same frame as the sea shader
	vec2 uv = (w + map_half) / (2.0 * map_half);
	bool inside = uv.x >= 0.0 && uv.x <= 1.0 && uv.y >= 0.0 && uv.y <= 1.0;
	float r = inside ? textureLod(reveal, uv, 0.0).r : 0.0;
	float far = inside ? textureLod(reveal, uv, 3.0).r : 0.0;   // blurred over ~100 m: how close the known edge is
	if (sight.z > 0.0) {
		// the live circle, shaped like the mask's own edge: a cell-wide ramp (12.5 m), and a ~100 m blur for `far`
		float ds = length(w - sight.xy);
		r = max(r, 1.0 - smoothstep(sight.z - 8.0, sight.z + 8.0, ds));
		far = max(far, 1.0 - smoothstep(sight.z - 55.0, sight.z + 55.0, ds));
	}
	if (r > 0.995 && far > 0.99) { discard; }                    // charted and nowhere near an edge

	// a ragged watercolour edge: two scales of noise push the threshold about
	float bleed = (nz(w, 20.0) - 0.5) * 0.34 + (nz2(w, 5.0) - 0.5) * 0.16;
	float inked = smoothstep(0.32, 0.6, r + bleed);
	vec3 col = paper_at(w);
	float px = length(vec2(dFdx(w.x), dFdy(w.x)));

	// pencil hatching on the blank side, near the known edge, in broken runs
	float near_edge = smoothstep(0.03, 0.4, far) * (1.0 - inked);
	if (near_edge > 0.0) {
		// long, light, slightly uneven pencil strokes laid at ~42°, each line wandering a little
		vec2 hd = vec2(0.743, -0.669);
		vec2 hp = vec2(-hd.y, hd.x);
		float t = (dot(w, hd) + (nz2(w, 14.0) - 0.5) * 1.6) / 4.6;
		float lane = floor(t + 0.5);
		float dline = abs(fract(t + 0.5) - 0.5) * 4.6;
		float lw = max(0.05, px * 0.4);
		float hatch = 1.0 - smoothstep(lw, lw + px, dline);
		// each line is drawn in a few long runs: noise along the line, offset per line
		float along = dot(w, hp) / 11.0 + lane * 3.71;
		float runs = smoothstep(0.42, 0.55, texture(noise_tex, vec2(along * 0.06, lane * 0.137)).r);
		col = mix(col, vec3(0.42, 0.40, 0.38), hatch * runs * near_edge * 0.34 * smoothstep(1.6, 0.5, px));
	}

	// the tide line: pigment pooled where the wash stops
	float tide = smoothstep(0.0, 0.45, inked) * (1.0 - smoothstep(0.45, 1.0, inked));
	col = mix(col, col * vec3(0.80, 0.72, 0.60), tide * 0.75);

	// the sea's vignette, so the unexplored paper matches the charted paper at the screen's rim
	vec2 suv = SCREEN_UV - 0.5;
	col = mix(col, col * vec3(0.9, 0.86, 0.8), smoothstep(0.5, 0.98, length(suv * vec2(1.05, 1.0))) * 0.55);
	COLOR = vec4(col, clamp(1.0 - inked + tide * 0.35, 0.0, 1.0));
}
