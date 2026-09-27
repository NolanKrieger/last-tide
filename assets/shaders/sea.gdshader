// The sea IS the chart (GDD §15): rag paper, portolan rhumb lines from the chart's wind roses, hand-inked wave strokes
// that ride the wind the sim is actually blowing (SeaLayer samples World.WindAt on a world-locked grid every frame, so
// gust patches, doldrums and storm swirls are the sim's own: the water shows the wind, pillar 1), darker deep water,
// a hand-tinted watercolour halo along every coast, dotted shallows, an animated surf line, and the islands'
// watercolour land with contour rings (the chart chunks ink the coastlines, stamps and names on top).
// World-locked; one full-screen pass with no draw calls of its own.
shader_type canvas_item;
render_mode unshaded;

#include "res://assets/shaders/world.gdshaderinc"

uniform vec2 cam_pos = vec2(0.0);             // metres at the screen centre (+y south)
uniform vec2 view_world = vec2(800.0, 450.0); // metres visible across and down the screen
uniform vec2 screen_px = vec2(1600.0, 900.0);
uniform vec2 map_half = vec2(3000.0, 2250.0);
uniform float time = 0.0;          // render clock (s): stroke lives, surf
uniform vec2 ship_wind_dir = vec2(1.0, 0.0);
uniform float ship_wind_speed = 8.0;
uniform vec2 drift = vec2(0.0);    // accumulated downwind drift of the strokes (m)
uniform sampler2D sdf : filter_linear, repeat_disable;            // signed coast distance (m), 6.25 m texels
uniform sampler2D water_tint : filter_linear, repeat_disable;     // blurred regional water colour
uniform sampler2D land_tint : filter_linear, repeat_disable;      // blurred regional land colour
uniform sampler2D reveal : filter_linear, repeat_disable;         // the chart's ink (skip work under blank paper)
uniform sampler2D wind_grid : filter_linear, repeat_enable;       // World.WindAt: rg = direction, b = speed/30, a = gust
uniform sampler2D cond_grid : filter_linear, repeat_enable;       // r = doldrums calm, g = fog, b = storm, a = ash
uniform float grid_step = 30.0;         // metres per cell; world cell (x, y) sits in texel (x mod W, y mod H)
uniform vec2 grid_texels = vec2(48.0, 28.0);
uniform bool has_field = false;    // false until the coast field is bound (plain sea)
uniform vec2 hubs[9];              // portolan wind-rose centres (m)
uniform int hub_count = 0;
uniform int region_deep = 2;       // RegionType.Deep, darker water

vec2 map_uv(vec2 w) { return (w + map_half) / (2.0 * map_half); }
vec2 grid_uv(vec2 w) { return (w / grid_step + 0.5) / grid_texels; }

// ---- Hand-inked strokes ----

// One quill stroke lying along `ax`, centred at the origin of `d` (metres). s runs tail (-1) → head (+1) downwind.
// Width swells in the middle and the head is heavier than the hairline tail; `drawn` (0..1) is how far the pen got.
float quill(vec2 d, vec2 ax, float len, float amp, float bend, float phase, float width, float drawn, float px) {
	float x = dot(d, ax);
	if (abs(x) > len + px) { return 0.0; }
	float y = dot(d, vec2(-ax.y, ax.x));
	float s = clamp(x / len, -1.0, 1.0);
	float yc = amp * sin(s * PI * bend + phase);
	float slope = amp * PI * bend / len * cos(s * PI * bend + phase);
	float dist = abs(y - yc) * inversesqrt(1.0 + slope * slope);
	float taper = sqrt(max(0.0, 1.0 - s * s)) * (0.62 + 0.38 * (s * 0.5 + 0.5));
	float hw = max(width * taper, px * 0.35);
	float cov = 1.0 - smoothstep(hw - px * 0.5, hw + px * 0.5, dist);
	float pen = 1.0 - smoothstep(drawn * 2.0 - 1.05, drawn * 2.0 - 0.95, s);
	return cov * pen * smoothstep(1.0 + px / len, 0.9, abs(s));
}

