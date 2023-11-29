public const float EPS = 0.01f;

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
public static Vector3D getVec(MatrixD m, Dir d) {
    switch (d) {
        case Dir.Forward: return m.Forward;
        case Dir.Backward: return m.Backward;
        case Dir.Left: return m.Left;
        case Dir.Right: return m.Right;
        case Dir.Up: return m.Up;
        case Dir.Down: return m.Down;
        default: return new Vector3D(0d, 0d, 0d);
    }
}
public static Vector3D mapVec(Vector3D v, Func<double, double> f) {
    return new Vector3D(f(v.X), f(v.Y), f(v.Z));
}

public enum Dir : byte {
    Forward,
    Backward,
    Left,
    Right,
    Up,
    Down
}
public struct Alignment {
    public Dir Forward;
    public Dir Left;
    public Dir Up;

    public Alignment(Dir f, Dir l, Dir u) {
        Forward = f;
        Left = l;
        Up = u;
    }
}
public class SBState {
    public bool active;
    public MatrixD location;

    public SBState(bool active, MatrixD location) {
        this.active = active;
        this.location = location;
    }

    private static Func<string, float> toFlt = s => (float)Convert.ToUInt32(s, 16);
    private static Action<MatrixD, int, int, float> mSet = (m, x, y, v) => {
        var row = m.GetRow(x);
        switch (y) {
            case 0: row.X = v; return;
            case 1: row.Y = v; return;
            case 2: row.Z = v; return;
            case 3: row.W = v; return;
            default: throw new Exception("Error: colum index out of bounds.");
        }
    };
    public SBState(string str) {
        var fields = str.Split('|');
        active = Convert.ToBoolean(fields[0]);
        
        var elems = fields[1].Split(',');
        for (int i = 0; i < elems.Count(); i++) mSet(location, i / 4, i % 4, toFlt(elems[i]));
    }

    private static Func<float, string> toHex = f => "0x" + ((uint)f).ToString("X");
    private static Func<MatrixD, int, int, float> mGet = (m, x, y) => {
        var row = m.GetRow(x);
        switch (y) {
            case 0: return row.X;
            case 1: return row.Y;
            case 2: return row.Z;
            case 3: return row.W;
            default: throw new Exception("Error: colum index out of bounds.");
        }
    };
    public override string ToString() {
        string m_str = "";
        for (int i = 0; i < 4; i++)
            for (int j = 0; j < 4; j++)
                m_str += toHex(mGet(location, i, j)) + ",";
        m_str = m_str.Substring(0, m_str.Length - 1);
        return (active ? "true" : "false") + "|" + m_str;
    }
}
public class ShipBrake {
    public IMyShipController controller;
    public IMyTextPanel console;

    public Dictionary<Dir, List<IMyThrust>> thrustMap;
    public Dictionary<IMyGyro, Alignment> gyroMap;

    private string output;
    public bool consoleLock;

