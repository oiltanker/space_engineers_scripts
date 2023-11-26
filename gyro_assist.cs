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
public class ShipBrake {
    public IMyShipController controller;
    public IMyTextPanel console;

    public Dictionary<IMyGyro, Alignment> gyroMap;

    private string output;
    private bool active;
    public bool consoleLock;

    public ShipBrake(List<IMyTerminalBlock> blocks, Action<string> echo) {
        active = false;
        consoleLock = false;

        controller = (IMyShipController) blocks.FirstOrDefault(b => b is IMyShipController && b.CustomName.Contains("[ga]"));
        if (controller == null) throw new Exception("No designated controller for SHB.");
        console = (IMyTextPanel) blocks.FirstOrDefault(b => b is IMyTextPanel && b.CustomName.Contains("[ga]"));
        if (console != null) console.ContentType = ContentType.TEXT_AND_IMAGE;

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
        blocks.Where(b => b is IMyGyro && b.CubeGrid == controller.CubeGrid && !b.CustomName.Contains("[-ga]")).Select(b => b as IMyGyro).ToList().ForEach(
            g => gyroMap[g] = new Alignment(
                matchDir(controller.WorldMatrix, g.WorldMatrix, g.WorldMatrix.Forward),
                matchDir(controller.WorldMatrix, g.WorldMatrix, g.WorldMatrix.Left),
                matchDir(controller.WorldMatrix, g.WorldMatrix, g.WorldMatrix.Up)
            ));
    }

    private static Func<Vector3D, Dir, double> mapVel = (v, d) => {
        // x(lt) - roll, y(fw) - pitch, z(up) - yaw
        switch (d) {
            case Dir.Forward: return v.Y;
            case Dir.Backward: return -v.Y;
            case Dir.Left: return v.X;
            case Dir.Right: return -v.X;
            case Dir.Up: return v.Z;
            case Dir.Down: return -v.Z;
            default: return 0d;
        }
    };
    private static Func<double, double> magnify = f => f/* * 2d*/;
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

    public void update() {
        output = "";
        if (controller == null) return;

        if (active) {
            var vel = controller.GetShipVelocities();
            var a_vel = vel.AngularVelocity;
            Vector2 rot = controller.RotationIndicator;
            float rol = controller.RollIndicator;

            Vector3D p_rot = new Vector3D(0d,0d,0d);
            if (Math.Abs(rot.X) > EPS) p_rot.X = rot.X;
            else p_rot.X = a_vel.Y;
            if (Math.Abs(rot.Y) > EPS) p_rot.Z = rot.Y;
            else p_rot.Z = a_vel.Z;
            if (Math.Abs(rol) > EPS) p_rot.Y = rol * 60f;
            else p_rot.Y = a_vel.X;
            output =
                "\nrot: " + rot.X.ToString("0.000") + ", " + rot.Y.ToString("0.000") +
                "\nrol: " + rol.ToString("0.000") +
                "\na_vel: " + a_vel.X.ToString("0.000") + ", " + a_vel.Y.ToString("0.000") + ", " + a_vel.Z.ToString("0.000") +
                " [" + (Math.Abs(a_vel.X) > Math.Abs(a_vel.Y) ? (Math.Abs(a_vel.X) > Math.Abs(a_vel.Z) ? "1" : "3") : (Math.Abs(a_vel.Y) > Math.Abs(a_vel.Z) ? "2" : "3")) + "]";

            var mass = controller.CalculateShipMass().PhysicalMass;

            if (!Double.IsNaN(p_rot.X) && !Double.IsNaN(p_rot.Y) && !Double.IsNaN(p_rot.Z)) brakeRotation(p_rot, mass);
        }

        if (console == null) return;
        if (!consoleLock) console.WriteText(output);
    }

    public void engage() {
        active = true;

        foreach (var g in gyroMap) g.Key.GyroOverride = true;
    }
    public void disengage() {
        active = false;

        foreach (var g in gyroMap.Keys) {
            g.Yaw = 0f;
            g.Pitch = 0f;
            g.Roll = 0f;
            g.GyroOverride = false;
        }
    }
}

public static ShipBrake brake = null;

public void Init() {
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    brake = new ShipBrake(blocks, str => Echo(str));
}

public Program() {
    Init();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Save() {
}

public static int lockCounter = 0;
public void Main(string arg, UpdateType updateSource) {
    if (brake.consoleLock) lockCounter ++;
    if (lockCounter >= 500) {
        lockCounter = 0;
        brake.consoleLock = false;
    }
    try {
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
