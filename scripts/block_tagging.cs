public Program() {
    Echo("");
}

public void Main(string argument, UpdateType updateSource) {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    blocks.Where(b => b is IMyGyro && b.CubeGrid == Me.CubeGrid).ToList().ForEach(b => (b as IMyGyro).CustomName += " @cdrive");
    // blocks.Where(b => b is IMyBatteryBlock && b.CubeGrid == Me.CubeGrid).ToList().ForEach(b => (b as IMyBatteryBlock).CustomName = "battery @s2b-static");
}