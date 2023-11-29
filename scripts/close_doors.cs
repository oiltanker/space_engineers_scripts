public const float TIME_CLOSE_MS = 2000f;

public bool active = true;
public class airlock {
    public IMyDoor first;
    public IMyDoor second;
    public airlock(IMyDoor first, IMyDoor second) {
        this.first = first;
        this.second = second;
    }
    public bool has(IMyDoor door) => first == door || second == door;
    public void enableOther(IMyDoor door) {
        if (first  == door) second.Enabled = true;
        if (second == door) first.Enabled  = true;
    }
    public void disableOther(IMyDoor door) {
        if (first  == door) second.Enabled = false;
        if (second == door) first.Enabled  = false;
    }
}
public List<airlock> airlocks = null;
public Dictionary<IMyDoor, float> doorsToClose = new Dictionary<IMyDoor, float>();
public IMyTextSurface mySurface = null;

public void print(string str) => mySurface.WriteText(str + "\n", true);
public void wipe() => mySurface.WriteText("", false);

public string serialize() {
    string str = active.ToString();
    foreach(var dv in doorsToClose) str += '\n' + dv.Key.EntityId.ToString() + '\t' + dv.Value.ToString();
    return str;
}

public void deserialize(string str) {
    string[] lines = str.Split('\n');
    try {
        active = bool.Parse(lines[0]);
        for (int i = 1; i < lines.Length; i++) {
            if (string.IsNullOrEmpty(lines[i])) continue;

            string[] kv = lines[i].Split('\t');
            IMyDoor d = (IMyDoor) GridTerminalSystem.GetBlockWithId(Int64.Parse(kv[0]));
            if (d == null || d is IMyAirtightHangarDoor) continue;
            doorsToClose[d] = float.Parse(kv[1]);
        }
    } catch (Exception e) {
        Echo(e.Message + "\n\n" + e.StackTrace.ToString());
        doorsToClose = new Dictionary<IMyDoor, float>();
    }
}

public Program() {
    Echo("");
    if (!string.IsNullOrEmpty(Storage)) deserialize(Storage);

    mySurface = Me.GetSurface(0);
    mySurface.ContentType = ContentType.TEXT_AND_IMAGE;
    wipe();
    print("Door closing initialized.\n    start - starts the process\n    stop - stops the script");
    if (active) {
        Runtime.UpdateFrequency = UpdateFrequency.Update10;
        updateDoors();
    }
}

public void Save() {
    Storage = serialize();
}

public void runCheck() {
    var delta = (float) Runtime.TimeSinceLastRun.TotalMilliseconds;
    print($"Time since last check: {delta}ms");
    var dkeys = doorsToClose.Keys.ToList();
    foreach (var door in dkeys) {
        if (!door.IsWorking) doorsToClose.Remove(door);
        if (door.Status == DoorStatus.Closing) doorsToClose[door] = 0f;
        else if (door.Status == DoorStatus.Closed) {
            if (door.Enabled) {
                var aLck = airlocks.FirstOrDefault(a => a.has(door));
                if (aLck != null) aLck.enableOther(door);
            }
            doorsToClose[door] = 0f;
        } else {
            var aLck = airlocks.FirstOrDefault(a => a.has(door));
            if (aLck != null) aLck.disableOther(door);
            doorsToClose[door] += delta;
            if (doorsToClose[door] >= TIME_CLOSE_MS) door.CloseDoor();
        }
    }
    print("Closed opened doors");
}

public void updateDoors() {
    print("Updating doors ...");
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    doorsToClose = new Dictionary<IMyDoor, float>(); 
    blocks.Where(b => b is IMyDoor && !(b is IMyAirtightHangarDoor) && b.IsSameConstructAs(Me)).Select(b => b as IMyDoor).ToList().ForEach(d => {
        if (!doorsToClose.ContainsKey(d)) doorsToClose.Add(d, 0f);
    });

    airlocks = new List<airlock>();
    var used = new List<IMyDoor>();
    foreach (var d in doorsToClose.Keys) {
        if (used.Contains(d)) continue;
        foreach (var d2 in doorsToClose.Keys) {
            if (used.Contains(d2)) continue;
            if ((d.Position - d2.Position).Length() == 1) {
                used.Add(d);
                used.Add(d2);
                airlocks.Add(new airlock(d, d2));
            }
        }
    }
}

int runTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (!string.IsNullOrEmpty(argument)) {
        if (argument == "start" && !active) {
            updateDoors();
            print("Door closing started ....");
            active = true;
            Runtime.UpdateFrequency = UpdateFrequency.Update10;
        } else if (argument == "stop" && active) {
            active = false;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            print("Door closing halted ....");
        }
    } else if (active) {
        wipe();
        try {
            if (runTick >= 20) {
                updateDoors();
                runTick = 0;
            } else runTick++;
            runCheck();
        } catch (Exception e) {
            print(e.Message + "\n\n" + e.StackTrace.ToString());
        }
    }
}