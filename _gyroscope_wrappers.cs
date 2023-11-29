public const double dEPS = 0.001d;

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

public static float chooseRPY(align align, dir dir, float roll, float pitch, float yaw) { // possibly faster than multiplying by dot products
    switch (dir) {
        case dir.forward:
            if (align.up == dir.forward || align.up == dir.backward) return -roll;
            else                                                     return  roll;
        case dir.backward:
            if (align.up == dir.forward || align.up == dir.backward) return  roll;
            else                                                     return -roll;
        case dir.left:
            if (align.up == dir.left    || align.up == dir.right)    return -pitch;
            else                                                     return  pitch;
        case dir.right:
            if (align.up == dir.left    || align.up == dir.right)    return  pitch;
            else                                                     return -pitch;
        case dir.up:
            if (align.up == dir.up      || align.up == dir.down)     return  yaw;
            else                                                     return -yaw;
        case dir.down:
            if (align.up == dir.up      || align.up == dir.down)     return -yaw;
            else                                                     return  yaw;
        default: return Single.NaN;
    }
}
public class wGyro {
    public IMyGyro gyro;
    public align alignment;
    public bool isWorking { get { return gyro.IsWorking; } }
    public bool isFunctional { get { return gyro.IsFunctional; } }
    public wGyro(IMyGyro gyro, MatrixD? anchor = null) {
        this.gyro = gyro;
        alignment = align.determine(gyro.WorldMatrix, anchor != null ? anchor.Value : gyro.CubeGrid.WorldMatrix);
    }
    public wGyro(IMyGyro gyro, IMyTerminalBlock anchor = null): this(gyro, anchor?.WorldMatrix) {}
    public void reset() { gyro.Roll = 0f; gyro.Pitch = 0f; gyro.Yaw = 0f; }
    public void capture() => gyro.GyroOverride = true;
    public void release(bool doReset = true) {
        gyro.GyroOverride = false;
        if (doReset) reset();
    }
    public void setRPY(float roll, float pitch, float yaw) {
        gyro.Roll  = chooseRPY(alignment, alignment.forward, roll, pitch, yaw);
        gyro.Pitch = chooseRPY(alignment, alignment.left   , roll, pitch, yaw);
        gyro.Yaw   = chooseRPY(alignment, alignment.up     , roll, pitch, yaw);
    }
}
public class wGyroArr {
    public MatrixD anchor;
    public Dictionary<align, List<IMyGyro>> gMap;
    public int count { get {
        var res = 0;
        foreach (var gs in gMap.Values) res += gs.Count(g => g.IsWorking);
        return res;
    } }
    public IEnumerable<IMyGyro> gyros { get {
        return gMap.Values.Cast<IEnumerable<IMyGyro>>().Aggregate((acc, next) => acc.Concat(next));
    } }
    public wGyroArr(IEnumerable<IMyGyro> gyros, MatrixD? anchor = null) {
        this.anchor = anchor != null ? anchor.Value : gyros.FirstOrDefault().CubeGrid.WorldMatrix;
        gMap = new Dictionary<align, List<IMyGyro>>();
        add(gyros);
    }
    public wGyroArr(IEnumerable<IMyGyro> gyros, IMyTerminalBlock anchor = null): this(gyros, anchor?.WorldMatrix) {}
    public void add(IMyGyro gyro) {
        var a = align.determine(gyro.WorldMatrix, anchor);
        if (!gMap.ContainsKey(a)) gMap.Add(a, new List<IMyGyro>{ gyro });
        else if (!gMap[a].Contains(gyro)) gMap[a].Add(gyro);
    }
    public void add(IEnumerable<IMyGyro> gyros) { foreach (var g in gyros) add(g); }
    public void remove(IMyGyro gyro) {
        foreach (var gs in gMap.Values) if (gs.Contains(gyro)) {
            gs.Remove(gyro);
            break;
        }
    }
    public void remove(IEnumerable<IMyGyro> gyros) { foreach (var g in gyros) remove(g); }
    public void clean() {
        foreach (var a in gMap.Keys.ToList()) {
            for (var i = 0; i < gMap[a].Count; i++) if (!gMap[a][i].IsWorking) { gMap[a].RemoveAt(i); i--; }
            if (gMap[a].Count <= 0) gMap.Remove(a);
        }
    }
    public void reset() { foreach (var gs in gMap.Values) gs.ForEach(g => { if (g.IsFunctional) { g.Roll = 0f; g.Pitch = 0f; g.Yaw = 0f; }; }); }
    public void capture() { foreach (var gs in gMap.Values) gs.ForEach(g => { if (g.IsFunctional) g.GyroOverride = true; }); }
    public void release(bool doReset = true) {
        foreach (var gs in gMap.Values) gs.ForEach(g => { if (g.IsFunctional) g.GyroOverride = false; });
        if (doReset) reset();
    }
    public void setRPY(float roll, float pitch, float yaw) { // setting values & cleaning array
        foreach (var a in gMap.Keys.ToList()) {
            var gRoll  = chooseRPY(a, a.forward, roll, pitch, yaw);
            var gPitch = chooseRPY(a, a.left   , roll, pitch, yaw);
            var gYaw   = chooseRPY(a, a.up     , roll, pitch, yaw);
            for (var i = 0; i < gMap[a].Count; i++) {
                var g = gMap[a][i];
                if (g.IsWorking) { g.Roll = gRoll; g.Pitch = gPitch; g.Yaw = gYaw; }
                else { gMap[a].RemoveAt(i); i--; }
            }
            if (gMap[a].Count <= 0) gMap.Remove(a);
        }
    }
}
