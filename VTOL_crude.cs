/*
 * cockpit [vtol]
 * <- rotor[vtol] <- cockpit's forward dir
 */
public const float EPS = 0.01f;

public static List<TUnit> tUnits = null;
public static List<IMyThrust> static_thrusters = null;

public static IMyTextPanel console = null;
public static IMyShipController controller = null;

public static float CosBetween(Vector3D v1, Vector3D v2) {
    return (float)(v1.Dot(v2) / (v1.Length() * v2.Length()));
}
public static float AngleBetween(Vector3D v1, Vector3D v2) {
    return (float)Math.Acos(v1.Dot(v2) / (v1.Length() * v2.Length()));
}
public static float CwMult(Vector3D v1, Vector3D v2, Vector3D n) {
    var c_vec = v1.Cross(v2);
    var cw_check = CosBetween(c_vec, n);
    return (float) Math.Abs(cw_check) > EPS ? (cw_check > 0f ? 1f : -1f) : 0f;
}
public static Vector3D projOnPlane(Vector3D v1, Vector3D v2) {
    return Vector3D.ProjectOnPlane(ref v1, ref v2);
}
public static Vector3D projectOnVector(Vector3D v1, Vector3D v2) {
    return Vector3D.ProjectOnVector(ref v1, ref v2);
}

public enum Dir : byte {
    Forward,
    Backward,
    Left,
    Right,
    Up,
    Down
}

public static Vector3D GetVec(MatrixD m, Dir d) {
    switch (d) {
        case Dir.Forward: return m.Forward;
        case Dir.Backward: return m.Backward;
        case Dir.Left: return m.Left;
        case Dir.Right: return m.Right;
        case Dir.Up: return m.Up;
        case Dir.Down: return m.Down;
        default: throw new Exception("Unknown Dir direction.");
    }
}
public static Dir GetAlign(MatrixD m, Vector3D v) {
    if ((m.Forward - v).Length() <= EPS) return Dir.Forward;
    else if ((m.Backward - v).Length() <= EPS) return Dir.Backward;
    else if ((m.Left - v).Length() <= EPS) return Dir.Left;
    else if ((m.Right - v).Length() <= EPS) return Dir.Right;
    else if ((m.Up - v).Length() <= EPS) return Dir.Up;
    else if ((m.Down - v).Length() <= EPS) return Dir.Down;
    else throw new Exception("Could not match direction.");
}

public class TUnit {
    public bool reverse;
    public IMyMotorStator stator;

    public Dictionary<Dir, List<IMyThrust>> thrust;
    public IMyShipController control;

    public IMyThrust mTruster;
    public MatrixD align;
    public Dictionary<Dir, float> totalForce;

    public string output;

    public TUnit(IMyShipController controller, IMyMotorStator stator, List<IMyThrust> thrusters, Action<string> echo) {
        this.stator = stator;
        control = controller;

        var left = control.WorldMatrix.Left;
        var forward = control.WorldMatrix.Forward;

        echo("1");
        reverse = (left - stator.Top.WorldMatrix.Up).Length() <= EPS;
        mTruster = thrusters.FirstOrDefault(t => t.CustomName.Contains("[vtol]"));
        if (mTruster == null) throw new Exception("No master thruster on thruster unit.");

        echo("2");
        align = new MatrixD(mTruster.WorldMatrix);
        align.Up = stator.Top.WorldMatrix.Up;
        align.Forward = mTruster.WorldMatrix.Forward;

        thrust = new Dictionary<Dir, List<IMyThrust>>();
        totalForce = new Dictionary<Dir, float>();
        foreach (Dir dir in (Dir[]) Enum.GetValues(typeof(Dir))) {
            thrust[dir] = new List<IMyThrust>();
            totalForce[dir] = 0f;
        }

        thrusters.ForEach(t => {
            var dir = GetAlign(align, t.WorldMatrix.Forward);
            thrust[dir].Add(t);
            totalForce[dir] += t.MaxThrust;
        });

        echo("3");
        output = "\n--------";
    }

    public void setThrust(Vector3D fn_vec, double force) {
        foreach (Dir dir in (Dir[]) Enum.GetValues(typeof(Dir))) {
            var vec = GetVec(align, dir);
            var mult = projectOnVector(fn_vec, vec).Length();
            if (CosBetween(vec, fn_vec) >= 0f) mult = 0d;
            var force_part = mult * force;

            thrust[dir].ForEach(t => {
                var part = Math.Min(force_part, (double) t.MaxThrust);
                t.ThrustOverride = (float) part;
                force_part -= part;
            });
        }
    }
    public void allign(Vector3D vec) {
        var un_vec = Vector3D.Normalize(stator.Top.WorldMatrix.Up);
        var d_vec = Vector3D.Normalize(mTruster.WorldMatrix.Forward);
        var p_vec = Vector3D.ProjectOnPlane(ref vec, ref un_vec);

        float angle = AngleBetween(d_vec, p_vec) * CwMult(d_vec, p_vec, un_vec);
        stator.TargetVelocityRad = angle;
    }

    public string collectOutput() {
        var ret = output + "\n";
        output = "\n--------";
        return ret;
    }
}

