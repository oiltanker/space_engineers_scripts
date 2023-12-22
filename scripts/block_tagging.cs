@import lib.grid

public Program() {
    Echo("");
}

public void Main(string argument, UpdateType updateSource) {
    var blocks = getBlocks();

    @Regex tagRegex = new @Regex(@"(.*\s|^)@cdrive(\s.*|$)");
    foreach (var g in blocks.Where(b => b is IMyGyro && b.CubeGrid == Me.CubeGrid && tagRegex.IsMatch(b.CustomName)).Cast<IMyGyro>()) {
        var match = tagRegex.Match(g.CustomName);
        g.CustomName = match.Groups[1] + "@mdrive" + match.Groups[2];
    }
    // blocks.Where(b => b is IMyGyro && b.CubeGrid == Me.CubeGrid).ToList().ForEach(b => (b as IMyGyro).CustomName += " @cdrive");
    // blocks.Where(b => b is IMyBatteryBlock && b.CubeGrid == Me.CubeGrid).ToList().ForEach(b => (b as IMyBatteryBlock).CustomName = "battery @s2b-static");
}