// A layer of wave strokes on a world-aligned grid: static in (w - drift) space so they drift downwind without the grid
// ever rotating; each cell's stroke is born (the pen draws it tail to head), lingers, fades, then is reborn elsewhere.
// Strokes stay within 0.45 of a cell of their centre (jittered 0.3–0.7), so only the 4 nearest cells can reach a
// pixel: a 2×2 search. Two hashes a cell, no trig for the stroke's heading (a small-angle nudge off the wind).
float wave_layer(vec2 w, vec2 dir, float cell, float len, float amp, float width, float density, float period, float salt, float px) {
	vec2 q = w - drift;
	vec2 id0 = floor(q / cell - 0.5);
	vec2 perp = vec2(-dir.y, dir.x);
	float cov = 0.0;
	float lmax = cell * 0.45;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id = id0 + vec2(float(i), float(j));
			vec3 h0 = hash3(id, salt);
			float per = period * (0.75 + 0.5 * h0.x);
			float ph = time / per + h0.y;
			float cyc = floor(ph);
			float life = ph - cyc;
			if (fract(h0.z * 97.31 + cyc * 0.6180339) > density) { continue; }
			vec3 h1 = hash3(id + vec2(cyc * 37.0, cyc * 11.0), salt + 1.0);
			vec2 centre = (id + 0.3 + 0.4 * h1.xy) * cell;
			vec2 ax = normalize(dir + perp * ((h1.z - 0.5) * 0.35));
			float r2 = fract(h1.x * 13.7 + h1.y * 7.3);
			float l = min(len * (0.65 + 0.7 * r2), lmax);
			float drawn = smoothstep(0.0, 0.22, life);
			float fade = 1.0 - smoothstep(0.62, 1.0, life);
			float c = quill(q - centre, ax, l, amp * (0.6 + 0.7 * h1.z), 0.55 + 0.5 * h0.x, (h0.z - 0.5) * 1.2, width, drawn, px);
			cov = max(cov, c * fade * (0.65 + 0.35 * r2));
		}
	}
	return cov;
}

// Curling crests (whitecaps): short arches bowed downwind that swell up and vanish; only in strong wind.
float crest_layer(vec2 w, vec2 dir, float cell, float density, float px) {
	vec2 q = w - drift * 1.3;
	vec2 id0 = floor(q / cell - 0.5);
	vec2 ay = vec2(-dir.y, dir.x);
	float cov = 0.0;
	for (int j = 0; j <= 1; j++) {
		for (int i = 0; i <= 1; i++) {
			vec2 id = id0 + vec2(float(i), float(j));
			vec3 h0 = hash3(id, 91.0);
			float ph = time / (2.2 + 1.5 * h0.x) + h0.y;
			float cyc = floor(ph);
			float life = ph - cyc;
			vec3 h1 = hash3(id + vec2(cyc * 13.0, cyc * 7.0), 92.0);
			if (h1.z > density) { continue; }
			vec2 d = q - (id + 0.3 + 0.4 * h1.xy) * cell;
			float x = dot(d, ay), y = dot(d, dir);
			float len = 1.6 + 1.4 * h1.x;
			float s = x / len;
			if (abs(s) > 1.1) { continue; }
			float yc = 0.55 * len * (1.0 - s * s) - 0.35 * len;
			float hw = max(0.13 * (1.0 - s * s * 0.7), px * 0.35);
			float a = 1.0 - smoothstep(hw - px * 0.5, hw + px * 0.5, abs(y - yc));
			cov = max(cov, a * smoothstep(1.05, 0.8, abs(s)) * sin(life * PI));
		}
	}
	return cov;
}

// ---- Portolan rhumb lines: 32 winds from each rose (8 winds in ink, 8 half-winds green, 16 quarter-winds red) ----
vec4 rhumbs(vec2 w, float px) {
	vec4 acc = vec4(0.0);
	for (int i = 0; i < 9; i++) {
		if (i >= hub_count) { break; }
		vec2 v = w - hubs[i];
		float r = length(v);
		if (r < 55.0 || r > 4200.0) { continue; }
		float ang = atan(v.y, v.x);
		float stp = TAU / 32.0;
		float k = floor(ang / stp + 0.5);
		float dist = r * abs(sin(ang - k * stp));
		int ki = int(mod(k, 32.0));
		bool major = ki % 4 == 0;
		bool halfw = ki % 4 == 2;
		float wid = px * (major ? 0.75 : 0.45);
		float a = 1.0 - smoothstep(wid, wid + px, dist);
		a *= smoothstep(55.0, 120.0, r) * (1.0 - smoothstep(2400.0, 4200.0, r));
		vec3 col = major ? INK : (halfw ? vec3(0.22, 0.42, 0.26) : vec3(0.62, 0.20, 0.14));
		float strength = major ? 0.13 : 0.09;
		if (a * strength > acc.a) { acc = vec4(col, a * strength); }
	}
	return acc;
}

