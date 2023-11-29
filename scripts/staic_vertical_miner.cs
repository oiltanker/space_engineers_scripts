/*
 * [MINE Projector] - projector block at the base of the walker - should always project [MINE Pillar] blueprint
 * [MINE Merge] - merge block at the base of the walker
 * [MINE BaseC] - connector at the base of the walker
 * [MINE Hand] - piston of the walker
 * [MINE HeadC] - connector at the piston's head of the walker
 * [MINE Rotor] - advanced rotor at the piston's head of the walker
 * [MINE Drills] - drills at the piston's head of the walker
 * [MINE Welders] - welders at the piston's head of the walker
 * [MINE Panel] *optional - optional LCD text panel output
 *
 * arguments:
 *   start - starts mining process
 *   stop  - stops mining process
 */

public const float EPS = 0.1f;

static IMyGridTerminalSystem gridTerminalSystem = null;
static Action wipe = null;
static Action<string> print = null;
static IMyTextPanel textPanel = null;
static IMyGridProgramRuntimeInfo runtime = null;

public class MineWalker {
    // state
    public enum State { None, Pause, Start, WorkExtend, WorkContract }
    public State state = State.None;
    public bool isOk {get; private set;}

    // parts
    public IMyShipMergeBlock merge {get; private set;}
    public IMyShipConnector baseC {get; private set;}
    public IMyPistonBase hand {get; private set;}
    public IMyShipConnector headC {get; private set;}
    public IMyMotorAdvancedStator rotor {get; private set;}
    public List<IMyShipDrill> drills {get; private set;}
    public List<IMyShipWelder> welders {get; private set;}

    public MineWalker(string storage) {
        if (!string.IsNullOrEmpty(storage)) state = (State) Int32.Parse(storage);
        initComponents();
        print("Initialized: STATUS: " + (isOk ? "OK" : "BROKEN"));
    }

    public string getSaveString() {
        return ((int) state).ToString();
    }

    private bool getGroup<T>(string name, Action<List<T>> callback) where T: class {
            IMyBlockGroup group = gridTerminalSystem.GetBlockGroupWithName(name);
            if (group == null) return false;
            List<T> blocks = new List<T>();
            group.GetBlocksOfType<T>(blocks);
            if (blocks.Count <= 0) return false;
            callback(blocks);
            return true;
    }
    public void initComponents() {
        try {
            merge = gridTerminalSystem.GetBlockWithName("[MINE Merge]") as IMyShipMergeBlock;
            baseC = gridTerminalSystem.GetBlockWithName("[MINE BaseC]") as IMyShipConnector;
            hand  = gridTerminalSystem.GetBlockWithName("[MINE Hand]")  as IMyPistonBase;
            headC = gridTerminalSystem.GetBlockWithName("[MINE HeadC]") as IMyShipConnector;
            rotor = gridTerminalSystem.GetBlockWithName("[MINE Rotor]") as IMyMotorAdvancedStator;
            if (
                (merge == null || baseC == null || hand == null || headC == null || rotor == null) ||
                (!getGroup<IMyShipDrill>("[MINE Drills]", (blocks) => drills = blocks)) ||
                (!getGroup<IMyShipWelder>("[MINE Welders]", (blocks) => welders = blocks))
            ) {
                isOk = false;
                return;
            }
        } catch (Exception e) {
            print(e.Message + '\n' + e.StackTrace.ToString());
            isOk = false;
            return;
        }
        isOk = true;
    }

    public void verifyParts() {
        isOk =
            merge != null && merge.IsFunctional &&
            baseC != null && baseC.IsWorking &&
            hand != null && hand.IsWorking &&
            headC != null && headC.IsWorking &&
            rotor != null && rotor.IsWorking &&
            drills.Where(d => !d.IsFunctional).Count() <= 0 &&
            welders.Where(w => !w.IsFunctional).Count() <= 0 &&
            (merge.IsConnected || baseC.IsConnected || headC.IsConnected); // attached to a pillar
    }

