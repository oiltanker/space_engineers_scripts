@import lib.eps
@import lib.printFull
@import lib.grid
@import lib.activeStabilization
@import lib.pid

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@mdrive(\s|$)");
public const float pVel = 5f;
public const double maxSpeed = 260d;

public enum force { none, positive, negative }
public enum aState { unasigned, begin, end, extending, retracting }
public struct arm {
    public const float maxArmExtension = 1.5f;
    public aState state { get; private set; }
    private IMyPistonBase piston;
    private List<IMyShipMergeBlock> merges;
    public float velocity { get { return piston.Velocity; } set { piston.Velocity = value; } }
    public bool enabled { get { return merges.First().Enabled; } set { merges.ForEach(m => m.Enabled = value); } }
    public arm(IMyPistonBase piston, List<IMyShipMergeBlock> merges) { this.piston = piston; this.merges = merges; state = aState.unasigned; }
    public void update() {
        piston.Enabled = true; piston.MaxLimit = maxArmExtension;
        if      (piston.CurrentPosition <= 0f              && piston.Velocity < 0f) state = aState.begin;
        else if (piston.CurrentPosition >= maxArmExtension && piston.Velocity > 0f) state = aState.end;
        else if (piston.Velocity > 0f)                                              state = aState.extending;
        else if (piston.Velocity < 0f)                                              state = aState.retracting;
        else state = aState.unasigned;
    }
}
public class aPump {
    public arm arm1;
    public arm arm2;
    public aPump(arm arm1, arm arm2) { this.arm1 = arm1; this.arm2 = arm2; }
    private void setEnabled(bool enabled1, bool enabled2) { arm1.enabled = enabled1; arm2.enabled = enabled2; }
    private void setVelocity(float vel1, float vel2) { arm1.velocity = vel1; arm2.velocity = vel2; }
    public void applyForce(force forceMode, float scale) {
        arm1.update();
        arm2.update();

        if (arm1.state == aState.unasigned || arm2.state == aState.unasigned) {
            setEnabled(false, false);
            setVelocity(-pVel, pVel);
            return;
        }

        if        (forceMode == force.none) {
            setEnabled(false, false);
            setVelocity(-pVel, pVel);
        } else if (forceMode == force.positive) {
            if        (arm2.state == aState.end || arm1.state == aState.extending) {
                arm1.enabled = true;
                if (arm2.state != aState.begin) arm2.enabled = false;
                else arm2.enabled = true;
                setVelocity(pVel * scale, -pVel);
            } else if (arm1.state == aState.end || arm2.state == aState.extending) {
                arm2.enabled = true;
                if (arm1.state != aState.begin) arm1.enabled = false;
                else arm1.enabled = true;
                setVelocity(-pVel, pVel * scale);
            }
        } else if (forceMode == force.negative) {
            if        (arm2.state == aState.begin || arm1.state == aState.retracting) {
                arm1.enabled = true;
                if (arm2.state != aState.end) arm2.enabled = false;
                else arm2.enabled = true;
                setVelocity(-pVel * scale, pVel);
            } else if (arm1.state == aState.begin || arm2.state == aState.retracting) {
                arm2.enabled = true;
                if (arm1.state != aState.end) arm1.enabled = false;
                else arm1.enabled = true;
                setVelocity(pVel, -pVel * scale);
            }
        }
    }
}

public int state = 0;
public bool allWorking = false;
public IMyShipController controller = null;
public Dictionary<dir, aPump> aMap = null;
public gStableArr gStabilizator = null;
public const float maxMul = 0.8f;
public float accelMul = 0f;
public int speed;

public static pidCtrl newDefaultPid() => new pidCtrl(1d, 0.05d, 0.3d, 1d / ENG_UPS,  0.95d);
public pidCtrl fPid = newDefaultPid(); public pidCtrl lPid = newDefaultPid(); public pidCtrl uPid = newDefaultPid();
public Vector3D sPos = Vector3D.Zero;

public void setAccelMul(int _speed) {
    speed = Math.Min(Math.Max(_speed, 0), 100);
    accelMul = (float) speed / 100f * maxMul;
}

