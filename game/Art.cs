using System.Collections.Generic;
using Godot;

namespace LastTide;

/// <summary>
/// Generated art (Codex image_gen, logged in docs/AI-ASSETS.md) lives under <c>res://assets/art/&lt;set&gt;/&lt;name&gt;.png</c>.
/// <see cref="Tex"/> returns null when a file is absent, so every drawing routine keeps its procedural fallback and the
/// game runs with or without any of the art.
/// </summary>
public static class Art
{
    static readonly Dictionary<string, Texture2D?> cache = new();

    /// <summary>The texture at <c>assets/art/{path}.png</c> (e.g. "ships/sloop"), or null if there is none.</summary>
    public static Texture2D? Tex(string path)
    {
        if (cache.TryGetValue(path, out var t)) return t;
        var res = "res://assets/art/" + path + ".png";
        t = ResourceLoader.Exists(res) ? GD.Load<Texture2D>(res) : null;
        cache[path] = t;
        return t;
    }

    /// <summary>True when the texture exists.</summary>
    public static bool Has(string path) => Tex(path) != null;
}
