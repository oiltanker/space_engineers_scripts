@import lib.eps
@import lib.printFull
@import lib.grid

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@<tag>(\s|$)");

public int state = 0;
public IMyShipController controller = null;

public void update() {
    // TODO: update component state
}

public bool init() {
    var blocks = getBlocks(b => b.IsSameConstructAs(Me));

    findDebugLcd(blocks, tagRegex);
    wipe();

    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName)) as IMyShipController;
    if (controller != null) {
        try {
        } catch (Exception e) {
            print($"Wrongly built <device_name>: could not evaluate components\n{e.Message}"); Echo("error");
            return false;
        }

        if (/* TODO: whatt components are needed? */) {
            Echo($"ok");
            return true;
        } else {
            print("Wrong <device_name> configuration");
            /* TODO: what's wrong? */
            Echo("error");
        }
    } else { print("No main controller"); Echo("error"); }

    return false;
}

public void shutdown() {
    /* TODO: shutdown components */
}

const string pName = "@<tag> program";
public Program() {
    Echo("");
    if (!Me.CustomName.StartsWith(pName)) Me.CustomName = pName;
    initMeLcd();

    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    if (state == 1) {
        if (!init()) shutdown();
        Runtime.UpdateFrequency = UpdateFrequency.Update1;
    } else {
        findDebugLcd(getBlocks(b => b.IsSameConstructAs(Me)), tagRegex);
        Echo("offline"); wipe(); print("<device_name> shut down");
    }
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update1) {
        if (state == 1) {
            refreshTick++;
            if (refreshTick >= 200) {
                if (!init()) shutdown();
                refreshTick = 0;
            }
            if (/* TODO: some go-ahead indicator */) {
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
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        } else if (argument == "stop" && state > 0) {
            shutdown();
            state = 0;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("offline"); wipe(); print("<device_name> shut down");
        }
    }
}