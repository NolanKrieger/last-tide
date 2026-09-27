// A storm cell as the chart draws it (GDD §9 "moving storm cells drawn as ink swirls"): a dark watercolour wash
// pooling toward the eye wall, a calm eye, and hatched spiral arms winding the way the sim's storm wind turns
// (anticlockwise on the north-up chart), dashed like quick pen work and slowly turning. Drawn on one quad per cell, under the ships.
shader_type canvas_item;

#include "res://assets/shaders/world.gdshaderinc"

uniform vec2 centre = vec2(0.0);   // world px
uniform float radius = 1400.0;     // world px
uniform float strength = 0.8;
uniform float fade = 1.0;
uniform float spin = 0.0;          // radians, accumulated
uniform float px_per_m = 4.0;

varying vec2 wpos;

void vertex() {
	wpos = VERTEX;
}

void fragment() {
	vec2 v = wpos - centre;
	float r = length(v) / radius;              // 0 at the eye, 1 at the rim
	if (r > 1.0) { discard; }
	vec2 w = wpos / px_per_m;
	float px = length(vec2(dFdx(w.x), dFdy(w.x)));
	float ring = smoothstep(0.08, 0.3, r) * (1.0 - smoothstep(0.72, 1.0, r));
	float blot = nz(w, 55.0);

	// the wash: slate, blotchy, darkest around the eye wall
	float wash = strength * fade * (0.30 * ring + 0.12 * (1.0 - smoothstep(0.0, 1.0, r))) * (0.75 + 0.5 * blot);
	vec3 col = vec3(0.24, 0.27, 0.36);
	float alpha = wash;

	// spiral arms: log spirals θ = k·ln(r) + const, clockwise inward, turning slowly
	float ang = atan(v.y, v.x);
	float arms = 9.0;
	float phase = (ang - 1.35 * log(max(r, 0.02)) + spin) * arms / TAU;
	float d = abs(fract(phase) - 0.5) / arms * TAU * r * radius / px_per_m;   // metres to the nearest arm
	float lw = max(0.32 * (0.6 + ring), px * 0.5);
	float hatch = 1.0 - smoothstep(lw, lw + px, d);
	float dash = smoothstep(0.35, 0.55, nz2(w * vec2(1.0, 1.0) + vec2(spin * 30.0, 0.0), 7.0));
	float h = hatch * dash * ring * strength * fade * 0.7;
	col = mix(col, INK, h / max(alpha + h, 1e-3));
	alpha = alpha + h - alpha * h;
	COLOR = vec4(col, clamp(alpha, 0.0, 0.8));
}
