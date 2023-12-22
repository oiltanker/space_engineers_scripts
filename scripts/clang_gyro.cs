/*
 * Select operation mode: retracted (resting position is retracted)  |  extended (resting position is extended)
 */
public enum restPos { retracted, extended }
// RESTING MODE CONSTANT
public const restPos restMode = restPos.extended;
public const bool negativeRotation = false;

@import lib.eps
@import lib.printFull
@import lib.grid
@import lib.alignment
@import lib.angularVelocity
@import lib.pid
@import lib.dockState

public const double radToDegMul = 180 / Math.PI;
public const string itProperty = "ShareInertiaTensor";
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@cgyro(\s|$)");
public static readonly @Regex tagPistons = new @Regex(@"(\s|^)@cgyro-(f|b)-(roll|pitch|yaw)(\s|$)");

public class pistonGyroArr {
    public pidCtrl rollPid; public pidCtrl pitchPid; public pidCtrl yawPid;
    public Dictionary<dir, List<IMyPistonBase>> pMap;
    public bool isLarge;
    public restPos restMode;
    private pidCtrl newDefaultPid() => new pidCtrl(1d, 0.25d, 0.25d, 1d / ENG_UPS, 0.95d);
    public pistonGyroArr(Dictionary<dir, List<IMyPistonBase>> pistons, IMyTerminalBlock anchor, restPos restMode) {
        if (pistons.Values.First().Count == 0) throw new ArgumentException("Piston count cannot be 0");
        var piston = pistons.Values.First().First();
        isLarge = piston.CubeGrid.GridSizeEnum == MyCubeSize.Large;
        this.restMode = restMode;
        rollPid = newDefaultPid(); pitchPid = newDefaultPid(); yawPid = newDefaultPid();
        pMap = pistons;
        foreach (var ps in pMap.Values) ps.ForEach(p => {
            if        (restMode == restPos.retracted) {
                if (p.IsFunctional) p.MinLimit = isLarge ? retLargeMin : retSmallMin;
            } else if (restMode == restPos.extended) {
                if (p.IsFunctional) p.MaxLimit = isLarge ? extLargeMax : extSmallMax;
            }
        });
    }
    const float retLargeMax = 0f;
    const float retLargeMin = 0f;
    const float retSmallMax = 1.7f;
    const float retSmallMin = 0.3f;
    const float extLargeMax = 6f;
    const float extLargeMin = 2.5f;
    const float extSmallMax = 1.7f;
    const float extSmallMin = 0.3f;
    private void setVel(List<IMyPistonBase> pistons, float val, double vel) {
        var max = restMode == restPos.retracted ? (isLarge ? retLargeMax : retSmallMax) : (isLarge ? extLargeMax : extSmallMax);
        var min = restMode == restPos.retracted ? (isLarge ? retLargeMin : retSmallMin) : (isLarge ? extLargeMin : extSmallMin);
        foreach (var piston in pistons) {
            if (val > 0f) {
                if        (restMode == restPos.retracted) {
                    piston.MaxLimit = Math.Min(min + (val / 2f), max);
                    if (piston.MaxLimit < piston.CurrentPosition) piston.Velocity = 5f;
                    else piston.Velocity = -5f;
                } else if (restMode == restPos.extended) {
                    piston.MinLimit = Math.Max(max - (val / 20f), min);
                    if (piston.CurrentPosition > piston.MinLimit) piston.Velocity = -5f;
                    else piston.Velocity = 5f;
                }
            } else if (Math.Abs(val) < dEPS && vel > 0d) {
                if        (restMode == restPos.retracted) {
                    piston.MaxLimit = Math.Min(min + (float) vel, max);
                    if (piston.MaxLimit < piston.CurrentPosition) piston.Velocity = 5f;
                    else piston.Velocity = -5f;
                } else if (restMode == restPos.extended) {
                    piston.MinLimit = Math.Max(max - (float) vel, min);
                    if (piston.MaxLimit < piston.CurrentPosition) piston.Velocity = 5f;
                    else piston.Velocity = -5f;
                }
            } else { piston.Velocity = 5f; piston.MinLimit = min; }
        }
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
        foreach (var ps in pMap.Values) ps.ForEach(p => { if (p.IsFunctional) { p.Velocity = -5f; p.MinLimit = 0f; } });
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

public double adjust(double ind, double rot, Vector3D vec, Vector3D normal, Func<Vector3D> get, Action<Vector3D> set) {
    if (Math.Abs(ind) < dEPS && Math.Abs(rot) < 0.1d) {
        var sVec = get();
        if (sVec.IsZero()) { sVec = vec; set(vec); }
        var pVec = Vector3D.ProjectOnPlane(ref sVec, ref normal);
        var mul = normal.Dot(vec.Cross(pVec));
        mul = double.IsNaN(mul) ? 0d : Math.Sign(mul);
        // print($"fine adjusting {(Vector3D.Angle(pVec, vec) * radToDegMul).ToString("0.000")}");
        return mul == 0d ? 0d : mul * -Vector3D.Angle(pVec, vec);
    } else set(vec);
    return rot;
}
public void update() {
    if (isCurrentlyDocked()) {
        print("Docked");
        pgArr.shutdown();
        Runtime.UpdateFrequency = UpdateFrequency.Update10;
        return;
    } else if (Runtime.UpdateFrequency != UpdateFrequency.Update1) Runtime.UpdateFrequency = UpdateFrequency.Update1;

    var rotInd = controller.RotationIndicator;
    var roll = controller.RollIndicator; var pitch = rotInd.X; var yaw = rotInd.Y;
    var rotVel = toLocalAVel(controller.GetShipVelocities().AngularVelocity, controller);
    print($"roll {roll.ToString("0.000")}    pitch {pitch.ToString("0.000")}    yaw {yaw.ToString("0.000")}");
    print($"vel(deg): roll {(rotVel.Z * radToDegMul).ToString("0.00")}    pitch {(rotVel.X * radToDegMul).ToString("0.00")}    yaw {(rotVel.Y * radToDegMul).ToString("0.00")}");
    print($"vel(rad): roll {rotVel.Z.ToString("0.000")}    pitch {rotVel.X.ToString("0.000")}    yaw {rotVel.Y.ToString("0.000")}");

    if (Math.Abs(roll)  < dEPS) rotVel.Z = adjust(roll,  rotVel.Z, controller.WorldMatrix.Left,    controller.WorldMatrix.Forward, () => { return sRot.left; },    v => sRot.left = v); // roll
    if (Math.Abs(pitch) < dEPS) rotVel.X = adjust(pitch, rotVel.X, controller.WorldMatrix.Up,      controller.WorldMatrix.Left,    () => { return sRot.up; },      v => sRot.up = v); // pitch
    if (Math.Abs(yaw)   < dEPS) rotVel.Y = adjust(yaw,   rotVel.Y, controller.WorldMatrix.Forward, controller.WorldMatrix.Down,    () => { return sRot.forward; }, v => sRot.forward = v); // yaw
    pgArr.update(roll, pitch, yaw, rotVel, Runtime.TimeSinceLastRun.TotalSeconds);
}

public bool init() {
    var blocks = getBlocks(b => b.IsSameConstructAs(Me))

    findDebugLcd(blocks, tagRegex);
    wipe();

    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName)) as IMyShipController;
    if (controller != null) {
        try {
            initDockState(blocks);

            pgArr?.shutdown();
            var pistons = blocks.Where(b => b is IMyPistonBase && b.IsWorking && tagPistons.IsMatch(b.CustomName)).Cast<IMyPistonBase>();
            var pMap = new Dictionary<dir, List<IMyPistonBase>>();
            foreach (var d in Enum.GetValues(typeof(dir)).Cast<dir>()) pMap.Add(d, new List<IMyPistonBase>());
            var pStr = negativeRotation ? "b" : "f"; var nStr = negativeRotation ? "f" : "b";
            foreach (var p in pistons) {
                var match = tagPistons.Match(p.CustomName);
                print($"match: {match.Success}  {match.Groups[2].Value}  {match.Groups[3].Value}");
                if        (match.Groups[3].Value == "roll") {
                    if      (match.Groups[2].Value == pStr) pMap[dir.forward ].Add(p);
                    else if (match.Groups[2].Value == nStr) pMap[dir.backward].Add(p);
                } else if (match.Groups[3].Value == "pitch") {
                    if      (match.Groups[2].Value == pStr) pMap[dir.left    ].Add(p);
                    else if (match.Groups[2].Value == nStr) pMap[dir.right   ].Add(p);
                } else if (match.Groups[3].Value == "yaw") {
                    if      (match.Groups[2].Value == pStr) pMap[dir.up      ].Add(p);
                    else if (match.Groups[2].Value == nStr) pMap[dir.down    ].Add(p);
                }
            }

            pgArr = new pistonGyroArr(pMap, controller, restMode);
        } catch (Exception e) {
            print($"Wrongly built clang gyroscope: could not evaluate components\n{e.Message}"); Echo("error");
            return false;
        }

        if (
            pgArr.pMap.Values.Count(ps => ps.Count == 0) == 0 &&
            pgArr.pMap[dir.forward].Count == pgArr.pMap[dir.backward].Count &&
            pgArr.pMap[dir.left   ].Count == pgArr.pMap[dir.right   ].Count &&
            pgArr.pMap[dir.up     ].Count == pgArr.pMap[dir.down    ].Count
        ) {
            Echo($"ok");
            return true;
        } else {
            print("Wrong clang gyroscope piston configuration");
            print($"  roll: forward - {pgArr.pMap[dir.forward].Count} | backward - {pgArr.pMap[dir.forward].Count}");
            print($"  pitch: forward - {pgArr.pMap[dir.left].Count} | backward - {pgArr.pMap[dir.right].Count}");
            print($"  yaw: forward - {pgArr.pMap[dir.up].Count} | backward - {pgArr.pMap[dir.down].Count}");
            Echo("error");
        }
    } else { print("No main controller."); Echo("error"); }

    return false;
}

public void shutdown() {
    pgArr?.shutdown();
    pgArr = null;
}

const string pName = "@cgyro program";
public Program() {
    Echo("");
    if (!Me.CustomName.StartsWith(pName)) Me.CustomName = pName;
    initMeLcd();

    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    if (state == 1) {
        if (!init()) shutdown();
        Runtime.UpdateFrequency = UpdateFrequency.Update1;
    } else {
        findDebugLcd(getBlocks(b => b.IsSameConstructAs(Me)), tagRegex);

        Echo("offline"); wipe(); print("Clang gyroscope shut down.");
    }
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update1 || updateSource == UpdateType.Update10) {
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