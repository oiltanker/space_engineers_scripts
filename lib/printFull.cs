public static IMyTextSurface myLcd = null;
public static IMyTextPanel debugLcd = null;
public static void wipe() {
    if (debugLcd != null) debugLcd.WriteText("");
    if (myLcd != null) myLcd.WriteText("");
}
public static void print(string str) {
    if (debugLcd != null) debugLcd.WriteText(str + '\n', true);
    if (myLcd != null) myLcd.WriteText(str + '\n', true);
}
public void initMeLcd(float fontSize = 1f) {
    if (Me.CubeGrid.GridSizeEnum == MyCubeSize.Large) {
        myLcd = Me.GetSurface(0);
        myLcd.ContentType = ContentType.TEXT_AND_IMAGE;
        myLcd.FontSize = fontSize;
        myLcd.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
    }
}

public void findDebugLcd(List<IMyTerminalBlock> blocks, @Regex tagRegex, float fontSize = 0.8f) {
    if (debugLcd == null || !debugLcd.IsWorking) {
        debugLcd = blocks.FirstOrDefault(b => b is IMyTextPanel && tagRegex.IsMatch(b.CustomName)) as IMyTextPanel;
        if (debugLcd != null) {
            debugLcd.ContentType = ContentType.TEXT_AND_IMAGE;
            debugLcd.FontSize = fontSize;
            debugLcd.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
        }
    }
}
