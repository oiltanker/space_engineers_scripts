string rotor_state = null;
string rotor1_name = "Rotor(base)";
string rotor2_name = "Rotor(top)";
IMyTerminalBlock rotor1 = null, rotor2 = null;

public Program()
{
    rotor_state = Storage;
    if (String.IsNullOrEmpty(rotor_state)) rotor_state = "stop";

    rotor1 = GridTerminalSystem.GetBlockWithName(rotor1_name);
    rotor2 = GridTerminalSystem.GetBlockWithName(rotor2_name);
}

public void Save()
{
    Storage = rotor_state;
}

public void Main(string argument, UpdateType updateSource)
{
    if (rotor_state.Equals("start")) {
        rotor1.SetValue("UpperLimit", 0f);
        rotor2.SetValue("LowerLimit", 0f);
        rotor_state = "stop";
    } else if (rotor_state.Equals("stop")) {
        rotor1.SetValue("UpperLimit", float.PositiveInfinity);
        rotor2.SetValue("LowerLimit", float.NegativeInfinity);
        rotor_state = "start";
    }
}