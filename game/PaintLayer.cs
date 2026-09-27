using Godot;

namespace LastTide;

/// <summary>
/// A bare drawing layer: a child node with its own z-index whose <see cref="Paint"/> callback draws it.
/// Lets one view put some of its ink under the ships (water-level effects, a kraken beneath the hull)
/// and the rest above them.
/// </summary>
public partial class PaintLayer : Node2D
{
    public Action<PaintLayer>? Paint;

    public override void _Draw() => Paint?.Invoke(this);

    /// <summary>One screen pixel in this layer's local units.</summary>
    public float ScreenPx()
    {
        var xf = GetViewport().GetFinalTransform() * GetGlobalTransformWithCanvas();
        float s = xf.X.Length();
        return s > 1e-4f ? 1f / s : 1f;
    }
}
