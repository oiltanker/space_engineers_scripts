public static IMyTextSurface myLcd = null;
public static IMyTextPanel debugLcd = null;
public static IMyTerminalBlock customLcdBlock = null;
public static IMyTextSurface customLcd = null;

public static void wipe() {
    if (debugLcd != null) debugLcd.WriteText("");
    if (myLcd != null) myLcd.WriteText("");
    if (customLcd != null) customLcd.WriteText("");
}
public static void print(string str) {
    if (debugLcd != null) debugLcd.WriteText(str + '\n', true);
    if (myLcd != null) myLcd.WriteText(str + '\n', true);
    if (customLcd != null) customLcd.WriteText(str + '\n', true);
}

public void initTextSurface(IMyTextSurface surface, float fontSize) {
    surface.ContentType = ContentType.TEXT_AND_IMAGE;
    surface.FontSize = fontSize;
    surface.Alignment = VRage.Game.GUI.TextPanel.TextAlignment.LEFT;
}

public void initMeLcd(float fontSize = 1f) {
    if (Me.CubeGrid.GridSizeEnum == MyCubeSize.Large) { myLcd = Me.GetSurface(0); initTextSurface(myLcd, fontSize); }
}

public void findDebugLcd(IEnumerable<IMyTerminalBlock> blocks, @Regex tagRegex, float fontSize = 0.8f) {
    if (debugLcd == null || !debugLcd.IsWorking) {
        debugLcd = blocks.FirstOrDefault(b => b is IMyTextPanel && tagRegex.IsMatch(b.CustomName)) as IMyTextPanel;
        if (debugLcd != null) initTextSurface(debugLcd, fontSize);
    }
}

public void findNonStandardLcd(IEnumerable<IMyTerminalBlock> blocks, string tabBase, float fontSize = 0.8f) {
    if (customLcdBlock != null && customLcdBlock.IsWorking) return;
    var tagRegex = new @Regex($"(^|\\s)@{tabBase}-(\\d+)($|\\s)");
    IMyTerminalBlock block = blocks.FirstOrDefault(b => b is IMyTextSurfaceProvider && tagRegex.IsMatch(b.CustomName));
    if (block != null) {
        var sIdx = int.Parse(tagRegex.Match(block.CustomName).Groups[2].Value);
        var provider = block as IMyTextSurfaceProvider;
        if (0 <= sIdx && sIdx < provider.SurfaceCount) { customLcdBlock = block; customLcd = provider.GetSurface(sIdx); initTextSurface(customLcd, fontSize); }
    } else { customLcdBlock = null; customLcd = null; }
}
