@import lib.eps
@import lib.printFull
@import lib.alignment
@import lib.angularVelocity
@import lib.pid

public const double radToDegMul = 180 / Math.PI;
public const string itProperty = "ShareInertiaTensor";
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@cgyro(\s|$)");
public static readonly @Regex tagPistons = new @Regex(@"(\s|^)@cgyro-(f|b)-(roll|pitch|yaw)(\s|$)");

public class pistonGyroArr {
    public pidCtrl rollPid; public pidCtrl pitchPid; public pidCtrl yawPid;
    public Dictionary<dir, IMyPistonBase> pMap;
    private pidCtrl newDefaultPid() => new pidCtrl(1d, 0.25d, 0.25d, 1d / ENG_UPS, 0.95d);
    public pistonGyroArr(Dictionary<dir, IMyPistonBase> pistons, IMyTerminalBlock anchor) {
        rollPid = newDefaultPid(); pitchPid = newDefaultPid(); yawPid = newDefaultPid();
        pMap = pistons;
    }
    private void setVel(IMyPistonBase piston, float val, double vel) {
        if (val > 0f) {
            piston.MinLimit = Math.Max(7f - (val / 20f), 5f);
            if (piston.CurrentPosition > piston.MinLimit) piston.Velocity = -5f;
            else piston.Velocity = 5f;
        } else if (Math.Abs(val) < dEPS && vel > 0d) {
            piston.MinLimit = Math.Max(7f - (float) vel, 5f);
            if (piston.CurrentPosition > piston.MinLimit) piston.Velocity = -5f;
            else piston.Velocity = 5f;
        } else { piston.Velocity = 5f; piston.MinLimit = 5f; }
    }
    public void update(float roll, float pitch, float yaw, Vector3D rotVel, double delta) {
        roll  *= -10f;
        pitch *= -1f;
        yaw   *=  1f;
        rotVel.Z =  rollPid.control(rotVel.Z * 10d, delta);
        rotVel.X = pitchPid.control(rotVel.X * 10d, delta);
        rotVel.Y =   yawPid.control(rotVel.Y * 10d, delta);
        setVel(pMap[dir.forward],   roll,   rotVel.Z);
        setVel(pMap[dir.backward], -roll,  -rotVel.Z);
        setVel(pMap[dir.left],      pitch,  rotVel.X);
        setVel(pMap[dir.right],    -pitch, -rotVel.X);
        setVel(pMap[dir.up],       -yaw,    rotVel.Y);
        setVel(pMap[dir.down],      yaw,   -rotVel.Y);
    }
    public void shutdown() {
        foreach (var p in pMap.Values) if (p != null && p.IsFunctional) { p.Velocity = -5f; p.MinLimit = 2.5f; }
    }
}

public struct rotStore {
    public Vector3D forward;
    public Vector3D left;
    public Vector3D up;
}

public int state = 0;
public IMyShipController controller = null;
public pistonGyroArr pgArr = null;
public rotStore sRot = new rotStore{ forward = Vector3D.Zero, left = Vector3D.Zero, up = Vector3D.Zero };

public double adjust(double rot, Vector3D vec, Vector3D normal, Func<Vector3D> get, Action<Vector3D> set) {
    if (Math.Abs(rot) < 0.03d) {
        var sVec = get();
        if (sVec.IsZero()) { sVec = vec; set(vec); }
        var pVec = Vector3D.ProjectOnPlane(ref sVec, ref normal);
        var mul = normal.Dot(vec.Cross(pVec));
        mul = double.IsNaN(mul) ? 0d : Math.Sign(mul);
        // print($"fine adjusting {(Vector3D.Angle(pVec, vec) * radToDegMul).ToString("0.000")}");
        return mul == 0d ? 0d : mul * Vector3D.Angle(pVec, vec);
    } else set(vec);
    return rot;
}
public void update() {
    var rotInd = controller.RotationIndicator;
    var roll = controller.RollIndicator; var pitch = rotInd.X; var yaw = rotInd.Y;
    print($"roll {roll.ToString("0.000")}    pitch {pitch.ToString("0.000")}    yaw {yaw.ToString("0.000")}");

    var rotVel = toLocalAVel(controller.GetShipVelocities().AngularVelocity, controller);
    if (Math.Abs(roll)  < dEPS) rotVel.Z = adjust(rotVel.Z, controller.WorldMatrix.Left,    controller.WorldMatrix.Backward, () => { return sRot.left; },    v => sRot.left = v); // roll
    if (Math.Abs(pitch) < dEPS) rotVel.X = adjust(rotVel.X, controller.WorldMatrix.Up,      controller.WorldMatrix.Right,    () => { return sRot.up; },      v => sRot.up = v); // pitch
    if (Math.Abs(yaw)   < dEPS) rotVel.Y = adjust(rotVel.Y, controller.WorldMatrix.Forward, controller.WorldMatrix.Up,       () => { return sRot.forward; }, v => sRot.forward = v); // yaw
    pgArr.update(roll, pitch, yaw, rotVel, Runtime.TimeSinceLastRun.TotalSeconds);
}

