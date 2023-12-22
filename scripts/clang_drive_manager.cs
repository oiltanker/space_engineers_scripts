@import lib.eps
@import lib.printFull
@import lib.grid
@import lib.activeStabilization
@import lib.pid
@import lib.angularVelocity
@import lib.dockState

public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@cdrive(\s|$)");
public static readonly @Regex tagPiston = new @Regex(@"(\s|^)@cdrive-(p|n)(\s|$)");
public const float maxVel = 5f;
public const double maxSpeed = 250d;

public int state = 0;
public bool allWorking = false;
public IMyShipController controller = null;
public Dictionary<dir, List<IMyPistonBase>> pMap = null;
public gStableArr gStabilizator = null;
public static pidCtrl newDefaultPid() => new pidCtrl(1d, 0.05d, 0.3d, 1d / ENG_UPS,  0.95d);
public pidCtrl fPid = newDefaultPid(); public pidCtrl lPid = newDefaultPid(); public pidCtrl uPid = newDefaultPid();


public void setPistonVals(IEnumerable<IMyPistonBase> pistons, float vel, float force) {
    foreach (var p in pistons) {
        p.Velocity = vel;
        p.SetValue<float>("MaxImpulseAxis", force);
        p.SetValue<float>("MaxImpulseNonAxis", force);
    }
}

public const float fInf = float.PositiveInfinity;
public void decide(double mov, dir normal, dir reverse, Vector3D axis, bool dampen, Vector3D vel, pidCtrl dirPid, double delta, double maxSpeed) {
    var pVel = vel.Dot(axis);
    var velMag = Math.Abs(pVel);
    if (Math.Abs(mov) > dEPS) { // override normal-reverse
        if (mov > 0d)  {
            var psitonVel = -pVel < maxSpeed ? maxVel : -maxVel;
            setPistonVals(pMap[normal ], -psitonVel, fInf); setPistonVals(pMap[reverse], psitonVel, fInf);
        }
        if (mov < 0d)  {
            var psitonVel = pVel < maxSpeed ? maxVel : -maxVel; 
            setPistonVals(pMap[reverse], -psitonVel, fInf); setPistonVals(pMap[normal ], psitonVel, fInf);
        }
    } else if (dampen) { // dampening normal-reverse
        if (velMag < 3d) pVel += dirPid.control(pVel, delta);
        var force = velMag > 3d ? fInf : (float) (velMag*velMag * 1000000d);
        if (pVel > 0d) { setPistonVals(pMap[normal ], -maxVel, force); setPistonVals(pMap[reverse], maxVel, force); }
        if (pVel < 0d) { setPistonVals(pMap[reverse], -maxVel, force); setPistonVals(pMap[normal ], maxVel, force); }
    } else { setPistonVals(pMap[normal ], -maxVel, fInf); setPistonVals(pMap[reverse], -maxVel, fInf); } 
}

public Vector3D sPos = Vector3D.Zero;
public void update(double delta) {
    var mat = controller.WorldMatrix;
    var vel = controller.GetShipVelocities().LinearVelocity; // vector
    var mov = controller.MoveIndicator;

    print($"{{{string.Join(", ", pMap.Keys)}}}\n{{{string.Join(", ", pMap.Values.Select(ps => ps.Count.ToString()))}}}");
    if (isCurrentlyDocked()) {
        print("Docked");
        foreach (var ps in pMap.Values) setPistonVals(ps, -maxVel, fInf);
        gStabilizator?.standby();
        Runtime.UpdateFrequency = UpdateFrequency.Update10;
        return;
    } else if (Runtime.UpdateFrequency != UpdateFrequency.Update1) Runtime.UpdateFrequency = UpdateFrequency.Update1;

    print("stabilizing ...");
    gStabilizator?.update();

    print("applying forces ...");
    if (vel.Length() < 3d && mov.Length() < dEPS) {
        if (sPos.IsZero()) sPos = mat.Translation;
        vel = mat.Translation - sPos;
        print($"fine adjusting to {vel.X.ToString("0.000")}, {vel.Y.ToString("0.000")}, {vel.Z.ToString("0.000")}");
    } else sPos = mat.Translation;

    var nMaxSpeed = maxSpeed / (double) Math.Max(mov.Length(), 1f);
    decide( mov.Z, dir.forward, dir.backward, mat.Forward, controller.DampenersOverride, vel, fPid, delta, nMaxSpeed);
    decide( mov.X, dir.left,   dir.right,     mat.Left,    controller.DampenersOverride, vel, lPid, delta, nMaxSpeed);
    decide(-mov.Y, dir.up,     dir.down,      mat.Up,      controller.DampenersOverride, vel, uPid, delta, nMaxSpeed);
}