    public ShipBrake(List<IMyTerminalBlock> blocks, Action<string> echo) {
        consoleLock = false;

        controller = (IMyShipController) blocks.FirstOrDefault(b => b is IMyShipController && b.CustomName.Contains("[shb]"));
        if (controller == null) throw new Exception("No designated controller for SHB.");
        console = (IMyTextPanel) blocks.FirstOrDefault(b => b is IMyTextPanel && b.CustomName.Contains("[shb]"));
        if (console != null) console.ContentType = ContentType.TEXT_AND_IMAGE;

        thrustMap = new Dictionary<Dir, List<IMyThrust>>();
        var thrusters = blocks.Where(b => b is IMyThrust && b.CubeGrid == controller.CubeGrid).Select(b => b as IMyThrust);
        thrustMap[Dir.Forward] = thrusters.Where(t => (t.WorldMatrix.Forward - controller.WorldMatrix.Backward).Length() <= EPS).OrderBy(t => t.MaxThrust).ToList();
        thrustMap[Dir.Backward] = thrusters.Where(t => (t.WorldMatrix.Forward - controller.WorldMatrix.Forward).Length() <= EPS).OrderBy(t => t.MaxThrust).ToList();
        thrustMap[Dir.Left] = thrusters.Where(t => (t.WorldMatrix.Forward - controller.WorldMatrix.Right).Length() <= EPS).OrderBy(t => t.MaxThrust).ToList();
        thrustMap[Dir.Right] = thrusters.Where(t => (t.WorldMatrix.Forward - controller.WorldMatrix.Left).Length() <= EPS).OrderBy(t => t.MaxThrust).ToList();
        thrustMap[Dir.Up] = thrusters.Where(t => (t.WorldMatrix.Forward - controller.WorldMatrix.Down).Length() <= EPS).OrderBy(t => t.MaxThrust).ToList();
        thrustMap[Dir.Down] = thrusters.Where(t => (t.WorldMatrix.Forward - controller.WorldMatrix.Up).Length() <= EPS).OrderBy(t => t.MaxThrust).ToList();

        gyroMap = new Dictionary<IMyGyro, Alignment>();
        Func<MatrixD, MatrixD, Vector3D, Dir> matchDir = (m1, m2, v) => {
            Dir ret;

            if ((m1.Forward - v).Length() <= EPS) ret = Dir.Forward;
            else if ((m1.Backward - v).Length() <= EPS) ret = Dir.Backward;
            else if ((m1.Left - v).Length() <= EPS) ret = Dir.Left;
            else if ((m1.Right - v).Length() <= EPS) ret = Dir.Right;
            else if ((m1.Up - v).Length() <= EPS) ret = Dir.Up;
            else if ((m1.Down - v).Length() <= EPS) ret = Dir.Down;
            else throw new Exception("No fitting direction for static mesh?");

            Func<Dir, Dir> flip = dir => {
                switch (dir) {
                    case Dir.Forward: return Dir.Backward;
                    case Dir.Backward: return Dir.Forward;
                    case Dir.Left: return Dir.Right;
                    case Dir.Right: return Dir.Left;
                    case Dir.Up: return Dir.Down;
                    case Dir.Down: return Dir.Up;
                    default: throw new Exception("No fitting direction for static mesh?");
                }
            };
            if (
                ((m1.Left - m2.Up).Length() <= EPS && !(ret == Dir.Forward || ret == Dir.Backward)) ||
                ((m1.Right - m2.Up).Length() <= EPS && !(ret == Dir.Forward || ret == Dir.Backward)) ||
                ((m1.Forward - m2.Up).Length() <= EPS && !(ret == Dir.Left || ret == Dir.Right)) ||
                ((m1.Backward - m2.Up).Length() <= EPS && !(ret == Dir.Left || ret == Dir.Right))
            ) ret = flip(ret);
            return ret;
        };
        blocks.Where(b => b is IMyGyro && b.CubeGrid == controller.CubeGrid && !b.CustomName.Contains("[-shb]")).Select(b => b as IMyGyro).ToList().ForEach(
            g => gyroMap[g] = new Alignment(
                matchDir(controller.WorldMatrix, g.WorldMatrix, g.WorldMatrix.Forward),
                matchDir(controller.WorldMatrix, g.WorldMatrix, g.WorldMatrix.Left),
                matchDir(controller.WorldMatrix, g.WorldMatrix, g.WorldMatrix.Up)
            ));
    }

    private void brakePosition(Dictionary<Dir, float> d_vel, float mass) {
        foreach (var dir in (Dir[]) Enum.GetValues(typeof(Dir))) {
            if (d_vel[dir] <= EPS) {
                thrustMap[dir].ForEach(t => t.ThrustOverride = 1.001f);
                continue;
            }

            var force = d_vel[dir] * mass;
            foreach (var t in thrustMap[dir]) {
                var amount = Math.Min(force, t.MaxThrust);
                t.ThrustOverride = amount;
                force -= amount;
                if (force <= 0f) break;
            };
        }
    }
    private static Func<Vector3D, Dir, double> mapVel = (v, d) => {
        // x(lt) - roll, y(fw) - pitch, z(up) - yaw
        switch (d) {
            case Dir.Forward: return v.Y;
            case Dir.Backward: return -v.Y;
            case Dir.Left: return v.X;
            case Dir.Right: return -v.X;
            case Dir.Up: return -v.Z;
            case Dir.Down: return v.Z;
            default: return 0d;
        }
    };
    private static Func<double, double> magnify = f => f * 2d;
    private void brakeRotation(Vector3D a_vel, float mass) {
        a_vel.X = magnify(a_vel.X);
        a_vel.Y = magnify(a_vel.Y);
        a_vel.Z = magnify(a_vel.Z);
        foreach (var g in gyroMap) {
            var gyro = g.Key;
            var align = g.Value;

            var roll = (float) mapVel(a_vel, align.Forward);
            var pitch = (float) mapVel(a_vel, align.Left);
            var yaw = (float) mapVel(a_vel, align.Up);
            if (gyro.Enabled) output +=
                "\n" + gyro.CustomName + " : " +
                "\n ff > " + align.Forward.ToString("G") + " : " + roll.ToString("0.000") +
                "\n lt > " + align.Left.ToString("G") + " : " + pitch.ToString("0.000") +
                "\n up > " + align.Up.ToString("G") + " : " + yaw.ToString("0.000");

            gyro.Roll = roll;
            gyro.Pitch = pitch;
            gyro.Yaw = yaw;
        }
    }

