@import lib.printFull

public Program() {
    Echo("");
    Me.CustomName = "@test program";
    initMeLcd();
}

public void Main(string argument, UpdateType updateSource) {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    var controller = blocks.FirstOrDefault(b => b is IMyShipController && b.CubeGrid == Me.CubeGrid) as IMyShipController;
    if (controller != null) {
        wipe();
        var gVec = controller.GetNaturalGravity();
        var mass = controller.CalculateShipMass().PhysicalMass;

        var thrusters = blocks.Where(b => b is IMyThrust && (b.WorldMatrix.Forward - Vector3D.Normalize(gVec)).Length() < 0.1d).Cast<IMyThrust>();
        print($"thrusters: {thrusters.Count()}");
        if (argument == "start") {
            var force = gVec.Length() * mass;
            print($"force: {force.ToString("0.000")}");
            var forceAvailable = thrusters.Sum(t => t.MaxEffectiveThrust);
            print($"forceAvailable: {forceAvailable.ToString("0.000")}");
            var tOverride = force / forceAvailable;
            print($"tOverride: {tOverride.ToString("0.000")}");
            foreach (var t in thrusters) t.ThrustOverridePercentage = (float) tOverride;
        } else if (argument == "stop") {
            print($"override disabled");
            foreach (var t in thrusters) t.ThrustOverride = 0f;
        }
        
    }
}