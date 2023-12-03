@import lib.eps

public enum dir {
    forward, backward,
    left, right,
    up, down
}
public struct align {
    public dir forward;
    public dir left;
    public dir up;
    private align(dir f, dir l, dir u) { this.forward = f; this.left = l;  this.up = u; }
    public static align determine(MatrixD target, MatrixD anchor) {
        return new align(matchDir(target.Forward, anchor), matchDir(target.Left, anchor), matchDir(target.Up, anchor));
    }
}
public static dir matchDir(Vector3D dV, MatrixD anchor) {
    if      ((dV -  anchor.Forward).Length() < dEPS) return dir.forward;
    else if ((dV - anchor.Backward).Length() < dEPS) return dir.backward;
    else if ((dV -     anchor.Left).Length() < dEPS) return dir.left;
    else if ((dV -    anchor.Right).Length() < dEPS) return dir.right;
    else if ((dV -       anchor.Up).Length() < dEPS) return dir.up;
    else if ((dV -     anchor.Down).Length() < dEPS) return dir.down;
    else throw new ArgumentException("Wrong anchor matrix.");
}
public static dir matchDirClosest(Vector3D dV, MatrixD anchor) {
    dir res = dir.forward; double diff = Double.MaxValue; double cDif = 0d;
    cDif = (dV -  anchor.Forward).Length(); if (cDif < diff) { diff = cDif; res = dir.forward; }
    cDif = (dV - anchor.Backward).Length(); if (cDif < diff) { diff = cDif; res = dir.backward; }
    cDif = (dV -     anchor.Left).Length(); if (cDif < diff) { diff = cDif; res = dir.left; }
    cDif = (dV -    anchor.Right).Length(); if (cDif < diff) { diff = cDif; res = dir.right; }
    cDif = (dV -       anchor.Up).Length(); if (cDif < diff) { diff = cDif; res = dir.up; }
    cDif = (dV -     anchor.Down).Length(); if (cDif < diff) { diff = cDif; res = dir.down; }
    return res;
}