    private static Func<MatrixD, Vector3D> getAngles = m => {
        Vector3D rot;
        MatrixD.GetEulerAnglesXYZ(ref m, out rot);
        return rot;
    };
    private static Func<MatrixD, Vector3D, Dir, float> getPVec = (m, v, d) => {
        var cn_vec = Vector3D.Normalize(getVec(m, d));

        var p_vec = Vector3D.ProjectOnVector(ref v, ref cn_vec);
        if (CosBetween(p_vec, cn_vec) <= 0f) p_vec = new Vector3D(0d, 0d, 0d);

        return (float) p_vec.Length();
    };
    private static Func<Vector3D, Vector3D, Vector3D> projOnPlane = (v1, v2) => Vector3D.ProjectOnPlane(ref v1, ref v2);
    private static Func<MatrixD, MatrixD, Vector3D> angBetween = (m1, m2) => {
        var z_mlt = CwMult(m1.Forward, m2.Forward, m1.Up);
        var z_ang = AngleBetween(m1.Forward, projOnPlane(m2.Forward, m1.Up));

        var x_mlt = CwMult(m1.Forward, m2.Forward, m1.Left);
        var x_ang = AngleBetween(m1.Forward, projOnPlane(m2.Forward, m1.Left));

        var y_mlt = CwMult(m1.Up, m2.Up, m1.Forward);
        var y_ang = AngleBetween(m1.Up, projOnPlane(m2.Up, m1.Forward));

        return new Vector3D(x_mlt * x_ang, y_mlt * y_ang, z_mlt * z_ang);
    };
    public void update() {
        output = "";
        if (controller == null) return;

        if (state.active) {
            var vel = controller.GetShipVelocities();
            var l_vel = vel.LinearVelocity;
            var a_vel = vel.AngularVelocity;

            var pos = state.location.Translation;
            var rot = getAngles(state.location);
            var c_pos = controller.WorldMatrix.Translation;
            var c_rot = getAngles(controller.WorldMatrix);

            var p_vec = pos - c_pos;
            //p_vec = p_vec - l_vel;
            p_vec = mapVec(p_vec - l_vel, d => {
                var nd = Math.Sign(d) * Math.Pow(d, 2);
                if (Math.Abs(nd) <= 0.25d) return 0d;
                else return nd;
            });
            //var p_rot = c_rot - rot;
            //p_rot = -a_vel;
            var p_rot = angBetween(controller.WorldMatrix, state.location);
            p_rot = p_rot - (new Vector3D(a_vel.X, a_vel.Z, a_vel.Y) * 0.25f);
            output +=
                "\np_rot: " + p_rot.X.ToString("0.000") + ", " + p_rot.Y.ToString("0.000") + ", " + p_rot.Z.ToString("0.000");

            var mass = controller.CalculateShipMass().PhysicalMass;

            var d_vel = new Dictionary<Dir, float>();
            d_vel[Dir.Forward] = getPVec(controller.WorldMatrix, p_vec, Dir.Forward);
            d_vel[Dir.Backward] = getPVec(controller.WorldMatrix, p_vec, Dir.Backward);
            d_vel[Dir.Left] = getPVec(controller.WorldMatrix, p_vec, Dir.Left);
            d_vel[Dir.Right] = getPVec(controller.WorldMatrix, p_vec, Dir.Right);
            d_vel[Dir.Up] = getPVec(controller.WorldMatrix, p_vec, Dir.Up);
            d_vel[Dir.Down] = getPVec(controller.WorldMatrix, p_vec, Dir.Down);
            foreach (var dir in (Dir[]) Enum.GetValues(typeof(Dir)))  d_vel[dir] *= mass;

            brakePosition(d_vel, mass);
            if (!Double.IsNaN(p_rot.X) && !Double.IsNaN(p_rot.Y) && !Double.IsNaN(p_rot.Z)) brakeRotation(p_rot, mass);
        }

        if (console == null) return;
        if (!consoleLock) console.WriteText(output);
    }

