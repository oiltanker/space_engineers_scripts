@import lib.eps
@import lib.printFull
@import lib.grid

public int state = 0;
public IMyShipController controller = null;
public IMyMotorAdvancedStator handJoint = null;
public static readonly System.Text.RegularExpressions.Regex tagRegex = new System.Text.RegularExpressions.Regex(@"(\s|^)@vminer(\s|$)");
public void init() {
    var blocks = getBlocks(b => b.IsSameConstructAs(Me));

    findDebugLcd(blocks, tagRegex);
    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName) && b.CubeGrid == Me.CubeGrid) as IMyShipController;
    if (controller != null) {
        handJoint = blocks.FirstOrDefault(b => b is IMyMotorAdvancedStator && tagRegex.IsMatch(b.CustomName)) as IMyMotorAdvancedStator;
    } else print("No main controller");
}

const string pName = "@vminer program";
public Program() {
    Echo("");
    if (!Me.CustomName.StartsWith(pName)) Me.CustomName = pName;
    initMeLcd();

    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    init();
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update1) { // general update
        refreshTick++;
        if (state == 1) {
            if (refreshTick >= 300) {
                init();
                refreshTick = 0;
            }
            if (controller != null) {
                wipe();
                var handRot = controller.RotationIndicator.X;
                print($"handRot: {handRot.ToString("0.000")}");
                if (Math.Abs(handRot) > dEPS) handJoint.TargetVelocityRPM = (float) (handRot * 0.05d);
                else handJoint.TargetVelocityRPM = 0f;
            }
        }
    } else {
        if (argument == "start") {
            if (controller == null) init();
            state = 1;
        } else if (argument == "stop" && state > 0) {
            state = 0;
            controller = null;
        }
    }
}