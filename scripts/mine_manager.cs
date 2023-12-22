/*
 * [MINE Prog] - mine programming block
 * [MINE Cargo] - cargo container for stone
 * arguments:
 *   start - starts mine management
 *   stop  - stops mine management
 */

@import lib.grid

public int state = 0;
public IMyProgrammableBlock control = null;
public IMyCargoContainer cargo = null;
public Action<string> print = null;

public bool checkMineComps() {
    var blocks = getBlocks();
    if (control == null) control = blocks.Where(b => b is IMyProgrammableBlock && b.CustomName.StartsWith("[MINE Prog]")).FirstOrDefault() as IMyProgrammableBlock;
    if (cargo == null) cargo = blocks.Where(b => b is IMyCargoContainer && b.CustomName.StartsWith("[MINE Cargo]")).FirstOrDefault() as IMyCargoContainer;
    return control != null && control.IsWorking && cargo != null && cargo.IsWorking;
}

public Program() {
    if (!String.IsNullOrEmpty(Storage)) state = Int32.Parse(Storage);
    Runtime.UpdateFrequency = UpdateFrequency.Update100;

    IMyTextSurface myLcd = Me.GetSurface(0);
    myLcd.ContentType = ContentType.TEXT_AND_IMAGE;
    myLcd.FontSize = 1;
    myLcd.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;

    print = (str) => {
        Echo(str);
        myLcd.WriteText(str);
    };
}

public void Save() => Storage = state.ToString();

public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update100) {
        if (!checkMineComps()) return;
        var inv = cargo.GetInventory();

        var str = "Cargo fill: " + (inv.VolumeFillFactor * 100f).ToString("0.00") + "%\n";
        if (state == 1) {
            if (inv.VolumeFillFactor < 0.60f) {
                str += "Mining in process ...\n";
                control.TryRun("start");
            } else if (inv.VolumeFillFactor > 0.80f) {
                str +=  "Mining paused/stopped ...\n";
                control.TryRun("stop");
            } else {
                str +=  "Idling ...\n";
            }
        } else if (state == 0) {
            str += "Managing stopped.\n";
            control.TryRun("stop");
            state = -1;
        }
        print(str);
    } else {
        if (argument == "start") state = 1;
        else if (argument == "stop" && state > 0) state = 0;
    }
}