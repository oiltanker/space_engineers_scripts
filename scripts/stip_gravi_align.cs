/*
 * programming block must be located in the same grid as tagged cockpit
 * @galign - tag for script block components, when on:
 *  - any cockpit - takes gravity and up vectors to align the whole vehicle
 *  - any LCD     - outputs debug information for troubleshooting
 * @galign-slave - tag for a slave cockpits, used for yaw control (can be located in sub-grids)
 */

@import lib.eps
@import lib.printFull
@import lib.gyroArray
@import lib.pid

public const double radToDegMul = 180 / Math.PI;

public static string prettyV3(Vector3D v) {
    return "< " + v.X.ToString("0.000") + ", " + v.Y.ToString("0.000") + ", " + v.Z.ToString("0.000");
}

public static pidCtrl newDefaultPid() => new pidCtrl(5d, 0.1d, 1d, 1d / ENG_UPS, 0.95d);
public class gyroCtrl {
    public pidCtrl ctrlRoll;
    public pidCtrl ctrlPitch;
    public pidCtrl ctrlYaw;
    public IMyShipController controller;
    List<IMyShipController> slaveControllers;
    public Dictionary<IMyCubeGrid, wGyroArr> gArrs;
    public Vector3D yawVec;
    private int appendGMap(IEnumerable<IMyGyro> gyros) {
        var updates = 0;
        foreach (var gs in gyros.GroupBy(g => g.CubeGrid)) {
            var cg = gs.First().CubeGrid;
            if (!gArrs.ContainsKey(cg)) {
                gArrs.Add(cg, new wGyroArr(gs));
                updates += gArrs[cg].count;
            } else updates += gArrs[cg].add(gs);
        }
        return updates;
    }
    public gyroCtrl(IMyShipController _controller, List<IMyShipController> _slaveControllers, IEnumerable<IMyGyro> gyros) {
        controller = _controller;
        slaveControllers = _slaveControllers;
        ctrlRoll  = newDefaultPid();
        ctrlPitch = newDefaultPid();
        ctrlYaw   = newDefaultPid();
        gArrs = new Dictionary<IMyCubeGrid, wGyroArr>();
        appendGMap(gyros);
        yawVec = new Vector3D(0d, 0d, 0d);
    }
    public void stop() { foreach (var ga in gArrs.Values) ga.release(); }
    public double getControlYaw() => slaveControllers.Concat(new[] { controller }).Select(c => c.RotationIndicator.Y).MaxBy(y => Math.Abs(y));
    private double getPlaneAngle(Vector3D vAct, Vector3D vBase, Vector3D normal) {
        var pvAct  = Vector3D.ProjectOnPlane(ref vAct,  ref normal);
        var pvBase = Vector3D.ProjectOnPlane(ref vBase, ref normal);
        var cwMul = Vector3D.Dot(pvAct.Cross(pvBase), normal);
        cwMul = Math.Abs(cwMul) > dEPS ? Math.Sign(cwMul) : 0d;
        var res = cwMul * Vector3D.Angle(pvAct, pvBase);
        return Double.IsNaN(res)  ? 0 : res * radToDegMul; 
    }
    public void update(double delta) {
        var cMat = controller.WorldMatrix;
        var ctMat = MatrixD.Transpose(cMat);
        var cuVec = Vector3D.Normalize(cMat.Up);
        var cfVec = Vector3D.Normalize(cMat.Forward);
        var clVec = Vector3D.Normalize(cMat.Left);
        var cgVec = -Vector3D.Normalize(controller.GetTotalGravity());

        var lcgVec = Vector3D.TransformNormal(cgVec, ctMat);
        var cRoll  = Math.Sign(-lcgVec.X) * Math.Acos(lcgVec.Y / Math.Sqrt(lcgVec.X*lcgVec.X + lcgVec.Y*lcgVec.Y)) * radToDegMul * 2;
        var cPitch = Math.Sign(-lcgVec.Z) * Math.Acos(lcgVec.Y / Math.Sqrt(lcgVec.Y*lcgVec.Y + lcgVec.Z*lcgVec.Z)) * radToDegMul * 2;

        if (yawVec.Length() < dEPS) yawVec = new Vector3D(clVec);
        var lyVec = Vector3D.TransformNormal(yawVec, ctMat);
        lyVec.Y = 0;
        lyVec = Vector3D.Normalize(lyVec);
        var dYaw = getControlYaw();
        if (Math.Abs(dYaw) > dEPS) {
            dYaw = dYaw * 0.0001d;
            lyVec.X = lyVec.X * Math.Cos(dYaw) - lyVec.Z * Math.Sin(dYaw);
            lyVec.Z = lyVec.X * Math.Sin(dYaw) + lyVec.Z * Math.Cos(dYaw);
            yawVec = Vector3D.Normalize(Vector3D.TransformNormal(lyVec, cMat));
        } else yawVec = Vector3D.Normalize(Vector3D.ProjectOnPlane(ref yawVec, ref cuVec));
        var cYaw = Math.Sign(lyVec.Z) * Math.Acos(-lyVec.X / Math.Sqrt(lyVec.X*lyVec.X + lyVec.Z*lyVec.Z)) * radToDegMul * 2;
        print($"yawVec: {prettyV3(yawVec)}    lyVec: {prettyV3(lyVec)}");
        print($"cRoll: {cRoll.ToString("0.000")}    cPitch: {cPitch.ToString("0.000")}    cYaw: {cYaw.ToString("0.000")}");
        cRoll  = cRoll  * Math.Min(Math.Abs(cRoll)  / 5d, 1d);
        cPitch = cPitch * Math.Min(Math.Abs(cPitch) / 5d, 1d);
        cYaw   = cYaw   * Math.Min(Math.Abs(cYaw)   / 5d, 1d);
        cRoll  =  ctrlRoll.control(-cRoll,  delta);
        cPitch = ctrlPitch.control( cPitch, delta);
        cYaw   =   ctrlYaw.control(-cYaw,   delta);
        print($"PIDroll: {cRoll.ToString("0.000")}    PIDpitch: {cPitch.ToString("0.000")}    PIDyaw: {cYaw.ToString("0.000")}");

        foreach (var cgc in gArrs) {
            print("---- ----");
            var cg = cgc.Key; var gc = cgc.Value;
            var mat = cg.WorldMatrix;
            var fVec = mat.Forward; var lVec = mat.Left; var uVec = mat.Up;

            var roll  = cRoll * Vector3D.Dot(cfVec, fVec) + cPitch * Vector3D.Dot(clVec, fVec) + cYaw * Vector3D.Dot(cuVec, fVec); // \
            var pitch = cRoll * Vector3D.Dot(cfVec, lVec) + cPitch * Vector3D.Dot(clVec, lVec) + cYaw * Vector3D.Dot(cuVec, lVec); //  } conflicting sign between grids, some upside-down and rotated
            var yaw   = cRoll * Vector3D.Dot(cfVec, uVec) + cPitch * Vector3D.Dot(clVec, uVec) + cYaw * Vector3D.Dot(cuVec, uVec); // /
             print($"roll: {roll.ToString("0.000")}    pitch: {pitch.ToString("0.000")}    yaw: {yaw.ToString("0.000")}");

            gc.capture();
            gc.setRPY((float) roll, (float) pitch, (float) yaw);
        }
    }
    public void updateGyros(IEnumerable<IMyGyro> gyros) {
        ctrlRoll.reset();
        ctrlPitch.reset();
        ctrlYaw.reset();
    }
}

