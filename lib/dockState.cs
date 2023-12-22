public List<IMyShipConnector> connectors = null;
public List<IMyLandingGear> landingGear = null;

public void initDockState(IEnumerable<IMyTerminalBlock> blocks) {
    connectors = blocks.Where(b => b is IMyShipConnector && b.IsSameConstructAs(Me)).Cast<IMyShipConnector>().ToList();
    landingGear = blocks.Where(b => b is IMyLandingGear && b.IsSameConstructAs(Me)).Cast<IMyLandingGear>().ToList();
}

public bool isCurrentlyDocked() => connectors.Any(c => c.IsConnected) || landingGear.Any(l => l.IsLocked);