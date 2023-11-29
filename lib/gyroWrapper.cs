@import lib.alignment
@import lib.gyroRPY

public class wGyro {
    public IMyGyro gyro;
    public align alignment;
    public bool isWorking { get { return gyro.IsWorking; } }
    public bool isFunctional { get { return gyro.IsFunctional; } }
    public wGyro(IMyGyro gyro, MatrixD? anchor = null) {
        this.gyro = gyro;
        alignment = align.determine(gyro.WorldMatrix, anchor != null ? anchor.Value : gyro.CubeGrid.WorldMatrix);
    }
    public wGyro(IMyGyro gyro, IMyTerminalBlock anchor = null): this(gyro, anchor?.WorldMatrix) {}
    public void reset() { gyro.Roll = 0f; gyro.Pitch = 0f; gyro.Yaw = 0f; }
    public void capture() => gyro.GyroOverride = true;
    public void release(bool doReset = true) {
        gyro.GyroOverride = false;
        if (doReset) reset();
    }
    public void setRPY(float roll, float pitch, float yaw) {
        gyro.Roll  = chooseRPY(alignment, alignment.forward, roll, pitch, yaw);
        gyro.Pitch = chooseRPY(alignment, alignment.left   , roll, pitch, yaw);
        gyro.Yaw   = chooseRPY(alignment, alignment.up     , roll, pitch, yaw);
    }
}