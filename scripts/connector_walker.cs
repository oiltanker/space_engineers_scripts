/*
 * @walker
 * @walker-head
 * @walker-tail
 *
 * arguments:
 *   start - starts
 *   stop  - stops
 */

public const double dEPS = 0.001d;
public const float EPS = 0.05f;

private static readonly System.Text.RegularExpressions.Regex tagEndRegex = new System.Text.RegularExpressions.Regex(@"(\s|^)@walker-end(\s|$)");
private static readonly System.Text.RegularExpressions.Regex tagRegex = new System.Text.RegularExpressions.Regex(@"(\s|^)@walker(-(head|tail))?(\s|$)");
public static IMyTextPanel debugLcd = null;
public static void wipe() { if (debugLcd != null) debugLcd.WriteText(""); }
public static void print(string str = "") { if (debugLcd != null) debugLcd.WriteText(str + '\n', true); }
public static string prettyV3(Vector3D v) {
    return "< " + v.X.ToString("0.000") + ", " + v.Y.ToString("0.000") + ", " + v.Z.ToString("0.000");
}


public class walker {
    public class terminal {
        public IMyShipMergeBlock merge;
        public IMyShipConnector connector;
        public bool isWorking { get { return merge != null && connector != null && merge.IsFunctional && connector.IsWorking; } }
        public bool isConnected { get { return merge.IsConnected; } }
        public bool isFullyConnected { get { return merge.IsConnected && connector.IsConnected; } }
        public terminal(IMyShipMergeBlock merge, IMyShipConnector connector) {
            this.merge = merge;
            this.connector = connector;
        }
    }
    public bool isOk {get; private set;}

    // parts
    public terminal head;
    public terminal tail;
    public List<IMyPistonBase> hands;
    public IMyShipController control;

    public walker(List<IMyTerminalBlock> blocks) {
        initComponents(blocks);
        print("Initialized: STATUS: " + (isOk ? "OK" : "BROKEN"));
    }

    public void initComponents(List<IMyTerminalBlock> blocks) {
        try {
            var wBlocks = blocks.Where(b => tagRegex.IsMatch(b.CustomName)).ToList();
            var headM = wBlocks.FirstOrDefault(b => b is IMyShipMergeBlock && b.CustomName.Contains("@walker-head")) as IMyShipMergeBlock;
            var tailM = wBlocks.FirstOrDefault(b => b is IMyShipMergeBlock && b.CustomName.Contains("@walker-tail")) as IMyShipMergeBlock;
            var headC = wBlocks.FirstOrDefault(b => b is IMyShipConnector && b.CubeGrid == headM.CubeGrid && (b.Position - headM.Position).Length() <= 1) as IMyShipConnector;
            var tailC = wBlocks.FirstOrDefault(b => b is IMyShipConnector && b.CubeGrid == tailM.CubeGrid && (b.Position - tailM.Position).Length() <= 1) as IMyShipConnector;
            hands = wBlocks.Where(b => b is IMyPistonBase).Select(b => b as IMyPistonBase).ToList();
            control = wBlocks.FirstOrDefault(b => b is IMyShipController) as IMyShipController;
            if (headM != null || headC != null) head = new terminal(headM, headC);
            if (tailM != null || tailC != null) tail = new terminal(tailM, tailC);
        } catch (Exception e) {
            print(e.Message + '\n' + e.StackTrace.ToString());
            isOk = false;
            return;
        }
        isOk = true;
    }

    public void verifyParts() {
        print($"head: {head != null}, working: {head != null && head.isWorking}");
        print($"tail: {tail != null}, working: {tail != null && tail.isWorking}");
        print($"control: {control != null}, working: {control != null && control.IsWorking}");
        var whs = hands != null ? hands.Where(d => !d.IsWorking).Count().ToString() : "N/A";
        print($"hands: {hands != null}, count broken: {whs}");
        print($"head/tail connected: {head != null && tail != null && (head.isConnected || tail.isConnected)}");
        isOk =
            head != null && head.isWorking &&
            tail != null && tail.isWorking &&
            control != null && control.IsWorking &&
            hands != null && hands.Where(d => !d.IsWorking).Count() <= 0 &&
            (head.isConnected || tail.isConnected); // attached to a rail
    }

