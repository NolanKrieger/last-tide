// Shared by the sea and fog shaders: the sim's value noise (mirrors LastTide.Sim.Noise.Value3 bit for bit on the
// lattice), cheap integer hashes for decoration, and the paper the whole chart is drawn on.

const vec3 PAPER = vec3(0.933, 0.886, 0.792);
const vec3 SHADE = vec3(0.855, 0.792, 0.667);
const vec3 INK = vec3(0.16, 0.13, 0.10);
const vec3 SEPIA = vec3(0.30, 0.22, 0.14);
const vec3 RUST = vec3(0.62, 0.40, 0.20);

uniform sampler2D paper_tex : filter_linear_mipmap, repeat_enable;
uniform sampler2D noise_tex : filter_linear_mipmap, repeat_enable;   // four smooth tileable noises (4/8/16/32 per tile)

// Cheap decorative noise in [0, 1] with features about `s` metres across (one texture fetch; channel picks the octave).
float nz(vec2 w, float s) { return texture(noise_tex, w / (4.0 * s)).r; }
float nz2(vec2 w, float s) { return texture(noise_tex, w / (8.0 * s) + vec2(0.37, 0.61)).g; }
float nz3(vec2 w, float s) { return texture(noise_tex, w / (16.0 * s) + vec2(0.13, 0.29)).b; }

uint ihash(ivec3 p, int s) {
	uint h = uint(p.x) * 0x8DA6B343u + uint(p.y) * 0xD8163841u + uint(p.z) * 0xCB1AB31Fu + uint(s) * 0x9E3779B1u;
	h ^= h >> 13u;
	h *= 0x5BD1E995u;
	h ^= h >> 15u;
	return h;
}

float lattice(ivec3 p, int s) {
	return float(ihash(p, s) & 0xFFFFFFu) / 16777216.0 * 2.0 - 1.0;
}

// Mirrors LastTide.Sim.Noise.Value3: seeded value noise, quintic fade, in [-1, 1].
float vnoise(vec3 p, int s) {
	vec3 fl = floor(p);
	ivec3 i = ivec3(fl);
	vec3 f = p - fl;
	vec3 u = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
	float c000 = lattice(i, s), c100 = lattice(i + ivec3(1, 0, 0), s);
	float c010 = lattice(i + ivec3(0, 1, 0), s), c110 = lattice(i + ivec3(1, 1, 0), s);
	float c001 = lattice(i + ivec3(0, 0, 1), s), c101 = lattice(i + ivec3(1, 0, 1), s);
	float c011 = lattice(i + ivec3(0, 1, 1), s), c111 = lattice(i + ivec3(1, 1, 1), s);
	float x00 = mix(c000, c100, u.x), x10 = mix(c010, c110, u.x);
	float x01 = mix(c001, c101, u.x), x11 = mix(c011, c111, u.x);
	return mix(mix(x00, x10, u.y), mix(x01, x11, u.y), u.z);
}

// Cheap 2D value noise for decoration only (not a sim mirror), in [0, 1].
float dnoise(vec2 p, int s) { return vnoise(vec3(p, 0.5), s) * 0.5 + 0.5; }

// PCG3D (Jarzynski & Olano 2020): three good uniform hashes from integer cell coordinates.
uvec3 pcg3(uvec3 v) {
	v = v * 1664525u + 1013904223u;
	v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
	v ^= v >> 16u;
	v.x += v.y * v.z; v.y += v.z * v.x; v.z += v.x * v.y;
	return v;
}
vec3 hash3(vec2 cell, float salt) {
	uvec3 h = pcg3(uvec3(uint(int(cell.x) + 65536), uint(int(cell.y) + 65536), uint(int(salt))));
	return vec3(h) * (1.0 / 4294967295.0);
}

// The sheet: rag-paper tone, fibres and a little foxing, world-locked (256 m tile blended with a rotated 613 m tile,
// so the repeat never shows). Returns the paper colour at world point w (metres).
vec3 paper_at(vec2 w) {
	vec4 a = texture(paper_tex, w / 256.0);
	vec2 wr = vec2(w.x * 0.799 - w.y * 0.602, w.x * 0.602 + w.y * 0.799);
	vec4 b = texture(paper_tex, wr / 613.0 + vec2(0.37, 0.11));
	float tone = (a.r - 0.5) * 1.2 + (b.r - 0.5) * 0.9;
	float fibre = (a.g - 0.5) * 2.0;
	float grain = (a.a - 0.5) * 2.0;
	float foxmask = smoothstep(0.62, 0.9, nz(w, 700.0));
	float fox = a.b * foxmask * 0.8;
	vec3 col = mix(PAPER, SHADE, clamp(0.28 - tone * 0.55, 0.0, 1.0));
	col *= 1.0 + grain * 0.022;
	col *= 1.0 + (fibre < 0.0 ? fibre * 0.09 : fibre * 0.035);
	col = mix(col, col * vec3(0.88, 0.8, 0.68), fox * 0.45);
	return col;
}
