using System;
using NXOpen;

// View-local flags restore independently even if restoring the transform fails.
internal sealed class NxViewState
{
    public readonly View View;
    public readonly Tag Tag;
    private readonly Matrix3x3 _rotation;
    private readonly Point3d _origin;
    private readonly double _scale;
    private readonly bool _wcs, _triad;

    public NxViewState(View view)
    {
        View = view; Tag = view.Tag;
        _rotation = view.Matrix; _origin = view.Origin; _scale = view.Scale;
        _wcs = view.WcsVisibility; _triad = view.TriadVisibility;
    }

    public void Restore() { Restore(View); }

    public void Restore(View liveView)
    {
        if (liveView == null || liveView.Tag != Tag)
            throw new InvalidOperationException("恢复视图的标识与原快照不符。");
        try
        {
            // Avoid writing an unchanged camera back and dirtying its part.
            Matrix3x3 m = liveView.Matrix;
            Point3d p = liveView.Origin;
            if (m.Xx != _rotation.Xx || m.Xy != _rotation.Xy || m.Xz != _rotation.Xz ||
                m.Yx != _rotation.Yx || m.Yy != _rotation.Yy || m.Yz != _rotation.Yz ||
                m.Zx != _rotation.Zx || m.Zy != _rotation.Zy || m.Zz != _rotation.Zz ||
                p.X != _origin.X || p.Y != _origin.Y || p.Z != _origin.Z || liveView.Scale != _scale)
                liveView.SetRotationTranslationScale(_rotation, _origin, _scale);
        }
        finally
        {
            try { if (liveView.WcsVisibility != _wcs) liveView.WcsVisibility = _wcs; }
            finally { if (liveView.TriadVisibility != _triad) liveView.TriadVisibility = _triad; }
        }
    }
}
