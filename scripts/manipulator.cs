@import lib.eps
@import lib.printFull
@import lib.alignment
@import lib.pid

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@manpul(\s|$)");
public static readonly @Regex manpulRegex = new @Regex(@"(\s|^)@manpul-(1|2|3|4)(\s|$)");

public class manSegment {
    public IMyMotorAdvancedStator hinge1;
    public IMyMotorAdvancedStator hinge2;
    public List<IMyMotorAdvancedStator> middleHinges;
    double length;
    public manSegment(IMyMotorAdvancedStator hinge1, IMyMotorAdvancedStator hinge2, List<IMyMotorAdvancedStator> middleHinges, double length) {
        this.hinge1 = hinge1; this.hinge2 = hinge2;
        this.middleHinges = middleHinges;
        this.length = length;
    }
}
public class manArm {
    public IMyTerminalBlock baseAnchor;
    public IMyTerminalBlock anchor;
    public manSegment seg1; // hinge2 only
    public manSegment seg2;
    public manSegment seg3;
    public manSegment seg4; // special case
    public Vector3D pBase;
    public Vector3D pHead;
    public manArm(IMyTerminalBlock baseAnchor, IMyTerminalBlock anchor) {
        this.baseAnchor = baseAnchor; this.anchor = anchor;
    }
    public void update(roll, pitch, yaw, Vector3D mov) {
        var baseMat = MatrixD.Transpose(baseAnchor.WorldMatrix);
        var mat = anchor.WorldMatrix;
        var fVec = Vector3D.TransformNormal(mat.Forward); var lVec = Vector3D.TransformNormal(mat.Left); var uVec = Vector3D.TransformNormal(mat.Up);
        pHead += fVec * mov.Z + lVec * mov.X + uVec * mov.Y;
    }
}

public int state = 0;
public IMyShipController controller = null;

public void update() {
    var rotInd = controller.RotationIndicator;
    var roll = controller.RollIndicator; var pitch = rotInd.X; var yaw = rotInd.Y;
    print($"roll {roll.ToString("0.000")}    pitch {pitch.ToString("0.000")}    yaw {yaw.ToString("0.000")}");
}

public struct sDef {
    public enum sType { rotor, hinge }
    public IMyMotorAdvancedStator block;
    public sType type;
    public IMyCubeGrid topGrid;
    public sDef(IMyMotorAdvancedStator stator) {
        ver def = stator.BlockDefinition.SubtypeName;
        if (def.Contains("Stator")) type = sType.rotor;
        else if (def.Contains("Hinge")) type = sType.hinge;
        else throw new ArgumentException("Uknown stator type");

        topGrid = stator.TopGrid;
        block = stator;
    }
}
public bool init() {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    findDebugLcd(blocks, tagRegex);
    wipe();

    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName) && b.CubeGrid == Me.CubeGrid) as IMyShipController;
    if (controller != null) {
        try {
            var stators = blocks.Where(b => b is IMyMotorAdvancedStator && b.IsSameConstructAs(Me)).Select(b => new sDef(b as IMyMotorAdvancedStator));
            var grids = stators.Select(s => s.TopGrid);
            var 
        } catch (Exception e) {
            print($"Wrongly built manipulator: could not evaluate components\n{e.Message}"); Echo("error");
            return false;
        }

        if (/* TODO: whatt components are needed? */) {
            Echo($"ok");
            return true;
        } else {
            print("Wrong manipulator configuration");
            /* TODO: what's wrong? */
            Echo("error");
        }
    } else { print("No main controller"); Echo("error"); }

    return false;
}

public void shutdown() {
    pgArr?.shutdown();
    pgArr = null;
}

const string pName = "@manpul program";
public Program() {
    Echo("");
    if (!Me.CustomName.StartsWith(pName)) Me.CustomName = pName;
    initMeLcd();

    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    if (state == 1) {
        if (!init()) shutdown();
        Runtime.UpdateFrequency = UpdateFrequency.Update1;
    } else {
        var blocks = new List<IMyTerminalBlock>();
        GridTerminalSystem.GetBlocks(blocks);
        findDebugLcd(blocks, tagRegex);

        Echo("offline"); wipe(); print("Manipulator shut down");
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
            if (pgArr != null) {
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
            sRot.forward = Vector3D.Zero; sRot.left = Vector3D.Zero; sRot.up = Vector3D.Zero;
            state = 0;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("offline"); wipe(); print("Manipulator shut down");
        }
    }
}