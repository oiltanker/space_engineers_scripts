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
public static int min2Len(Vector3I v1, Vector3I v2) {
    int[] arr = new int[] {
        Math.Abs(v1.X - v2.X),
        Math.Abs(v1.Y - v2.Y),
        Math.Abs(v1.Z - v2.Z)
    };
    int min1 = Int32.MaxValue, min2 = Int32.MaxValue;
    for (int i = 0; i < 3; i++) {
        if (min1 > arr[i]) {
            min2 = min1;
            min1 = arr[i];
        }
    }
    return min1 + min2;
}


public class StatorWalker {
    public class Arm {
        public IMyPistonBase piston;
        public IMyMotorStator stator;
        public IMyLandingGear leg;

        public Arm(IMyPistonBase piston, IMyMotorStator stator, IMyLandingGear leg) {
            this.piston = piston;
            this.stator = stator;
            this.leg = leg;
        }
    }

    public enum Mov { none, left, right }
    public enum State {
        unknown,
        malfunction,
        mov_left,
        stationary,
        mov_right,
    }
    private Action<string> echo;
    public Mov move;
    public State state;

    public string name;
    public Arm aLeft;
    public Arm aRight;
    public List<IMyTerminalBlock> edges;

    private bool isConnected(IMyMotorStator ms) {
        return ms.IsAttached;
    }
    // ef - effectively
    private static bool efExtended(IMyPistonBase pb) {
        return pb.MaxLimit - pb.CurrentPosition < 1f;
    }
    private static bool efRetracted(IMyPistonBase pb) {
        return pb.CurrentPosition - pb.MinLimit < 1f;
    }
    public StatorWalker(string name, List<IMyTerminalBlock> myBlocks, List<IMyTerminalBlock> ends, Action<string> echo) {
        this.echo = echo;
        move = Mov.none;
        state = State.unknown;
        this.name = name;

        aLeft = new Arm(
            (IMyPistonBase) myBlocks.FirstOrDefault(b => b is IMyPistonBase && b.CustomName.Contains(" L ")),
            (IMyMotorStator) myBlocks.FirstOrDefault(b => b is IMyMotorStator && b.CustomName.Contains(" L ")),
            (IMyLandingGear) myBlocks.FirstOrDefault(b => b is IMyLandingGear && b.CustomName.Contains(" L "))
        );
        aRight = new Arm(
            (IMyPistonBase) myBlocks.FirstOrDefault(b => b is IMyPistonBase && b.CustomName.Contains(" R ")),
            (IMyMotorStator) myBlocks.FirstOrDefault(b => b is IMyMotorStator && b.CustomName.Contains(" R ")),
            (IMyLandingGear) myBlocks.FirstOrDefault(b => b is IMyLandingGear && b.CustomName.Contains(" R "))
        );

        var r_vec = Vector3D.Normalize(aLeft.stator.WorldMatrix.Translation - aRight.stator.WorldMatrix.Translation);
        edges = ends;
    }