public void init() {
    allWorking = false;

    var blocks = getBlocks(b => b.IsSameConstructAs(Me));

    findDebugLcd(blocks, tagRegex);
    wipe();
    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName)) as IMyShipController;
    if (controller != null) {
        var mat = controller.WorldMatrix;
        try {
            initDockState(blocks);

            pMap = new Dictionary<dir, List<IMyPistonBase>>();
            foreach (var d in Enum.GetValues(typeof(dir)).Cast<dir>()) pMap.Add(d, new List<IMyPistonBase>());
            foreach (var p in blocks.Where(b => b is IMyPistonBase && b.IsWorking).Cast<IMyPistonBase>()) {
                var match = tagPiston.Match(p.CustomName);
                if (!match.Success) continue;
                pMap[matchDirClosest(match.Groups[2].Value == "p" ? p.WorldMatrix.Down : p.WorldMatrix.Up, mat)].Add(p);
            }

            gStabilizator?.release();
            var gyros = blocks.Where(b => b is IMyGyro && b.CubeGrid == controller.CubeGrid && b.IsWorking && tagRegex.IsMatch(b.CustomName)).Cast<IMyGyro>();
            if (gyros.Count() > 0) { gStabilizator = new gStableArr(gyros, controller); gStabilizator.capture(); }
            else gStabilizator = null;

            foreach (var b in blocks.Where(b => b is IMyThrust && b.CubeGrid == controller.CubeGrid && tagRegex.IsMatch(b.CustomName))) (b as IMyThrust).Enabled = false;
        } catch (Exception e) {
            Echo("error"); print($"Wrongly built clang drive.\n{e.Message}");
            return;
        }

        if (pMap.Count == 6) {
            allWorking = true;
            Echo("ok"); print("Online");
        }
    } else { Echo("error"); print("No main controller.");} 
}

public void shutdown() {
    if (pMap != null) foreach (var ps in pMap.Values) ps.ForEach(p => { if (p.IsFunctional) p.Velocity = -5f; });
    gStabilizator?.release();

    pMap = null; controller = null; gStabilizator = null;
    allWorking = false;
}

const string pName = "@cdrive program";
public Program() {
    Echo("");
    if (!Me.CustomName.StartsWith(pName)) Me.CustomName = pName;
    initMeLcd();

    if (!string.IsNullOrEmpty(Storage)) {
        try { state = int.Parse(Storage); }
        catch (Exception e) { state = 1; }
    }

    if (state == 1) init();
    else {
        findDebugLcd(getBlocks(b => b.IsSameConstructAs(Me)), tagRegex);
        Echo("offline"); wipe(); print($"Clang drive shut down");
    }
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update1 || updateSource == UpdateType.Update10) {
        if (state == 1) {
            refreshTick++;
            if (refreshTick >= 200) {
                init();
                refreshTick = 0;
            }
            if (allWorking) {
                wipe();
                try {
                    update(Runtime.TimeSinceLastRun.TotalSeconds);
                } catch (Exception e) {
                    print($"Error: {e.Message}\n{e.StackTrace}");
                    shutdown();
                }
            } else shutdown();
        }
    } else {
        if (argument == "start" && state != 1) {
            state = 1;
            init();
            if (!allWorking) shutdown();
        } else if (argument == "stop" && state > 0) {
            shutdown();
            state = 0;
            Echo("offline"); wipe(); print($"Clang drive shut down");
        }
    }
}