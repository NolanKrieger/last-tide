// Night, dusk and dawn, the lantern's pool, fog, rain, ash, lightning and the drowning edge, as one wash over the world
// (the HUD sits above it). Everything that belongs to a place is world-locked: mist banks, rain and ash come from the
// sim's own fields (SeaLayer's grid of World.ConditionsAt), so what you see is where the weather is. Readability first:
// the lantern's pool always clears the water she can see, and the washes stay translucent over the ships.
shader_type canvas_item;
render_mode unshaded;

#include "res://assets/shaders/world.gdshaderinc"

uniform vec2 cam_pos = vec2(0.0);             // metres at the screen centre
uniform vec2 view_world = vec2(800.0, 450.0); // metres across and down the screen
uniform vec2 screen_px = vec2(1600.0, 900.0); // real pixels
uniform float time = 0.0;
uniform float night = 0.0;        // 0 day … 1 full night
uniform float dusk = 0.0;         // 0..1 the low-sun tint
uniform float dawn = 0.0;         // 1 = morning light (rose, from the east), 0 = evening (amber, from the west)
uniform vec2 pool_centre = vec2(800.0, 450.0);   // real pixels
uniform float pool_radius = 300.0;               // real pixels
uniform float lantern = 1.0;
uniform float vision_px = 400.0;  // her sight in real pixels
uniform float fog_here = 0.0;     // at the ship (used when there is no grid)
uniform float rain_here = 0.0;
uniform float ash_here = 0.0;
uniform float founder = 0.0;      // 0..1 the sea closing in as she goes down
uniform float flash = 0.0;        // lightning, 0..1
uniform vec2 wind_dir = vec2(1.0, 0.0);
uniform vec2 drift = vec2(0.0);   // accumulated drift of the wind (m): mist and ash ride it
uniform sampler2D cond_grid : filter_linear, repeat_enable;    // r doldrums, g fog, b storm, a ash (wrapped, see SeaLayer)
uniform float grid_step = 30.0;
uniform vec2 grid_texels = vec2(32.0, 20.0);
uniform bool has_grid = false;
uniform sampler2D sdf : filter_linear, repeat_disable;         // coast distance (stars glint only on water)
uniform vec2 map_half = vec2(3000.0, 2250.0);
uniform bool has_sdf = false;

// "over" compositing into (col, alpha)
void over(inout vec3 col, inout float alpha, vec3 c, float a) {
	if (a <= 0.0) { return; }
	col = mix(col, c, a / max(a + alpha - a * alpha, 1e-4));
	alpha = a + alpha - a * alpha;
}

// Slanting rain: short ink strokes in lanes, falling fast along the wind; one layer.
float rain_layer(vec2 p, vec2 sd, float lane_w, float speed, float seg, float salt, float density) {
	vec2 sp = vec2(-sd.y, sd.x);
	float u = dot(p, sd) - time * speed;
	float v = dot(p, sp);
	float lane = floor(v / lane_w);
	vec3 hl = hash3(vec2(lane, salt), 301.0);
	float s = seg * (0.8 + 0.4 * hl.x);
	float uu = u + hl.y * s;
	float id = floor(uu / s);
	vec3 hs = hash3(vec2(lane, id), 302.0 + salt);
	if (hs.z > density) { return 0.0; }
	float len = s * (0.12 + 0.12 * hs.x);
	float x = uu - id * s - hs.y * (s - len);
	float cx = (lane + 0.5 + (hs.x - 0.5) * 0.5) * lane_w;
	float across = abs(v - cx);
	float along = step(0.0, x) * step(x, len) * sin(clamp(x / len, 0.0, 1.0) * PI);
	return (1.0 - smoothstep(0.55, 1.35, across)) * along;
}

