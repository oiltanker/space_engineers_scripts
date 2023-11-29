/*
 * programming block must be located in the same grid as tagged cockpit
 * @galign - tag for script block components, when on:
 *  - any cockpit - takes gravity and up vectors to align the whole vehicle
 *  - any LCD     - outputs debug information for troubleshooting
 * @galign-slave - tag for a slave cockpits, used for yaw control (can be located in sub-grids)
 */
public const double dEPS = 0.001d;
public const float EPS = 0.01f;
public const int ENG_UPS = 60;
public const double radToDegMul = 180 / Math.PI;

public static IMyTextPanel debugLcd = null;
public static void wipe() { if (debugLcd != null) debugLcd.WriteText(""); }
public static void print(string str) { if (debugLcd != null) debugLcd.WriteText(str + '\n', true); }

public static string prettyV3(Vector3D v) {
    return "< " + v.X.ToString("0.000") + ", " + v.Y.ToString("0.000") + ", " + v.Z.ToString("0.000");
}
public static string prettyMD(MatrixD m) {
    return
        "| " + m.M11.ToString("0.000") + ", " + m.M12.ToString("0.000") + ", " + m.M13.ToString("0.000") + ", " + m.M14.ToString("0.000") + "\n" +
        "|  " + m.M21.ToString("0.000") + ", " + m.M22.ToString("0.000") + ", " + m.M23.ToString("0.000") + ", " + m.M24.ToString("0.000") + "\n" +
        "|  " + m.M31.ToString("0.000") + ", " + m.M32.ToString("0.000") + ", " + m.M33.ToString("0.000") + ", " + m.M34.ToString("0.000") + "\n" +
        "| " + m.M41.ToString("0.000") + ", " + m.M42.ToString("0.000") + ", " + m.M43.ToString("0.000") + ", " + m.M44.ToString("0.000");
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
        return new align(matchDir(target.Backward, anchor), matchDir(target.Left, anchor), matchDir(target.Up, anchor));
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

public class pidCtrl {
    // component constants
    public double constP;
    public double constI;
    public double constD;
    public double integralFalloff;
    // PID variables
    double timeStep;
    double invTimeStep;
    double errorSum;
    double lastError;
    bool firstRun;
    public pidCtrl(double constP = 1d, double constI = 0.25d, double constD = 0.1d, double timeStep = 1d / ENG_UPS, double integralFalloff = 0.95d) {
        this.constP = constP;
        this.constI = constI;
        this.constD = constD;
        setTimeStep(timeStep);
        this.integralFalloff = integralFalloff;
        reset();
    }
    public void setTimeStep(double timeStep) {
        if (timeStep != this.timeStep) {
            this.timeStep = timeStep;
            invTimeStep = 1d / this.timeStep;
        }
    }
    public void reset() {
        errorSum = 0d;
        lastError = 0d;
        firstRun = true;
    }
    public double getIntegral(double currentError) => errorSum * integralFalloff + currentError * timeStep; // error for integral component
    public double control(double error) { // for assumed constant time
        double errorDerivative = (error - lastError) * invTimeStep;
        if (firstRun) {
            errorDerivative = 0d;
            firstRun = false;
        }
        errorSum = getIntegral(error);
        lastError = error;
        return constP * error + constI * errorSum + constD * errorDerivative;
    }
    public double control(double error, double timeStep) { // for exact time
        setTimeStep(timeStep);
        return control(error);
    }
}
public static pidCtrl newDefaultPid() => new pidCtrl(5d, 1d, 2.5d, 1d / ENG_UPS, 0.95d);
public class gyroArray {
    public class gridCtrl {
        public pidCtrl ctrlRoll;
        public pidCtrl ctrlPitch;
        public pidCtrl ctrlYaw;
        public IMyCubeGrid grid;
        public gridCtrl(IMyCubeGrid grid) {
            this.grid = grid;
            ctrlRoll  = newDefaultPid();
            ctrlPitch = newDefaultPid();
            ctrlYaw   = newDefaultPid();
        }
    }
    public IMyShipController controller;
    List<IMyShipController> slaveControllers;
    public List<IMyGyro> gyros;
    public Dictionary<gridCtrl, Dictionary<align, List<IMyGyro>>> gMap;
    public Vector3D yawVec;
    private int appendGMap(Dictionary<IMyCubeGrid, List<IMyGyro>> _gyros) {
        var updates = 0;
        foreach (var cgs in _gyros) {
            var cKey = gMap.Keys.FirstOrDefault(c => c.grid == cgs.Key);
            Dictionary<align, List<IMyGyro>> aMap = null;
            if (cKey != null) aMap = gMap[cKey];
            else {
                aMap = new Dictionary<align, List<IMyGyro>>();
                gMap.Add(new gridCtrl(cgs.Key), aMap);
            }

            cgs.Value.ForEach(g => {
                if (aMap.Values.Any(gs => gs.Contains(g))) return;
                var alignment = align.determine(g.WorldMatrix, cgs.Key.WorldMatrix);
                if (aMap.ContainsKey(alignment)) aMap[alignment].Add(g);
                else aMap.Add(alignment, new List<IMyGyro>{ g });
                gyros.Add(g);
                updates++;
            });
        }
        return updates;
    }
    public gyroArray(IMyShipController _controller, List<IMyShipController> _slaveControllers, Dictionary<IMyCubeGrid, List<IMyGyro>> _gyros) {
        controller = _controller;
        slaveControllers = _slaveControllers;
        gyros = new List<IMyGyro>();
        gMap = new Dictionary<gridCtrl, Dictionary<align, List<IMyGyro>>>();
        appendGMap(_gyros);
        yawVec = new Vector3D(0d, 0d, 0d);
    }
    public void stop() => gyros.ForEach(g => {
        // g.Roll = 0f; g.Pitch = 0f; g.Yaw = 0f;
        g.GyroOverride = false;
    });
    private static double chooseRPY(dir dir, double roll, double pitch, double yaw) { // possibly faster that multiplying by dot products
        switch (dir) {
            case dir.forward:  return roll;
            case dir.backward: return -roll;
            case dir.left:     return pitch;
            case dir.right:    return -pitch;
            case dir.up:       return yaw;
            case dir.down:     return -yaw;
            default:           return Double.NaN;
        }
    }
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
        // var cfVec = Vector3D.Normalize(cMat.Forward);
        var clVec = Vector3D.Normalize(cMat.Left);
        var cgVec = -Vector3D.Normalize(controller.GetTotalGravity());

        // var lcgVec = Vector3D.TransformNormal(cgVec, ctMat);
        // var cRoll  = Math.Sign(-lcgVec.X) * Math.Acos(lcgVec.Y / Math.Sqrt(lcgVec.X*lcgVec.X + lcgVec.Y*lcgVec.Y)) * radToDegMul;
        // var cPitch = Math.Sign(-lcgVec.Z) * Math.Acos(lcgVec.Y / Math.Sqrt(lcgVec.Y*lcgVec.Y + lcgVec.Z*lcgVec.Z)) * radToDegMul;

        if (yawVec.Length() < dEPS) yawVec = new Vector3D(clVec);
        // var lyVec = Vector3D.TransformNormal(yawVec, ctMat);
        // lyVec.Y = 0;
        // lyVec = Vector3D.Normalize(lyVec);
        var dYaw = getControlYaw();
        if (Math.Abs(dYaw) > dEPS) {
            // dYaw = dYaw * 0.0001d;
            // lyVec.X = lyVec.X * Math.Cos(dYaw) - lyVec.Z * Math.Sin(dYaw);
            // lyVec.Z = lyVec.X * Math.Sin(dYaw) + lyVec.Z * Math.Cos(dYaw);
            // yawVec = Vector3D.Normalize(Vector3D.TransformNormal(lyVec, cMat));
            yawVec = Vector3D.ProjectOnPlane(ref yawVec, ref cuVec).Rotate(cuVec, -dYaw * 0.0001d);
        } else yawVec = Vector3D.Normalize(Vector3D.ProjectOnPlane(ref yawVec, ref cuVec));
        // var cYaw = Math.Sign(lyVec.Z) * Math.Acos(-lyVec.X / Math.Sqrt(lyVec.X*lyVec.X + lyVec.Z*lyVec.Z)) * radToDegMul;
        // print($"yawVec: {prettyV3(yawVec)}    lyVec: {prettyV3(lyVec)}");
        // var cYaw = -getControlYaw() * 5d; impossible due to PIDs sliding (until error subsides) on yaw axis when input stops
        // print($"cRoll: {cRoll.ToString("0.000")}    cPitch: {cPitch.ToString("0.000")}    cYaw: {cYaw.ToString("0.000")}");

        // print($"updating {gMap.Count} gyro groups");
        foreach (var cags in gMap) {
            var gCtrl = cags.Key.grid;
            print("---- ----");
            var mat = gCtrl.WorldMatrix;
            // var fVec = mat.Forward; var lVec = mat.Left; var uVec = mat.Up;

            // print($"rrMul: {Vector3D.Dot(cfVec, fVec).ToString("0.000")}    prMul: {Vector3D.Dot(cfVec, lVec).ToString("0.000")}    yrMul: {Vector3D.Dot(cfVec, uVec).ToString("0.000")}");
            // print($"rpMul: {Vector3D.Dot(clVec, fVec).ToString("0.000")}    ppMul: {Vector3D.Dot(clVec, lVec).ToString("0.000")}    ypMul: {Vector3D.Dot(clVec, uVec).ToString("0.000")}");
            // print($"ryMul: {Vector3D.Dot(cuVec, fVec).ToString("0.000")}    pyMul: {Vector3D.Dot(cuVec, lVec).ToString("0.000")}    yyMul: {Vector3D.Dot(cuVec, uVec).ToString("0.000")}");
            // var roll  = cRoll * Vector3D.Dot(cfVec, fVec) + cPitch * Vector3D.Dot(clVec, fVec) - cYaw * Vector3D.Dot(cuVec, fVec); // \
            // var pitch = cRoll * Vector3D.Dot(cfVec, lVec) + cPitch * Vector3D.Dot(clVec, lVec) + cYaw * Vector3D.Dot(cuVec, lVec); //  } conflicting sign between grids, some upside-down and rotated
            // var yaw   = cRoll * Vector3D.Dot(cfVec, uVec) + cPitch * Vector3D.Dot(clVec, uVec) + cYaw * Vector3D.Dot(cuVec, uVec); // /
            var roll  = -getPlaneAngle(cgVec,  cuVec, mat.Forward) + getPlaneAngle(yawVec, clVec, mat.Forward);
            var pitch =  getPlaneAngle(cgVec,  cuVec, mat.Left)    + getPlaneAngle(yawVec, clVec, mat.Left);
            var yaw   =  getPlaneAngle(cgVec,  cuVec, mat.Up)      + getPlaneAngle(yawVec, clVec, mat.Up);
            print($"roll: {roll.ToString("0.000")}    pitch: {pitch.ToString("0.000")}    yaw: {yaw.ToString("0.000")}");
            roll  =  cags.Key.ctrlRoll.control(roll,  delta);
            pitch = cags.Key.ctrlPitch.control(pitch, delta);
            yaw   =   cags.Key.ctrlYaw.control(yaw,   delta);
            print($"PIDroll: {roll.ToString("0.000")}    PIDpitch: {pitch.ToString("0.000")}    PIDyaw: {yaw.ToString("0.000")}");

            foreach (var ags in cags.Value) {
                var align = ags.Key; var gs = ags.Value;

                var gRoll  = (float) chooseRPY(align.forward, roll, pitch, yaw);
                var gPitch = (float) chooseRPY(align.left,    roll, pitch, yaw);
                var gYaw   = (float) chooseRPY(align.up,      roll, pitch, yaw);
                print($"applying to {gs.Count}    gRoll: {gRoll.ToString("0.000")}    gPitch: {gPitch.ToString("0.000")}    gYaw: {gYaw.ToString("0.000")}    e.g. {gs.First().CustomName}");
                // var str = "  ";
                gs.ForEach(g => {
                    // str += $"{g.CustomName}, ";
                    g.GyroOverride = true;
                    g.Roll  = gRoll;
                    g.Pitch = gPitch;
                    g.Yaw   = gYaw;
                });
                // print(str);
            }
        }
    }
    public void updateGyros(Dictionary<IMyCubeGrid, List<IMyGyro>> _gyros) {
        foreach (var cags in gMap.ToList()) { // remove non-functional gyros
            var agss = cags.Value;
            foreach (var ags in agss.ToList()) {
                var gs = ags.Value;
                for (var i = 0; i < gs.Count; i++) {
                    if (gs[i].Closed || !gs[i].IsWorking) {
                        gs[i].GyroOverride = false;
                        gyros.Remove(gs[i]);
                        gs.RemoveAt(i);
                        i--;
                    }
                }
                if (gs.Count <= 0) agss.Remove(ags.Key);
            }
            if (agss.Count <= 0) gMap.Remove(cags.Key);
        }
        if (appendGMap(_gyros) > 0) {
            foreach(var c in gMap.Keys) {
                c.ctrlRoll.reset();
                c.ctrlPitch.reset();
                c.ctrlYaw.reset();
            }
        }
    }
}

