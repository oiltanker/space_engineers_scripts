@import lib.eps
@import lib.printFull
@import lib.activeStabilization

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@mdrive(\s|$)");
public const float pVel = 5f;

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
        print($"state1: {arm1.state.ToString("G")}    state2: {arm2.state.ToString("G")}");

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

public void setAccelMul(int _speed) {
    speed = Math.Min(Math.Max(_speed, 0), 100);
    accelMul = (float) speed / 100f * maxMul;
}

public void decide(Vector3D vel, double move, Vector3D direction, dir normal, dir reverse) {
    var dirVel = vel.Dot(direction);
    var velMag = Math.Abs(dirVel);
    var mul = Math.Min((float) velMag / 10f, 1f) * maxMul;
    if (Math.Abs(move) > dEPS) { // override forward-backward
        if        (move > 0d) {
            var scale = dirVel < 0d ? accelMul : mul;
            if      (aMap.ContainsKey(normal )) aMap[normal ].applyForce(force.negative, scale);
            else if (aMap.ContainsKey(reverse)) aMap[reverse].applyForce(force.positive, scale);
        } else if (move < 0d) {
            var scale = dirVel > 0d ? accelMul : mul;
            if      (aMap.ContainsKey(normal )) aMap[normal ].applyForce(force.positive, scale);
            else if (aMap.ContainsKey(reverse)) aMap[reverse].applyForce(force.negative, scale);
        }
    } else if (controller.DampenersOverride && velMag > 0.5d) { // dampening forward-backward
        if        (vel.Dot(direction) > 0d) {
            if      (aMap.ContainsKey(normal )) aMap[normal ].applyForce(force.negative, mul);
            else if (aMap.ContainsKey(reverse)) aMap[reverse].applyForce(force.positive, mul);
        } else if (vel.Dot(direction) < 0d) {
            if      (aMap.ContainsKey(normal )) aMap[normal ].applyForce(force.positive, mul);
            else if (aMap.ContainsKey(reverse)) aMap[reverse].applyForce(force.negative, mul);
        }
    } else {
        if      (aMap.ContainsKey(normal )) aMap[normal ].applyForce(force.none, 0f);
        else if (aMap.ContainsKey(reverse)) aMap[reverse].applyForce(force.none, 0f);
    }
}
public void update() {
    var mat = controller.WorldMatrix;
    var vel = controller.GetShipVelocities().LinearVelocity; // vector
    var mov = controller.MoveIndicator;

    print($"current speed: {speed}");
    print("stabilizing ...");
    print($"roll: {(-controller.RollIndicator).ToString("0.000")}    pitch: {controller.RotationIndicator.X.ToString("0.000")}    yaw: {(-controller.RotationIndicator.Y).ToString("0.000")}");
    gStabilizator?.update();
    print("applying forces ...");

    decide(vel,  mov.Z, mat.Forward, dir.forward, dir.backward);
    decide(vel,  mov.X, mat.Left,    dir.left,    dir.right);
    decide(vel, -mov.Y, mat.Up,      dir.up,      dir.down);
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
    allWorking = false;

    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    findDebugLcd(blocks);
    wipe();
    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName) && b.CubeGrid == Me.CubeGrid) as IMyShipController;
    if (controller != null) {
        var mat = controller.WorldMatrix;

        try {
            aMap = new Dictionary<dir, aPump>();
            foreach (var ap in blocks.Where(b => b is IMyPistonBase && b.IsSameConstructAs(Me) && b.IsFunctional && tagRegex.IsMatch(b.CustomName)).Cast<IMyPistonBase>().GroupBy(p => matchDir(p.WorldMatrix.Down, mat))) {
                var piston1 = ap.AsEnumerable().First(); var piston2 = ap.AsEnumerable().Skip(1).First();
                aMap.Add(ap.Key, new aPump(
                    new arm(piston1, blocks.Where(b => b is IMyShipMergeBlock && b.CubeGrid == piston1.TopGrid && b.IsFunctional).Cast<IMyShipMergeBlock>().ToList()),
                    new arm(piston2, blocks.Where(b => b is IMyShipMergeBlock && b.CubeGrid == piston2.TopGrid && b.IsFunctional).Cast<IMyShipMergeBlock>().ToList())
                ));
            }
        } catch (Exception e) {
            print("Wrongly built merge drive.");
            return;
        }

        gStabilizator?.release();
        var gyros = blocks.Where(b => b is IMyGyro && b.CubeGrid == Me.CubeGrid && b.IsWorking && tagRegex.IsMatch(b.CustomName)).Select(b => b as IMyGyro);
        if (gyros.Count() > 0) gStabilizator = new gStableArr(gyros, controller);

        foreach (var b in blocks.Where(b => b is IMyThrust && b.CubeGrid == controller.CubeGrid && tagRegex.IsMatch(b.CustomName))) (b as IMyThrust).Enabled = false;

        if (
            (aMap.ContainsKey(dir.forward) != aMap.ContainsKey(dir.backward)) &&
            (aMap.ContainsKey(dir.left) != aMap.ContainsKey(dir.right)) &&
            (aMap.ContainsKey(dir.up) != aMap.ContainsKey(dir.down))
        ) allWorking = true;
    } else print("No main controller.");
}

public void shutdown() {
    if (aMap != null) foreach (var a in aMap.Values) a.applyForce(force.none, 0f);
    gStabilizator?.release();

    aMap = null; controller = null; gStabilizator = null;
    allWorking = false;
}

public Program() {
    Echo("");
    Me.CustomName = "@mdrive program";
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
        var blocks = new List<IMyTerminalBlock>();
        GridTerminalSystem.GetBlocks(blocks);
        findDebugLcd(blocks);
        wipe();
        print("Merge drive shut down.");
        Echo("offline");
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
            wipe();
            Echo("offline");
            print("Merge drive shut down.");
        } else if (speedCmd.IsMatch(argument)) {
            var match = speedCmd.Match(argument);
            setAccelMul(int.Parse(match.Groups[1].ToString()));
            Echo($"speed\n{speed}");
        }
    }
}