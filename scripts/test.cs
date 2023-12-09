public void Main(string argument, UpdateType updateSource) {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    var output = "";

    // var rotor = blocks.FirstOrDefault(b => b is IMyMotorStator) as IMyMotorStator;
    // if (rotor != null) {
    //     var actions = new List<ITerminalAction>();
    //     rotor.GetActions(actions);
    //     actions.ForEach(a => {
    //         output += $"{a.Id} | {a.Name}\n";
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

    var stators = blocks.Where(b => b is IMyMotorAdvancedStator && b.IsSameConstructAs(Me)).Cast<IMyMotorAdvancedStator>();
    foreach (var s in stators) {
        output += $"{s.CustomName} | {s.BlockDefinition.SubtypeName}\n";
    }
    
    Echo(output);
}