public List<IMyCubeGrid> getRelatedCGs(IMyCubeGrid mainGrid, List<IMyTerminalBlock> blocks) {
    var res = new List<IMyCubeGrid>();

    var toQuery = new Queue<IMyCubeGrid>();
    toQuery.Enqueue(mainGrid);
    while (toQuery.Count > 0) {
        var grid = toQuery.Dequeue();
        var nGrids = blocks.Where(b => b is IMyMechanicalConnectionBlock && b.CubeGrid == grid).Select(b => (b as IMyMechanicalConnectionBlock).TopGrid as IMyCubeGrid).ToList();
        if (nGrids.Count > 0) {
            res.AddRange(nGrids);
            nGrids.ForEach(g => toQuery.Enqueue(g));
        }
    }
    return res;
}

public Dictionary<IMyCubeGrid, List<IMyGyro>> getConnectedGyros(IMyShipController controller, List<IMyTerminalBlock> blocks, bool includeMechSubgrids = false) {
    var res = new Dictionary<IMyCubeGrid, List<IMyGyro>>();
    var gyros = blocks.Where(b => b is IMyGyro && b.IsWorking).Select(b => b as IMyGyro).ToList();
    res.Add(controller.CubeGrid, gyros.Where(g => g.CubeGrid == controller.CubeGrid).ToList());
    if (includeMechSubgrids) {
        var relatedCGs = getRelatedCGs(controller.CubeGrid, blocks);
        relatedCGs.ForEach(cg => print($"$cg: {cg.CustomName}"));
        gyros.ForEach(g => {
            if (g.CubeGrid != controller.CubeGrid) print($"indirect gyro found on {g.CubeGrid.CustomName}, is connected: {relatedCGs.Contains(g.CubeGrid)}");
            if (relatedCGs.Contains(g.CubeGrid)) {
                if (res.ContainsKey(g.CubeGrid)) res[g.CubeGrid].Add(g);
                else res.Add(g.CubeGrid, new List<IMyGyro> { g });
            }
        });
    }
    return res;
}