public IEnumerable<IMyGyro> getConnectedGyros(List<IMyTerminalBlock> blocks, bool includeMechSubgrids = false) {
    if (includeMechSubgrids) return blocks.Where(b => b is IMyGyro && b.IsSameConstructAs(controller)).Cast<IMyGyro>();
    else return blocks.Where(b => b is IMyGyro && b.CubeGrid == controller.CubeGrid).Cast<IMyGyro>();
}

public int state = 0;
public IMyShipController controller = null;
public List<IMyShipController> slaveControllers = null;
public gyroCtrl gCtrl = null;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@galign(\s|$)");
public static readonly @Regex slaveTagRegex = new @Regex(@"(\s|^)@galign-slave(\s|$)");
public void collectSlaveControllers(List<IMyTerminalBlock> blocks) {
    slaveControllers = blocks.Where(b => b is IMyShipController && b.IsSameConstructAs(controller) && slaveTagRegex.IsMatch(b.CustomName)).Select(b => b as IMyShipController).ToList();
}

public void init() {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName) && b.CubeGrid == Me.CubeGrid) as IMyShipController;
    if (controller != null) {
        collectSlaveControllers(blocks);
        findDebugLcd(blocks, tagRegex);
        gCtrl = new gyroCtrl(controller, slaveControllers, getConnectedGyros(blocks, true));
    }
}

public void stateUpdate() {
    if (controller == null || controller.Closed) {
        init();
        return;
    }

    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    collectSlaveControllers(blocks);
    findDebugLcd(blocks, tagRegex);
    gCtrl.updateGyros(getConnectedGyros(blocks, true));
}

public Program() {
    Echo("");
    Me.CustomName = "@galign program";
    initMeLcd();
    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    if (state == 1) init();
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update1) {
        if (state == 1) {
            refreshTick++;
            if (refreshTick >= 200) {
                stateUpdate();
                refreshTick = 0;
            }
            if (gCtrl != null) {
                wipe();
                gCtrl.update(Runtime.TimeSinceLastRun.TotalSeconds);
            }
            
        }
    } else {
        if (argument == "start" && state != 1) {
            init();
            state = 1;
        } else if (argument == "stop" && state > 0) {
            if (gCtrl != null) gCtrl.stop();
            state = 0;
            gCtrl = null;
            controller = null;
        }
    }
}
