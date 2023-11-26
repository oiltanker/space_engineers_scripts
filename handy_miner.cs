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
public static Vector3D projOnPlane(Vector3D v1, Vector3D v2) {
    return Vector3D.ProjectOnPlane(ref v1, ref v2);
}
public static Vector3D LocalAngVel(IMyShipController controller) {
	var worldLocalAngVelocities = controller.GetShipVelocities().AngularVelocity;
	var worldToAnchorLocalMatrix = Matrix.Transpose(controller.WorldMatrix.GetOrientation());
	return Vector3D.Transform(worldLocalAngVelocities, worldToAnchorLocalMatrix);
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
public struct MineHand{
    public IMyMotorStator stator;
    public bool reverse;
    public Dir forward;

    public MineHand(IMyMotorStator stator, bool reverse, Dir forward) {
        this.stator = stator;
        this.reverse = reverse;
        this.forward = forward;
    }
}
public class MineHandBrake {
    public IMyShipController controller;
    public IMyTextPanel console;

    public Dictionary<IMyGyro, Alignment> gyroMap;
    public List<MineHand> hands;
    public float handAngle;

    private string output;
    private bool active;
    public bool consoleLock;

    public MineHandBrake(List<IMyTerminalBlock> blocks, Action<string> echo) {
        active = false;
        consoleLock = false;
        handAngle = 0f;

        controller = (IMyShipController) blocks.FirstOrDefault(b => b is IMyShipController && b.CustomName.Contains("[mhb]"));
        if (controller == null) throw new Exception("No designated controller for SHB.");
        console = (IMyTextPanel) blocks.FirstOrDefault(b => b is IMyTextPanel && b.CustomName.Contains("[mhb]"));
        if (console != null) console.ContentType = ContentType.TEXT_AND_IMAGE;

        hands = blocks.Where(b => b is IMyMotorStator && b.CustomName.Contains("[mhb]")).Select(b => {
            //var drill = block.Where(b => b is IMyShipDrill && b.CubeGrid == b.CubeGrid);
            return new MineHand(b as IMyMotorStator, CosBetween(b.WorldMatrix.Up, controller.WorldMatrix.Left) < 0f, Dir.Forward);
        }).ToList();

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
        blocks.Where(b => b is IMyGyro && b.CubeGrid == controller.CubeGrid && !b.CustomName.Contains("[-mhb]")).Select(b => b as IMyGyro).ToList().ForEach(
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
    private static Func<double, double> magnify = f => f * 10d;
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
            /*if (gyro.Enabled) output +=
                "\n" + gyro.CustomName + " : " +
                "\n ff > " + align.Forward.ToString("G") + " : " + roll.ToString("0.000") +
                "\n lt > " + align.Left.ToString("G") + " : " + pitch.ToString("0.000") +
                "\n up > " + align.Up.ToString("G") + " : " + yaw.ToString("0.000");*/

            gyro.Roll = roll;
            gyro.Pitch = pitch;
            gyro.Yaw = yaw;
        }
    }
    private void moveArms(float angle) {
        foreach(var hand in hands) {
            var m1 = hand.stator.Top.WorldMatrix;
            var m2 = controller.WorldMatrix;
            var f_vec = hand.reverse ? m1.Left : m1.Right;
            var u_vec = hand.reverse ? m1.Up : m1.Up;
            var h_angle = CwMult(f_vec, m2.Forward, u_vec) * AngleBetween(f_vec, projOnPlane(m2.Forward, u_vec));
            var diff = (hand.reverse ? -angle : angle) - h_angle;
            output +=
                "\n" + hand.stator.CustomName + "(" + hand.reverse + ")" +
                "\n    diff: " + diff.ToString("0.000");
            hand.stator.TargetVelocityRad = diff * 10f;
        }
    }

    private static Func<MatrixD, Vector3D, Vector3D> angBetween = (m1, v2) => {
        var x_mlt = CwMult(m1.Down, v2, m1.Left);
        var x_ang = AngleBetween(m1.Down, projOnPlane(v2, m1.Left));

        var y_mlt = CwMult(m1.Down, v2, m1.Forward);
        var y_ang = AngleBetween(m1.Down, projOnPlane(v2, m1.Forward));

        return new Vector3D(x_mlt * x_ang, y_mlt * y_ang, 0d);
    };
    private int update_count = 0;
    public void update() {
        output = "";
        if (controller == null) return;

        if (active) {
            Vector2 rot = controller.RotationIndicator;
            if (Math.Abs(rot.X) > EPS) {
                handAngle -= rot.X / 500f;
                if (Math.Abs(handAngle) > 1.5708f) handAngle =  Math.Sign(handAngle) * 1.5708f;
            }

            //var vel = controller.GetShipVelocities();
            //var a_vel = vel.AngularVelocity;
            var a_vel = LocalAngVel(controller);

            var p_rot = angBetween(controller.WorldMatrix, controller.GetTotalGravity());
            var a_rot = new Vector3D(a_vel.X, 0, 0);
            var len = p_rot.Length() + a_rot.Length();
            p_rot = p_rot * (p_rot.Length() / len) + a_rot * (a_rot.Length() / len);
            if (Math.Abs(rot.Y) > EPS) p_rot.Z = rot.Y;
            output +=
                "\nrot: " + rot.X.ToString("0.000") + ", " + rot.Y.ToString("0.000") +
                "\na_vel: " + a_vel.X.ToString("0.000") + ", " + a_vel.Y.ToString("0.000") + ", " + a_vel.Z.ToString("0.000") +
                " [" + (Math.Abs(a_vel.X) > Math.Abs(a_vel.Y) ? (Math.Abs(a_vel.X) > Math.Abs(a_vel.Z) ? "1" : "3") : (Math.Abs(a_vel.Y) > Math.Abs(a_vel.Z) ? "2" : "3")) + "]";

            var mass = controller.CalculateShipMass().PhysicalMass;

            if (!Double.IsNaN(p_rot.X) && !Double.IsNaN(p_rot.Y) && !Double.IsNaN(p_rot.Z)) brakeRotation(p_rot, mass);
        } else if (update_count > 0) {
            foreach(var hand in hands) hand.stator.Enabled = false;
            if (update_count >= 15) {
                foreach(var hand in hands) hand.stator.Enabled = true;
                update_count = 0;
            } else update_count++;
        }
        output +=
            "\n ha: " + handAngle.ToString("0.000");
        moveArms(handAngle);

        if (console == null) return;
        if (!consoleLock) console.WriteText(output);
    }

    public void engage() {
        active = true;
        update_count = 0;
        foreach(var hand in hands) hand.stator.Enabled = true;

        foreach (var g in gyroMap) g.Key.GyroOverride = true;
    }
    public void disengage() {
        active = false;
        update_count = 1;
        handAngle = 0f;

        foreach (var g in gyroMap.Keys) {
            g.Yaw = 0f;
            g.Pitch = 0f;
            g.Roll = 0f;
            g.GyroOverride = false;
        }
    }
}

public static MineHandBrake brake = null;

public void Init() {
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    brake = new MineHandBrake(blocks, str => Echo(str));
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
