public List<IMyShipConnector> connectors = null;
public List<IMyLandingGear> landingGear = null;
public static readonly @Regex tagExclude = new @Regex(@"(^|\s)@exclude($|\s)");

public void initDockState(IEnumerable<IMyTerminalBlock> blocks) {
    connectors = blocks.Where(b => b is IMyShipConnector && b.IsSameConstructAs(Me) && !tagExclude.IsMatch(b.CustomName)).Cast<IMyShipConnector>().ToList();
    landingGear = blocks.Where(b => b is IMyLandingGear && b.IsSameConstructAs(Me) && !tagExclude.IsMatch(b.CustomName)).Cast<IMyLandingGear>().ToList();
}

public bool isCurrentlyDocked() => connectors.Any(c => c.IsConnected) || landingGear.Any(l => l.IsLocked);