    public bool isAtEnd(IMyMotorStator ms) {
        return isConnected(ms) && (
            (ms.WorldMatrix.Translation - edges[0].WorldMatrix.Translation).Length() <= 4d ||
            (ms.WorldMatrix.Translation - edges[1].WorldMatrix.Translation).Length() <= 4d
        );
    }
    private int switchArm(Arm from, Arm to) {
        if (to.leg != null && to.leg.LockMode != LandingGearMode.Locked) {
            to.leg.Lock();
            return 0;
        } else {
            if (from.leg != null && from.leg.LockMode == LandingGearMode.Locked) from.leg.Unlock();
            from.stator.Detach();
            return 1;
        }
    }
    private int moveArm(Arm from, Arm to) {
        if (isConnected(to.stator) && isConnected(from.stator)) {
            if (efRetracted(to.piston)) {
                if (from.leg != null) {
                    from.leg.Enabled = true;
                }
                return switchArm(to, from);
            } else if (efExtended(to.piston)) {
                if (to.leg != null) {
                    to.leg.Enabled = true;
                }
                return switchArm(from, to);
            }
        } else if (isConnected(to.stator)) {
            if (to.piston.Status == PistonStatus.Extending || to.piston.Status == PistonStatus.Extended) {
                to.piston.Velocity = -to.piston.MaxVelocity;
                from.piston.Velocity = -from.piston.MaxVelocity;
                if (from.leg != null) from.leg.Enabled = false;
            } else if (to.piston.Status == PistonStatus.Retracted && from.piston.Status == PistonStatus.Retracted) {
                if (isConnected(from.stator)) return 1;
                else from.stator.Attach();
            } else {
                to.piston.Velocity = -to.piston.MaxVelocity;
                from.piston.Velocity = -from.piston.MaxVelocity;
                if (from.leg != null) from.leg.Enabled = false;
            }
        } else if (isConnected(from.stator)) {
            if (to.piston.Status == PistonStatus.Retracting || to.piston.Status == PistonStatus.Retracted) {
                to.piston.Velocity = to.piston.MaxVelocity;
                from.piston.Velocity = from.piston.MaxVelocity;
                if (to.leg != null) to.leg.Enabled = false;
            } else if (to.piston.Status == PistonStatus.Extended && from.piston.Status == PistonStatus.Extended) {
                if (isConnected(to.stator)) return 1;
                else to.stator.Attach();
            } else {
                to.piston.Velocity = to.piston.MaxVelocity;
                from.piston.Velocity = from.piston.MaxVelocity;
                if (to.leg != null) to.leg.Enabled = false;
            }
        } else return -1;

        return 0;
    }
    public void update() {
        if (state == State.unknown) {
            move = Mov.none;
            if (aLeft.piston.Status != aRight.piston.Status) {
                if (isConnected(aLeft.stator)) aRight.piston.Velocity = aLeft.piston.Velocity;
                else if (isConnected(aRight.stator)) aLeft.piston.Velocity = aRight.piston.Velocity;
            }

            if (isConnected(aLeft.stator) && isConnected(aRight.stator)) {
                state = State.stationary;
                if (aLeft.piston.CurrentPosition - aLeft.piston.MinLimit <= 1f) {
                    aLeft.piston.Velocity = -aLeft.piston.MaxVelocity;
                    aRight.piston.Velocity = -aRight.piston.MaxVelocity;
                } else if (aLeft.piston.MaxLimit - aLeft.piston.CurrentPosition <= 1f) {
                    aLeft.piston.Velocity = aLeft.piston.MaxVelocity;
                    aRight.piston.Velocity = aRight.piston.MaxVelocity;
                }
            } else if (isConnected(aLeft.stator)) {
                if (aLeft.piston.Status == PistonStatus.Extending) state = State.mov_right;
                else if (aLeft.piston.Status == PistonStatus.Retracting) state = State.mov_left;
                else state = State.stationary;
            } else if (isConnected(aRight.stator)) {
                if (aLeft.piston.Status == PistonStatus.Extending) state = State.mov_left;
                else if (aLeft.piston.Status == PistonStatus.Retracting) state = State.mov_right;
                else state = State.stationary;
            } else state = State.malfunction;

        } else if (state != State.malfunction) {
            if (move == Mov.left) {
                if (isAtEnd(aLeft.stator) && aLeft.piston.Status == PistonStatus.Retracted) move = Mov.none;
                else {
                    state = State.mov_left;
                    var res = moveArm(aRight, aLeft);
                    if (res == -1) state = State.malfunction;
                    else if (res == 1) {
                        move = Mov.none;
                        state = State.stationary;
                    }
                }
            } else if (move == Mov.right) {
                if (isAtEnd(aRight.stator) && aRight.piston.Status == PistonStatus.Retracted) move = Mov.none;
                else {
                    state = State.mov_right;
                    var res = moveArm(aLeft, aRight);
                    if (res == -1) state = State.malfunction;
                    else if (res == 1) {
                        move = Mov.none;
                        state = State.stationary;
                    }
                }
            } else if (move == Mov.none) {
                aLeft.piston.Velocity = 0f;
                aRight.piston.Velocity = 0f;
            }
        }
    }

    public void walkLeft() { move = Mov.left; }
    public void walkRight() { move = Mov.right; }
    public void walkStop() { move = Mov.none; }
}
public StatorWalker walker_x;
public StatorWalker walker_y;
public StatorWalker walker_z;

IMyTextPanel console;
IMyShipController controller;
IMyCameraBlock camera;
bool swapXY;

public void Init() {
    Echo("-> Init");
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);
    var reg = new System.Text.RegularExpressions.Regex(@"\[w3p(\:[^\]]+)?\]", System.Text.RegularExpressions.RegexOptions.Compiled);
    var w3pBlocks = blocks.Where(b => reg.IsMatch(b.CustomName)).ToList();

    controller = (IMyShipController) w3pBlocks.FirstOrDefault(b => b is IMyShipController);
    if (controller == null) throw new Exception("A controller block must be present.");
    else Echo("    Controller is present.");
    console = (IMyTextPanel) w3pBlocks.FirstOrDefault(b => b is IMyTextPanel);
    if (console != null) {
        Echo("    Console output is present.");
        console.ContentType = ContentType.TEXT_AND_IMAGE;
    }
    camera = (IMyCameraBlock) w3pBlocks.FirstOrDefault(b => b is IMyCameraBlock);
    if (camera != null) Echo("    Camera is present.");

    Me.CustomData = "";
    var ends = w3pBlocks.Where(b => b.CustomName.Contains("/end]")).ToList();
    walker_x = new StatorWalker("walker X",
        w3pBlocks.Where(b => b.CustomName.Contains("[w3p:x]")).ToList(),
        ends.Where(b => b.CustomName.Contains("x/end]")).ToList(),
        s => Echo(s));
    walker_y = new StatorWalker("walker Y",
        w3pBlocks.Where(b => b.CustomName.Contains("[w3p:y]")).ToList(),
        ends.Where(b => b.CustomName.Contains("y/end]")).ToList(),
        s => Echo(s));
    walker_z = new StatorWalker("walker Z",
        w3pBlocks.Where(b => b.CustomName.Contains("[w3p:z]")).ToList(),
        ends.Where(b => b.CustomName.Contains("z/end]")).ToList(),
        s => Echo(s));

    var vec = walker_y.aLeft.piston.WorldMatrix.Up;
    if (camera != null && Math.Abs(CosBetween(camera.WorldMatrix.Up, vec)) <= EPS) swapXY = true;
    else swapXY = false;
    Func<Vector3D, string> v2s = v => "(" + v.X.ToString("0.000") + ", " + v.Y.ToString("0.000") + ", " + v.Z.ToString("0.000") + ")";
    Echo("    vec: " + v2s(vec) + "\n    cam: " + v2s(camera.WorldMatrix.Up) + "\n    cos: " + CosBetween(camera.WorldMatrix.Up, vec).ToString("0.000"));
    Echo("<- Init");
}

