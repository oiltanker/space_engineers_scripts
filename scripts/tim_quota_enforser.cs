public Dictionary<string, string> quotas = null;
public bool active = false;

public void echo(string str) {
    Me.GetSurface(0).WriteText(str + "\n", true);
}

public string serialize() {
    string str = active + "\0";
    foreach(var q in quotas) {
        str += '\0' + q.Key + '\t' + q.Value;
    }
    return str;
}

public void deserialize(string str) {
    string[] lines = str.Split('\0');
    quotas = new Dictionary<string, string>();
    foreach (var l in lines) {
        if (string.IsNullOrEmpty(l)) continue;
        else if (!l.Contains("\t")) {
            active = bool.Parse(l);
        } else {
            string[] kv = l.Split('\t');
            for (int i = 2; i < kv.Length; i++) kv[1] += "\t" + kv[i];
            quotas[kv[0]] = kv[1];
        }
    }
}

public Program() {
    if (!string.IsNullOrEmpty(Storage)) deserialize(Storage);
    Me.GetSurface(0).ContentType = ContentType.TEXT_AND_IMAGE;
    Runtime.UpdateFrequency = UpdateFrequency.Update100;
}

public void Save() {
    if (quotas != null) Storage = serialize();
}

public void Main(string argument, UpdateType updateSource) {
    if (!string.IsNullOrEmpty(argument)) {
        if (argument == "%start") {
            quotas = null;
            active = true;
        } else if (argument == "%stop") {
            active = false;
        }
    } else if (active) {
        Me.GetSurface(0).WriteText("", false);

        var blocks = new List<IMyTerminalBlock>();
        GridTerminalSystem.GetBlocks(blocks);

        var quotaPanels = blocks
            .Where(b => b is IMyTextPanel && b.CustomName.Contains("TIM") && b.CustomName.Contains("QUOTA"))
            .Select(b => b as IMyTextPanel).ToList();

        if (quotas == null) {
            quotas = new Dictionary<string, string>();
            quotaPanels.ForEach(p => quotas[p.CustomName] = p.GetText());
        } else {
            quotaPanels.ForEach(p => {
                if (quotas.ContainsKey(p.CustomName)) p.WriteText(quotas[p.CustomName]);
                else {
                    quotas[p.CustomName] = p.GetText();
                    echo("quota ADDED: \"" + p.CustomName + "\"");
                }
            });
            var panels = blocks.Where(b => b is IMyTextPanel).ToList();
            var keys = new List<string>(quotas.Keys);
            foreach (var k in keys) {
                if (panels.FirstOrDefault(b => b.CustomName == k) == null) {
                    echo("quota REMOVED: \"" + k + "\"");
                    quotas.Remove(k);
                }
            }
        }

        echo("\nCurrent quotas:");
        foreach(var k in quotas.Keys) echo("    \"" + k + "\"");
    }
}
