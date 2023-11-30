@import lib.gyroArray

public class gStableArr {
    public IMyShipController controller;
    public wGyroArr first;
    public wGyroArr second;
    public gStableArr(IEnumerable<IMyGyro> gyros, IMyShipController controller) {
        this.controller = controller;
        int cnt = gyros.Count() % 2 == 0 ? gyros.Count() / 2 : gyros.Count() / 2 - 1;
        if (cnt == 0) throw new ArgumentException("Not enough gyros to create stabilization array");
        first  = new wGyroArr(gyros.Take(cnt),           controller);
        second = new wGyroArr(gyros.Skip(cnt).Take(cnt), controller);
    }
    private static void transfer(wGyroArr from, wGyroArr to) {
        int toTransfer = (from.count - to.count) / 2;
        if (toTransfer > 0) {
            var gs = from.gyros.Take(toTransfer);
            from.remove(gs, false, false);
            to.add(gs);
        } else {
            var g = from.gyros.First();
            from.remove(g);
        }
    }
    public void balance() {
        if (first.count > second.count) transfer(first, second);
        if (first.count < second.count) transfer(second, first);
    }
    public void release(bool doReset = true) { first.release(doReset); second.release(doReset); }
    public void capture() { first.capture(); second.capture(); }
    public void update() {
        if (!controller.IsWorking || first.count + second.count < 2) {
            release();
            return;
        }
        balance();

        var cRoll = -controller.RollIndicator; var cPitch = controller.RotationIndicator.X; var cYaw = -controller.RotationIndicator.Y;
        var pRoll  = cRoll  < -EPS ? -60f : 60f; var nRoll  = cRoll  > EPS ? 60f : -60f;
        var pPitch = cPitch < -EPS ? -60f : 60f; var nPitch = cPitch > EPS ? 60f : -60f;
        var pYaw   = cYaw   < -EPS ? -60f : 60f; var nYaw   = cYaw   > EPS ? 60f : -60f;

        capture();
        first.setRPY (pRoll, pPitch, pYaw);
        second.setRPY(nRoll, nPitch, nYaw);
    }
}