    public void update() {
        wipe();
        verifyParts();
        print("\nSTATUS: " + (isOk ? "OK" : "BROKEN"));
        if (!isOk) {
            return;
        }

        if (head.isConnected && !head.isFullyConnected) { // ensure head's full connection
            head.connector.Connect();
            return;
        }
        if (tail.isConnected && !tail.isFullyConnected) { // ensure tail's full connection
            tail.connector.Connect();
            return;
        }

        var mov = control.MoveIndicator.Z; 

        var sumLen = hands.Sum(p => p.CurrentPosition);
        var sumMax = hands.Sum(p => p.HighestPosition);
        var sumMin = hands.Sum(p => p.LowestPosition);
        var ppEPS = (sumMax - sumMin) * EPS; // piston position EPS
        if (Math.Abs(mov) < dEPS) { // be stationary
            print("Action: idling");
            hands.ForEach(p => p.Velocity = 0f);
        } else if (mov > 0) { // move left
            print("Action: moving LEFT");
            if (head.isFullyConnected && tail.isFullyConnected) { // in one of full positions
                print("Stage: switching");
                if (sumMax - sumLen < ppEPS) { // fully extended
                    tail.connector.Disconnect();
                    tail.merge.Enabled = false;
                } else if (sumLen - sumMin < ppEPS) { // fully contracted
                    if (!tagEndRegex.IsMatch(head.connector.OtherConnector.CustomName)) {
                        head.connector.Disconnect();
                        head.merge.Enabled = false;
                    } else print("Wont move, end reached.");
                } else print("ERROR: Unknown fully connected state!");
            } else if (head.isFullyConnected) { // contract
                print("Stage: contracting");
                if (sumLen - sumMin < EPS) { // fully contracted
                    hands.ForEach(p => p.Velocity = 0f);
                    if (tail.isConnected) tail.connector.Connect();
                    else tail.merge.Enabled = true;
                } else { // fully extended OR in the middle
                    hands.ForEach(p => p.Velocity = -1.5f);
                }
            } else if (tail.isFullyConnected) { // extend
                print("Stage: extending");
                if (sumMax - sumLen < EPS) { // fully extended
                    hands.ForEach(p => p.Velocity = 0f);
                    if (head.isConnected) head.connector.Connect();
                    else head.merge.Enabled = true;
                } else { // fully contracted OR in the middle
                    hands.ForEach(p => p.Velocity = 1.5f);
                }
            }
        } else if (mov < 0) { // move right
            print("Action: moving RIGHT");
            if (tail.isFullyConnected && head.isFullyConnected) { // in one of full positions
                print("Stage: switching");
                if (sumMax - sumLen < ppEPS) { // fully extended
                    head.connector.Disconnect();
                    head.merge.Enabled = false;
                } else if (sumLen - sumMin < ppEPS) { // fully contracted
                    if (!tagEndRegex.IsMatch(tail.connector.OtherConnector.CustomName)) {
                        tail.connector.Disconnect();
                        tail.merge.Enabled = false;
                    } else print("Wont move, end reached.");
                } else print("ERROR: Unknown fully connected state!");
            } else if (tail.isFullyConnected) { // contract
                print("Stage: contracting");
                if (sumLen - sumMin < EPS) { // fully contracted
                    hands.ForEach(p => p.Velocity = 0f);
                    if (head.isConnected) head.connector.Connect();
                    else head.merge.Enabled = true;
                } else { // fully extended OR in the middle
                    hands.ForEach(p => p.Velocity = -1.5f);
                }
            } else if (head.isFullyConnected) { // extend
                print("Stage: extending");
                if (sumMax - sumLen < EPS) { // fully extended
                    hands.ForEach(p => p.Velocity = 0f);
                    if (tail.isConnected) tail.connector.Connect();
                    else tail.merge.Enabled = true;
                } else { // fully contracted OR in the middle
                    hands.ForEach(p => p.Velocity = 1.5f);
                }
            }
        }
    }

    public void stop() { if (hands != null) hands.ForEach(p => p.Velocity = 0f); }
}
walker walkerO = null;

public void findPanel(List<IMyTerminalBlock> blocks) {
    if (debugLcd == null || !debugLcd.IsWorking) {
        debugLcd = blocks.FirstOrDefault(b => b is IMyTextPanel && tagRegex.IsMatch(b.CustomName)) as IMyTextPanel;
        if (debugLcd != null) {
            debugLcd.ContentType = ContentType.TEXT_AND_IMAGE;
            debugLcd.FontSize = 0.8f;
            debugLcd.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
        }
    }
}

public void init() {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    findPanel(blocks);
    walkerO = new walker(blocks);
}

public static int state = 0;
public Program() {
    Echo("");
    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
    Me.CustomName = "@walker program";
    if (state == 1) init();
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update1) {
        if (state == 1) {
            refreshTick++;
            if (refreshTick >= 200) {
                if (walkerO != null && walkerO.isOk) walkerO.stop();
                init();
                refreshTick = 0;
            }
            if (walkerO != null) walkerO.update();
        }
    } else {
        if (argument == "start") {
            if (walkerO == null) init();
            state = 1;
        } else if (argument == "stop" && state > 0) {
            if (walkerO != null) walkerO.stop();
            state = 0;
            walkerO = null;
        }
    }
}
