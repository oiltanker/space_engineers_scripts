/*
 * cockpit [vtol]
 * <- [piston[-vtol] ...] rotor[vtol] [piston[+vtol] ...] <- cockpit's forward dir
 */
public const float EPS = 0.01f;

public static List<StatorDim> stators = null;
public static List<IMyThrust> static_thrusters = null;
public static int thruster_cont = 0;

public static IMyTextPanel console = null;
public static IMyShipController controller = null;

public static float CosBetween(Vector3D v1, Vector3D v2) {
    return (float)(v1.Dot(v2) / (v1.Length() * v2.Length()));
}
public static float AngleBetween(Vector3D v1, Vector3D v2) {
    return (float)Math.Acos(v1.Dot(v2) / (v1.Length() * v2.Length()));
}

public class StatorDim {
    public IMyMotorStator stator;
    public List<IMyThrust> thrusters;
    public List<IMyPistonBase> pistons_forward;
    public List<IMyPistonBase> pistons_reverse;

    public string output;

    public StatorDim(IMyMotorStator stator) {
        this.stator = stator;
        thrusters = new List<IMyThrust>();
        pistons_forward = new List<IMyPistonBase>();
        pistons_reverse = new List<IMyPistonBase>();

        output = "\n--------";
    }

    public void setThrust(float newtons, Vector3D ng_vec) {
        foreach (var thruster in thrusters) thruster.ThrustOverride = newtons;
    }
    public void lift() {
        foreach (var thruster in thrusters) thruster.ThrustOverride = thruster.MaxThrust;
    }
    public void allign(Vector3D g_vec) {
        var fn_vec = Vector3D.Normalize(stator.Top.WorldMatrix.Down);
        var d_vec = thrusters.Count > 0 ? thrusters.First().WorldMatrix.Forward : stator.Top.WorldMatrix.Left;
        var pg_vec = Vector3D.ProjectOnPlane(ref g_vec, ref fn_vec);

        float angle = MyMath.AngleBetween(d_vec, pg_vec);
        var cross = d_vec.Cross(pg_vec);
        float cw_check = (float)(fn_vec.Dot(cross) / (fn_vec.Length() * cross.Length()));
        cw_check = (float)Math.Abs(cw_check) > EPS ? (cw_check > 0f ? 1f : -1f) : 0f;
        float angle2 = cw_check * (float)Math.Sqrt(angle) * 10f;
        angle2 = Single.IsNaN(angle2) ? 0f : angle2;
        stator.TargetVelocityRad = angle2;

        if (thrusters.Count > 0) {
            var dir = thrusters.First().GridThrustDirection;
            output +=
                "\ndir: " + dir.X + ", " + dir.Y + ", " + dir.Z
            ;
        }
    }
    public void move(Vector3D f_vec, Vector3D com) {
        var pos = stator.GetPosition();
        var c_vec = Vector3D.Subtract(com, pos);
        var cp_vec = Vector3D.ProjectOnVector(ref c_vec, ref f_vec);
        var len = (float)cp_vec.Length();
        //if (len > 0.1f) {
            if (CosBetween(cp_vec, f_vec) > 0f) {
                foreach(var p in pistons_forward) p.Velocity = len;
                foreach(var p in pistons_reverse) p.Velocity = -len;
            } else {
                foreach(var p in pistons_forward) p.Velocity = -len;
                foreach(var p in pistons_reverse) p.Velocity = len;
            }
        /*} else {
            foreach(var p in pistons_forward) p.Velocity = 0f;
            foreach(var p in pistons_reverse) p.Velocity = 0f;
        }*/
        output +=
            "\ncom_len: " + len.ToString("0.00000")
        ;
    }

    public string collectOutput() {
        var ret = output + "\n";
        output = "\n--------";
        return ret;
    }
}

