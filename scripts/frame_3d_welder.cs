/*
 * PARAMETERS
 *
 * - invert_up=[T|F]        inverts up movement
 * - invert_right=[T|F]     inverts right movement
 * - invert_forward=[T|F]   inverts forward movement
 * - swap_up_forward=[T|F]  swap up and forward movements
 */

public bool invert_up = false;
public bool invert_right = false;
public bool invert_forward = false;
public bool swap_up_forward = true;

public const float EPS = 0.01f;

public class PistonDim {
    public List<IMyPistonBase> forward;
    public List<IMyPistonBase> reverse;

    public PistonDim() {
        forward = new List<IMyPistonBase>();
        reverse = new List<IMyPistonBase>();
    }

    public void stop() {
        foreach(var piston in forward) piston.Velocity = 0f;
        foreach(var piston in reverse) piston.Velocity = 0f;
    }
    public void extend() {
        foreach(var piston in forward) piston.Velocity = 1f;
        foreach(var piston in reverse) piston.Velocity = -1f;
    }
    public void retract() {
        foreach(var piston in forward) piston.Velocity = -1f;
        foreach(var piston in reverse) piston.Velocity = 1f;
    }
}
public class DimMap {
    public PistonDim up;
    public PistonDim right;
    public PistonDim forward;
}
DimMap dim_map;
PistonDim pistons_x, pistons_y, pistons_z;

IMyTextPanel console;
IMyShipController controller;
List<IMyThrust> thrusters;


public MatrixD getOrt(PistonDim pistons) {
    return pistons.forward.Count > 0 ? pistons.forward.First().WorldMatrix : pistons.reverse.First().WorldMatrix;
}
public void DesiceAngle(Vector3 ort, Action<PistonDim> setter) {
    Vector3 ort_x = getOrt(pistons_x).Up,
            ort_y = getOrt(pistons_y).Up,
            ort_z = getOrt(pistons_z).Up;

    float angle_x = MyMath.AngleBetween(ort, ort_x),
          angle_y = MyMath.AngleBetween(ort, ort_y),
          angle_z = MyMath.AngleBetween(ort, ort_z);

    if (angle_x <= EPS || Math.Abs(angle_x - 3.14159) <= EPS || Single.IsNaN(angle_x)) setter(pistons_x);
    else if (angle_y <= EPS || Math.Abs(angle_y - 3.14159) <= EPS || Single.IsNaN(angle_y)) setter(pistons_y);
    else if (angle_z <= EPS || Math.Abs(angle_z - 3.14159) <= EPS || Single.IsNaN(angle_z)) setter(pistons_z);
}

public void Init() {
    dim_map = new DimMap();
    pistons_x = new PistonDim();
    pistons_y = new PistonDim();
    pistons_z = new PistonDim();

    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    controller = (IMyShipController) blocks.FirstOrDefault(b => b is IMyShipController && b.CustomName.Contains("[w3p]"));
    if (controller == null) throw new Exception("A controller block must be present.");
    console = (IMyTextPanel) blocks.FirstOrDefault(b => b is IMyTextPanel && b.CustomName.Contains("[w3p]"));
    if (console != null) console.ContentType = ContentType.TEXT_AND_IMAGE;
    thrusters = blocks.Where(b => b is IMyThrust && b.IsSameConstructAs(controller)).Select(b => b as IMyThrust).ToList();

    foreach(var block in blocks) {
        string[] name = block.CustomName.Split(' ');
        if (block is IMyPistonBase) foreach(var part in name) {
            if (part.StartsWith("[w3p:") && part.EndsWith("]")) {
                var piston = (IMyPistonBase) block;
                PistonDim dim = null;

                piston.MinLimit = 0f;
                piston.MaxLimit = 10f;

                if (part.Contains("x")) dim = pistons_x;
                else if (part.Contains("y")) dim = pistons_y;
                else if (part.Contains("z")) dim = pistons_z;

                if (part.Contains("-")) dim.reverse.Add(piston);
                else dim.forward.Add(piston);
            }
        }
    }
    pistons_x.stop();
    pistons_y.stop();
    pistons_z.stop();

    if (controller != null) {
        MatrixD ort = controller.WorldMatrix;
        DesiceAngle(ort.Up, (dim) => dim_map.forward = dim);
        DesiceAngle(ort.Right, (dim) => dim_map.right = dim);
        DesiceAngle(ort.Forward, (dim) => dim_map.up = dim);
    }
}