    public void act(string arg) {
        if (arg == "start") { // start/begin mining
            if (state != State.None && state != State.Start && state != State.WorkContract && state != State.WorkExtend) {
                print("start/begin mining");
                state = State.Start;
            }
        }
        else if (arg == "stop") { // stop/pause mining
            print("stop/pause mining");
            if (state != State.None  && state != State.Pause) state = State.Pause;
        }
        else print("Unknown argument '" + arg + "'");
    }
    public void update() {
        verifyParts();
        wipe();
        print("STATUS: " + (isOk ? "OK" : "BROKEN") + "  TSLR: " + runtime.TimeSinceLastRun.TotalSeconds.ToString() + "s\nSTATE: " + state.ToString("G"));
        if (!isOk) {
            runtime.UpdateFrequency = UpdateFrequency.Update100;
            state = State.None;
            return;
        }

        if (state != State.None) print("PISTON POS: " + hand.CurrentPosition.ToString("0.00"));

        if (state == State.None) {
            state = State.Pause;
        } else if (state == State.Pause) { // stop/pause
            runtime.UpdateFrequency = UpdateFrequency.Update100;
            if (!merge.IsConnected && baseC.IsConnected && headC.IsConnected) { // bottom is connected, need to switch connection to merge block
                merge.Enabled = false;
                baseC.Disconnect();
            } else merge.Enabled = true;
            hand.Velocity = 0f;
            if (Math.Abs(hand.HighestPosition - hand.CurrentPosition) < EPS || Math.Abs(hand.CurrentPosition - hand.LowestPosition) < EPS)
                if (merge.IsConnected && baseC.Status == MyShipConnectorStatus.Connectable) baseC.Connect();
                if (headC.Status == MyShipConnectorStatus.Connectable) headC.Connect();
            rotor.RotorLock = true;
            rotor.TargetVelocityRPM = 0f;
            drills.ForEach(d => d.Enabled = false);
            welders.ForEach(w => w.Enabled = false);
        } else if (state == State.Start) { // start/begin
            runtime.UpdateFrequency = UpdateFrequency.Update10;
            if (baseC.IsConnected) {
                if (Math.Abs(hand.HighestPosition - hand.CurrentPosition) < EPS) state = headC.IsConnected ? State.WorkContract : State.WorkExtend; // fully extended
                else state = State.WorkExtend;
            } else if (headC.IsConnected) {
                if (baseC.IsConnected) {
                    if (Math.Abs(hand.HighestPosition - hand.CurrentPosition) > EPS) state = State.WorkContract;
                    else {
                        headC.Connect();
                        state = State.WorkExtend;
                    }
                } else state = State.WorkContract;
            } else baseC.Connect(); // only merge block is connected => connect base connector
        } else if (state == State.WorkExtend) { // drill down
            if (baseC.IsConnected) {
                if (Math.Abs(hand.HighestPosition - hand.CurrentPosition) > EPS) { // not fully extended
                    headC.Disconnect();
                    hand.Velocity = 0.05f;
                    drills.ForEach(d => d.Enabled = true);
                    rotor.RotorLock = false;
                    rotor.TargetVelocityRPM = 1f;
                    if ( // TODO: specify piston positions where welders must be active
                        (hand.CurrentPosition <= 1f) ||
                        (3.5f <= hand.CurrentPosition && hand.CurrentPosition <= 4f) ||
                        (5.5f <= hand.CurrentPosition && hand.CurrentPosition <= 8f)
                    ) {
                        print("Welding online");
                        welders.ForEach(w => w.Enabled = true);
                    } else {
                        welders.ForEach(w => w.Enabled = false);
                        print("Welders offline");
                    }
                } else {
                    headC.Connect();
                    hand.Velocity = 0f;
                    drills.ForEach(d => d.Enabled = false);
                    welders.ForEach(w => w.Enabled = false);
                    rotor.RotorLock = true;
                    rotor.TargetVelocityRPM = 0f;
                    state = State.WorkContract;
                }
            } else { // switch state or should we continue with our state
                if (Math.Abs(hand.CurrentPosition - hand.LowestPosition) < EPS) { // fully contracted
                    hand.Velocity = -0.5f;
                    if (!merge.IsConnected) if (!merge.Enabled) merge.Enabled = true;
                    else baseC.Connect();
                } else state = State.WorkContract;
            }
        } else if (state == State.WorkContract) { // move base down
            if (!headC.IsConnected) headC.Connect();
            else {
                baseC.Disconnect();
                merge.Enabled = false;
                hand.Velocity = -1f;
                if (Math.Abs(hand.CurrentPosition - hand.LowestPosition) < EPS) { // fully contracted
                    merge.Enabled = true;
                    //baseC.Connect();
                    hand.Velocity = -0.5f;
                    state = State.WorkExtend;
                }
            }
        }
    }
}
MineWalker mineState = null;

public void findPanel() {
    if (textPanel == null) {
        textPanel = gridTerminalSystem.GetBlockWithName("[MINE Panel]") as IMyTextPanel;
        if (textPanel != null) {
            textPanel.ContentType = ContentType.TEXT_AND_IMAGE;
            textPanel.FontSize = 1f;
            textPanel.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
        }
    }
}

public Program() {
    runtime = Runtime;
    gridTerminalSystem = GridTerminalSystem;

    IMyTextSurface myLcd = Me.GetSurface(0);
    myLcd.ContentType = ContentType.TEXT_AND_IMAGE;
    myLcd.FontSize = 1;
    myLcd.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;

    wipe = () => {
        myLcd.WriteText("");
        if (textPanel != null) textPanel.WriteText("");
    };
    print = (str) => {
        myLcd.WriteText(str + '\n', true);
        if (textPanel != null) textPanel.WriteText(str + '\n', true);
    };
    print("Initializing ...");

    mineState = new MineWalker(Storage);
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
}

public void Save() {
    if (mineState != null) Storage = mineState.getSaveString();
}

public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update10 || updateSource == UpdateType.Update100) {
        findPanel();
        if (mineState != null) mineState.update();
    }
    else mineState.act(argument);
}
