@import lib.grid

public Program() {}
public void Save() {}

public void Main(string argument, UpdateType updateSource) {
   var blocks = getBlocks(b => b is IMyConveyorSorter && b.CubeGrid == Me.CubeGrid).Cast<IMyConveyorSorter>();
   Echo($"Sorters detected: {blocks.Count()}");

   MyInventoryItemFilter stone = new MyInventoryItemFilter("MyObjectBuilder_Ore/Stone", false);
   MyInventoryItemFilter ice = new MyInventoryItemFilter("MyObjectBuilder_Ore/Ice", false);
   List<MyInventoryItemFilter> filters = new List<MyInventoryItemFilter>() { stone, ice };
   
   foreach (var block in blocks) block.SetFilter(MyConveyorSorterMode.Whitelist, filters);
}