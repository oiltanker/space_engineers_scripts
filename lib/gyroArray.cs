@import lib.alignment
@import lib.gyroRPY

public class wGyroArr {
    private Func<MatrixD> getAnchor;
    public MatrixD anchor { get { return getAnchor(); } }
    public Dictionary<align, List<IMyGyro>> gMap;
    public int count { get {
        var res = 0;
        foreach (var gs in gMap.Values) res += gs.Count(g => g.IsWorking);
        return res;
    } }
    public IEnumerable<IMyGyro> gyros { get {
        return gMap.Values.Cast<IEnumerable<IMyGyro>>().Aggregate((acc, next) => acc.Concat(next));
    } }
    private void init(IEnumerable<IMyGyro> gyros, Func<MatrixD> aGetter = null) {
        gMap = new Dictionary<align, List<IMyGyro>>();
        if (aGetter == null) {
            getAnchor = () => gyros.First().CubeGrid.WorldMatrix;
            add(gyros);
            getAnchor = () => this.gyros.First().CubeGrid.WorldMatrix;
        } else {
            getAnchor = aGetter;
            add(gyros);
        }
    }
    public wGyroArr(IEnumerable<IMyGyro> gyros) { init(gyros); }
    public wGyroArr(IEnumerable<IMyGyro> gyros, IMyTerminalBlock anchor) {
        if (anchor == null) throw new ArgumentNullException("Argument 'anchor' cannot be null");
        else init(gyros, () => anchor.CubeGrid.WorldMatrix);
    }
    private static void release(IMyGyro g) { if (g.IsFunctional) g.GyroOverride = false; }
    private static void reset(IMyGyro g) { if (g.IsFunctional) { g.Roll = 0f; g.Pitch = 0f; g.Yaw = 0f; }; }
    public bool add(IMyGyro gyro) {
        var a = align.determine(gyro.WorldMatrix, anchor);
        if (!gMap.ContainsKey(a)) {
            gMap.Add(a, new List<IMyGyro>{ gyro });
            return true;
        } else if (!gMap[a].Contains(gyro)) {
            gMap[a].Add(gyro);
            return true;
        }
        return false;
    }
    public int add(IEnumerable<IMyGyro> gyros) => gyros.Select(g => add(g)).Count(r => r);
    public void remove(IMyGyro gyro, bool doRelease = true, bool doReset = true) {
        foreach (var gs in gMap.Values) if (gs.Contains(gyro)) {
            gs.Remove(gyro);
            if (doReset) reset(gyro);
            if (doRelease) release(gyro);
            break;
        }
    }
    public void remove(IEnumerable<IMyGyro> gyros, bool doRelease = true, bool doReset = true) { foreach (var g in gyros) remove(g, doRelease, doReset); }
    public void clean(bool doRelease = true, bool doReset = true) {
        foreach (var a in gMap.Keys.ToList()) {
            for (var i = 0; i < gMap[a].Count; i++) {
                var g = gMap[a][i];
                if (!g.IsWorking) {
                    gMap[a].RemoveAt(i);
                    if (doReset) reset(g);
                    if (doRelease) release(g);
                    i--;
                }
            }
            if (gMap[a].Count <= 0) gMap.Remove(a);
        }
    }
    public void reset() { foreach (var gs in gMap.Values) gs.ForEach(g => reset(g)); }
    public void capture() { foreach (var gs in gMap.Values) gs.ForEach(g => { if (g.IsFunctional) g.GyroOverride = true; }); }
    public void release(bool doReset = true) {
        if (doReset) foreach (var gs in gMap.Values) gs.ForEach(g => { reset(g); release(g); }); 
        else foreach (var gs in gMap.Values) gs.ForEach(g => release(g)); 
    }
    public void setRPY(float roll, float pitch, float yaw) { // setting values & cleaning array
        foreach (var a in gMap.Keys.ToList()) {
            var gRoll  = chooseRPY(a, a.forward, roll, pitch, yaw);
            var gPitch = chooseRPY(a, a.left   , roll, pitch, yaw);
            var gYaw   = chooseRPY(a, a.up     , roll, pitch, yaw);
            for (var i = 0; i < gMap[a].Count; i++) {
                var g = gMap[a][i];
                if (g.IsWorking) { g.Roll = gRoll; g.Pitch = gPitch; g.Yaw = gYaw; }
                else {
                    gMap[a].RemoveAt(i);
                    reset(g); release(g);
                    i--;
                }
            }
            if (gMap[a].Count <= 0) gMap.Remove(a);
        }
    }
}