public Program() {
    Init();
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
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

                if (key.Equals("invert_up")) invert_up = val.Equals("F") ? false : true;
                if (key.Equals("invert_right")) invert_right = val.Equals("F") ? false : true;
                if (key.Equals("invert_forward")) invert_forward = val.Equals("F") ? false : true;
                if (key.Equals("swap_up_forward")) swap_up_forward = val.Equals("F") ? false : true;
            }
        }
    }

    var mov = controller.MoveIndicator;
    var ort = controller.WorldMatrix;

    if (console != null) {
        string dim_up = (dim_map.up == pistons_x) ? "x" : (dim_map.up == pistons_y) ? "y" : "z",
                dim_right = (dim_map.right == pistons_x) ? "x" : (dim_map.right == pistons_y) ? "y" : "z",
                dim_forward = (dim_map.forward == pistons_x) ? "x" : (dim_map.forward == pistons_y) ? "y" : "z";

        console.WriteText(
            "mov: " + mov.X + ", " + mov.Y + ", " + mov.Z +
            "\nort: " + ort.Up.X.ToString("0.000") + ", " + ort.Up.Y.ToString("0.000") + ", " + ort.Up.Z.ToString("0.000") +
            "\nangles (up): " +
                MyMath.AngleBetween(ort.Up, getOrt(pistons_x).Up).ToString("0.000") + ", "
                + MyMath.AngleBetween(ort.Up, getOrt(pistons_y).Up).ToString("0.000") + ", "
                + MyMath.AngleBetween(ort.Up, getOrt(pistons_z).Up).ToString("0.000") +
            "\nangles (right): " +
                MyMath.AngleBetween(ort.Right, getOrt(pistons_x).Up).ToString("0.000") + ", "
                + MyMath.AngleBetween(ort.Right, getOrt(pistons_y).Up).ToString("0.000") + ", "
                + MyMath.AngleBetween(ort.Right, getOrt(pistons_z).Up).ToString("0.000") + 
            "\ndimmap: up-" + dim_up + ", right-" + dim_right + ", forward-" + dim_forward);
    }

    if (mov.Length() > 0d) thrusters.ForEach(t => t.Enabled = false);
    else thrusters.ForEach(t => t.Enabled = true);
    
    if (swap_up_forward) {
        if (invert_forward) {
            if (mov.Z > 0f) dim_map.forward.extend(); else if (mov.Z < 0f) dim_map.forward.retract(); else dim_map.forward.stop();
        } else {
            if (mov.Z > 0f) dim_map.forward.retract(); else if (mov.Z < 0f) dim_map.forward.extend(); else dim_map.forward.stop();
        }

        if (invert_up) {
            if (mov.Y > 0f) dim_map.up.extend(); else if (mov.Y < 0f) dim_map.up.retract(); else dim_map.up.stop();
        } else {
            if (mov.Y > 0f) dim_map.up.retract(); else if (mov.Y < 0f) dim_map.up.extend(); else dim_map.up.stop();
        }
    } else {
        if (invert_up) {
            if (mov.Z > 0f) dim_map.up.extend(); else if (mov.Z < 0f) dim_map.up.retract(); else dim_map.up.stop();
        } else {
            if (mov.Z > 0f) dim_map.up.retract(); else if (mov.Z < 0f) dim_map.up.extend(); else dim_map.up.stop();
        }

        if (invert_forward) {
            if (mov.Y > 0f) dim_map.forward.extend(); else if (mov.Y < 0f) dim_map.forward.retract(); else dim_map.forward.stop();
        } else {
            if (mov.Y > 0f) dim_map.forward.retract(); else if (mov.Y < 0f) dim_map.forward.extend(); else dim_map.forward.stop();
        }
    }

    if (invert_right) {
        if (mov.X > 0f) dim_map.right.extend(); else if (mov.X < 0f) dim_map.right.retract(); else dim_map.right.stop();
    } else {
        if (mov.X > 0f) dim_map.right.retract(); else if (mov.X < 0f) dim_map.right.extend(); else dim_map.right.stop();
    }
}