public bool init() {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    findDebugLcd(blocks, tagRegex);
    wipe();

    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName) && b.CubeGrid == Me.CubeGrid) as IMyShipController;
    if (controller != null) {
        try {
            pgArr?.shutdown();
            var pistons = blocks.Where(b => b is IMyPistonBase && b.IsWorking && b.CubeGrid == controller.CubeGrid && tagPistons.IsMatch(b.CustomName)).Cast<IMyPistonBase>();
            var pMap = new Dictionary<dir, IMyPistonBase>();
            foreach (var d in Enum.GetValues(typeof(dir)).Cast<dir>()) pMap.Add(d, null);
            foreach (var p in pistons) {
                var match = tagPistons.Match(p.CustomName);
                print($"match: {match.Success}  {match.Groups[2].Value}  {match.Groups[3].Value}");
                if        (match.Groups[3].Value == "roll") {
                    if      (match.Groups[2].Value == "f") pMap[dir.forward]  = p;
                    else if (match.Groups[2].Value == "b") pMap[dir.backward] = p;
                } else if (match.Groups[3].Value == "pitch") {
                    if      (match.Groups[2].Value == "f") pMap[dir.left]     = p;
                    else if (match.Groups[2].Value == "b") pMap[dir.right]    = p;
                } else if (match.Groups[3].Value == "yaw") {
                    if      (match.Groups[2].Value == "f") pMap[dir.up]       = p;
                    else if (match.Groups[2].Value == "b") pMap[dir.down]     = p;
                }
            }

            pgArr = new pistonGyroArr(pMap, controller);
        } catch (Exception e) {
            print($"Wrongly clang gyroscope: could not evaluate components\n{e.Message}"); Echo("error");
            return false;
        }

        if (!pgArr.pMap.Values.Any(p => p == null)) {
            Echo($"ok");
            return true;
        } else {
            print("Wrong clang gyroscope piston configuration");
            print($"  roll: forward - {pgArr.pMap[dir.forward] != null} | backward - {pgArr.pMap[dir.forward] != null}");
            print($"  pitch: forward - {pgArr.pMap[dir.left] != null} | backward - {pgArr.pMap[dir.right] != null}");
            print($"  yaw: forward - {pgArr.pMap[dir.up] != null} | backward - {pgArr.pMap[dir.down] != null}");
            Echo("error");
        }
    } else { print("No main controller."); Echo("error"); }

    return false;
}

public void shutdown() {
    pgArr?.shutdown();
    pgArr = null;
}

public Program() {
    Echo("");
    Me.CustomName = "@cgyro program";
    initMeLcd();

    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    if (state == 1) {
        if (!init()) shutdown();
        Runtime.UpdateFrequency = UpdateFrequency.Update1;
    } else {
        var blocks = new List<IMyTerminalBlock>();
        GridTerminalSystem.GetBlocks(blocks);
        findDebugLcd(blocks, tagRegex);

        Echo("offline"); wipe(); print("Clang gyroscope shut down.");
    }
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update1) {
        if (state == 1) {
            refreshTick++;
            if (refreshTick >= 200) {
                if (!init()) shutdown();
                refreshTick = 0;
            }
            if (pgArr != null) {
                try {
                    wipe();
                    update();
                } catch (Exception e) {
                    shutdown();
                    Echo("error"); wipe(); print($"Exception occcured while execution:\n{e.Message}\n{e.StackTrace}");
                }
            }
        }
    } else {
        if (argument == "start" && state != 1) {
            state = 1;
            if (!init()) shutdown();
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        } else if (argument == "stop" && state > 0) {
            shutdown();
            sRot.forward = Vector3D.Zero; sRot.left = Vector3D.Zero; sRot.up = Vector3D.Zero;
            state = 0;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("offline"); wipe(); print("Clang gyroscope shut down.");
        }
    }
}