// ---- The chart's edge: a strong neatline, a thin graduated scale band (alternate bars every 100 m, like a chart's
// latitude border) and a fine inner rule; a blank margin beyond ----
vec4 neatline(vec2 w, float px) {
	vec2 e = abs(w) - map_half;                // > 0 outside the chart
	float outside = max(e.x, e.y);
	if (outside > 0.0) { return vec4(SHADE * 0.97, 1.0); }
	float inner = -outside;                    // metres inside the edge
	float lw = max(px * 0.6, 0.35);
	float a = 1.0 - smoothstep(lw * 2.2, lw * 2.2 + px, inner);             // the neatline
	a = max(a, 1.0 - smoothstep(lw, lw + px, abs(inner - 7.0)));             // the band's inner edge
	a = max(a, (1.0 - smoothstep(lw * 0.7, lw * 0.7 + px, abs(inner - 12.5))) * 0.6);   // a fine rule inside it
	if (inner > 2.0 && inner < 7.0) {
		float along = e.x > e.y ? w.y : w.x;   // alternate filled bars every 100 m along the edge
		a = max(a, step(0.5, fract(along / 200.0)) * 0.8);
	}
	return vec4(INK, a * 0.85);
}

void fragment() {
	vec2 w = cam_pos + (SCREEN_UV - 0.5) * view_world;
	float px = view_world.x / screen_px.x;     // metres per screen pixel

	vec3 col = paper_at(w);
	float d = 1000.0;
	float inked = 1.0;
	vec3 wt = vec3(0.55, 0.66, 0.66);
	int region = 0;
	if (has_field) {
		vec2 uv = map_uv(w);
		d = texture(sdf, uv).r;
		wt = texture(water_tint, uv).rgb;
		inked = texture(reveal, uv).r;
	}

	if (inked < 0.02) {
		// blank paper under the fog: nothing to draw (the fog layer paints it)
	} else if (d < 0.0) {
		// ---- Land: a hand-tinted watercolour wash that pools darker at the shore, with contour rings inland ----
		float inland = -d;
		vec3 lt = texture(land_tint, map_uv(w)).rgb;
		vec4 n = texture(noise_tex, w / 160.0);
		float green = smoothstep(0.4, 0.8, n.r) * smoothstep(8.0, 30.0, inland);
		vec3 tint = mix(lt, lt * vec3(0.78, 0.92, 0.68), green * 0.6);
		tint *= 1.0 - 0.1 * ((n.g - 0.5) * 0.6 + (n.b - 0.5) * 0.4);
		col *= mix(vec3(1.0), tint * 1.12, 0.92);
		// pooled pigment and the dry "tide line" at the wash's edge
		col *= 1.0 - 0.16 * exp(-inland / 2.6) - 0.06 * exp(-inland / 9.0);
		float tide = exp(-pow((inland - 1.2 - n.a * 0.8) / 0.6, 2.0));
		col = mix(col, col * vec3(0.78, 0.72, 0.62), tide * 0.35);
		// contour rings: iso-lines of the inland distance, wobbling like a hand-drawn ring, broken like a dry pen
		float wob = (nz2(w, 9.0) - 0.5) * 2.2;
		float spacing = 13.0 + inland * 0.18;
		float ring = abs(fract((inland - 9.0 + wob) / spacing) - 0.5) * spacing;
		float rw = max(0.12, px * 0.45);
		float rings = (1.0 - smoothstep(rw, rw + px, ring)) * smoothstep(6.0, 12.0, inland);
		rings *= 0.55 + 0.45 * smoothstep(0.3, 0.7, nz(w, 6.0));
		col = mix(col, SEPIA, rings * 0.28);
	} else {
		// ---- Sea: the sim's wind at this spot ----
		vec2 guv = grid_uv(w);
		vec4 wg = texture(wind_grid, guv);
		vec4 cg = texture(cond_grid, guv);
		vec2 dir = wg.rg * 2.0 - 1.0;
		dir = dot(dir, dir) > 1e-4 ? normalize(dir) : ship_wind_dir;
		float spd = wg.b * 30.0;
		float gust = wg.a;
		float calm = cg.r;
		float strength = smoothstep(1.5, 14.0, spd);
		float near = smoothstep(4.0, 28.0, d);     // strokes thin out in the lee of the land
		float deep = smoothstep(90.0, 320.0, d);

		// regional water tint (faint everywhere), deeper and cooler offshore, gust patches darken the water
		col = mix(col, col * wt * 1.18, 0.10 + 0.13 * deep);
		col *= 1.0 - deep * 0.06;
		col = mix(col, col * vec3(0.93, 0.93, 0.95), gust * 0.4 * strength);
		col = mix(col, col * vec3(1.03, 1.03, 1.0), calm * 0.5);

		// portolan rhumb lines, strongest on open water
		vec4 rh = rhumbs(w, px);
		col = mix(col, rh.rgb, rh.a * (0.5 + 0.5 * smoothstep(20.0, 120.0, d)));

		// wave strokes: denser, shorter, busier and quicker with the wind; two interleaved ripple layers
		float ripple_fade = 1.0 - smoothstep(0.45, 1.05, px);
		float dens = mix(0.07, 0.9, strength) * (0.8 + 0.45 * gust) * (1.0 - 0.8 * calm) * near;
		float rl = mix(5.4, 3.8, strength);
		float ramp = mix(0.45, 1.05, strength);
		float rwid = mix(0.26, 0.34, strength);
		float rper = mix(9.0, 4.2, strength);
		float ripple = 0.0;
		if (ripple_fade > 0.0) {
			ripple = wave_layer(w, dir, 12.0, rl, ramp, rwid, dens, rper, 0.0, px) * ripple_fade;
		}
		float swell = wave_layer(w, dir, 52.0, mix(23.0, 16.0, strength), mix(2.2, 3.4, strength), mix(0.30, 0.42, strength) + px * 0.25,
			mix(0.22, 0.6, strength) * near * (1.0 - 0.6 * calm), mix(14.0, 7.0, strength), 3.0, px);
		float crest = 0.0;
		if (strength > 0.6 && ripple_fade > 0.0) {
			crest = crest_layer(w, dir, 19.0, smoothstep(0.6, 0.95, strength) * 0.6 * near, px) * ripple_fade;
		}
		vec3 wave_ink = mix(SEPIA, INK, 0.5 + 0.5 * strength);
		col = mix(col, wave_ink, ripple * (0.55 + 0.15 * strength) + swell * (0.22 + 0.12 * (1.0 - ripple_fade)) + crest * 0.55);

		// ---- The coast: watercolour halo, dotted shallows, surf ----
		if (has_field && d < 70.0) {
			float edge_n = nz(w, 16.0);
			float halo = exp(-d / (10.0 + 8.0 * edge_n)) * 0.75 + exp(-d / 34.0) * 0.28;
			vec3 tint = mix(wt, vec3(0.40, 0.60, 0.62), 0.3);
			col = mix(col, col * tint * 1.22, clamp(halo, 0.0, 1.0) * 0.72);
			col *= 1.0 - 0.1 * exp(-d / 1.6);

			// dotted shallows: stipple on a jittered 2.4 m grid, thinning offshore
			vec2 gid = floor(w / 2.4);
			vec3 hs = hash3(gid, 51.0);
			vec2 dot_c = (gid + 0.2 + 0.6 * hs.xy) * 2.4;
			float dens_s = smoothstep(34.0, 7.0, d) * smoothstep(2.0, 5.0, d) * 0.75;
			float dot_r = max(0.14 + 0.08 * hs.x, px * 0.6);
			float dotv = (hs.z < dens_s) ? 1.0 - smoothstep(dot_r - px * 0.5, dot_r + px * 0.5, distance(w, dot_c)) : 0.0;
			col = mix(col, SEPIA, dotv * 0.5 * smoothstep(1.4, 0.6, px));

			// surf: a thin line washing in toward the shore and fading, broken into dashes
			float sp = time * 0.28 + nz(w, 70.0) * 1.7;
			float cyc = fract(sp);
			float surf_d = mix(8.0, 1.6, cyc);
			float sw = max(0.09, px * 0.4);
			float surf = 1.0 - smoothstep(sw, sw + px, abs(d - surf_d));
			surf *= sin(cyc * PI) * smoothstep(0.3, 0.6, nz2(w + vec2(floor(sp) * 37.1, 0.0), 4.0));
			col = mix(col, SEPIA, surf * 0.45 * smoothstep(2.0, 0.8, px));
			// a steady waterline a couple of metres off the coast (the double coastline of the old charts)
			float wlw = max(0.08, px * 0.35);
			float wl = 1.0 - smoothstep(wlw, wlw + px, abs(d - 2.6 - (edge_n - 0.5) * 0.8));
			col = mix(col, SEPIA, wl * 0.22);
		}
	}

	vec4 nl = neatline(w, px);
	col = mix(col, nl.rgb, nl.a);

	// a soft vignette so the view reads as a page
	vec2 uv = SCREEN_UV - 0.5;
	float rim = smoothstep(0.5, 0.98, length(uv * vec2(1.05, 1.0)));
	col = mix(col, col * vec3(0.9, 0.86, 0.8), rim * 0.55);
	COLOR = vec4(col, 1.0);
}
