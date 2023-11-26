List<IMyPistonBase> pistons_x = new List<IMyPistonBase>();
List<IMyPistonBase> pistons_y = new List<IMyPistonBase>();
List<IMyPistonBase> pistons_z = new List<IMyPistonBase>();

public void stopDim(List<IMyPistonBase> pistons) {
    foreach(var piston in pistons) {
        piston.Velocity = 0f;
    }
}
public void extendDim(List<IMyPistonBase> pistons) {
    foreach(var piston in pistons) {
        piston.Velocity = 0.5f;
    }
}
public void retractDim(List<IMyPistonBase> pistons) {
    foreach(var piston in pistons) {
        piston.Velocity = -0.5f;
    }
}

public Program() {
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);

    foreach(var block in blocks) {
        string[] name = block.CustomName.Split(' ');
        if (block is IMyPistonBase) foreach(var part in name) {
            if (part.StartsWith("[w3p:")) {
                var piston = (IMyPistonBase) block;
                piston.MinLimit = 0f;
                piston.MaxLimit = 10f;

                if (part.Equals("[w3p:x]")) pistons_x.Add(piston);
                else if (part.Equals("[w3p:y]")) pistons_y.Add(piston);
                else if (part.Equals("[w3p:z]")) pistons_z.Add(piston);
            }
        }
    }
    stopDim(pistons_x);
    stopDim(pistons_y);
    stopDim(pistons_z);
}

public void Save() {
}

public void Main(string arg, UpdateType updateSource) {
    Echo(arg);
    if (!String.IsNullOrEmpty(arg)) {
        if (arg.StartsWith("(") && arg.EndsWith(")")) {
            string[] args = arg.Substring(1, arg.Length - 2).Split('/');
            foreach(var a in args) Echo(a);
            var action = args[0];
            List<IMyPistonBase> dim_pistons = null;

            if (args[1].Equals("x")) dim_pistons = pistons_x;
            if (args[1].Equals("y")) dim_pistons = pistons_y;
            if (args[1].Equals("z")) dim_pistons = pistons_z;

            if (action.Equals("stop")) stopDim(dim_pistons);
            if (action.Equals("extend")) extendDim(dim_pistons);
            if (action.Equals("retract")) retractDim(dim_pistons);
        }
    }
}