void fragment() {
	vec2 px = SCREEN_UV * screen_px;                            // real pixels, top-left origin
	vec2 w = cam_pos + (SCREEN_UV - 0.5) * view_world;          // metres
	float mpp = view_world.x / screen_px.x;                     // metres per real pixel
	vec3 col = vec3(0.0);
	float alpha = 0.0;
	vec4 cg = has_grid ? texture(cond_grid, (w / grid_step + 0.5) / grid_texels) : vec4(0.0, fog_here, rain_here, ash_here);
	float d_ship = distance(px, pool_centre);

	// Dusk and dawn: a low-sun wash, warmer toward the side the sun is on (west at dusk, east at dawn).
	if (dusk > 0.0) {
		float side = dawn > 0.5 ? SCREEN_UV.x : 1.0 - SCREEN_UV.x;
		vec3 warm = dawn > 0.5 ? vec3(0.96, 0.62, 0.56) : vec3(0.90, 0.54, 0.24);
		over(col, alpha, warm, dusk * (0.12 + 0.17 * side) * (0.85 + 0.3 * nz(w, 80.0)));
	}

	// Night: a blotchy indigo watercolour that deepens toward the limit of her sight; the lamp throws a warm,
	// gently flickering pool around her; stars glint on the open water.
	if (night > 0.0) {
		float flicker = 1.0 + (0.025 * sin(time * 13.0) + 0.015 * sin(time * 7.3 + 1.0)) * lantern;
		float gr = pool_radius * flicker;
		float glow = lantern * (1.0 - smoothstep(gr * 0.12, gr, d_ship));
		float sight = smoothstep(vision_px * 0.3, vision_px * 1.05, d_ship);
		vec3 indigo = vec3(0.10, 0.12, 0.34) * (0.9 + 0.2 * nz(w, 70.0));
		float a = night * (0.44 + 0.3 * sight - 0.28 * glow) * (0.92 + 0.16 * nz2(w, 30.0));
		vec3 lit = mix(indigo, vec3(0.98, 0.74, 0.40), glow * 0.8);
		over(col, alpha, lit, clamp(a, 0.0, 0.85));
		float water = has_sdf ? smoothstep(3.0, 12.0, texture(sdf, (w + map_half) / (2.0 * map_half)).r) : 1.0;
		vec2 gid = floor(w / 9.0);
		vec3 h = hash3(gid, 211.0);
		if (h.z < 0.16) {
			vec2 c = (gid + 0.2 + 0.6 * h.xy) * 9.0;
			float r = distance(w, c) / mpp;                       // in pixels: glints stay pixel-sized at any zoom
			float tw = 0.5 + 0.5 * sin(time * (1.3 + 2.0 * h.x) + h.y * 6.28);
			float g = (1.0 - smoothstep(0.4, 1.4, r)) * tw * (1.0 - glow) * water;
			over(col, alpha, vec3(0.95, 0.94, 0.84), g * night * 0.75);
		}
	}

	// Fog: world-locked mist banks where the sim's fog is, drifting with the wind. Within her sight it is only a soft
	// haze (ships she can see stay plain), beyond it the mist closes in thick, as the sim's vision does.
	float fog = smoothstep(0.0, 0.55, cg.g);
	if (fog > 0.01) {
		float m1 = nz(w - drift * 0.5, 110.0);
		float m2 = nz2(w - drift * 0.8 + vec2(time * 0.6, time * 0.2), 45.0);
		float mist = 0.55 * m1 + 0.45 * m2;
		float beyond = smoothstep(vision_px * 0.8, vision_px * 1.12, d_ship);
		float a = fog * mix(0.14 + 0.22 * mist, 0.62 + 0.3 * mist, beyond);
		over(col, alpha, vec3(0.86, 0.86, 0.84) * (0.98 + 0.04 * m2), min(a, 0.92));
	}

	// Rain: in the storm, a slate wash and slanting strokes driven along the wind.
	float storm = cg.b;
	if (storm > 0.02) {
		over(col, alpha, vec3(0.26, 0.30, 0.40), storm * 0.22 * (0.8 + 0.4 * nz(w, 40.0)));
		vec2 sd = normalize(wind_dir * 0.8 + vec2(0.0, 0.45));
		float streak = max(rain_layer(px, sd, 7.0, 1150.0, 150.0, 0.0, 0.62), rain_layer(px + vec2(3.0, 0.0), sd, 11.0, 800.0, 200.0, 5.0, 0.5) * 0.65);
		over(col, alpha, vec3(0.18, 0.21, 0.30), streak * storm * 0.7);
	}

	// Ash: grey-brown haze and drifting flakes, a few embers still glowing.
	float ash = cg.a;
	if (ash > 0.01) {
		over(col, alpha, vec3(0.42, 0.38, 0.34), ash * 0.16 * (0.8 + 0.4 * nz(w, 60.0)));
		vec2 q = w - drift * 0.9 - vec2(0.0, time * 1.2);
		float cell = max(4.5, mpp * 9.0);            // flakes thin out as the view widens, so they never turn to grime
		vec2 gid = floor(q / cell);
		vec3 h = hash3(gid, 231.0);
		if (h.z < 0.2 * ash) {
			vec2 c = (gid + 0.2 + 0.6 * h.xy) * cell + vec2(sin(time * 1.3 + h.x * 9.0), cos(time * 1.1 + h.y * 7.0)) * 0.4;
			float r = distance(q, c) / mpp;
			float size = 1.3 + 2.2 * h.x;
			float f = 1.0 - smoothstep(size * 0.5, size, r);
			bool ember = h.y < 0.07;
			vec3 fc = ember ? vec3(1.0, 0.55, 0.2) : vec3(0.28, 0.25, 0.23);
			float fa = ember ? 0.85 * (0.6 + 0.4 * sin(time * 9.0 + h.x * 20.0)) : 0.5;
			over(col, alpha, fc, f * fa * min(1.0, ash * 1.5));
		}
	}

	// Lightning: the whole sea goes pale for an instant.
	if (flash > 0.0) {
		over(col, alpha, vec3(0.95, 0.96, 1.0), flash * 0.42);
	}

	// Foundering: the edges of the world drown in indigo as the water rises.
	if (founder > 0.0) {
		vec2 n = (px - screen_px * 0.5) / (screen_px * 0.5);
		float edge = smoothstep(0.25, 1.15, length(n) + (nz(w, 30.0) - 0.5) * 0.25);
		over(col, alpha, vec3(0.08, 0.10, 0.20), founder * (0.15 + 0.65 * edge));
	}
	COLOR = vec4(col, alpha);
}
