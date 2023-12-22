public const int releaseTick = 1;
public const int resetTick = 6;

@import lib.eps
@import lib.printFull
@import lib.grid

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@cgun(\s|$)");


public int state = 0;
public bool allOk = false;
public bool isReady = false;
public IMyShipMergeBlock merge = null;
public IMyMotorStator rotor = null;
public IMyShipWelder welder = null;
public IMyProjector proj = null;
public List<IMyPistonBase> pistons = null;

public IEnumerable<IMyWarhead> getWarheads() => getBlocks(b => b is IMyWarhead && b.IsSameConstructAs(Me) && tagRegex.IsMatch(b.CustomName)).Cast<IMyWarhead>();

public bool resetLogic() {
    merge.Enabled = true;
    welder.Enabled = true;
    if (rotor.IsAttached || pistons.Count(p => p.MaxLimit - p.CurrentPosition < EPS) == pistons.Count) {
        if (rotor.IsAttached) {
            if (pistons.Count(p => p.CurrentPosition - p.MinLimit < EPS) == pistons.Count) {
                if (merge.IsConnected && proj.RemainingBlocks + proj.RemainingArmorBlocks == 0) return true;
                else print("  awaiting projectile ...");
            } else { pistons.ForEach(p => p.Velocity = -5f); print($"  retracting pistons ({pistons.Count}) ..."); }
        } else { rotor.Attach(); print("  attaching rotor ..."); }
    } else { pistons.ForEach(p => p.Velocity = 5f); print($"  extending pistons ({pistons.Count}) ..."); }

    return false;
}

public void update(bool fire) {
    print($"State: {state}");
    if (state == 0) {
        if (isReady && fire) {
            isReady = false;
            init();
            if (!allOk) return;

            print($"firing ...");
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
            foreach (var w in getWarheads()) { w.IsArmed = true;  w.DetonationTime = 10f; w.StartCountdown(); }
            welder.Enabled = false;
            rotor.Detach();
            state = 1;
        } else {
            if (resetLogic()) {
                isReady = true;
                Runtime.UpdateFrequency = UpdateFrequency.Update100;
                print($"ready");
            } else {
                Runtime.UpdateFrequency = UpdateFrequency.Update1;
                print($"arming ...");
            }
        }
    }
    
    if (state > 0 && state < releaseTick + 1) {
        print($"firing ...");
        state++;
    } else if (state == releaseTick + 1) {
        print($"firing ...");
        merge.Enabled = false;
        state = releaseTick + 2;
    } else if (state > releaseTick + 1 && state < resetTick) {
        print($"firing ...");
        state++;
    } else if (state == resetTick) {
        print($"arming ...");
        if (resetLogic()) state = 0;
    }
}

public void init() {
    var blocks = getBlocks(b => b.IsSameConstructAs(Me));

    findDebugLcd(blocks, tagRegex);
    wipe();

    merge = blocks.FirstOrDefault(b => b is IMyShipMergeBlock && tagRegex.IsMatch(b.CustomName)) as IMyShipMergeBlock;
    rotor = blocks.FirstOrDefault(b => b is IMyMotorStator && tagRegex.IsMatch(b.CustomName)) as IMyMotorStator;
    welder = blocks.FirstOrDefault(b => b is IMyShipWelder && tagRegex.IsMatch(b.CustomName)) as IMyShipWelder;
    proj = blocks.FirstOrDefault(b => b is IMyProjector && tagRegex.IsMatch(b.CustomName)) as IMyProjector;
    pistons = blocks.Where(b => b is IMyPistonBase && tagRegex.IsMatch(b.CustomName)).Cast<IMyPistonBase>().ToList();
    if (merge == null || rotor == null || welder == null || proj == null || pistons.Count == 0) {
        print($"Some blocks do not exist (tag: '@cgun')\n  rotor: {rotor != null}\n  merge block: {merge != null}\n  welder: {welder != null}\n  projector: {proj != null}\n  pistons: {pistons.Count}");
        allOk = false;
    } else allOk = true;
}


const string pName = "@cgun program";
public Program() {
    Echo("");
    if (!Me.CustomName.StartsWith(pName)) Me.CustomName = pName;
    initMeLcd();
    foreach (var w in getWarheads()) { w.IsArmed = false; w.StopCountdown(); w.DetonationTime = 10f; }
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
    init();
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if      (refreshTick > 200) { init(); refreshTick = 0; }
    else if (updateSource == UpdateType.Update1) refreshTick += 1;
    else if (updateSource == UpdateType.Update100) refreshTick += 100;

    if (allOk) { wipe(); update(argument == "fire"); }
}