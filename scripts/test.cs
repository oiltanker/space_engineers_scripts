@import lib.printFull
@import lib.grid

public @Regex tag = new @Regex(@"(^|\s+)@manpul-(\d+)($|\s+)");

public void Main(string argument, UpdateType updateSource) {
    var blocks = getBlocks(b => b.IsSameConstructAs(Me));

    var output = "";

    foreach (var b in blocks) {
        if (b is IMyTextSurfaceProvider) {
            var match = tag.Match(b.CustomName);
            if (match.Success) {
                output += $"{b.CustomName} | {(b as IMyTextSurfaceProvider).SurfaceCount}\n";
                var sIdx = int.Parse(match.Groups[2].Value);
                output += $"sIdx: {sIdx} | {0 <= sIdx && sIdx < (b as IMyTextSurfaceProvider).SurfaceCount}\n";
            }
        }
    }

    // var rotor = blocks.FirstOrDefault(b => b is IMyMotorStator && tag.IsMatch(b.CustomName)) as IMyMotorStator;
    // if (rotor != null) {
    //     var actions = new List<ITerminalAction>();
    //     rotor.GetActions(actions);
    //     actions.ForEach(a => {
    //         output += $"{a.Id} | {a.Name}\n";
    //     });
    // }

    // var block = blocks.FirstOrDefault(b => b is IMyPistonBase) as IMyPistonBase;
    // if (block != null) {
    //     var actions = new List<ITerminalAction>();
    //     block.GetActions(actions);
    //     output += "\n-- ACTIONS --\n";
    //     actions.ForEach(a => {
    //         output += $"{a.Id} | {a.Name}\n";
    //     });
    //     var properties = new List<ITerminalProperty>();
    //     block.GetProperties(properties);
    //     output += "\n-- PROPERTIES --\n";
    //     properties.ForEach(p => {
    //         output += $"{p.Id} | {p.TypeName}\n";
    //     });
    // }

    // foreach (var r in blocks.Where(b => b is IMyMotorStator).Cast<IMyMotorStator>()) {
    //     output += $"{r.CustomName} - {r.GetValue<bool>("ShareInertiaTensor")}\n";

    //     var action = r.GetActionWithName("ShareInertiaTensor");
    //     if (action != null) output += $"action | {action.Id} - {action.IsEnabled(r)}\n";
    //     else output += "null\n";

    //     var property = r.GetProperty("ShareInertiaTensor");
    //     if (property != null) output += $"property | {property.Id} - {property.TypeName}\n";
    //     else output += "null\n";

    //     output += "\n";
    // }

    // foreach (var p in blocks.Where(b => b is IMyPistonBase && b.IsSameConstructAs(Me)).Cast<IMyPistonBase>()) {
    //     output += $"{p.CustomName} - {p.GetValue<float>("MaxImpulseAxis")} | {p.GetValue<float>("MaxImpulseNonAxis")}\n";
    //     p.SetValue<float>("MaxImpulseAxis", float.PositiveInfinity);
    //     p.SetValue<float>("MaxImpulseNonAxis", float.PositiveInfinity);
    //     output += "\n";
    // }

    // var stators = blocks.Where(b => b is IMyMotorAdvancedStator && b.IsSameConstructAs(Me)).Cast<IMyMotorAdvancedStator>();
    // foreach (var s in stators) {
    //     output += $"{s.CustomName} | {s.BlockDefinition.SubtypeName}\n";
    // }
    
    Echo(output);
}