public Program() {
    Echo("-> Program");
    Init();
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    Echo("<- Program");
}

public void Save() {
}

int console_timeout = 0;
int tick = 0;
public void Main(string arg, UpdateType updateSource) {
    if (controller == null) return;
    try {
        var output = "";
        var mov = controller.MoveIndicator;

            walker_x.update();
            output +=
                "\n" + walker_x.name + ":" +
                "\n  state: " + walker_x.state.ToString("G") + 
                "\n  move: " + walker_x.move.ToString("G") + 
                "\n  con: " + walker_x.aLeft.stator.IsAttached + " | " + walker_x.aRight.stator.IsAttached +
                "\n  p_stat: " + walker_x.aLeft.piston.Status.ToString("G") + " | " + walker_x.aRight.piston.Status.ToString("G") +
                "\n  end: " + walker_x.isAtEnd(walker_x.aLeft.stator) + " | " + walker_x.isAtEnd(walker_x.aRight.stator);

            walker_y.update();
            output +=
                "\n" + walker_y.name + ":" +
                "\n  state: " + walker_y.state.ToString("G") + 
                "\n  move: " + walker_y.move.ToString("G") +
                "\n  con: " + walker_y.aLeft.stator.IsAttached + " | " + walker_y.aRight.stator.IsAttached +
                "\n  p_stat: " + walker_y.aLeft.piston.Status.ToString("G") + " | " + walker_y.aRight.piston.Status.ToString("G") +
                "\n  end: " + walker_y.isAtEnd(walker_y.aLeft.stator) + " | " + walker_y.isAtEnd(walker_y.aRight.stator);

            walker_z.update();
            output +=
                "\n" + walker_z.name + ":" +
                "\n  state: " + walker_z.state.ToString("G") + 
                "\n  move: " + walker_z.move.ToString("G") +
                "\n  con: " + walker_z.aLeft.stator.IsAttached + " | " + walker_z.aRight.stator.IsAttached +
                "\n  p_stat: " + walker_z.aLeft.piston.Status.ToString("G") + " | " + walker_z.aRight.piston.Status.ToString("G") +
                "\n  end: " + walker_z.isAtEnd(walker_z.aLeft.stator) + " | " + walker_z.isAtEnd(walker_z.aRight.stator);

        var walk_x = !swapXY ? mov.X : mov.Z;
        var walk_y = !swapXY ? mov.Z : mov.X;
        var walk_z = mov.Y;
        if (Math.Abs(walk_x) > EPS) {
            if (walk_x > 0f) walker_x.walkRight();
            else if (walk_x < 0f) walker_x.walkLeft();
        } else walker_x.walkStop();
        if (Math.Abs(walk_y) > EPS) {
            if (walk_y > 0f) walker_y.walkRight();
            else if (walk_y < 0f) walker_y.walkLeft();
        } else walker_y.walkStop();
        if (Math.Abs(walk_z) > EPS) {
            if (walk_z > 0f) walker_z.walkRight();
            else if (walk_z < 0f) walker_z.walkLeft();
        } else walker_z.walkStop();

        output +=
            "\n\nmov: " + mov.X.ToString("0.000") + ", " + mov.Y.ToString("0.000") + ", " + mov.Z.ToString("0.000") +
            "\nwalk: " + walk_x.ToString("0.000") + ", " + walk_y.ToString("0.000");
        if (console_timeout <= 0)  if (console != null) console.WriteText("All fine (" + tick + ")\n" + output);
        else console_timeout--;
    } catch (Exception e) {
        if (console_timeout <= 0) {
            console_timeout = 500;
            if (console != null) console.WriteText("Exception (" + tick + ")\n" + e.ToString());
        } else console_timeout--;
    }
    tick++;
}
