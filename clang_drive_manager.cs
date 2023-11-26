public const double dEPS = 0.001d;
public const float EPS = 0.01f;
public const int ENG_UPS = 60;
public const double radToDegMul = 180 / Math.PI;
public static readonly System.Text.RegularExpressions.Regex tagRegex = new System.Text.RegularExpressions.Regex(@"(\s|^)@cdrive(\s|$)");
public const float onVel = 5f;
public const float offVel = -5f;

public static IMyTextPanel debugLcd = null;
public static void wipe() { if (debugLcd != null) debugLcd.WriteText(""); }
public static void print(string str) { if (debugLcd != null) debugLcd.WriteText(str + '\n', true); }

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

public int state = 0;
public bool allWorking = false;
public IMyShipController controller = null;
public Dictionary<dir, IMyPistonBase> pMap = null;
public Dictionary<IMyGyro, align> gMap = null;

public float chooseRPY(dir dir, float roll, float pitch, float yaw) { // possibly faster that multiplying by dot products
    switch (dir) {
        case dir.forward:  return roll;
        case dir.backward: return -roll;
        case dir.left:     return pitch;
        case dir.right:    return -pitch;
        case dir.up:       return yaw;
        case dir.down:     return -yaw;
        default:           return Single.NaN;
    }
}
public bool checkGyros() {
    var cRoll = controller.RollIndicator; var cPitch = controller.RotationIndicator.X; var cYaw = controller.RotationIndicator.Y;
    print($"cRoll: {cRoll.ToString("0.000")}   cPitch: {cPitch.ToString("0.000")}   cYaw: {cYaw.ToString("0.000")}");
    // var cPitch = controller.RotationIndicator.X; var cYaw = controller.RotationIndicator.Y;
    // print($"cPitch: {cPitch.ToString("0.000")}   cYaw: {cYaw.ToString("0.000")}");
    foreach (var g in gMap.Keys) {
        g.Enabled = true;
        g.GyroOverride = true;
        g.Roll = 0f; g.Pitch = 0f; g.Yaw = 0f;
    }
    var wGs = gMap.Where(ga => ga.Key.IsWorking).ToList();
    var cnt = wGs.Count % 2 == 0 ? wGs.Count - 1 : wGs.Count - 2;
    var first = wGs.GetRange(0, cnt / 2); var second = wGs.GetRange(cnt / 2 + 1, cnt / 2);

    var pRoll  = cRoll  < -EPS ? -60f : 60f; var nRoll  = cRoll  > EPS ? 60f : -60f;
    // var pRoll  = 60f;                        var nRoll  = -60f;
    var pPitch = cPitch < -EPS ? -60f : 60f; var nPitch = cPitch > EPS ? 60f : -60f;
    var pYaw   = cYaw   < -EPS ? -60f : 60f; var nYaw   = cYaw   > EPS ? 60f : -60f;
    first.ForEach(ga => {
        var g = ga.Key; var a = ga.Value;
        g.Roll  = chooseRPY(a.forward, pRoll, pPitch, pYaw);
        g.Pitch = chooseRPY(a.left   , pRoll, pPitch, pYaw);
        g.Yaw   = chooseRPY(a.up     , pRoll, pPitch, pYaw);
    });
    second.ForEach(ga => {
        var g = ga.Key; var a = ga.Value;
        g.Roll  = chooseRPY(a.forward, nRoll, nPitch, nYaw);
        g.Pitch = chooseRPY(a.left   , nRoll, nPitch, nYaw);
        g.Yaw   = chooseRPY(a.up     , nRoll, nPitch, nYaw);
    });
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

        gMap = new Dictionary<IMyGyro, align>();
        blocks.Where(b => b is IMyGyro && b.IsSameConstructAs(Me) && b.IsFunctional && tagRegex.IsMatch(b.CustomName))
              .ToList().ForEach(b => gMap.Add(b as IMyGyro, align.determine(b.WorldMatrix, mat)));

        blocks.Where(b => b is IMyThrust && b.CubeGrid == controller.CubeGrid && tagRegex.IsMatch(b.CustomName)).ToList().ForEach(b => (b as IMyThrust).Enabled = false);

        if (pMap.Count == 6) allWorking = true;
    } else print("No main controller.");
}

public void shutdown() {
    foreach (var p in pMap.Values) p.Velocity = -5f;
    foreach (var g in gMap.Keys) {
        g.Roll = 0f; g.Pitch = 0f; g.Yaw = 0f;
        g.GyroOverride = false;
    }

    pMap = null; gMap = null; controller = null;
    allWorking = false;
}

public Program() {
    Echo("");
    Me.CustomName = "@cdrive program";
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