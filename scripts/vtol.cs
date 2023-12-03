@import lib.eps
@import lib.printFull
@import lib.alignment
@import lib.gyroArray
@import lib.pid

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@vtol(\s|$)");

public class vtol {
    public dir orientation;
    public IMyMotorStator rotor;
    public Dictionary<dir, List<IMyThrust>> thrusters;
    public vtol(dir orientation, IMyMotorStator rotor, List<IMyTerminalBlock> blocks) {
        if (orientation != dir.left && orientation != dir.right) throw new ArgumentException($"vtol only supports left or right rotors, got {orientation.ToString("G")}");
        this.orientation = orientation;
        this.rotor = rotor;

        thrusters = new Dictionary<dir, List<IMyThrust>>();
        var cGrid = rotor.TopGrid; var mat = rotor.Top.WorldMatrix;
        foreach (var dt in blocks.Where(b => b is IMyThrust && b.CubeGrid == cGrid).Cast<IMyThrust>().GroupBy(t => matchDir(t.WorldMatrix.Forward, mat))) {
            thrusters.Add(dt.Key, dt.AsEnumerable().ToList());
        }
    }
    private void decide(Vector3D vel, double move, Vector3D direction, dir normal, dir reverse, bool dampening) {
        var dirVel = vel.Dot(direction);
        var velMag = Math.Abs(dirVel);
        var mul = Math.Min((float) velMag / 2f, 1f);
        if (Math.Abs(move) > dEPS) { // override forward-backward
            if      (move > 0d && thrusters.ContainsKey(normal )) thrusters[normal ].ForEach(t => t.ThrustOverridePercentage = 1f);
            else if (move < 0d && thrusters.ContainsKey(reverse)) thrusters[reverse].ForEach(t => t.ThrustOverridePercentage = 1f);
        } else if (dampening && velMag > dEPS) { // dampening forward-backward
            if      (vel.Dot(direction) > 0d && thrusters.ContainsKey(normal )) thrusters[normal ].ForEach(t => t.ThrustOverridePercentage = mul);
            else if (vel.Dot(direction) < 0d && thrusters.ContainsKey(reverse)) thrusters[reverse].ForEach(t => t.ThrustOverridePercentage = mul);
        }
    }
    public void update(Vector3D gVec, bool dampening, Vector3D vel, Vector3 mov) {
        var mat = rotor.Top.WorldMatrix;
        var fVec = mat.Forward; var lVec = mat.Left; var uVec = mat.Up;

        var pgVec = Vector3D.ProjectOnPlane(ref gVec, ref uVec);
        var mul = Vector3D.Dot(lVec.Cross(pgVec), uVec);
        mul = Double.IsNaN(mul) ? 0d : Math.Sign(mul);
        var angle = mul != 0d ? mul * Math.Min(Vector3D.Angle(lVec, pgVec) * 10d, Math.PI) : 0d;

        foreach (var ts in thrusters.Values) ts.ForEach(t => t.ThrustOverridePercentage = EPS);
        if        (orientation == dir.left) {
            rotor.TargetVelocityRad = (float) -angle;
            decide(vel,  mov.Z, fVec,  dir.forward, dir.backward, dampening); // forward
            decide(vel,  mov.X, uVec,  dir.up,      dir.down,     dampening); // left
            decide(vel, -mov.Y, -lVec, dir.right,   dir.left,     dampening); // up
        } else if (orientation == dir.right) {
            rotor.TargetVelocityRad = (float) -angle;
            decide(vel,  mov.Z, -fVec, dir.backward, dir.forward, dampening); // forward
            decide(vel,  mov.X, -uVec, dir.down,     dir.up,      dampening); // left
            decide(vel, -mov.Y, -lVec, dir.right,    dir.left,    dampening); // up
        }
    }
    public void shutdown() {
        rotor.TargetVelocityRad = 0f;
        foreach (var ts in thrusters.Values) ts.ForEach(t => t.ThrustOverride = 0f);
    }
}
public class cRot {
    public Vector3D forward;
    public Vector3D left;
    public cRot(Vector3D forward, Vector3D left) { this.forward = forward; this.left = left; }
}

public int state = 0;
public bool allWorking = false;
public IMyShipController controller = null;
public wGyroArr gyroArray = null;
public List<vtol> vtols = null;
public cRot rotation = null;
public pidCtrl pidRoll = null;
public pidCtrl pidYaw = null;

