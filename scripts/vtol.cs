@import lib.eps
@import lib.printFull
@import lib.alignment
@import lib.gyroArray
@import lib.pid

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@vtol(\s|$)");

public static pidCtrl newDefaultPid() => new pidCtrl(2d, 0.1d, 0.5d, 1d / ENG_UPS, 0.95d);

public class vtol {
    public dir orientation;
    pidCtrl rotPid;
    public IMyMotorStator rotor;
    public List<IMyThrust> thrusters;
    public Dictionary<dir, List<IMyThrust>> tMap;
    private dir decideTDir(dir tDir) {
        if        (orientation == dir.left) {
            if (tDir == dir.forward)  return dir.forward;
            if (tDir == dir.backward) return dir.backward;
            if (tDir == dir.up)       return dir.left;
            if (tDir == dir.down)     return dir.right;
            if (tDir == dir.right)    return dir.up;
            if (tDir == dir.left)     return dir.down;
        } else if (orientation == dir.right) {
            if (tDir == dir.forward)  return dir.backward;
            if (tDir == dir.backward) return dir.forward;
            if (tDir == dir.up)       return dir.right;
            if (tDir == dir.down)     return dir.left;
            if (tDir == dir.right)    return dir.up;
            if (tDir == dir.left)     return dir.down;
        }
        throw new ArgumentException("Unimplemented case");
    }
    public vtol(dir orientation, IMyMotorStator rotor, List<IMyTerminalBlock> blocks) {
        if (orientation != dir.left && orientation != dir.right) throw new ArgumentException($"vtol only supports left or right rotors, got {orientation.ToString("G")}");
        this.orientation = orientation;
        this.rotor = rotor;
        rotPid = new pidCtrl(2d, 0.05d, 0.1d, 1d / ENG_UPS, 0.95d);

        thrusters = new List<IMyThrust>();
        tMap = new Dictionary<dir, List<IMyThrust>>();
        var cGrid = rotor.TopGrid; var mat = rotor.Top.WorldMatrix;
        foreach (var dt in blocks.Where(b => b is IMyThrust && b.CubeGrid == cGrid).Cast<IMyThrust>().GroupBy(t => matchDir(t.WorldMatrix.Forward, mat))) {
            var ts = dt.AsEnumerable().ToList();
            thrusters.AddRange(ts);
            tMap.Add(decideTDir(dt.Key), ts);
        }
    }
    public void update(Vector3D gVec) {
        var mat = rotor.Top.WorldMatrix;
        var lVec = mat.Left; var uVec = mat.Up;

        var pgVec = Vector3D.ProjectOnPlane(ref gVec, ref uVec);
        var mul = Vector3D.Dot(lVec.Cross(pgVec), uVec);
        mul = Double.IsNaN(mul) ? 0d : Math.Sign(mul);
        var angle = mul != 0d ? mul * Math.Min(Vector3D.Angle(lVec, pgVec) * 10d, Math.PI) : 0d;

        rotor.TargetVelocityRad = (float) rotPid.control(-angle);
        foreach (var ts in tMap.Values) ts.ForEach(t => t.ThrustOverride = EPS);
    }
    public void shutdown() {
        rotor.TargetVelocityRad = 0f;
        foreach (var ts in tMap.Values) ts.ForEach(t => t.ThrustOverride = 0f);
    }
}
public class vtolMgr {
    private struct vKey {
        public Vector3D vec;
        public bool Equals(vKey? v2) => v2 != null && (vec - v2.Value.vec).Length() <= 0.01d;
        public override bool Equals(object obj) => obj is vKey? && Equals(obj as vKey?);
        public override int GetHashCode() => 0;
    }
    public pidCtrl velPid;
    public List<vtol> vtols;
    public List<IMyThrust> thrusters;
    public Vector3D? stopPoint;
    public IMyTerminalBlock anchor;
    public vtolMgr(IEnumerable<IMyMotorStator> rotors, IMyTerminalBlock anchor, pidCtrl velPid, List<IMyTerminalBlock> blocks) {
        vtols = rotors.Select(r => new vtol(matchDir(r.WorldMatrix.Up, anchor.WorldMatrix), r, blocks)).ToList();
        thrusters = vtols.Select(v => v.thrusters).Aggregate((acc, next) => { acc.AddRange(next); return acc; });
        this.velPid = velPid;
        this.anchor = anchor;
        stopPoint = null;
    }
    private void decide(double move, dir normal, dir reverse) {
        if (Math.Abs(move) > dEPS) vtols.ForEach(v => { // override normal-reverse
            if        (move > 0d) {
                if (v.tMap.ContainsKey(normal )) v.tMap[normal ].ForEach(t => t.ThrustOverridePercentage = 1f);
                if (v.tMap.ContainsKey(reverse)) v.tMap[reverse].ForEach(t => t.ThrustOverride = EPS);
            } else if (move < 0d) {
                if (v.tMap.ContainsKey(reverse)) v.tMap[reverse].ForEach(t => t.ThrustOverridePercentage = 1f);
                if (v.tMap.ContainsKey(normal )) v.tMap[normal ].ForEach(t => t.ThrustOverride = EPS);
            }
        });
    }
    public void update(Vector3D gVec, bool dampening, Vector3D vel, Vector3 mov, double mass, double delta) {
        print($"mov: {mov.X.ToString("0")}, {mov.Y.ToString("0")}, {mov.Z.ToString("0")}");
        vtols.ForEach(v => v.update(gVec));

        if (dampening) {
            if (vel.Length() < 0.5d && stopPoint != null) {
                var lp = stopPoint.Value;
                print($"fine adjusting to {lp.X.ToString("0.000")}, {lp.Y.ToString("0.000")}, {lp.Z.ToString("0.000")}");
                vel = anchor.WorldMatrix.Translation - lp;
                vel = Vector3D.Normalize(vel) * velPid.control(vel.Length(), delta);
            } else stopPoint = anchor.WorldMatrix.Translation;
            var force = (vel + gVec) * mass; // force to counteract
            print($"force: {force.X.ToString("0.000")}, {force.Y.ToString("0.000")}, {force.Z.ToString("0.000")}");
            foreach (var vts in thrusters.GroupBy(t => new vKey{ vec = t.WorldMatrix.Forward })) {
                var v = vts.Key.vec; var ts = vts.AsEnumerable();
                var maxThrust = ts.Sum(t => t.MaxEffectiveThrust);
                var mtForce = Math.Min(force.Dot(v), maxThrust);
                if (mtForce > 0d) {
                    force -= v * mtForce;
                    var tOverride = mtForce / maxThrust;
                    foreach (var t in ts) t.ThrustOverridePercentage = (float) tOverride;
                }
            }
        }
        decide( mov.Z, dir.forward, dir.backward);
        decide( mov.X, dir.left,    dir.right);
        decide(-mov.Y, dir.up,      dir.down);
    }
    public void shutdown() => vtols.ForEach(v => v.shutdown());
}