public void decide(Vector3D vel, double move, Vector3D direction, dir normal, dir reverse, double maxSpeed, pidCtrl dirPid) {
    var dirVel = vel.Dot(direction);
    var velMag = Math.Abs(dirVel);
    var velMul = Math.Min((float) (maxSpeed - velMag) / 10f, 1f);
    var mul = Math.Min((float) velMag / 10f, 1f) * maxMul;
    print($" - achieving {normal.ToString("G")} speed of {(Math.Sign(dirVel) * maxSpeed).ToString("0.00")}");
    print($"Math.Abs(move) {Math.Abs(move)}");
    if (Math.Abs(move) > dEPS) { // override forward-backward
        if        (move > 0d) {
            var scale = dirVel <  5d && velMul > 0f ? accelMul * velMul : mul;
            print($"scale {scale}");
            if      (aMap.ContainsKey(normal )) aMap[normal ].applyForce(velMul > 0f ? force.negative : force.positive, scale);
            else if (aMap.ContainsKey(reverse)) aMap[reverse].applyForce(velMul > 0f ? force.positive : force.negative, scale);
        } else if (move < 0d) {
            var scale = dirVel > -5d && velMul > 0f ? accelMul * velMul : mul;
            print($"scale {scale}");
            if      (aMap.ContainsKey(normal )) aMap[normal ].applyForce(velMul > 0f ? force.positive : force.negative, scale);
            else if (aMap.ContainsKey(reverse)) aMap[reverse].applyForce(velMul > 0f ? force.negative : force.positive, scale);
        }
    } else if (controller.DampenersOverride) { // dampening forward-backward
        if (velMag > 5d) {
            if        (dirVel > 0d) {
                if      (aMap.ContainsKey(normal )) aMap[normal ].applyForce(force.negative, mul);
                else if (aMap.ContainsKey(reverse)) aMap[reverse].applyForce(force.positive, mul);
            } else if (dirVel < 0d) {
                if      (aMap.ContainsKey(normal )) aMap[normal ].applyForce(force.positive, mul);
                else if (aMap.ContainsKey(reverse)) aMap[reverse].applyForce(force.negative, mul);
            }
        } else {
            mul = (float) -dirPid.control(dirVel * 1f) / 100f;
            if      (aMap.ContainsKey(normal )) aMap[normal ].applyForce(mul > 0f ? force.positive : force.negative,  Math.Abs(mul));
            else if (aMap.ContainsKey(reverse)) aMap[reverse].applyForce(mul > 0f ? force.negative : force.positive,  Math.Abs(mul));
        }
        
    } else {
        if      (aMap.ContainsKey(normal )) aMap[normal ].applyForce(force.none, 0f);
        else if (aMap.ContainsKey(reverse)) aMap[reverse].applyForce(force.none, 0f);
    }
}
public double gMul = 1d;
public void update() {
    var mat = controller.WorldMatrix;
    var vel = controller.GetShipVelocities().LinearVelocity; // vector
    var gVec = controller.GetNaturalGravity();
    var mov = controller.MoveIndicator;

    print($"current speed: {speed}");
    print("stabilizing ...");
    print($"roll: {(-controller.RollIndicator).ToString("0.000")}    pitch: {controller.RotationIndicator.X.ToString("0.000")}    yaw: {(-controller.RotationIndicator.Y).ToString("0.000")}");
    gStabilizator?.update();
    print($"applying forces ...  (gavity multiplier: {gMul.ToString("0.000")})");
    print($"forward: {mov.Z.ToString("0.000")}    left: {mov.X.ToString("0.000")}    up: {(-mov.Y).ToString("0.000")}");

    if (gVec.Length() > dEPS && mov.Length() < dEPS && controller.DampenersOverride) {
        var vVel = vel.Dot(mat.Up);
        if (Math.Abs(vVel) < 5d) {
            gMul -= vVel * 0.01d;
        }
    }
    if (vel.Length() < 1d && mov.Length() < dEPS) {
        if (sPos.IsZero()) sPos = mat.Translation;
        vel = mat.Translation - sPos;
        print($"fine adjusting to {vel.X.ToString("0.000")}, {vel.Y.ToString("0.000")}, {vel.Z.ToString("0.000")}");
    } else sPos = mat.Translation;

    var nMaxSpeed = maxSpeed / (double) Math.Max(mov.Length(), 1f);
    vel += gVec * gMul;
    decide(vel,  mov.Z, mat.Forward, dir.forward, dir.backward, nMaxSpeed, fPid);
    decide(vel,  mov.X, mat.Left,    dir.left,    dir.right,    nMaxSpeed, lPid);
    decide(vel, -mov.Y, mat.Up,      dir.up,      dir.down,     nMaxSpeed, uPid);
    print($"forward: {mov.Z.ToString("0.000")}    left: {mov.X.ToString("0.000")}    up: {(-mov.Y).ToString("0.000")}");
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
            aMap = new Dictionary<dir, aPump>();
            foreach (var ap in blocks.Where(b => b is IMyPistonBase && b.IsFunctional && tagRegex.IsMatch(b.CustomName)).Cast<IMyPistonBase>().GroupBy(p => matchDirClosest(p.WorldMatrix.Down, mat))) {
                var piston1 = ap.AsEnumerable().First(); var piston2 = ap.AsEnumerable().Skip(1).First();
                aMap.Add(ap.Key, new aPump(
                    new arm(piston1, blocks.Where(b => b is IMyShipMergeBlock && b.CubeGrid == piston1.TopGrid && b.IsFunctional).Cast<IMyShipMergeBlock>().ToList()),
                    new arm(piston2, blocks.Where(b => b is IMyShipMergeBlock && b.CubeGrid == piston2.TopGrid && b.IsFunctional).Cast<IMyShipMergeBlock>().ToList())
                ));
            }
        } catch (Exception e) {
            print($"Wrongly built merge drive: could not evaluate components\n{e.Message}"); Echo("error");
            return;
        }

        gStabilizator?.release();
        var gyros = blocks.Where(b => b is IMyGyro && b.CubeGrid == Me.CubeGrid && b.IsWorking && tagRegex.IsMatch(b.CustomName)).Select(b => b as IMyGyro);
        if (gyros.Count() > 0) gStabilizator = new gStableArr(gyros, controller);

        foreach (var b in blocks.Where(b => b is IMyThrust && b.CubeGrid == controller.CubeGrid && tagRegex.IsMatch(b.CustomName))) (b as IMyThrust).Enabled = false;

        if (
            (aMap.ContainsKey(dir.forward) != aMap.ContainsKey(dir.backward) || (!aMap.ContainsKey(dir.forward) && !aMap.ContainsKey(dir.backward))) &&
            (aMap.ContainsKey(dir.left) != aMap.ContainsKey(dir.right) || (!aMap.ContainsKey(dir.left) && !aMap.ContainsKey(dir.right))) &&
            (aMap.ContainsKey(dir.up) != aMap.ContainsKey(dir.down) || (!aMap.ContainsKey(dir.up) && !aMap.ContainsKey(dir.down)))
        ) { allWorking = true; Echo($"speed\n{speed}"); }
        else { print("Wrongly built merge drive: wrong components"); Echo("error"); }
    } else { print("No main controller."); Echo("error"); }
}

