@import lib.gyroArray
@import lib.angularVelocity

public class gStableArr {
    public IMyShipController controller;
    public wGyroArr first;
    public wGyroArr second;
    bool passive;
    public gStableArr(IEnumerable<IMyGyro> gyros, IMyShipController controller, bool passive = false) {
        this.controller = controller; this.passive = passive;
        int cnt = gyros.Count() / 2;
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
    public void standby() { first.standby(); second.standby(); }
    public void update(double delta = 1d / ENG_UPS) {
        if (!controller.IsWorking || first.count + second.count < 2) {
            release();
            return;
        }
        balance();

        float pRoll, pPitch, pYaw, nRoll, nPitch, nYaw;
        if (!passive) {
            var cRoll = controller.RollIndicator; var cPitch = controller.RotationIndicator.X; var cYaw = controller.RotationIndicator.Y;
            var rotVel = toLocalAVel(controller.GetShipVelocities().AngularVelocity, controller);
            pRoll  = (cRoll  < -EPS || rotVel.Z > 0d) ? -60f : 60f; nRoll  = (cRoll  > EPS || rotVel.Z < 0d) ? 60f : -60f;
            pPitch = (cPitch < -EPS || rotVel.X > 0d) ? -60f : 60f; nPitch = (cPitch > EPS || rotVel.X < 0d) ? 60f : -60f;
            pYaw   = (cYaw   < -EPS || rotVel.Y > 0d) ? -60f : 60f; nYaw   = (cYaw   > EPS || rotVel.Y < 0d) ? 60f : -60f;
        } else {
            pRoll  = 60f; nRoll  = -60f;
            pPitch = 60f; nPitch = -60f;
            pYaw   = 60f; nYaw   = -60f;
        }

        capture();
        first.setRPY (pRoll, pPitch, pYaw);
        second.setRPY(nRoll, nPitch, nYaw);
    }
}