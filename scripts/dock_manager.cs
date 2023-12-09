@import lib.eps
@import lib.printFull

public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@dock(-(charge|onoff|run|trigger|start|lock))?(\s|$)");

public class dAct {
    public enum aType { charge, onoff, run, trigger, start, slock }
    public aType type;
    public IMyTerminalBlock block;
    public dAct(aType type, IMyTerminalBlock block) {
        if      (!(block is IMyBatteryBlock)      && type == aType.charge)                           throw new ArgumentException("Not a bettery");
        else if (!(block is IMyProgrammableBlock) && type == aType.run)                              throw new ArgumentException("Not a programming block");
        else if (!(block is IMyTimerBlock)        && (type == aType.trigger || type == aType.start)) throw new ArgumentException("Not a timer block");
        else if (!(block is IMyFunctionalBlock)   && type == aType.onoff)                            throw new ArgumentException("Not a functional block");
        else if (!(block is IMyMotorStator)       && type == aType.slock)                             throw new ArgumentException("Not a stator");

        this.type = type; this.block = block;
    }
    public bool tryAct(bool docked) {
        try {
            if (type == aType.charge) {
                if (docked) (block as IMyBatteryBlock).ChargeMode = ChargeMode.Recharge;
                else (block as IMyBatteryBlock).ChargeMode = ChargeMode.Auto;
            } else if (type == aType.trigger) {
                if (docked) (block as IMyTimerBlock).Trigger();
                else (block as IMyTimerBlock).Trigger();
            } else if (type == aType.start) {
                if (docked) (block as IMyTimerBlock).StopCountdown();
                else (block as IMyTimerBlock).StartCountdown();
            } else if (type == aType.run) {
                if (docked) (block as IMyProgrammableBlock).TryRun("stop");
                else (block as IMyProgrammableBlock).TryRun("start");
            } else if (type == aType.slock) {
                if (docked) (block as IMyMotorStator).RotorLock = true;
                else (block as IMyMotorStator).RotorLock = false;
            } else if (type == aType.onoff) {
                if (docked) (block as IMyFunctionalBlock).Enabled = false;
                else (block as IMyFunctionalBlock).Enabled = true;
            }
            return true;
        } catch (Exception e) {
            print((docked ? "Docking" : "Undocking") + $" action failed for {block.CustomName}:\n  {e.Message}");
            return false;
        }
    }
    public override string ToString() => $"{block.CustomName}  |  {type.ToString("G")}";
}

public int state = 0;
public int dockState = 0;
public bool allOk = false;
public List<dAct> actions = null;
public List<IMyShipConnector> connectors = null;
public List<IMyLandingGear> landingGear = null;

public void update() {
    var newState = connectors.Any(c => c.IsConnected) || landingGear.Any(l => l.IsLocked) ? 1 : 0;
    if (dockState == -1) dockState = newState;
    if (dockState != newState) {
        dockState = newState;
        var succ = actions.Count(a => a.tryAct(newState == 1));
        print($"\n{actions.Count - succ}/{actions.Count} actions failed");
    } else actions.ForEach(a => print($"{a}"));
}

public static dAct.aType getAType(@Match match) {
    if (match.Groups[2].Success) {
        var typeStr = match.Groups[3].Value;
        if      (typeStr == "charge")  return dAct.aType.charge;
        else if (typeStr == "onoff")   return dAct.aType.onoff;
        else if (typeStr == "run")     return dAct.aType.run;
        else if (typeStr == "trigger") return dAct.aType.trigger;
        else if (typeStr == "start")   return dAct.aType.start;
        else if (typeStr == "lock")    return dAct.aType.slock;
    }
    return dAct.aType.onoff;
}
public bool init() {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);
    var blockGroups = new List<IMyBlockGroup>();
    GridTerminalSystem.GetBlockGroups(blockGroups);

    allOk = false;

    findDebugLcd(blocks, tagRegex);
    wipe();

    try {
        connectors = blocks.Where(b => b is IMyShipConnector && b.IsSameConstructAs(Me)).Cast<IMyShipConnector>().ToList();
        landingGear = blocks.Where(b => b is IMyLandingGear && b.IsSameConstructAs(Me)).Cast<IMyLandingGear>().ToList();
        actions = new List<dAct>();
        var added = new List<IMyTerminalBlock>();
        foreach (var b in blocks) {
            if (b == debugLcd) continue;

            var match = tagRegex.Match(b.CustomName);
            if (match.Success) {
                added.Add(b);
                actions.Add(new dAct(getAType(match), b));
            }
        }
        foreach (var bg in blockGroups) {
            var match = tagRegex.Match(bg.Name);
            if (match.Success) {
                var type = getAType(match);
                var bs = new List<IMyTerminalBlock>();
                bg.GetBlocks(bs);
                foreach (var b in bs) {
                    if (!added.Contains(b)) {
                        added.Add(b);
                        actions.Add(new dAct(type, b));
                    }
                }
            }
        }
    } catch (Exception e) {
        print($"Wrongly built docking: could not evaluate components\n{e.Message}"); Echo("error");
        return false;
    }

    if ((connectors.Count > 0 || landingGear.Count > 0) && actions.Count > 0) {
        allOk = true;
        Echo($"ok");
        return true;
    } else {
        print("Wrong docking configuration");
        if ((connectors.Count <= 0 && landingGear.Count <= 0)) print("No docking/landing blocks");
        if (actions.Count <= 0) print("No docking actions detected");
        Echo("error");
    }

    return false;
}

public void shutdown() {
    connectors = null; landingGear = null; actions = null;
    allOk = false;
}

const string pName = "@docking program";
public Program() {
    Echo("");
    if (!Me.CustomName.StartsWith(pName)) Me.CustomName = pName;
    initMeLcd();

    if (!string.IsNullOrEmpty(Storage)) try {
        var strs = Storage.Split(';');
        state = int.Parse(strs[0]);
        dockState = int.Parse(strs[1]);
    } catch (Exception e) {
        state = 0; dockState = -1;
    }

    if (state == 1) {
        if (!init()) shutdown();
        Runtime.UpdateFrequency = UpdateFrequency.Update10;
    } else {
        var blocks = new List<IMyTerminalBlock>();
        GridTerminalSystem.GetBlocks(blocks);
        findDebugLcd(blocks, tagRegex);

        Echo("offline"); wipe(); print("docking manager shut down");
    }
}

public void Save() => Storage = $"{state};{dockState}";

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update10) {
        if (state == 1) {
            refreshTick++;
            if (refreshTick >= 20) {
                if (!init()) shutdown();
                refreshTick = 0;
            }
            if (allOk) {
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
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        } else if (argument == "stop" && state > 0) {
            shutdown();
            state = 0;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("offline"); wipe(); print("<device_name> shut down");
        }
    }
}