public void shutdown() {
    if (aMap != null) foreach (var a in aMap.Values) a.applyForce(force.none, 0f);
    gStabilizator?.release();

    aMap = null; controller = null; gStabilizator = null;
    allWorking = false;
}

const string pName = "@mdrive program";
public Program() {
    Echo("");
    if (!Me.CustomName.StartsWith(pName)) Me.CustomName = pName;
    initMeLcd();

    if (!string.IsNullOrEmpty(Storage)) {
        try {
            var strs = Storage.Split(';');
            state = int.Parse(strs[0]);
            setAccelMul(int.Parse(strs[1]));
        } catch (Exception e) {
            state = 0;
            setAccelMul(65);
        }
    } else setAccelMul(65);
    if (state == 1) {
        Echo($"speed\n{speed}");
        init();
        Runtime.UpdateFrequency = UpdateFrequency.Update1;
    } else {
        findDebugLcd(getBlocks(b => b.IsSameConstructAs(Me)), tagRegex);
        Echo("offline");
        wipe();
        print("Merge drive shut down.");
    }
}

public void Save() => Storage = state.ToString() + ";" + speed.ToString();

public static readonly @Regex speedCmd = new @Regex(@"^speed\s*(\d+)\s*$");
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
            } else shutdown();
        }
    } else {
        if (argument == "start" && state != 1) {
            state = 1;
            init();
            if (!allWorking) shutdown();
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            Echo($"speed\n{speed}");
        } else if (argument == "stop" && state > 0) {
            shutdown();
            state = 0;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("offline");
            wipe();
            print("Merge drive shut down.");
        } else if (speedCmd.IsMatch(argument)) {
            var match = speedCmd.Match(argument);
            setAccelMul(int.Parse(match.Groups[1].ToString()));
            Echo($"speed\n{speed}");
        }
    }
}
