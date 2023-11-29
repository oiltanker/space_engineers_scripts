/*
 * rotor [parm1] rotor [parm2] [piston [parm]...] rotor [parm3] cockpit [parm]
 */
public const float EPS = 0.01f;

public static float CosBetween(Vector3D v1, Vector3D v2) {
    return (float)(v1.Dot(v2) / (v1.Length() * v2.Length()));
}

public class PistonArm {
    public IMyShipController controller;
    public IMyTextPanel console;

    public IMyMotorStator stator1;
    public IMyMotorStator stator2;
    public IMyMotorStator stator3;
    public List<IMyPistonBase> extenders;

    private string output;

    public PistonArm(List<IMyTerminalBlock> blocks, Action<string> echo) {
        controller = (IMyShipController) blocks.FirstOrDefault(b => b is IMyShipController && b.CustomName.Contains("[parm]"));
        console = (IMyTextPanel) blocks.FirstOrDefault(b => b is IMyTextPanel && b.CustomName.Contains("[parm]"));
        if (console != null) console.ContentType = ContentType.TEXT_AND_IMAGE;

        var stators = blocks.Where(b => b is IMyMotorStator && b.CustomName.Contains("[parm")).Select(b => b as IMyMotorStator);
        stator1 = stators.FirstOrDefault(s => s.CustomName.Contains("parm1]"));
        stator2 = stators.FirstOrDefault(s => s.CustomName.Contains("parm2]"));
        stator3 = stators.FirstOrDefault(s => s.CustomName.Contains("parm3]"));

        extenders = blocks.Where(b => b is IMyPistonBase && b.CustomName.Contains("[parm]")).Select(b => b as IMyPistonBase).ToList();
    }

    private void movePistons(Vector3D mov) {
        if (mov.Z < 0d) {
            extenders.ForEach(p => p.Velocity = 2f);
        } else if (mov.Z > 0d) {
            extenders.ForEach(p => p.Velocity = -2f);
        } else {
            extenders.ForEach(p => p.Velocity = 0f);
        }
    }
    private void rotate(MatrixD cwm, Vector2 rot, float rol) {
        var xu_vec = Vector3D.Normalize(stator1.Top.WorldMatrix.Up);
        var xr_vec = Vector3D.Normalize(stator1.Top.WorldMatrix.Right);
        var yu_vec = Vector3D.Normalize(stator2.Top.WorldMatrix.Up);
        var yr_vec = Vector3D.Normalize(stator2.Top.WorldMatrix.Right);

        var cr_vec = Vector3D.Normalize(cwm.Right);
        var cu_vec = Vector3D.Normalize(cwm.Up);

        var x2x_vec = Vector3D.ProjectOnPlane(ref cr_vec, ref xu_vec);
        var y2x_vec = Vector3D.ProjectOnPlane(ref cu_vec, ref xu_vec);
        var x2y_vec = Vector3D.ProjectOnPlane(ref cr_vec, ref yu_vec);
        var y2y_vec = Vector3D.ProjectOnPlane(ref cu_vec, ref yu_vec);

        var x2x_mul = (CosBetween(xu_vec, cu_vec) < 0f ? 1f : -1f) * (float) x2x_vec.Length();
        var y2x_mul = (CosBetween(xu_vec, cr_vec) < 0f ? 1f : -1f) * (float) y2x_vec.Length();
        var x2y_mul = (CosBetween(yr_vec, cr_vec) > 0f ? 1f : -1f) * (float) x2y_vec.Length();
        var y2y_mul = (CosBetween(yr_vec, cu_vec) > 0f ? 1f : -1f) * (float) y2y_vec.Length();

        var rot_x = (x2x_mul * rot.Y) + (y2x_mul * rot.X);
        var rot_y = (x2y_mul * rot.Y) + (y2y_mul * -rot.X);

        output +=
            "\nx: " + rot_x.ToString("0.000") + " (" + x2x_mul.ToString("0.000") + ", " + y2x_mul.ToString("0.000") + ")" +
            "\ny: " + rot_y.ToString("0.000") + " (" + x2y_mul.ToString("0.000") + ", " + y2y_mul.ToString("0.000") + ")";

        stator1.TargetVelocityRad = -rot_x / 5f;
        stator2.TargetVelocityRad = -rot_y / 5f;
        stator3.TargetVelocityRPM = rol * -15f;
    }

    public void update() {
        output = "";
        if (controller == null) return;
        var mov = controller.MoveIndicator;
        var rot = controller.RotationIndicator;
        var rol = controller.RollIndicator;        

        movePistons(mov);
        rotate(controller.WorldMatrix, rot, rol);

        if (console == null) return;
        output +=
            "\nmov: " + mov.X.ToString("0.000") + ", " + mov.Y.ToString("0.000") + ", " + mov.Z.ToString("0.000") +
            "\nrot: " + rot.X.ToString("0.000") + ", " + rot.Y.ToString("0.000") +
            "\nrol: " + rol.ToString("0.000");
        console.WriteText(output);
    }
}
public static PistonArm p_arm = null;

public void Init() {
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    p_arm = new PistonArm(blocks, str => Echo(str));
}

public Program() {
    Init();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Save() {
}

public void Main(string arg, UpdateType updateSource) {
    if (p_arm != null) p_arm.update();
}
