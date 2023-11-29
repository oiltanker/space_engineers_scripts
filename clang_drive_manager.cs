public const double dEPS = 0.001d;
public const float EPS = 0.01f;
public const int ENG_UPS = 60;
public const double radToDegMul = 180 / Math.PI;
public static readonly System.Text.RegularExpressions.Regex tagRegex = new System.Text.RegularExpressions.Regex(@"(\s|^)@cdrive(\s|$)");
public const float onVel = 5f;
public const float offVel = -5f;

public static IMyTextSurface myLcd = null;
public static IMyTextPanel debugLcd = null;
public static void wipe() {
    if (debugLcd != null) debugLcd.WriteText("");
    if (myLcd != null) myLcd.WriteText("");
}
public static void print(string str) {
    if (debugLcd != null) debugLcd.WriteText(str + '\n', true);
    if (myLcd != null) myLcd.WriteText(str + '\n', true);
}

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
    private static void release(IMyGyro g) { if (g.IsFunctional) g.GyroOverride = false; }
    private static void reset(IMyGyro g) { if (g.IsFunctional) { g.Roll = 0f; g.Pitch = 0f; g.Yaw = 0f; }; }
    public void add(IMyGyro gyro) {
        var a = align.determine(gyro.WorldMatrix, anchor);
        if (!gMap.ContainsKey(a)) gMap.Add(a, new List<IMyGyro>{ gyro });
        else if (!gMap[a].Contains(gyro)) gMap[a].Add(gyro);
    }
    public void add(IEnumerable<IMyGyro> gyros) { foreach (var g in gyros) add(g); }
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

public int state = 0;
public bool allWorking = false;
public IMyShipController controller = null;
public Dictionary<dir, IMyPistonBase> pMap = null;
public wGyroArr gFirst  = null;
public wGyroArr gSecond = null;


public bool checkGyros() {
    var cRoll = controller.RollIndicator; var cPitch = controller.RotationIndicator.X; var cYaw = controller.RotationIndicator.Y;
    print($"cRoll: {cRoll.ToString("0.000")}   cPitch: {cPitch.ToString("0.000")}   cYaw: {cYaw.ToString("0.000")}");

    if (gFirst.count + gSecond.count < 2) return true;

    Action<wGyroArr, wGyroArr> transferTo = (from, to) => {
        int toTransfer = (from.count - to.count) / 2;
        if (toTransfer > 0) {
            var gs = from.gyros.Take(toTransfer);
            from.remove(gs, false, false);
            to.add(gs);
        } else {
            var g = from.gyros.First();
            from.remove(g);
        }
    };
    if      (gFirst.count  > gSecond.count) transferTo(gFirst,  gSecond);
    else if (gSecond.count > gFirst.count)  transferTo(gSecond, gFirst);

    gFirst.capture();
    gSecond.capture();

    var pRoll  = cRoll  < -EPS ? -60f : 60f; var nRoll  = cRoll  > EPS ? 60f : -60f;
    var pPitch = cPitch < -EPS ? -60f : 60f; var nPitch = cPitch > EPS ? 60f : -60f;
    var pYaw   = cYaw   < -EPS ? -60f : 60f; var nYaw   = cYaw   > EPS ? 60f : -60f;

    gFirst.setRPY (pRoll, pPitch, pYaw);
    gSecond.setRPY(nRoll, nPitch, nYaw);
    return true; // no fail condition
}
public void update() {
    var mat = controller.WorldMatrix;
    var vel = controller.GetShipVelocities().LinearVelocity; // vector
    var mov = controller.MoveIndicator;

    foreach (var p in pMap.Values) p.Enabled = true;

    if (!checkGyros()) {
        foreach (var p in pMap.Values) p.Velocity = -5f;
        return;
    }
    print("applying forces");

    if (Math.Abs(mov.Z) > dEPS) { // override forward-backward
        if (mov.Z > 0d) { pMap[dir.forward ].Velocity = offVel; pMap[dir.backward].Velocity = onVel; }
        if (mov.Z < 0d) { pMap[dir.backward].Velocity = offVel; pMap[dir.forward ].Velocity = onVel; }
    } else if (controller.DampenersOverride && vel.Length() > 0.5d) { // dampening forward-backward
        if (vel.Dot(mat.Forward)  > 0d) { pMap[dir.forward ].Velocity = offVel; pMap[dir.backward].Velocity = onVel; }
        if (vel.Dot(mat.Backward) > 0d) { pMap[dir.backward].Velocity = offVel; pMap[dir.forward ].Velocity = onVel; }
    } else { pMap[dir.forward ].Velocity = offVel; pMap[dir.backward].Velocity = offVel; }
    if (Math.Abs(mov.X) > dEPS) { // override left-right
        if (mov.X > 0d) { pMap[dir.left    ].Velocity = offVel; pMap[dir.right   ].Velocity = onVel; }
        if (mov.X < 0d) { pMap[dir.right   ].Velocity = offVel; pMap[dir.left    ].Velocity = onVel; }
    } else if (controller.DampenersOverride && vel.Length() > 0.5d) { // dampening left-right
        if (vel.Dot(mat.Left)     > 0d) { pMap[dir.left    ].Velocity = offVel; pMap[dir.right   ].Velocity = onVel; }
        if (vel.Dot(mat.Right)    > 0d) { pMap[dir.right   ].Velocity = offVel; pMap[dir.left    ].Velocity = onVel; }
    } else { pMap[dir.left    ].Velocity = offVel; pMap[dir.right   ].Velocity = offVel; }
    if (Math.Abs(mov.Y) > dEPS) { // override up-down
        if (mov.Y < 0d) { pMap[dir.up      ].Velocity = offVel; pMap[dir.down    ].Velocity = onVel; }
        if (mov.Y > 0d) { pMap[dir.down    ].Velocity = offVel; pMap[dir.up      ].Velocity = onVel; }
    } else if (controller.DampenersOverride && vel.Length() > 0.5d) { // dampening up-down
        if (vel.Dot(mat.Up)       > 0d) { pMap[dir.up      ].Velocity = offVel; pMap[dir.down    ].Velocity = onVel; }
        if (vel.Dot(mat.Down)     > 0d) { pMap[dir.down    ].Velocity = offVel; pMap[dir.up      ].Velocity = onVel; }
    } else { pMap[dir.up      ].Velocity = offVel; pMap[dir.down    ].Velocity = offVel; }
}