public void Init() {
    stators = new List<StatorDim>();
    thruster_cont = 0;

    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();

    GridTerminalSystem.GetBlocksOfType<IMyTextPanel>(blocks);
    foreach(var block in blocks) {
        if (block.CustomName.Contains("[vtol]")) {
            console = block as IMyTextPanel;
            console.ContentType = ContentType.TEXT_AND_IMAGE;
            break;
        }
    }

    blocks.Clear();
    GridTerminalSystem.GetBlocksOfType<IMyShipController>(blocks);
    foreach(var block in blocks) {
        if (block.CustomName.Contains("[vtol]")) {
            controller = block as IMyShipController;
            break;
        }
    }

    IEnumerable<IMyTerminalBlock> thrusters = new List<IMyTerminalBlock>(),
                                  stators_ = new List<IMyTerminalBlock>(),
                                  pistons = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocksOfType<IMyThrust>(thrusters as List<IMyTerminalBlock>);
    GridTerminalSystem.GetBlocksOfType<IMyMotorStator>(stators_ as List<IMyTerminalBlock>);
    GridTerminalSystem.GetBlocksOfType<IMyPistonBase>(pistons as List<IMyTerminalBlock>);

    stators_ = stators_.Where(s => s.CustomName.Contains("vtol]"));
    pistons = pistons.Where(p => p.CustomName.Contains("vtol]"));
    static_thrusters = thrusters.Where(t => t.CubeGrid == controller.CubeGrid).Select(t => t as IMyThrust).ToList();

    stators_.ToList().ForEach(s => {
        var stator = s as IMyMotorStator;
        stator.LowerLimitDeg = float.NegativeInfinity;
        stator.UpperLimitDeg = float.PositiveInfinity;

        StatorDim dim = new StatorDim(stator);
        dim.thrusters = thrusters.Where(t => t.CubeGrid == stator.TopGrid).Select(t => t as IMyThrust).ToList();
        thruster_cont += dim.thrusters.Count;
        for(var grid = stator.CubeGrid; grid != controller.CubeGrid;) {
            var piston = pistons.Select(p => p as IMyPistonBase).FirstOrDefault(p => p.TopGrid == grid);
            if (piston != null) {
                if (!piston.CustomName.Contains("-")) dim.pistons_forward.Add(piston);
                else dim.pistons_reverse.Add(piston);
                grid = piston.CubeGrid;
            } else break;
        }

        stators.Add(dim);
    });
}

public Program() {
    Init();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Save() {
}

private string getFDir(IMyThrust thr, MatrixD cm) {
    var fn_vec = Vector3D.Normalize(thr.WorldMatrix.Forward);
    KeyValuePair<string, Vector3D> same_vec = new KeyValuePair<string, Vector3D>(null, new Vector3D(0d,0d,0d));
    foreach(var vec in new Dictionary<string, Vector3D>(){
        { "forward", cm.Forward },
        { "backward", cm.Backward },
        { "up", cm.Up },
        { "down", cm.Down },
        { "left", cm.Left},
        { "right", cm.Right }
    }) {
        if (
            (same_vec.Key == null) ||
            (MyMath.AngleBetween(fn_vec, vec.Value) < MyMath.AngleBetween(fn_vec, same_vec.Value))
        ) same_vec = vec;
    }
    return same_vec.Key;
}
public void Main(string arg, UpdateType updateSource) {
    if (controller != null) {
        var mov = controller.MoveIndicator;

        var g_vec = controller.GetTotalGravity();
        var ng_vec = Vector3D.Normalize(g_vec);
        var o_vel = controller.GetShipVelocities().LinearVelocity;
        var mass = controller.CalculateShipMass().PhysicalMass;

        var a_vec = Vector3D.Add(g_vec, o_vel);

        var s_vec = new Vector3D(0d, 0d, 0d);
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
        }

        if (console != null) {
            var s_out = "";
            foreach(var sDim in stators) s_out += sDim.collectOutput();

            console.WriteText(
                "mov: " + mov.X + ", " + mov.Y + ", " + mov.Z +
                "\nvel : " + vel.ToString("0.00000") +
                "\ng_vec: " + g_vec.X.ToString("0.000") + ", " + g_vec.Y.ToString("0.000") + ", " + g_vec.Z.ToString("0.000") +
                "\na_vec: " + a_vec.X.ToString("0.000") + ", " + a_vec.Y.ToString("0.000") + ", " + a_vec.Z.ToString("0.000") +
                "\ns_vec: " + s_vec.X.ToString("0.000") + ", " + s_vec.Y.ToString("0.000") + ", " + s_vec.Z.ToString("0.000") +
                "\ns_vel: " + s_vec.Length().ToString("0.00000") +
                "\nmass: " + mass.ToString("0.00000") + 
                "\nforce_part: " + force_part.ToString("0.00000") +
                "\n? " + Vector3D.ProjectOnVector(ref o_vel, ref ng_vec).Length().ToString("0.00000") + ", " + o_vel.Dot(ng_vec).ToString("0.00000") +
                "\n" + s_out
            );
        }
    } else Init();
}