public int state = 0;
public bool allWorking = false;
public IMyShipController controller = null;
public wGyroArr gyroArray = null;
public vtolMgr vtolManager = null;
public Vector3D pitchVec = Vector3D.Zero;
public double pvAge = 0;
// pids
public pidCtrl pidRoll = null;
public pidCtrl pidPitch = null;
public pidCtrl pidYaw = null;
public pidCtrl velPid = new pidCtrl(1d, 10d, 0.5d, 1d / ENG_UPS, 0.95d);

public void update(double delta) {
    var gVec = controller.GetNaturalGravity();
    var vels = controller.GetShipVelocities();
    print($"gForce: {gVec.Length().ToString("0.000")}");
    print($"gVec: {gVec.X.ToString("0.000")}, {gVec.Y.ToString("0.000")}, {gVec.Z.ToString("0.000")}");

    vtolManager.update(gVec, controller.DampenersOverride, vels.LinearVelocity, controller.MoveIndicator, controller.CalculateShipMass().PhysicalMass, delta);
    if (gyroArray != null) {
        var mat = controller.WorldMatrix;
        var fVec = mat.Forward; var lVec = mat.Left; var uVec = mat.Up;
        var ifVec = Vector3D.Normalize(mat.Left.Cross(gVec));
        var rMul = ifVec.Dot(fVec); rMul = Double.IsNaN(rMul) ? 0d : rMul;
        var yMul = ifVec.Dot(uVec); yMul = Double.IsNaN(yMul) ? 0d : yMul;
        print($"rMul: {rMul.ToString("0.000")}    yMul: {yMul.ToString("0.000")}");
        var roll = Math.Abs(rMul) < EPS ? 0f : (float) (rMul * (Vector3D.Angle(Vector3D.ProjectOnPlane(ref gVec, ref fVec), lVec) * radToDegMul - 90));
        var yaw  = Math.Abs(yMul) < EPS ? 0f : (float) (yMul * (Vector3D.Angle(Vector3D.ProjectOnPlane(ref gVec, ref uVec), lVec) * radToDegMul - 90));
        print($"roll: {roll.ToString("0.000")}    yaw: {yaw.ToString("0.000")}");

        var yRoll = controller.RotationIndicator.Y * Math.Sign(yMul * rMul) * (1d - Math.Abs(rMul)); var yYaw = controller.RotationIndicator.Y * (1d - Math.Abs(yMul));
        print($"yRoll: {yRoll.ToString("0.000")}    yYaw: {yYaw.ToString("0.000")}");

        var pInd = controller.RotationIndicator.X;
        var pitch = 0d;
        if (Math.Abs(pInd) > dEPS) {
            pitchVec = fVec;
            pitch = pInd;
        } else if (Math.Abs(vels.AngularVelocity.X) < 0.02) {
            print($"pitchVel: {vels.AngularVelocity.X.ToString("0.000")}");
            pitchVec = Vector3D.Normalize(Vector3D.ProjectOnPlane(ref pitchVec, ref lVec));
            var pMul = lVec.Dot(fVec.Cross(pitchVec)); pMul = Double.IsNaN(pMul) ? 0d : Math.Sign(pMul);
            pitch = pidPitch.control(pMul == 0d ? 0d : pMul * Vector3D.Angle(fVec, pitchVec) * 10, delta);
        }
        print($"pitch: {pitch}");


        gyroArray.capture();
        gyroArray.setRPY((float) (pidRoll.control(roll, delta) + yRoll), (float) pitch, (float) (pidYaw.control(-yaw, delta) + yYaw));
    }
}