public int state = 0;
public IMyShipController controller = null;
public List<IMyShipController> slaveControllers = null;
public gyroArray gArr = null;
public static readonly System.Text.RegularExpressions.Regex tagRegex = new System.Text.RegularExpressions.Regex(@"(\s|^)@galign(\s|$)");
public static readonly System.Text.RegularExpressions.Regex slaveTagRegex = new System.Text.RegularExpressions.Regex(@"(\s|^)@galign-slave(\s|$)");
public void collectSlaveControllers(List<IMyTerminalBlock> blocks) {
    var relatedCGs = getRelatedCGs(controller.CubeGrid, blocks);
    relatedCGs.Add(controller.CubeGrid);
    slaveControllers = blocks.Where(b => b is IMyShipController && relatedCGs.Contains(b.CubeGrid) && slaveTagRegex.IsMatch(b.CustomName)).Select(b => b as IMyShipController).ToList();
}
public void findDebugLcd(List<IMyTerminalBlock> blocks) {
    debugLcd = blocks.FirstOrDefault(b => b is IMyTextPanel && tagRegex.IsMatch(b.CustomName)&& b.CubeGrid == controller.CubeGrid) as IMyTextPanel;
    if (debugLcd != null) {
        debugLcd.ContentType = ContentType.TEXT_AND_IMAGE;
        debugLcd.FontSize = 0.8f;
        debugLcd.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
    }
}
public void init() {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName) && b.CubeGrid == Me.CubeGrid) as IMyShipController;
    if (controller != null) {
        collectSlaveControllers(blocks);
        findDebugLcd(blocks);
        gArr = new gyroArray(controller, slaveControllers, getConnectedGyros(controller, blocks, true));
    }
}
public void stateUpdate() {
    if (controller.Closed) {
        init();
        return;
    }

    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    collectSlaveControllers(blocks);
    findDebugLcd(blocks);
    gArr.updateGyros(getConnectedGyros(controller, blocks, true));
}

public Program() {
    Echo("");
    Me.CustomName = "@galign program";
    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    init();
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update1) {
        if (state == 1) {
            refreshTick++;
            if (refreshTick >= 200) {
                // wipe();
                // print("Update 200");
                stateUpdate();
                refreshTick = 0;
            }
            if (gArr != null) {
                wipe();
                gArr.update(Runtime.TimeSinceLastRun.TotalSeconds);
            }
            
        }
    } else {
        if (argument == "start") {
            if (gArr == null) init();
            state = 1;
        } else if (argument == "stop" && state > 0) {
            if (gArr != null) gArr.stop();
            state = 0;
            gArr = null;
            controller = null;
        }
    }
}
