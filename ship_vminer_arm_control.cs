public const double dEPS = 0.001d;
public const float EPS = 0.01f;

public static IMyTextPanel debugLcd = null;
public static void wipe() { if (debugLcd != null) debugLcd.WriteText(""); }
public static void print(string str) { if (debugLcd != null) debugLcd.WriteText(str + '\n', true); }

public int state = 0;
public IMyShipController controller = null;
public IMyMotorAdvancedStator handJoint = null;
public static readonly System.Text.RegularExpressions.Regex tagRegex = new System.Text.RegularExpressions.Regex(@"(\s|^)@vminer(\s|$)");
public void init() {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName) && b.CubeGrid == Me.CubeGrid) as IMyShipController;
    if (controller != null) {
        debugLcd = blocks.FirstOrDefault(b => b is IMyTextPanel && tagRegex.IsMatch(b.CustomName)&& b.CubeGrid == controller.CubeGrid) as IMyTextPanel;
        if (debugLcd != null) {
            debugLcd.ContentType = ContentType.TEXT_AND_IMAGE;
            debugLcd.FontSize = 0.8f;
            debugLcd.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
        }
        handJoint = blocks.FirstOrDefault(b => b is IMyMotorAdvancedStator && tagRegex.IsMatch(b.CustomName)) as IMyMotorAdvancedStator;
    }
}

public Program() {
    Echo("");
    Me.CustomName = "@vminer program";
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