/*
 * PARAMETERS
 *
 * - invert_yaw=[T|F]   inverts yaw movement
 * - invert_pitch=[T|F] inverts pitch movement
 * - invert_roll=[T|F]  inverts roll movement
 */

public bool invert_up = false;
public bool invert_right = false;
public bool invert_forward = false;
public bool swap_up_forward = true;

public const float EPS = 0.01f;

public class StatorDim {
    public IMyMotorAdvancedStator forward;
    public IMyMotorAdvancedStator reverse;

    public void apply(float vel) {
        if (forward != null) forward.TargetVelocityRad = vel;
        if (reverse != null) reverse.TargetVelocityRad = -vel;
    }
}
StatorDim s_yaw, s_pitch, s_roll;

IMyTextPanel console;
IMyShipController controller;


public void Init() {
    s_yaw = new StatorDim();
    s_pitch = new StatorDim();
    s_roll = new StatorDim();

    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    foreach(var block in blocks) {
        string[] name = block.CustomName.Split(' ');
        if (block is IMyMotorAdvancedStator) foreach(var part in name) {
            if (part.StartsWith("[") && part.EndsWith("]")) {
                var stator = (IMyMotorAdvancedStator) block;
                StatorDim dim = null;

                stator.LowerLimitDeg = float.NegativeInfinity;
                stator.UpperLimitDeg = float.PositiveInfinity;

                if (part.Contains("yaw")) dim = s_yaw;
                else if (part.Contains("pitch")) dim = s_pitch;
                else if (part.Contains("roll")) dim = s_roll;

                if (part.Contains("-")) dim.reverse = stator;
                else dim.forward = stator;
            }
        }
    }
    s_yaw.apply(0f);
    s_pitch.apply(0f);
    s_roll.apply(0f);

    blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(blocks);
    foreach(var block in blocks) {
        if (block.CustomName.Contains("[gyro]")) {
            console = block as IMyTextPanel;
            console.ContentType = ContentType.TEXT_AND_IMAGE;
            break;
        }
    }

    blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyShipController>(blocks);
    foreach(var block in blocks) {
        if (block.CustomName.Contains("[gyro]")) {
            controller = block as IMyShipController;
            break;
        }
    }
}

public Program() {
    Init();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Save() {
}

public void Main(string arg, UpdateType updateSource) {
    if (!String.IsNullOrEmpty(Me.CustomData)) {
        var args = Me.CustomData.Contains(" ") ? Me.CustomData.Split(' ') : new string[] {Me.CustomData};
        foreach (var arg1 in args) {
            if (arg1.Contains("=")) {
                var key = arg1.Split('=')[0];
                var val = arg1.Split('=')[1];

                if (key.Equals("invert_yaw")) invert_up = val.Equals("F") ? false : true;
                if (key.Equals("invert_pitch")) invert_right = val.Equals("F") ? false : true;
                if (key.Equals("invert_roll")) invert_forward = val.Equals("F") ? false : true;
            }
        }
    }

    if (controller != null) {
        var mov = controller.RotationIndicator;

        s_pitch.apply(-mov.X);
        s_yaw.apply(mov.Y);

        var g_vec = controller.GetNaturalGravity();
        var fn_vec = Vector3D.Normalize(controller.WorldMatrix.Forward);
        var pg_vec = Vector3D.ProjectOnPlane(ref g_vec, ref fn_vec);

        var d_vec = controller.WorldMatrix.Down;
        var angle1 = (float)Math.Acos(d_vec.Dot(pg_vec) / (d_vec.Length() * pg_vec.Length()));
        var cross = d_vec.Cross(pg_vec);
        var cw_check = (float)(fn_vec.Dot(cross) / (fn_vec.Length() * cross.Length()));
        s_roll.apply((cw_check > 0) ? -angle1 * 10f : angle1 * 10f);

        if (console != null) {
            console.WriteText(
                angle1.ToString("0.00000") +
                "\ncw: " + cross.X.ToString("0.000") + ", " + cross.Y.ToString("0.000") + ", " + cross.Z.ToString("0.000") + 
                "\n" + cw_check.ToString("0.00000")
            );
        }
    } else Init();
}
