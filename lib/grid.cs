public IEnumerable<IMyTerminalBlock> getBlocks() {
    var blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.GetBlocks(blocks);
    return blocks;
}
public IEnumerable<IMyTerminalBlock> getBlocks(Func<IMyTerminalBlock, bool> filter) => getBlocks().Where(filter);

public IEnumerable<IMyBlockGroup> getGroups() {
    var groups = new List<IMyBlockGroup>();
    GridTerminalSystem.GetBlockGroups(groups);
    return groups;
}
public IEnumerable<IMyBlockGroup> getGroups(Func<IMyBlockGroup, bool> filter) => getGroups().Where(filter);

public IEnumerable<IMyTerminalBlock> getGroupBlocks(IMyBlockGroup group) {
    var blocks = new List<IMyTerminalBlock>();
    group.GetBlocks(blocks);
    return blocks;
}
public IEnumerable<IMyTerminalBlock> getGroupBlocks(IMyBlockGroup group, Func<IMyTerminalBlock, bool> filter) => getGroupBlocks(group).Where(filter);
