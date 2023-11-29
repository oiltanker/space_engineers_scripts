@import lib.eps
@import lib.printFull
@import lib.gyroWrapper

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@cdrive(\s|$)");
public const float onVel = 5f;
public const float offVel = -5f;

public int state = 0;
public bool allWorking = false;
public IMyShipController controller = null;
public Dictionary<dir, IMyPistonBase> pMap = null;
public wGyroArr gFirst  = null;
public wGyroArr gSecond = null;


public bool checkGyros() {
    var cRoll = controller.RollIndicator; var cPitch = controller.RotationIndicator.X; var cYaw = controller.RotationIndicator.Y;
    print($"cRoll: {cRoll.ToString("0.000")}   cPitch: {cPitch.ToString("0.000")}   cYaw: {cYaw.ToString("0.000")}");

    if (gFirst.count + gSecond.count < 2) return true;

    Action<wGyroArr, wGyroArr> transferTo = (from, to) => {
        int toTransfer = (from.count - to.count) / 2;
        if (toTransfer > 0) {
            var gs = from.gyros.Take(toTransfer);
            from.remove(gs, false, false);
            to.add(gs);
        } else {
            var g = from.gyros.First();
            from.remove(g);
        }
    };
    if      (gFirst.count  > gSecond.count) transferTo(gFirst,  gSecond);
    else if (gSecond.count > gFirst.count)  transferTo(gSecond, gFirst);

    gFirst.capture();
    gSecond.capture();

    var pRoll  = cRoll  < -EPS ? -60f : 60f; var nRoll  = cRoll  > EPS ? 60f : -60f;
    var pPitch = cPitch < -EPS ? -60f : 60f; var nPitch = cPitch > EPS ? 60f : -60f;
    var pYaw   = cYaw   < -EPS ? -60f : 60f; var nYaw   = cYaw   > EPS ? 60f : -60f;

    gFirst.setRPY (pRoll, pPitch, pYaw);
    gSecond.setRPY(nRoll, nPitch, nYaw);
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

        if (gFirst != null) gFirst.release();
        if (gSecond != null) gSecond.release();
        var gyros = blocks.Where(b => b is IMyGyro && b.CubeGrid == Me.CubeGrid && b.IsWorking && tagRegex.IsMatch(b.CustomName)).Select(b => b as IMyGyro).ToList();
        if (gyros.Count > 0) {
            int cnt = (gyros.Count % 2 == 0 ? gyros.Count : gyros.Count - 1) / 2;
            gFirst  = new wGyroArr(gyros.GetRange(0,   cnt), mat);
            gSecond = new wGyroArr(gyros.GetRange(cnt, cnt), mat);
        }

        blocks.Where(b => b is IMyThrust && b.CubeGrid == controller.CubeGrid && tagRegex.IsMatch(b.CustomName)).ToList().ForEach(b => (b as IMyThrust).Enabled = false);

        if (pMap.Count == 6) allWorking = true;
    } else print("No main controller.");
}

public void shutdown() {
    foreach (var p in pMap.Values) p.Velocity = -5f;
    gFirst.release();
    gSecond.release();

    pMap = null; controller = null; gFirst = null; gSecond = null;
    allWorking = false;
}

public Program() {
    Echo("");
    Me.CustomName = "@cdrive program";
    initMeLcd();

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