public void update(double delta) {
    var gVec = controller.GetTotalGravity();
    print($"gForce: {gVec.Length().ToString("0.000")}");
    var vel = controller.GetShipVelocities().LinearVelocity + (Vector3D.Normalize(gVec) / 9d);
    var mov = controller.MoveIndicator;
    bool dampening = controller.DampenersOverride;
    print($"mov: {mov.X.ToString("0")}, {mov.Y.ToString("0")}, {mov.Z.ToString("0")}");
    print($"gVec: {gVec.X.ToString("0.000")}, {gVec.Y.ToString("0.000")}, {gVec.Z.ToString("0.000")}");

    vtols.ForEach(v => v.update(gVec, dampening, vel, mov));
    if (gyroArray != null) {
        var mat = controller.WorldMatrix;
        //var dVec = (new List<Vector3D>{ mat.Forward, mat.Backward, mat.Up, mat.Down }).Min(v => (v - gVec).Length());
        var fVec = mat.Forward; var lVec = mat.Left; var uVec = mat.Up;
        var ifVec = Vector3D.Normalize(mat.Left.Cross(gVec));
        print($"{ifVec.Dot(fVec)}    {ifVec.Dot(uVec)}");
        var rMul = ifVec.Dot(fVec); rMul = Double.IsNaN(rMul) ? 0d : rMul;
        var yMul = ifVec.Dot(uVec); yMul = Double.IsNaN(yMul) ? 0d : yMul;
        var roll = Math.Abs(rMul) < 0.05d ? 0f : (float) (rMul * (Vector3D.Angle(Vector3D.ProjectOnPlane(ref gVec, ref fVec), lVec) * radToDegMul - 90));
        var yaw  = Math.Abs(yMul) < 0.05d ? 0f : (float) (yMul * (Vector3D.Angle(Vector3D.ProjectOnPlane(ref gVec, ref uVec), lVec) * radToDegMul - 90));
        print($"roll: {roll.ToString("0.000")}    yaw: {yaw.ToString("0.000")}");

        var yRoll = controller.RotationIndicator.Y * Math.Sign(rMul) * (1d - Math.Abs(rMul)); var yYaw = controller.RotationIndicator.Y * Math.Sign(yMul) * (1d - Math.Abs(yMul));
        print($"yRoll: {yRoll.ToString("0.000")}    yYaw: {yYaw.ToString("0.000")}");

        gyroArray.capture();
        gyroArray.setRPY((float) (pidRoll.control(roll, delta) - yRoll), (float) controller.RotationIndicator.X, (float) (pidYaw.control(-yaw, delta) - yYaw));
    }
}

public void findDebugLcd(List<IMyTerminalBlock> blocks) {
    debugLcd = blocks.FirstOrDefault(b => b is IMyTextPanel && tagRegex.IsMatch(b.CustomName) && b.IsSameConstructAs(Me)) as IMyTextPanel;
    if (debugLcd != null) {
        debugLcd.ContentType = ContentType.TEXT_AND_IMAGE;
        debugLcd.FontSize = 0.8f;
        debugLcd.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
    }
}

public static pidCtrl newDefaultPid() => new pidCtrl(5d, 0.1d, 1d, 1d / ENG_UPS, 0.95d);
public void init() {
    allWorking = false;

    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    findDebugLcd(blocks);
    wipe();
    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName) && b.CubeGrid == Me.CubeGrid) as IMyShipController;
    if (controller != null) {
        try {
            vtols = blocks.Where(b => b is IMyMotorStator && b.CubeGrid == controller.CubeGrid && tagRegex.IsMatch(b.CustomName))
                          .Select(b => new vtol(matchDir(b.WorldMatrix.Up, controller.WorldMatrix), b as IMyMotorStator, blocks)).ToList();

            rotation = new cRot(controller.WorldMatrix.Forward, controller.WorldMatrix.Left);

            pidRoll = newDefaultPid();
            pidYaw  = newDefaultPid();
            gyroArray?.release();
            var gyros = blocks.Where(b => b is IMyGyro && b.CubeGrid == controller.CubeGrid).Cast<IMyGyro>();
            if (gyros.Count() > 0) gyroArray = new wGyroArr(gyros, controller);

            if (vtols.Count > 0) { allWorking = true; Echo("ok"); }
            else { print("No vtol components detected"); Echo("error"); }
        } catch (Exception e) { print($"Error initializing vtols: {e.Message}"); Echo("error"); }
    } else { print("No vtol controller"); Echo("error"); }
}

public void shutdown() {
    vtols?.ForEach(v => v.shutdown());
    gyroArray?.release();
    vtols = null; gyroArray = null; rotation = null;
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
        findDebugLcd(blocks);
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
                wipe();
                update(Runtime.TimeSinceLastRun.TotalSeconds);
            } else shutdown();
        }
    } else {
        if (argument == "start" && state != 1) {
            state = 1;
            init();
            if (!allWorking) shutdown();
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        } else if (argument == "stop" && state > 0) {
            shutdown();
            state = 0;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("offline"); wipe(); print("offline");
        }
    }
}