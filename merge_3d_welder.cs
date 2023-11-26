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


public class MergeWalker {
    public class Arm {
        public IMyPistonBase piston;
        public IMyShipMergeBlock merge;
        public IMyShipConnector connect;

        public Arm(IMyPistonBase piston, IMyShipMergeBlock merge, IMyShipConnector connect) {
            this.piston = piston;
            this.merge = merge;
            this.connect = connect;
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
    public List<IMyShipMergeBlock> edges;

    private bool isConnected(IMyShipMergeBlock mb) {
        return edges[0].CubeGrid == mb.CubeGrid;
    }
    // ef - effectively
    private static bool efExtended(IMyPistonBase pb) {
        return pb.MaxLimit - pb.CurrentPosition < 1f;
    }
    private static bool efRetracted(IMyPistonBase pb) {
        return pb.CurrentPosition - pb.MinLimit < 1f;
    }
    public MergeWalker(string name, List<IMyTerminalBlock> myBlocks, List<IMyShipMergeBlock> merges, Action<string> echo) {
        this.echo = echo;
        move = Mov.none;
        state = State.unknown;
        this.name = name;

        //var myBlocks = blocks.Where(b => b.CustomName.Contains("[w3p:" + name + "]"));
        aLeft = new Arm(
            (IMyPistonBase) myBlocks.FirstOrDefault(b => b is IMyPistonBase && b.CustomName.Contains(" L ")),
            (IMyShipMergeBlock) myBlocks.FirstOrDefault(b => b is IMyShipMergeBlock && b.CustomName.Contains(" L ")),
            (IMyShipConnector) myBlocks.FirstOrDefault(b => b is IMyShipConnector && b.CustomName.Contains(" L "))
        );
        aRight = new Arm(
            (IMyPistonBase) myBlocks.FirstOrDefault(b => b is IMyPistonBase && b.CustomName.Contains(" R ")),
            (IMyShipMergeBlock) myBlocks.FirstOrDefault(b => b is IMyShipMergeBlock && b.CustomName.Contains(" R ")),
            (IMyShipConnector) myBlocks.FirstOrDefault(b => b is IMyShipConnector && b.CustomName.Contains(" R "))
        );

        var r_vec = Vector3D.Normalize(aLeft.merge.WorldMatrix.Translation - aRight.merge.WorldMatrix.Translation);
        Func<IMyTerminalBlock, bool> isRail = (b) => {
            if (b == aLeft.merge || b == aRight.merge) return false;
            var b_vec = b.WorldMatrix.Translation - aRight.merge.WorldMatrix.Translation;
            var dot = b_vec.Dot(r_vec);
            var b_len = b_vec.Length();
            var dist = Math.Sqrt(b_len*b_len - dot*dot);
            return dist < 3d;
        };
        var rail = merges.Where(b => isRail(b));
        edges = new List<IMyShipMergeBlock>();
        edges.Add(rail.FirstOrDefault(b => b.Position == rail.Max(b1 => b1.Position)));
        edges.Add(rail.FirstOrDefault(b => (b.Position - edges[0].Position).Length() == rail.Max(b1 => (b1.Position - edges[0].Position).Length())));
    }

    public bool isAtEnd(IMyShipMergeBlock mBlock) {
        return isConnected(mBlock) &&(
            Vector3I.DistanceManhattan(mBlock.Position, edges[0].Position) <= 1 ||
            Vector3I.DistanceManhattan(mBlock.Position, edges[1].Position) <= 1);
    }
    private int switchArm(Arm from, Arm to) {
        if (to.connect != null && to.connect.Status != MyShipConnectorStatus.Connected) {
            to.connect.Connect();
            return 0;
        } else {
            if (from.connect != null && from.connect.Status == MyShipConnectorStatus.Connected) from.connect.Disconnect();
            from.merge.Enabled = false;
            return 1;
        }
    }
    private int moveArm(Arm from, Arm to) {
        if (isConnected(to.merge) && isConnected(from.merge)) {
            if (efRetracted(to.piston)) {
                return switchArm(to, from);
            } else if (efExtended(to.piston)) {
                return switchArm(from, to);
            }
        } else if (isConnected(to.merge)) {
            if (to.piston.Status == PistonStatus.Extending || to.piston.Status == PistonStatus.Extended) {
                to.piston.Velocity = -to.piston.MaxVelocity;
                from.piston.Velocity = -from.piston.MaxVelocity;
                from.merge.Enabled = false;
            } else if (to.piston.Status == PistonStatus.Retracted) {
                from.merge.Enabled = true;
                return 1;
            } else {
                to.piston.Velocity = -to.piston.MaxVelocity;
                from.piston.Velocity = -from.piston.MaxVelocity;
                from.merge.Enabled = false;
            }
        } else if (isConnected(from.merge)) {
            if (to.piston.Status == PistonStatus.Retracting || to.piston.Status == PistonStatus.Retracted) {
                to.piston.Velocity = to.piston.MaxVelocity;
                from.piston.Velocity = from.piston.MaxVelocity;
                to.merge.Enabled = false;
            } else if (to.piston.Status == PistonStatus.Extended) {
                to.merge.Enabled = true;
                return 1;
            } else {
                to.piston.Velocity = to.piston.MaxVelocity;
                from.piston.Velocity = from.piston.MaxVelocity;
                to.merge.Enabled = false;
            }
        } else return -1;

        return 0;
    }
    public void update() {
        if (state == State.unknown) {
            move = Mov.none;
            if (aLeft.piston.Status != aRight.piston.Status) {
                if (isConnected(aLeft.merge)) aRight.piston.Velocity = aLeft.piston.Velocity;
                else if (isConnected(aRight.merge)) aLeft.piston.Velocity = aRight.piston.Velocity;
            }

            if (isConnected(aLeft.merge) && isConnected(aRight.merge)) {
                state = State.stationary;
                if (aLeft.piston.CurrentPosition - aLeft.piston.MinLimit <= 1f) {
                    aLeft.piston.Velocity = -aLeft.piston.MaxVelocity;
                    aRight.piston.Velocity = -aRight.piston.MaxVelocity;
                } else if (aLeft.piston.MaxLimit - aLeft.piston.CurrentPosition <= 1f) {
                    aLeft.piston.Velocity = aLeft.piston.MaxVelocity;
                    aRight.piston.Velocity = aRight.piston.MaxVelocity;
                }
            } else if (isConnected(aLeft.merge)) {
                if (aLeft.piston.Status == PistonStatus.Extending) state = State.mov_right;
                else if (aLeft.piston.Status == PistonStatus.Retracting) state = State.mov_left;
                else state = State.stationary;
            } else if (isConnected(aRight.merge)) {
                if (aLeft.piston.Status == PistonStatus.Extending) state = State.mov_left;
                else if (aLeft.piston.Status == PistonStatus.Retracting) state = State.mov_right;
                else state = State.stationary;
            } else state = State.malfunction;

        } else if (state != State.malfunction) {
            if (move == Mov.left) {
                if (isAtEnd(aLeft.merge) && aLeft.piston.Status == PistonStatus.Retracted) move = Mov.none;
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
                if (isAtEnd(aRight.merge) && aRight.piston.Status == PistonStatus.Retracted) move = Mov.none;
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
public MergeWalker walker_x;
public MergeWalker walker_y;
public MergeWalker walker_z;

IMyTextPanel console;
IMyShipController controller;
IMyCameraBlock camera;
bool swapXY;

public void Init() {
    Echo("-> Init");
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    controller = (IMyShipController) blocks.FirstOrDefault(b => b is IMyShipController && b.CustomName.Contains("[w3p]"));
    if (controller == null) throw new Exception("A controller block must be present.");
    console = (IMyTextPanel) blocks.FirstOrDefault(b => b is IMyTextPanel && b.CustomName.Contains("[w3p]"));
    if (console != null) console.ContentType = ContentType.TEXT_AND_IMAGE;
    camera = (IMyCameraBlock) blocks.FirstOrDefault(b => b is IMyCameraBlock && b.CustomName.Contains("[w3p]"));

    var merges = blocks.Where(b => b is IMyShipMergeBlock).Select(b => b as IMyShipMergeBlock).ToList();
    walker_x = new MergeWalker("merge X", blocks.Where(b => b.CustomName.Contains("[w3p:x]")).ToList(), merges, s => Echo(s));
    walker_y = new MergeWalker("merge Y", blocks.Where(b => b.CustomName.Contains("[w3p:y]")).ToList(), merges, s => Echo(s));
    walker_z = new MergeWalker("merge Z", blocks.Where(b => b.CustomName.Contains("[w3p:z]")).ToList(), merges, s => Echo(s));

    var vec = walker_x.aLeft.piston.WorldMatrix.Up;
    if (camera != null && Math.Abs(CosBetween(camera.WorldMatrix.Up, vec)) - 1f <= EPS) swapXY = true;
    else swapXY = false;
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
                "\n  con: " + walker_x.aLeft.merge.IsConnected + " | " + walker_x.aRight.merge.IsConnected +
                "\n  p_stat: " + walker_x.aLeft.piston.Status.ToString("G") + " | " + walker_x.aRight.piston.Status.ToString("G") +
                "\n  end: " + walker_x.isAtEnd(walker_x.aLeft.merge) + " | " + walker_x.isAtEnd(walker_x.aRight.merge);

            walker_y.update();
            output +=
                "\n" + walker_y.name + ":" +
                "\n  state: " + walker_y.state.ToString("G") + 
                "\n  move: " + walker_y.move.ToString("G") +
                "\n  con: " + walker_y.aLeft.merge.IsConnected + " | " + walker_y.aRight.merge.IsConnected +
                "\n  p_stat: " + walker_y.aLeft.piston.Status.ToString("G") + " | " + walker_y.aRight.piston.Status.ToString("G") +
                "\n  end: " + walker_y.isAtEnd(walker_y.aLeft.merge) + " | " + walker_y.isAtEnd(walker_y.aRight.merge);

            walker_z.update();
            output +=
                "\n" + walker_z.name + ":" +
                "\n  state: " + walker_z.state.ToString("G") + 
                "\n  move: " + walker_z.move.ToString("G") +
                "\n  con: " + walker_z.aLeft.merge.IsConnected + " | " + walker_z.aRight.merge.IsConnected +
                "\n  p_stat: " + walker_z.aLeft.piston.Status.ToString("G") + " | " + walker_z.aRight.piston.Status.ToString("G") +
                "\n  end: " + walker_z.isAtEnd(walker_z.aLeft.merge) + " | " + walker_z.isAtEnd(walker_z.aRight.merge);

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
        if (console_timeout == 0) console.WriteText("All fine!\n" + output);
        else console_timeout--;
    } catch (Exception e) {
        console_timeout = 500;
        if (console == null) {
            if (console_timeout == 0) console.WriteText(e.Message);
            else console_timeout--;
        }
    }
}
