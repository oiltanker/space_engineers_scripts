public const float charge_level_required = 0.5f; // battery charge deactivation level - between 0.0f (0%) and 1.0f (100%)
public const float h2_level_required = 0.5f; // h2 generator deactivation level - between 0.0f (0%) and 1.0f (100%)
public const float ice_fill_level = 0.9f; // ice storage cargo fill level to deactivate mining - between 0.0f (0%) and 1.0f (100%)
public const string prefix = "[hpm]";
public const string prefix_startTrigger = "[hpm:start]";
public const string prefix_pauseTrigger = "[hpm:pause]";

public bool active = true;

public void echo(string str) {
    Me.GetSurface(0).WriteText(str + "\n", true);
}

public Program() {
    Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;
    Runtime.UpdateFrequency = UpdateFrequency.Update1;
}

public void Save() {
}

public void runCheck() {
    echo("-> runCheck\n");
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    var batteries = blocks
        .Where(b => b is IMyBatteryBlock && b.CubeGrid == Me.CubeGrid)
        .Select(b => b as IMyBatteryBlock).ToList();
    var cargo = blocks
        .Where(b => b is IMyCargoContainer && b.CustomName.Contains(prefix))
        .Select(b => b as IMyCargoContainer).ToList();
    var h2gens = blocks
        .Where(b => b is IMyGasGenerator && b.CustomName.Contains(prefix))
        .Select(b => b as IMyGasGenerator).ToList();
    var tanks = blocks
        .Where(b => b is IMyGasTank && b.CustomName.Contains(prefix)
            && b.BlockDefinition.SubtypeName.Contains("Hydrogen"))
        .Select(b => b as IMyGasTank).ToList();
    var engines = blocks
        .Where(b => b is IMyPowerProducer && b.CustomName.Contains(prefix)
            && b.BlockDefinition.SubtypeName.Contains("Hydrogen"))
        .Select(b => b as IMyPowerProducer).ToList();
    var startMineTrigger = (IMyTimerBlock) blocks
        .FirstOrDefault(b => b is IMyTimerBlock && b.CustomName.Contains(prefix_startTrigger));
    var pauseMineTrigger = (IMyTimerBlock) blocks
        .FirstOrDefault(b => b is IMyTimerBlock && b.CustomName.Contains(prefix_pauseTrigger));
    
    echo("batteries: " + batteries.Count);
    //batteries.ForEach(b => echo("  " + b.CustomName));
    echo("cargo: " + cargo.Count);
    //cargo.ForEach(c => echo("  " + c.CustomName));
    echo("h2gens: " + h2gens.Count);
    //h2gens.ForEach(g => echo("  " + g.CustomName));
    echo("tanks: " + tanks.Count);
    //tanks.ForEach(t => echo("  " + t.CustomName));
    echo("engines: " + engines.Count);
    //engines.ForEach(e => echo("  " + e.CustomName));
    echo("startMineTrigger: " + (startMineTrigger != null ? startMineTrigger.CustomName : "null"));
    echo("pauseMineTrigger: " + (pauseMineTrigger != null ? pauseMineTrigger.CustomName : "null"));

    echo("");
    
    float powerLevel = 0f;
    batteries.ForEach(b => powerLevel += b.CurrentStoredPower / b.MaxStoredPower);
    powerLevel /= batteries.Count;

    if (float.IsNaN(powerLevel)) echo("powerLevel: ! MALFUNCTION !");
    else {
        echo("powerLevel: " + powerLevel.ToString("0.000") + "/" + charge_level_required.ToString("0.000"));
        if (powerLevel >= charge_level_required) engines.ForEach(e => e.Enabled = false);
        else engines.ForEach(e => e.Enabled = true);
    }

    float h2Level = 0f;
    tanks.ForEach(t => h2Level += (float) t.FilledRatio);
    h2Level /= tanks.Count;

    if (float.IsNaN(h2Level)) echo("h2Level: ! MALFUNCTION !");
    else {
        echo("h2Level: " + h2Level.ToString("0.000") + "/" + h2_level_required.ToString("0.000"));
        if (h2Level >= h2_level_required) h2gens.ForEach(g => g.Enabled = false);
        else h2gens.ForEach(g => g.Enabled = true);
    }

    float iceLevel = 0f;
    cargo.ForEach(c => iceLevel += (float) (Double.Parse(c.GetInventory(0).CurrentVolume.SerializeString()) / Double.Parse(c.GetInventory(0).MaxVolume.SerializeString())));
    iceLevel /= cargo.Count;
    echo("iceLevel: " + iceLevel.ToString("0.000") + "/" + ice_fill_level.ToString("0.000"));

    if (float.IsNaN(iceLevel)) echo("iceLevel: ! MALFUNCTION !");
    else {
        if (iceLevel < ice_fill_level) {
            if (startMineTrigger != null) startMineTrigger.Trigger();
            echo("\n-- Triggering START mining --\n");
        } else {
            if (pauseMineTrigger != null) pauseMineTrigger.Trigger();
            echo("\n-- Triggering PAUSE mining --\n");
        }
    }

    echo("<- runCheck");
}

public int tick_count = 0;
public void Main(string argument, UpdateType updateSource) {
    if (!string.IsNullOrEmpty(argument)) {
        if (argument == "%start") {
            active = true;
        } else if (argument == "%stop") {
            active = false;
        }
    } else if (active) {
        if (tick_count >= 120) {
            Me.GetSurface(0).WriteText("", false);
            tick_count = 0;
            runCheck();
        } else tick_count++;
    }
}