public void findDebugLcd(List<IMyTerminalBlock> blocks) {
    debugLcd = blocks.FirstOrDefault(b => b is IMyTextPanel && tagRegex.IsMatch(b.CustomName) && b.IsSameConstructAs(Me)) as IMyTextPanel;
    if (debugLcd != null) {
        debugLcd.ContentType = ContentType.TEXT_AND_IMAGE;
        debugLcd.FontSize = 0.8f;
        debugLcd.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
    }
}

public void init() {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    findDebugLcd(blocks);
    wipe();
    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName) && b.CubeGrid == Me.CubeGrid) as IMyShipController;
    if (controller != null) {
        var mat = controller.WorldMatrix;

        try {
            pMap = new Dictionary<dir, IMyPistonBase>();
            blocks.Where(b => b is IMyPistonBase && b.IsSameConstructAs(Me) && b.IsFunctional && tagRegex.IsMatch(b.CustomName))
                  .ToList().ForEach(b => pMap.Add(matchDir(b.WorldMatrix.Down, mat), b as IMyPistonBase));
        } catch (Exception e) {
            print("Wrongly built clang drive.");
        }

        if (gFirst != null) gFirst.release();
        if (gSecond != null) gSecond.release();
        var gyros = blocks.Where(b => b is IMyGyro && b.CubeGrid == Me.CubeGrid && b.IsWorking && tagRegex.IsMatch(b.CustomName)).Select(b => b as IMyGyro).ToList();
        if (gyros.Count > 0) {
            var cnt = gyros.Count % 2 == 0 ? gyros.Count : gyros.Count - 1;
            gFirst  = new wGyroArr(gyros.GetRange(0,       cnt / 2), mat);
            gSecond = new wGyroArr(gyros.GetRange(cnt / 2, cnt / 2), mat);
        }

        blocks.Where(b => b is IMyThrust && b.CubeGrid == controller.CubeGrid && tagRegex.IsMatch(b.CustomName)).ToList().ForEach(b => (b as IMyThrust).Enabled = false);

        if (pMap.Count == 6) allWorking = true;
    } else print("No main controller.");
}

public void shutdown() {
    foreach (var p in pMap.Values) p.Velocity = -5f;
    gFirst.release();
    gSecond.release();

    pMap = null; controller = null; gFirst = null; gSecond = null;
    allWorking = false;
}

public Program() {
    Echo("");
    Me.CustomName = "@cdrive program";
    if (Me.CubeGrid.GridSizeEnum == MyCubeSize.Large) {
        myLcd = Me.GetSurface(0);
        myLcd.ContentType = ContentType.TEXT_AND_IMAGE;
        myLcd.FontSize = 1;
        myLcd.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
    }

    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    if (state == 1) init();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update1) {
        if (state == 1) {
            refreshTick++;
            if (refreshTick >= 200) {
                init();
                refreshTick = 0;
            }
            if (allWorking) {
                wipe();
                update();
            }
            
        }
    } else {
        if (argument == "start") {
            if (!allWorking) init();
            state = 1;
        } else if (argument == "stop" && state > 0) {
            if (allWorking) shutdown();
            state = 0;
        }
    }
}