public void Init() {
    tUnits = new List<TUnit>();

    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    controller = (IMyShipController) blocks.FirstOrDefault(b => b is IMyShipController && b.CustomName.Contains("[vtol]"));
    if (controller == null) throw new Exception("No designated controller for VTOL.");
    console = (IMyTextPanel) blocks.FirstOrDefault(b => b is IMyTextPanel && b.CustomName.Contains("[vtol]"));
    if (console != null) console.ContentType = ContentType.TEXT_AND_IMAGE;

    var stators = blocks.Where(b => b is IMyMotorStator && b.CustomName.Contains("[vtol]")).Select(b => b as IMyMotorStator).ToList();
    var thrusters = blocks.Where(b => b is IMyThrust).Select(b => b as IMyThrust).ToList();
    stators.ForEach(s => tUnits.Add(new TUnit(controller, s, thrusters.Where(t => t.CubeGrid == s.Top.CubeGrid).ToList(), str => Echo(str))));
}

public Program() {
    Init();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Save() {
}

public void Main(string arg, UpdateType updateSource) {
    if (controller != null) {
        var cm = controller.WorldMatrix;
        var mov = controller.MoveIndicator;
        var mass = controller.CalculateShipMass().PhysicalMass;

        var g_vec = controller.GetTotalGravity();
        var l_vel = controller.GetShipVelocities().LinearVelocity;

        var a_vec = l_vel + g_vec;
        a_vec += cm.Backward * mov.Z;
        a_vec += cm.Right * mov.X;
        a_vec += cm.Up * mov.Y;

        /*var s_vec = new Vector3D(0d, 0d, 0d);
        foreach(var t in static_thrusters) {
            var t_vec = Vector3D.Normalize(t.WorldMatrix.Forward);
            var t_vel = t.CurrentThrust / mass;
            s_vec = Vector3D.Add(s_vec, Vector3D.Multiply(t_vec, t_vel));
        }
        var cwm = controller.WorldMatrix;
        if (Math.Abs(mov.Z) > 0d) {
            var fb_vec = Vector3D.Normalize(mov.Z < 0f ? cwm.Forward : cwm.Backward);

            var fba_vec = Vector3D.ProjectOnVector(ref a_vec, ref fb_vec);
            if (CosBetween(fba_vec, fb_vec) > 0f) a_vec = Vector3D.Subtract(a_vec, fba_vec);

            var fbs_vec = Vector3D.ProjectOnVector(ref s_vec, ref fb_vec);
            if (CosBetween(fbs_vec, fb_vec) < 0f) s_vec = Vector3D.Subtract(s_vec, fbs_vec);
        }
        if (Math.Abs(mov.Y) > 0d) {
            var da_vec = Vector3D.ProjectOnVector(ref a_vec, ref ng_vec);
            if (mov.Y > 0f) {
                if (CosBetween(da_vec, ng_vec) < 0f) a_vec = Vector3D.Subtract(a_vec, da_vec);
            } else if (mov.Y < 0f)
                if (CosBetween(da_vec, ng_vec) > 0f) a_vec = Vector3D.Subtract(a_vec, da_vec);

            var ds_vec = Vector3D.ProjectOnVector(ref s_vec, ref ng_vec);
            if (mov.Y > 0f)
                if (CosBetween(ds_vec, ng_vec) > 0f) s_vec = Vector3D.Subtract(s_vec, ds_vec);
            else if (mov.Y < 0f)
                if (CosBetween(ds_vec, ng_vec) < 0f) s_vec = Vector3D.Subtract(s_vec, ds_vec);
        }
        a_vec = Vector3D.Subtract(a_vec, s_vec);
        if (mov.Y > 0f) a_vec = Vector3D.Add(a_vec, g_vec);
        
        var vel = (float)a_vec.Length();
        var force_part = (vel * mass) / (float)thruster_cont;

        foreach(var sDim in stators) {
            sDim.move(controller.WorldMatrix.Forward, controller.CenterOfMass);
            sDim.allign(a_vec);

            if (mov.Y == 0f && vel > 0f) {
                sDim.setThrust(force_part, a_vec);
            } else if (mov.Y > 0f) {
                sDim.lift();
            } else {
                sDim.setThrust(0, ng_vec);
            }
        }*/

        if (console != null) {
            var s_out = "";
            foreach(var u in tUnits) s_out += u.collectOutput();

            console.WriteText(
                "mov: " + mov.X + ", " + mov.Y + ", " + mov.Z +
                "\ng_vec: " + g_vec.X.ToString("0.000") + ", " + g_vec.Y.ToString("0.000") + ", " + g_vec.Z.ToString("0.000") +
                "\nl_vel: " + l_vel.X.ToString("0.000") + ", " + l_vel.Y.ToString("0.000") + ", " + l_vel.Z.ToString("0.000") +
                "\na_vec: " + a_vec.X.ToString("0.000") + ", " + a_vec.Y.ToString("0.000") + ", " + a_vec.Z.ToString("0.000") +
                "\nmass: " + mass.ToString("0.00000") + 
                "\n" + s_out
            );
        }
    } else Init();
}
