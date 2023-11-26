public const double dEPS = 0.001d;
public const float EPS = 0.001f;
public const int ENG_UPS = 60;
public const double radToDegMul = 180 / Math.PI;

public static IMyTextPanel textPanel = null;
public static void wipe() { if (textPanel != null) textPanel.WriteText(""); }
public static void print(string str) { if (textPanel != null) textPanel.WriteText(str + '\n', true); }

public int state = 0;
public List<IMyShipDrill> drills = null;

public void update() {
    var invs = drills.Select(d => d.GetInventory()).OrderBy(i => i.VolumeFillFactor).ToList();
    // print($"inv count: {invs.Count}");
    var avgFill = (float) invs.Select(i => i.VolumeFillFactor).Sum() / (float) invs.Count;
    // print($"avgFill: {avgFill}");
    var least = 0; var most = invs.Count - 1;
    while (least <= most && Math.Abs(avgFill - invs[least].VolumeFillFactor) < EPS) least++;
    while (most >= least && Math.Abs(invs[most].VolumeFillFactor - avgFill) < EPS) most--;
    while (least < most) {
        // print($"  -- --\n  least: {least}    most: {most}");
        var lInv = invs[least]; var mInv = invs[most];
        var maxVolume = lInv.MaxVolume;
        var totalTransfer = (float) Math.Min(avgFill - lInv.VolumeFillFactor, mInv.VolumeFillFactor - avgFill);
        var transfersDone = 0f;
        for (var i = mInv.ItemCount - 1; i >= 0 && totalTransfer > 0f; i--) {
            // print($"    -- {i}    totalTransfer {totalTransfer}");
            var itm = mInv.GetItemAt(i).Value;
            var totalAmount = itm.Amount;
            // print($"    totalAmount: {totalAmount}");
            var totalFill = ((float) (totalAmount * itm.Type.GetItemInfo().Volume)) / (float) maxVolume;
            // print($"    totalFill: {totalFill}");
            var toTransfer = Math.Min(totalFill, totalTransfer);
            // print($"    toTransfer: {toTransfer}");
            var transferAmount = totalAmount * (toTransfer / totalFill);
            transfersDone += (float) transferAmount;
            // print($"    transferAmount: {transferAmount}");
            mInv.TransferItemTo(lInv, i, null, true, transferAmount);
            totalTransfer -= toTransfer;
        }
        var lDelta = Math.Abs(avgFill - lInv.VolumeFillFactor); var mDelta = Math.Abs(mInv.VolumeFillFactor - avgFill);
        if (lDelta < EPS || (transfersDone < EPS && lDelta < mDelta)) least++;
        if (mDelta < EPS || (transfersDone < EPS && lDelta > mDelta)) most--;
    }
}

public void init() {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    findPanel(blocks);
    drills = blocks.Where(b => b is IMyShipDrill && b.IsSameConstructAs(Me)).Select(b => b as IMyShipDrill).ToList();
}

public static readonly System.Text.RegularExpressions.Regex tagRegex = new System.Text.RegularExpressions.Regex(@"(\s|^)@drill_inv_eq(\s|$)");
public void findPanel(List<IMyTerminalBlock> blocks) {
    textPanel = blocks.FirstOrDefault(b => b is IMyTextPanel && b.IsWorking && b.IsSameConstructAs(Me) && tagRegex.IsMatch(b.CustomName)) as IMyTextPanel;
    if (textPanel != null) {
        textPanel.ContentType = ContentType.TEXT_AND_IMAGE;
        textPanel.FontSize = 0.8f;
        textPanel.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
    }
}

public Program() {
    Echo("");
    Me.CustomName = "@drill_inv_eq program";

    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    Runtime.UpdateFrequency = UpdateFrequency.Update10;
    if (state == 1) init();
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update10) {
        if (state == 1) {
            refreshTick++;
            if (refreshTick >= 20) {
                init();
                refreshTick = 0;
            } else if (drills != null && drills.Count > 0) {
                wipe();
                update();
            }
            
        }
    } else {
        if (argument == "start") {
            if (drills == null || drills.Count <= 0) init();
            state = 1;
        } else if (argument == "stop" && state > 0) {
            state = 0;
            drills = null;
        }
    }
}