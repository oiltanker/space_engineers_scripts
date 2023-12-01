@import lib.eps
@import lib.printFull
@import lib.activeStabilization

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@cdrive(\s|$)");
public const float onVel = 5f;
public const float offVel = -5f;

public int state = 0;
public bool allWorking = false;
public IMyShipController controller = null;
public Dictionary<dir, IMyPistonBase> pMap = null;
public gStableArr gStabilizator = null;


public void update() {
    var mat = controller.WorldMatrix;
    var vel = controller.GetShipVelocities().LinearVelocity; // vector
    var mov = controller.MoveIndicator;

    print("stabilizing ...");
    gStabilizator?.update();
    print("applying forces ...");
    foreach (var p in pMap.Values) p.Enabled = true;

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
    allWorking = false;

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
            return;
        }

        gStabilizator?.release();
        var gyros = blocks.Where(b => b is IMyGyro && b.CubeGrid == Me.CubeGrid && b.IsWorking && tagRegex.IsMatch(b.CustomName)).Select(b => b as IMyGyro);
        if (gyros.Count() > 0) gStabilizator = new gStableArr(gyros, controller);

        foreach (var b in blocks.Where(b => b is IMyThrust && b.CubeGrid == controller.CubeGrid && tagRegex.IsMatch(b.CustomName))) (b as IMyThrust).Enabled = false;

        if (pMap.Count == 6) allWorking = true;
    } else print("No main controller.");
}

public void shutdown() {
    foreach (var p in pMap.Values) p.Velocity = -5f;
    gStabilizator?.release();

    pMap = null; controller = null; gStabilizator = null;
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
        }
    }
}