public void init() {
    allWorking = false;

    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    findDebugLcd(blocks, tagRegex);
    wipe();
    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName) && b.CubeGrid == Me.CubeGrid) as IMyShipController;
    if (controller != null) {
        try {
            vtolManager?.shutdown();
            var rotors = blocks.Where(b => b is IMyMotorStator && b.CubeGrid == controller.CubeGrid && tagRegex.IsMatch(b.CustomName)).Cast<IMyMotorStator>();
            if (rotors.Count() > 0) vtolManager = new vtolMgr(rotors, controller, velPid, blocks);

            if (pitchVec.IsZero() || pvAge > 5) pitchVec = controller.WorldMatrix.Forward;

            pidRoll = newDefaultPid();
            pidPitch = new pidCtrl(1d, 0.1d, 0.8d, 1d / ENG_UPS, 0.95d);
            pidYaw  = newDefaultPid();
            gyroArray?.release();
            var gyros = blocks.Where(b => b is IMyGyro && b.CubeGrid == controller.CubeGrid).Cast<IMyGyro>();
            if (gyros.Count() > 0) gyroArray = new wGyroArr(gyros, controller);

            if (rotors.Count() > 0) { allWorking = true; Echo("ok"); }
            else { print("No vtol components detected"); Echo("error"); }
        } catch (Exception e) { print($"Error initializing vtols: {e.Message}"); Echo("error"); }
    } else { print("No vtol controller"); Echo("error"); }
}

public void shutdown() {
    try {
        vtolManager?.shutdown();
        gyroArray?.release();
    } catch (Exception e) {}
    vtolManager = null; gyroArray = null;
    allWorking = false;
}

public Program() {
    Echo("");
    Me.CustomName = "@vtol program";
    initMeLcd();

    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    if (state == 1) init();
    if (state == 1) {
        init();
        Runtime.UpdateFrequency = UpdateFrequency.Update1;
    } else {
        var blocks = new List<IMyTerminalBlock>();
        GridTerminalSystem.GetBlocks(blocks);
        findDebugLcd(blocks, tagRegex);
        Echo("offline"); wipe(); print("offline");
    }
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
                try {
                    wipe();
                    update(Runtime.TimeSinceLastRun.TotalSeconds);
                    pvAge = 0;
                } catch (Exception e) {
                    shutdown();
                    Echo("error"); wipe(); print($"Error while running:\n${e.Message}\n{e.StackTrace}");
                }
            } else {
                pvAge += Runtime.TimeSinceLastRun.TotalSeconds;
                shutdown();
            }
        }
    } else {
        if (argument == "start" && state != 1) {
            pvAge += Runtime.TimeSinceLastRun.TotalSeconds;
            state = 1;
            init();
            if (!allWorking) shutdown();
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        } else if (argument == "stop" && state > 0) {
            shutdown();
            velPid.reset();
            state = 0;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("offline"); wipe(); print("offline");
        }
    }
}