    public void engage() {
        state.location = new MatrixD(controller.WorldMatrix);
        state.active = true;

        foreach (var g in gyroMap) g.Key.GyroOverride = true;
        foreach (var dir in (Dir[]) Enum.GetValues(typeof(Dir))) thrustMap[dir].ForEach(t => t.ThrustOverride = 1.001f);
    }
    public void disengage() {
        state.active = false;
        state.location = new MatrixD();

        foreach (var g in gyroMap.Keys) {
            g.Yaw = 0f;
            g.Pitch = 0f;
            g.Roll = 0f;
            g.GyroOverride = false;
        }
        foreach (var dir in (Dir[]) Enum.GetValues(typeof(Dir))) thrustMap[dir].ForEach(t => t.ThrustOverride = 0f);
    }
}

public static SBState state = null;
public static ShipBrake brake = null;

public void Init() {
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    brake = new ShipBrake(blocks, str => Echo(str));
    if (state == null) state = new SBState(false, brake.controller.WorldMatrix);
}

public Program() {
    if (!string.IsNullOrEmpty(Storage)) state = new SBState(Storage);
    Init();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Save() {
    Storage = state.ToString();
}

public static int lockCounter = 0;
public void Main(string arg, UpdateType updateSource) {
    if (brake.consoleLock) lockCounter ++;
    if (lockCounter >= 500) {
        lockCounter = 0;
        brake.consoleLock = false;
    }
    try {
        if (arg == "%1") {
            List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
            GridTerminalSystem.GetBlocks(blocks);
            Func<MatrixD, Vector3D, string> matchDir = (m1, v) => {
                if ((m1.Forward - v).Length() <= EPS) return "f";
                else if ((m1.Backward - v).Length() <= EPS) return "b";
                else if ((m1.Left - v).Length() <= EPS) return "l";
                else if ((m1.Right - v).Length() <= EPS) return "r";
                else if ((m1.Up - v).Length() <= EPS) return "u";
                else if ((m1.Down - v).Length() <= EPS) return "d";
                else throw new Exception("No fitting direction for static mesh?");
            };
            var c = (IMyShipController) blocks.FirstOrDefault(b => b is IMyShipController && b.CustomName.Contains("[shb]"));
            blocks.Where(b => b is IMyGyro && !b.CustomName.Contains("[-shb]")).Select(b => b as IMyGyro).ToList().ForEach(g => {
                g.CustomName = "Gyroscope [" + matchDir(c.WorldMatrix, g.WorldMatrix.Up).ToUpper() +
                    "] (" + matchDir(c.WorldMatrix, g.WorldMatrix.Left) +
                    "/" + matchDir(c.WorldMatrix, g.WorldMatrix.Forward) +
                    "/" + matchDir(c.WorldMatrix, g.WorldMatrix.Up) + ")";
                g.GyroOverride = true;
                g.Pitch = -2f;
                g.Roll = 2f;
                g.Yaw = 2;
            });
        }
        if (brake != null) {
            if (string.IsNullOrEmpty(arg)) brake.update();
            else if (arg == "%engage") brake.engage();
            else if (arg == "%disengage") brake.disengage();
        }
    } catch (Exception e) {
        if (brake.console != null) {
            brake.consoleLock = true;
            string msg = e.Message + "\n\n" + e.StackTrace;
            int last_brake = 0;
            for (int i = 0; i < msg.Length; i++) {
                if (msg[i] == '\n') last_brake = 0;
                else if (last_brake > 30) msg.Insert(i, "\n");
            }
            brake.console.WriteText(msg);
        }
    }
}
