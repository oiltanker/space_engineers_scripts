@import lib.eps
@import lib.printFull
@import lib.grid
@import lib.alignment
@import lib.pid

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@manpul(\s|$)");

public static void setStatorVel(IMyMotorStator stator, double vel) {
    if (Math.Abs(vel) < 0.001d) {
        //stator.RotorLock = true;
        stator.TargetVelocityRad = 0f;
    } else {
        //stator.RotorLock = false;
        stator.TargetVelocityRad = (float) vel;
    }
}
public static void shutdownStator(IMyMotorStator stator) {
    if (stator.IsFunctional) { stator.RotorLock = false; stator.TargetVelocityRad = 0f; }
}
public static double getPiAngle(IMyMotorStator stator) => stator.Angle > Math.PI ? (2d * Math.PI - stator.Angle < EPS ? 0d : stator.Angle - 2d * Math.PI) : (stator.Angle < EPS ? 0d : stator.Angle);
public class manNozzle {
    public Vector3I angleMul;
    public IMyMotorStator hingePitch;
    public IMyMotorStator hingeYaw;
    public IMyMotorStator rotorRoll;
    public manNozzle(IMyMotorStator hingePitch, IMyMotorStator hingeYaw, IMyMotorStator rotorRoll, Vector3I angleMul) {
        this.hingePitch = hingePitch; this.hingeYaw = hingeYaw; this.rotorRoll = rotorRoll; this.angleMul = angleMul;
    }
    public void setRPY(double roll, double pitch, double yaw) {
        setStatorVel(rotorRoll,  angleMul.Z * roll);
        setStatorVel(hingePitch, angleMul.X * pitch);
        setStatorVel(hingeYaw,   angleMul.Y * yaw);
    }
    public void shutdown() {
        shutdownStator(rotorRoll); shutdownStator(hingePitch); shutdownStator(hingeYaw);
    }
}
public class manSegment {
    public int angleMul;
    public IMyMotorStator rotor;
    public List<IMyPistonBase> pistons;
    public IMyTerminalBlock measreTo;
    public pidCtrl anglePid = new pidCtrl(1d, 0.05d, 0.5d, 1d / ENG_UPS, 0.95d);
    public double length { get { return measreTo == null ? 0d : (measreTo.WorldMatrix.Translation - rotor.WorldMatrix.Translation).Length(); } }
    private static int getMul(Vector3D anchor, IMyMotorStator hinge) { // anchor to be base rotor head left vector
        var dp = anchor.Dot(hinge.WorldMatrix.Up);
        if (Math.Abs(dp) < 0.1d) dp = anchor.Dot(hinge.WorldMatrix.Forward);
        return Math.Sign(dp);
    }
    public manSegment(IMyMotorStator rotor, int angleMul, List<IMyPistonBase> pistons, IMyTerminalBlock measreTo) {
        this.angleMul = angleMul;
        this.rotor = rotor;
        this.pistons = pistons;
        this.measreTo = measreTo;
    }
    public void pack() {
        pistons.ForEach(p => p.Velocity = -5f);
    }
    public void unpack() {
        pistons.ForEach(p => p.Velocity = 5f);
    }
    public bool isPacked() => pistons.Sum(p => p.CurrentPosition - p.LowestPosition) < 0.01d;
    public bool isUnpacked() => pistons.Sum(p => p.HighestPosition - p.CurrentPosition) < 0.01d;
    public void shutdown() {
        pistons.ForEach(p => { if (p.IsFunctional) p.Velocity = 0f; });
        shutdownStator(rotor);
    }
}
public class nozzleAligment {
    public Vector3D roll; public Vector3D pitch; public Vector3D yaw;
    public Vector3D pos;
    public nozzleAligment() { reset(); }
    public void reset() { roll = Vector3D.Zero; resetRot(); }
    public void resetRot() { pitch = Vector3D.Zero; yaw = Vector3D.Zero; pos = Vector3D.Zero; }
}
public class failStateMgr {
    public int lastStep;
    public int stateFailCounter;
    public Action failAction;
    public failStateMgr(Action failAction = null) { reset(); this.failAction = failAction; }
    public bool failed(int step) {
        if (step > lastStep) {
            lastStep = step;
            stateFailCounter = 0;
        } else {
            if (stateFailCounter > 800) {
                print($"---- FAILED! ----");
                if (failAction != null) failAction();
                return true;
            } else stateFailCounter++;
        }
        return false;
    }
    public void reset() { lastStep = 0; stateFailCounter = 0; }
}
public class rampMgr {
    public int counter;
    public int time;
    public rampMgr(int time = 300) { reset(); this.time = time; }
    public float rampUp() {
        if (counter < time) counter++;
        return (float) counter / (float) time;
    }
    public float rampDown() {
        if (counter > 0) counter--;
        return (float) counter / (float) time;
    }
    public void reset() { counter = 0; }
}
public class manArm {
    public enum armState { none, packed, unpacked, packing, unpacking, fail }
    private armState _state;
    public armState state { set { print($" - set {value.ToString("G")}"); _state = value; } get { return _state; } }
    double rBaseOffset;
    public manSegment seg1;
    public manSegment seg2;
    public manSegment seg3;
    public manNozzle seg4;
    public IMyMotorStator rotorBase;
    public IMyMotorStator rotor1;
    public IMyMotorStator rotor2;
    public IMyShipController controller;
    public nozzleAligment nAlign;
    public failStateMgr failMgr;
    public rampMgr ramp;
    private static double getOffset(Vector3D anchor, IMyAttachableTopBlock top) {
        var mat = top.WorldMatrix;
        var boMul = mat.Up.Dot(mat.Left.Cross(anchor)); boMul = double.IsNaN(boMul) ? 0d : Math.Sign(boMul);
        return boMul == 0d ? 0d : boMul * Vector3D.Angle(mat.Left, anchor);
    }
    public manArm(IMyShipController controller, manSegment seg1, manSegment seg2, manSegment seg3, manNozzle seg4, IMyMotorStator rotorBase, IMyMotorStator rotor1, IMyMotorStator rotor2,
                  nozzleAligment nAlign = null, armState state = armState.none, failStateMgr failMgr = null, rampMgr ramp = null) {
        var errStr = "";
        if (seg4.rotorRoll == null || seg4.hingePitch == null || seg4.hingeYaw == null) errStr += "Wrong segment 4 configuration\n";
        if (seg3.rotor == null || seg3.measreTo == null) errStr += "Wrong segment 3 configuration\n";
        if (seg2.rotor == null || seg2.measreTo == null) errStr += "Wrong segment 2 configuration\n";
        if (seg1.rotor == null || seg1.measreTo == null) errStr += "Wrong segment 1 configuration\n";
        if (!String.IsNullOrEmpty(errStr)) throw new ArgumentException(errStr);

        rBaseOffset = getOffset(seg1.rotor.WorldMatrix.Forward, rotorBase.Top);

        this.state = state;
        this.controller = controller;
        this.seg1 = seg1; this.seg2 = seg2; this.seg3 = seg3; this.seg4 = seg4;
        this.rotorBase = rotorBase; this.rotor1 = rotor1; this.rotor2 = rotor2;
        this.nAlign = nAlign != null ? nAlign : new nozzleAligment();
        this.failMgr = failMgr != null ? failMgr : new failStateMgr();
        this.failMgr.failAction = () => { this.state = armState.fail; this.shutdown(); this.nAlign.resetRot(); };
        this.ramp = ramp != null ? ramp : new rampMgr(300);
    }

    public static double signSquare(double x) => Math.Sign(x)*x*x;
    public static double signCube(double x) => x*x*x;
    public void setHeadPos() {
        var offVec = seg3.measreTo.WorldMatrix.Translation - seg1.rotor.WorldMatrix.Translation;
        var uVec = seg1.rotor.WorldMatrix.Up;
        offVec = Vector3D.ProjectOnPlane(ref offVec, ref uVec);
        nAlign.pos = offVec + seg1.rotor.WorldMatrix.Translation; // remove drift
    }

    private double getOpositeIntersection(double tTheta, Vector3D dVec, Vector3D dest, double radius, double cbAngle) {
        var ndVec = Vector3D.Normalize(dVec);
        var dLen = dest.Length();
        var tangentAngle = cbAngle + Math.PI / 2d;
        var mul = Math.Sign(ndVec.X * Math.Sin(tangentAngle) + ndVec.Y * Math.Cos(tangentAngle));
        var nAngle = Math.Acos((seg1.length*seg1.length + dLen*dLen - radius*radius) / (2d * seg1.length * dLen));
        return tTheta + mul * nAngle;
    }
    private static double getAngle(IMyMotorStator rotor, int angleMul) {
        var aMul = rotor.WorldMatrix.Up.Dot(rotor.WorldMatrix.Backward.Cross(rotor.Top.WorldMatrix.Forward));
        var angle = double.IsNaN(aMul) ? 0d : angleMul * Math.Sign(aMul) *  Vector3D.Angle(rotor.WorldMatrix.Backward, rotor.Top.WorldMatrix.Forward);
        return (angle < 0d) ? 2d * Math.PI + angle : angle;
    }
    private double getHingeAngleDelta(IMyMotorStator hinge, Vector3D measure, ref Vector3D stored, double rot) {
        var uVec = hinge.WorldMatrix.Up;
        var pMeasure = Vector3D.ProjectOnPlane(ref measure, ref uVec);
        if (pMeasure.Length() < 0.1d) return 0d;

        if (Math.Abs(rot) > dEPS || stored.IsZero()) stored = pMeasure;
        else stored = Vector3D.ProjectOnPlane(ref stored, ref uVec);
        var mul = uVec.Dot(pMeasure.Cross(stored));
        var diff = double.IsNaN(mul) ? 0d : Math.Sign(mul) * Vector3D.Angle(pMeasure, stored);
        return rot - (double.IsNaN(diff) ? 0d : signSquare(diff * 10f) * 10f);
    }
    public void update(Vector3D mov, Vector3D rot) {
        if (!hanleState()) return;

        if (nAlign.pos.IsZero()) setHeadPos();
        var pHead = nAlign.pos + mov * 0.5d;

        var s1r = seg1.rotor.WorldMatrix;
        var vec = pHead - s1r.Translation;
        var uVec = s1r.Left;
        var lVec = Vector3D.Normalize(Vector3D.ProjectOnPlane(ref vec, ref uVec));
        var fVec = Vector3D.Normalize(uVec.Cross(lVec));

        var brMul = uVec.Dot(lVec.Cross(s1r.Forward));
        var brAngle = double.IsNaN(brMul) ? 0d : Math.Sign(brMul) * Vector3D.Angle(lVec, s1r.Forward);
        var brChange = brAngle - rBaseOffset;
        print($"rBaseOffset: {rBaseOffset.ToString("0.000")}    rBase change: {brChange.ToString("0.000")}");
        setStatorVel(rotorBase, signSquare(brChange * 2f) * 2f);

        var s1D = seg1.rotor.Top.WorldMatrix.Forward;
        var s2D = seg2.rotor.WorldMatrix.Forward;
        var mul12 = uVec.Dot(s1D.Cross(s2D));
        var r1Angle = double.IsNaN(mul12) ? 0d : Math.Sign(mul12) * Vector3D.Angle(s1D, s2D);
        var r1Vel = r1Angle;
        print($"r1Angle: {(r1Angle * radToDegMul).ToString("0.000")}    r1Vel: {(r1Vel * radToDegMul).ToString("0.000")}");
        setStatorVel(rotor1, signSquare(r1Vel * 2f) * 2f);

        var tMat = new MatrixD();
        tMat.Forward = fVec;
        tMat.Left =    lVec;
        tMat.Up =      uVec;
        tMat = MatrixD.Transpose(tMat);

        var offset = Vector3D.TransformNormal(seg2.rotor.WorldMatrix.Translation - s1r.Translation, MatrixD.Transpose(s1r));
        offset.Y = -offset.X; offset.X = offset.Z; offset.Z = 0d;
        print($"offset: {offset.X.ToString("0.000")}, {offset.Y.ToString("0.000")}, {offset.Z.ToString("0.000")}");
        var dest = Vector3D.TransformNormal(pHead - s1r.Translation, tMat); dest.Z = 0d;
        print($"dest: {dest.X.ToString("0.000")}, {dest.Y.ToString("0.000")}, {dest.Z.ToString("0.000")}");

        var dVec = dest - offset;
        print($"dVec: {dVec.X.ToString("0.000")}, {dVec.Y.ToString("0.000")}, {dVec.Z.ToString("0.000")}");
        var dist = dVec.Length();
        print($"dist: {dist.ToString("0.000")}");
        var theta = Math.PI + Math.Atan2(dVec.X, dVec.Y);
        print($"theta: {(theta * radToDegMul).ToString("0.000")}");

        var beta = -Math.Acos((dist*dist - seg2.length*seg2.length - seg3.length*seg3.length) / (2d * seg2.length * seg3.length));
        var innerAngle = beta + Math.PI;

        var s1Angle = getAngle(seg1.rotor, seg1.angleMul);
        // var nS1Angle = s1Angle;
        if (dist + 3d > seg2.length + seg3.length) { // can't reach
            print("can't reach");
            var tdAngle = Math.PI + Math.Atan2(dest.X, dest.Y);
            print($"tdAngle: {(tdAngle * radToDegMul).ToString("0.000")}");
            seg1.rotor.TargetVelocityRad = seg1.angleMul * ramp.rampUp() * (tdAngle < s1Angle ? 0.2f : -0.2f);
            // nS1Angle = s1Angle - 0.1f * (s1Angle - tdAngle);
            // nS1Angle = getOpositeIntersection(Math.PI + Math.Atan2(dest.X, dest.Y), dVec, dest, seg2.length + seg3.length, s1Angle);
        } else if (dist < Math.Abs(seg2.length - seg3.length)) { // too close
            print("too close");
            seg1.rotor.TargetVelocityRad = seg1.angleMul * ramp.rampUp() * -0.2f;
            // var tdAngle = Math.PI + Math.Atan2(dest.X, dest.Y);
            // nS1Angle = s1Angle + 0.1f * (s1Angle - tdAngle);
            // nS1Angle = getOpositeIntersection(Math.PI + Math.Atan2(dest.X, dest.Y), dVec, dest, Math.Abs(seg2.length - seg3.length), s1Angle);
        } else if (innerAngle <= Math.PI / 6d) {  // too close alt
            print("too close alternative");
            seg1.rotor.TargetVelocityRad = seg1.angleMul * ramp.rampUp() * -0.2f;
            // var tdAngle = Math.PI + Math.Atan2(dest.X, dest.Y);
            // nS1Angle = s1Angle + 0.5f * (s1Angle - tdAngle);
        } else seg1.rotor.TargetVelocityRad = ramp.rampDown() * Math.Sign(seg1.rotor.TargetVelocityRad) * 0.2f;
        // print($"s1Angle: {(s1Angle * radToDegMul).ToString("0.000")}    nS1Angle: {(nS1Angle * radToDegMul).ToString("0.000")}");
        print($"s1Angle: {(s1Angle * radToDegMul).ToString("0.000")}    ramp: {((float) ramp.counter / (float) ramp.time * 100f).ToString("0")}%");
        
        double nS2Angle = getAngle(seg2.rotor, seg2.angleMul);
        double nS3Angle = getAngle(seg3.rotor, seg3.angleMul);
        if (innerAngle > Math.PI / 6d) {
            print($"lenTotal: {seg2.length + seg3.length}");
            var alphaDiv = seg2.length + seg3.length * Math.Cos(beta);
            var alpha = theta - Math.Atan((seg3.length * Math.Sin(beta)) / alphaDiv) - (alphaDiv >= 0d ? 0d : Math.PI);
            print($"beta: {(beta * radToDegMul).ToString("0.000")}    alpha: {(alpha * radToDegMul).ToString("0.000")}");

            nS2Angle = Math.PI + alpha - s1Angle;
            nS3Angle = innerAngle;
        }

        adjustAngle(seg2, nS2Angle);
        adjustAngle(seg3, nS3Angle);
        if (!double.IsNaN(/*nS1Angle + */nS2Angle + nS3Angle)) nAlign.pos = pHead;

        // TODO: fix angle calculation and adjusment;
        var cMat = controller.WorldMatrix;
        var iPitch =  rot.X * cMat.Up.Dot(seg4.hingePitch.Top.WorldMatrix.Forward)   + rot.Y * cMat.Left.Dot(seg4.hingeYaw.Top.WorldMatrix.Up);
        var iYaw   = -rot.X * cMat.Left.Dot(seg4.hingePitch.Top.WorldMatrix.Forward) + rot.Y * cMat.Up.Dot(seg4.hingeYaw.Top.WorldMatrix.Up);
        var roll  = getHingeAngleDelta(seg4.rotorRoll,  controller.WorldMatrix.Left,    ref nAlign.roll,  rot.Z);
        var pitch = getHingeAngleDelta(seg4.hingePitch, controller.WorldMatrix.Forward, ref nAlign.pitch, iPitch);
        var yaw   = getHingeAngleDelta(seg4.hingeYaw,   controller.WorldMatrix.Forward, ref nAlign.yaw,   iYaw);
        print($"pitch: {(pitch * radToDegMul).ToString("0.000")}    yaw: {(yaw * radToDegMul).ToString("0.000")}");

        seg4.setRPY(roll, pitch, yaw); //(rot.X + difS1 + difS2 - difS3 + 0.01d * pPid.control(pAngle * 100f)),
    }

    private double getHingePackAngle(IMyMotorStator hinge, Vector3D measure) {
        var uVec = hinge.WorldMatrix.Up; var rVec = hinge.WorldMatrix.Right;
        var pMeasure = Vector3D.ProjectOnPlane(ref measure, ref uVec);

        var mul = uVec.Dot(pMeasure.Cross(rVec));
        var diff = double.IsNaN(mul) ? 0d : Math.Sign(mul) * Vector3D.Angle(pMeasure, rVec);
        return double.IsNaN(diff) ? 0d : signSquare(diff * 10f) * 10f;
    }
    private bool anglesAdjusted(double nS1Angle, double nS2Angle, double nS3Angle, double eps = 0.1d) =>
        Math.Abs(adjustAngle(seg1, nS1Angle)) + Math.Abs(adjustAngle(seg2, nS2Angle)) + Math.Abs(adjustAngle(seg3, nS3Angle)) < eps;
    private static double adjustAngle(manSegment seg, double nAngle, double mul = 2d) {
        var sAngle = getAngle(seg.rotor, seg.angleMul);
        var diff = double.IsNaN(nAngle) ? 0f : (float) (seg.angleMul * (sAngle - nAngle));
        setStatorVel(seg.rotor, signSquare(diff * mul) * mul);
        return diff;
    }
    private static bool angleAdjusted(manSegment seg, double nAngle, double eps = 0.1d) => Math.Abs(adjustAngle(seg, nAngle, 2d)) < eps;
    private bool segmentsPacked() {
        seg1.pack(); seg2.pack(); seg3.pack();
        return seg1.isPacked() && seg2.isPacked() && seg3.isPacked();
    }
    private bool segmentsUnpacked() {
        seg1.unpack(); seg2.unpack(); seg3.unpack();
        return seg1.isUnpacked() && seg2.isUnpacked() && seg3.isUnpacked();
    }
    private bool nozzleAdjusted() {
        var pitch = signSquare(seg4.hingePitch.Angle * -2f) * 10f;//getHingePackAngle(seg4.hingePitch, seg3.rotor.Top.WorldMatrix.Right);
        var yaw   = signSquare(seg4.hingeYaw.Angle   * -2f) * 10f;
        var roll  = signSquare(getPiAngle(seg4.rotorRoll)  * -2f) * 10f;
        seg4.setRPY(roll, pitch, yaw);
        return Math.Abs(pitch) + Math.Abs(yaw) + Math.Abs(roll) < 0.1d;
    }
    private bool adjusted = false;
    private static readonly List<Func<manArm, bool>> packActions = new List<Func<manArm, bool>>{
        (ma) => ma.nozzleAdjusted(), (ma) => ma.segmentsPacked(),
        (ma) => manArm.angleAdjusted(ma.seg3, Math.PI * 15d / 8d), (ma) => manArm.angleAdjusted(ma.seg2, Math.PI / 8d), (ma) => manArm.angleAdjusted(ma.seg1, Math.PI)
    };
    private static readonly List<Func<manArm, bool>> unpackActions = new List<Func<manArm, bool>>{
        (ma) => manArm.angleAdjusted(ma.seg1, Math.PI), (ma) => manArm.angleAdjusted(ma.seg2, Math.PI), (ma) => manArm.angleAdjusted(ma.seg3, Math.PI / 2d),
        (ma) => ma.nozzleAdjusted(), (ma) => ma.segmentsUnpacked(),
    };
    private bool performActions(List<Func<manArm, bool>> actions) {
        for (var i = 0; i < actions.Count; i++) {
            if (failMgr.failed(i) || !actions[i](this)) {
                print($" -- {i}");
                return false;
            }
        }
        return true;
    }
    private bool hanleState() {
        print($"Arm state: {state.ToString("G")}");
        var allPistons = seg1.pistons.Concat(seg2.pistons).Concat(seg3.pistons);
        if        (state == armState.none) {
            if      (allPistons.Sum(p => p.HighestPosition - p.CurrentPosition) < 0.01d) state = armState.unpacked;
            else if (allPistons.Sum(p => p.CurrentPosition - p.LowestPosition)  < 0.01d) state = armState.packed;
            else if (allPistons.Sum(p => p.Velocity) < 0d)                               state = armState.packing;
            else                                                                         state = armState.unpacking;
        } else if (state == armState.packing) {
            if (performActions(packActions)) { adjusted = false; shutdown(); state = armState.packed; }
        } else if (state == armState.unpacking) {
            if (performActions(unpackActions)) { shutdown(); state = armState.unpacked; }
        } else if (state == armState.packed) {
            print($"adjusted {adjusted}");
            if (!adjusted && anglesAdjusted(Math.PI, Math.PI / 8d, Math.PI * 15d / 8d, 0.05d)) {
                adjusted = true;
                shutdown();
            }
        } else if (state != armState.fail) failMgr.reset();
        return state == armState.unpacked || state == armState.fail;
    }

    public void pack()   { if (state == armState.unpacked || state == armState.fail) { state = armState.packing;   failMgr.reset(); } }
    public void unpack() { if (state == armState.packed || state == armState.fail)   { state = armState.unpacking; failMgr.reset(); } }
    public void shutdown() {
        seg1?.shutdown(); seg2?.shutdown(); seg3?.shutdown(); seg4?.shutdown();
        shutdownStator(rotorBase); shutdownStator(rotor1); shutdownStator(rotor2);
        rotor2.RotorLock = true;
    }
}

public class manHolder {
    public enum holderState { retractedAttached, retractedDetached, extendedAttached, extendedDetached }
    public enum holderAct { extend, retract, attach, detach, next }
    public holderState state;
    public List<holderAct> actionQueue;
    public List<IMyPistonBase> pistons;
    public List<IMyLandingGear> holders;
    public IMyMotorStator holderRotor;
    public IMyMotorStator nozzleRotor;
    public IMyLandingGear currentClosest;
    public manHolder(List<IMyPistonBase> pistons, List<IMyLandingGear> holders, IMyMotorStator holderRotor, IMyMotorStator nozzleRotor, List<holderAct> actionQueue = null) {
        if (nozzleRotor.Top != null) {
            if (pistons.Sum(p => p.Velocity) > 0f || pistons.Sum(p => p.HighestPosition - p.CurrentPosition) <= EPS) state = holderState.extendedAttached;
            else                                                                                                     state = holderState.retractedAttached;
        } else {
            if (pistons.Sum(p => p.Velocity) > 0f || pistons.Sum(p => p.HighestPosition - p.CurrentPosition) <= EPS) state = holderState.extendedDetached;
            else                                                                                                     state = holderState.retractedDetached;
        }
        this.actionQueue = actionQueue != null ? actionQueue : new List<holderAct>();
        this.pistons = pistons; this.holders = holders; this.holderRotor = holderRotor; this.nozzleRotor = nozzleRotor;
        currentClosest = getClosestHolder();
        if ((state == holderState.retractedAttached || state == holderState.retractedDetached) && currentClosest.IsLocked && pistons.Sum(p => p.CurrentPosition - p.LowestPosition) > EPS) { // needs to be fully extended
            pistons.ForEach(p => p.Velocity = 5f);
            if (nozzleRotor.Top != null) state = holderState.extendedAttached;
            else                         state = holderState.extendedDetached;
        }
    }
    private IMyLandingGear getClosestHolder() {
        IMyLandingGear closest = null; var dist = double.PositiveInfinity;
        var nPos = nozzleRotor.WorldMatrix.Translation;
        foreach (var h in holders) {
            var hDist = (nPos - h.WorldMatrix.Translation).LengthSquared();
            if (dist > hDist) { dist = hDist; closest = h; }
        }
        return closest;
    }
    public void toggle() {
        if (actionQueue.Count <= 0 || actionQueue.Last() != holderAct.retract && actionQueue.Last() != holderAct.extend) {
            if (state == holderState.extendedAttached || state == holderState.extendedDetached) actionQueue.Add(holderAct.retract);
            else                                                                                actionQueue.Add(holderAct.extend);
        }
    }
    public void attach() { // TODO: fix bugs (`actionQueue.Count > 0` missing from last comparison)
        if ((state == holderState.extendedDetached && (actionQueue.Count <= 0 || (actionQueue.Last() != holderAct.attach && actionQueue.Last() != holderAct.detach))) ||
            (actionQueue.Count > 0 && actionQueue.Last() == holderAct.extend)) actionQueue.Add(holderAct.attach);
    }
    public void detach() {
        if ((state == holderState.extendedAttached && (actionQueue.Count <= 0 || (actionQueue.Last() != holderAct.attach && actionQueue.Last() != holderAct.detach))) ||
            (actionQueue.Count > 0 && actionQueue.Last() == holderAct.extend)) actionQueue.Add(holderAct.detach);
    }
    public void selectNext() {
        if ((state == holderState.extendedDetached && actionQueue.Count <= 0) || actionQueue.Count > 0 && (
            (state == holderState.retractedDetached && actionQueue.Last() == holderAct.extend) ||
            (actionQueue.Last() == holderAct.detach || actionQueue.Last() == holderAct.next))) actionQueue.Add(holderAct.next);
    }
    private bool act(double holderAngle) {
        print($"Action queue: {string.Join(", ", actionQueue.Select(a => a.ToString("G")))}");
        print($"currentClosest: {currentClosest.CustomName}");
        if (actionQueue.Count <= 0) return false;
        var curAct = actionQueue.First();
        var safeAngle = Math.Abs(holderAngle) < 0.005d;

        if        (curAct == holderAct.extend) {
            if (currentClosest.IsLocked) currentClosest.Unlock();
            else {
                pistons.ForEach(p => p.Velocity = 5f);
                if (pistons.Sum(p => p.HighestPosition - p.CurrentPosition) <= EPS) {
                    if (state == holderState.retractedAttached) state = holderState.extendedAttached;
                    else                                        state = holderState.extendedDetached;
                    actionQueue.RemoveAt(0);
                }
            }
        } else if (curAct == holderAct.retract && safeAngle) {
            var distOK = pistons.Sum(p => p.CurrentPosition - p.LowestPosition) <= EPS;
            if (!currentClosest.IsLocked || distOK) {
                pistons.ForEach(p => p.Velocity = -5f);
                if (distOK) {
                    if (!currentClosest.IsLocked) currentClosest.Lock();
                    else {
                        if (state == holderState.extendedAttached) state = holderState.retractedAttached;
                        else                                       state = holderState.retractedDetached;
                        actionQueue.RemoveAt(0);
                    }
                }
            } else actionQueue.Clear();
        } else if (curAct == holderAct.attach) {
            if (currentClosest.IsLocked) {
                if (nozzleRotor.Top == null) {
                    if (!safeAngle) return false;
                    pistons.ForEach(p => p.Velocity = -0.1f);
                    nozzleRotor.ApplyAction("Attach");
                } else {
                    pistons.ForEach(p => p.Velocity = 5f);
                    currentClosest.Unlock();
                    actionQueue.RemoveAt(0);
                }
            } else actionQueue.Clear();
        } else if (curAct == holderAct.detach) {
            if (!currentClosest.IsLocked) {
                if (!safeAngle) return false;
                currentClosest.Lock();
            } else if (nozzleRotor.Top != null) nozzleRotor.ApplyAction("Detach");
            else actionQueue.RemoveAt(0);
        } else if (curAct == holderAct.next) {
            if (nozzleRotor.Top == null) {
                if (currentClosest == getClosestHolder()) {
                    holderRotor.TargetVelocityRad = (float) (Math.PI / 2d);
                    return true;
                } else {
                    currentClosest = getClosestHolder();
                    actionQueue.RemoveAt(0);
                }
            } else actionQueue.Clear();
        }

        return false;
    }
    public void update() {
        print(state.ToString("G"));       
        var hMat = holderRotor.WorldMatrix;
        var uVec = hMat.Up; var hPos = hMat.Translation;
        var nVec = nozzleRotor.WorldMatrix.Translation - hPos; nVec = Vector3D.ProjectOnPlane(ref nVec, ref uVec);
        var hVec = currentClosest.WorldMatrix.Translation - hPos; hVec = Vector3D.ProjectOnPlane(ref hVec, ref uVec);
        var mat = uVec.Dot(nVec.Cross(hVec));
        var angle = double.IsNaN(mat) ? 0d : Math.Sign(mat) * Vector3D.Angle(nVec, hVec);
        print($"holder angle {(angle * radToDegMul).ToString("0.000")}");
        if (!act(angle)) {
            holderRotor.TargetVelocityRad = (float) angle * 5f;
            if (state == holderState.retractedAttached || state == holderState.extendedAttached) {
                nozzleRotor.TargetVelocityRad = (float) getPiAngle(nozzleRotor) * -5f;
            }
        }
    }

    public void shutdown() {
        shutdownStator(holderRotor); shutdownStator(nozzleRotor);
        pistons.ForEach(p => { if (p.IsFunctional) p.Velocity = 0f; });
    }
}

public int state = 0;
public IMyShipController controller = null;
public nozzleAligment nAlign = null;
public manArm.armState armState = manArm.armState.none;
public manArm arm = null;
public manHolder holder = null;

public void update() {
    var rotInd = controller.RotationIndicator;
    var roll = controller.RollIndicator; var pitch = rotInd.X; var yaw = rotInd.Y;
    print($"roll: {roll.ToString("0.000")}    pitch: {pitch.ToString("0.000")}    yaw: {yaw.ToString("0.000")}");

    var mat = controller.WorldMatrix;
    var mov = controller.MoveIndicator;
    print($"mov: {mov.X.ToString("0.0")}, {mov.Y.ToString("0.0")}, {mov.Z.ToString("0.0")}");

    holder.update();
    arm.update(mat.Backward * mov.Z * 0.25d + mat.Right * mov.X * 0.25d + mat.Up * mov.Y * 0.25d, new Vector3D(pitch * -0.01f, yaw * 0.01f, roll * -0.5f));
}

public bool isRotor(IMyMotorStator stator) => stator.BlockDefinition.SubtypeName.Contains("Stator");
public bool isHinge(IMyMotorStator stator) => stator.BlockDefinition.SubtypeName.Contains("Hinge");
public List<IMyPistonBase> walkPistons(IMyCubeGrid begin, IEnumerable<IMyPistonBase> pistons) {
    var res = new List<IMyPistonBase>();
    var grid = begin; var piston = pistons.FirstOrDefault(p => p.CubeGrid == grid);
    while (piston != null) {
        res.Add(piston);
        grid = piston.Top.CubeGrid; piston = pistons.FirstOrDefault(p => p.CubeGrid == grid);
    }
    return res;
}
public manSegment findSegment(string sIdx, IMyTerminalBlock measreTo, IEnumerable<IMyMotorStator> rotors, IEnumerable<IMyPistonBase> pistons) {
    print($"  segment {sIdx}");
    var manpulRegex = new @Regex($"(\\s|^)@manpul-{sIdx}-(p|n)(\\s|$)");
    IMyMotorStator rotor = null; int angleMul = 0;
    foreach (var r in rotors) {
        var match = manpulRegex.Match(r.CustomName);
        if (!match.Success) continue;
        if      (match.Groups[2].Value == "p") angleMul =  1;
        else if (match.Groups[2].Value == "n") angleMul = -1;
        rotor = r; break;
    }
    if (rotor != null) print($"  - rotor: '{rotor.CustomName}'");
    else throw new ArgumentException($"Did not find rotor (tag: '@manpul-{sIdx}-<p|n>')");
    return new manSegment(rotor, angleMul, walkPistons(rotor.TopGrid, pistons), measreTo);
}
public manNozzle findNozzle(string sIdx, IEnumerable<IMyMotorStator> hinges, IEnumerable<IMyMotorStator> rotors) {
    print($"  nozzle - segment {sIdx}");
    IMyMotorStator hingePtich = null; IMyMotorStator hingeYaw = null; IMyMotorStator rotorRoll = null;
    Vector3I angleMul = Vector3I.Zero;
    var manpulRegex = new @Regex($"(\\s|^)@manpul-{sIdx}-(p|n)-(roll|pitch|yaw)(\\s|$)");
    foreach (var h in hinges) {
        var match = manpulRegex.Match(h.CustomName);
        if (!match.Success) continue;
        if        (match.Groups[3].Value == "pitch") {
            if      (match.Groups[2].Value == "p") angleMul.X =  1;
            else if (match.Groups[2].Value == "n") angleMul.X = -1;
            hingePtich = h;
        } else if (match.Groups[3].Value == "yaw") {
            if      (match.Groups[2].Value == "p") angleMul.Y =  1;
            else if (match.Groups[2].Value == "n") angleMul.Y = -1;
            hingeYaw = h;
        } else throw new ArgumentException("Nozzle roll cannot be controled by a hinge");
    }
    foreach (var r in rotors) {
        var match = manpulRegex.Match(r.CustomName);
        if (!match.Success) continue;
        if (match.Groups[3].Value == "roll") {
            if      (match.Groups[2].Value == "p") angleMul.Z =  1;
            else if (match.Groups[2].Value == "n") angleMul.Z = -1;
            rotorRoll = r;
        } else throw new ArgumentException("Nozzle pitch/yaw cannot be controled by a rotor");
    }
    var errStr = "";
    if (hingePtich != null) print($"  - pitch hinge: '{hingePtich.CustomName}'");
    else errStr += $"Did not find pitch hinge (tag: '@manpul-{sIdx}-<p|n>-pitch')";
    if (hingeYaw != null) print($"  - yaw hinge: '{hingeYaw.CustomName}'");
    else errStr += $"Did not find yaw hinge (tag: '@manpul-{sIdx}-<p|n>-yaw')";
    if (rotorRoll != null) print($"  - roll rotor: '{rotorRoll.CustomName}'");
    else errStr += $"Did not find roll rotor (tag: '@manpul-{sIdx}-<p|n>-roll')";
    if (!string.IsNullOrEmpty(errStr)) throw new ArgumentException(errStr);
    return new manNozzle(hingePtich, hingeYaw, rotorRoll, angleMul);
}
public bool init() {
    var blocks = getBlocks(b => b.IsSameConstructAs(Me));
    findDebugLcd(blocks, tagRegex);
    findNonStandardLcd(blocks, "manpul");
    wipe();

    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName)) as IMyShipController;
    if (controller != null) {
        try {
            var prevActionQueue = holder?.actionQueue;
            var prevArmState = arm != null ? arm.state : manArm.armState.none;
            var failMgr = arm?.failMgr; var ramp = arm?.ramp;
            shutdown();

            var stators = blocks.Where(b => b is IMyMotorStator).Cast<IMyMotorStator>();
            var pistons = blocks.Where(b => b is IMyPistonBase).Cast<IMyPistonBase>();

            var rBaseRegex = new @Regex($"(\\s|^)@manpul-base(\\s|$)");
            var r1Regex = new @Regex($"(\\s|^)@manpul-1-end(\\s|$)");
            var r2Regex = new @Regex($"(\\s|^)@manpul-3-end(\\s|$)");

            var rotors = stators.Where(s => isRotor(s));
            print("Evaluating rotors ...");
            IMyMotorStator rotorBase = rotors.FirstOrDefault(r => rBaseRegex.IsMatch(r.CustomName));
            IMyMotorStator rotor1 =    rotors.FirstOrDefault(r => r1Regex.IsMatch(r.CustomName));
            IMyMotorStator rotor2 =    rotors.FirstOrDefault(r => r2Regex.IsMatch(r.CustomName));
            if (rotorBase == null || rotor1 == null || rotor2 == null) {
                var rbStr = rotorBase != null ? "present" : "missing";
                var r1Str = rotor1 != null ? "present" : "missing";
                var r2Str = rotor2 != null ? "present" : "missing";
                throw new ArgumentException($"@manpul-base {rbStr}, @manpul-1-end {r1Str}, @manpul-3-end {r2Str}");
            }

            var hinges = stators.Where(s => isHinge(s));
            print("Evaluating nozzle ...");
            manNozzle  seg4 =  findNozzle("4", hinges, rotors);
            print("Evaluating segments ...");
            manSegment seg3 = findSegment("3", rotor2, rotors, pistons);
            manSegment seg2 = findSegment("2", seg3.rotor, rotors, pistons);
            manSegment seg1 = findSegment("1", seg2.rotor, rotors, pistons);

            print("Evaluating arm ...");
            arm = new manArm(controller, seg1, seg2, seg3, seg4, rotorBase, rotor1, rotor2, nAlign, prevArmState, failMgr, ramp);

            print("Evaluating holder ...");
            var nozzleRegex = new @Regex($"(\\s|^)@manpul-nozzle(\\s|$)");
            var holderRegex = new @Regex($"(\\s|^)@manpul-holder(\\s|$)");
            var nRotor = stators.FirstOrDefault(s => nozzleRegex.IsMatch(s.CustomName));
            if (nRotor == null) throw new ArgumentException($"nozzle rotor (tag: @manpul-nozzle) not found");
            IMyPistonBase hPiston = pistons.FirstOrDefault(p => holderRegex.IsMatch(p.CustomName));
            if (hPiston == null) throw new ArgumentException($"nozzle rotor (tag: @manpul-nozzle) not found");
            var hPistons = walkPistons(hPiston.TopGrid, pistons);
            hPistons.Insert(0, hPiston);
            var hRotor = stators.FirstOrDefault(s => s.CubeGrid == hPistons.Last().TopGrid);
            if (hRotor == null) throw new ArgumentException($"holder tip rotor not found");
            var holders = blocks.Where(b => b is IMyLandingGear && b.CubeGrid == hRotor.TopGrid).Cast<IMyLandingGear>().ToList();
            if (holders.Count <= 0) throw new ArgumentException($"Holder does not have any landing gear");
            
            holder = new manHolder(hPistons, holders, hRotor, nRotor, prevActionQueue);
        } catch (Exception e) {
            print($"Wrongly built manipulator: could not evaluate components\n{e.Message}"); Echo("error");
            return false;
        }

        if (arm != null) {
            Echo($"ok");
            return true;
        } else {
            print("Wrong manipulator configuration");
            /* TODO: what's wrong? */
            Echo("error");
        }
    } else { print("No main controller"); Echo("error"); }

    return false;
}

public void shutdown() {
    arm?.shutdown(); holder?.shutdown(); 
    arm = null; holder = null;
}

const string pName = "@manpul program";
public Program() {
    Echo("");
    if (!Me.CustomName.StartsWith(pName)) Me.CustomName = pName;
    initMeLcd();

    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    if (state == 1) {
        nAlign = new nozzleAligment();
        if (!init()) shutdown();
        Runtime.UpdateFrequency = UpdateFrequency.Update1;
    } else {
        var blocks = getBlocks(b => b.IsSameConstructAs(Me));
        findDebugLcd(blocks, tagRegex);
        findNonStandardLcd(blocks, "manpul");

        Echo("offline"); wipe(); print("Manipulator shut down");
    }
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update1) {
        if (state == 1) {
            refreshTick++;
            if (refreshTick >= 200) {
                if (!init()) shutdown();
                refreshTick = 0;
            }
            if (arm != null) {
                try {
                    wipe();
                    update();
                } catch (Exception e) {
                    shutdown();
                    Echo("error"); wipe(); print($"Exception occcured while execution:\n{e.Message}\n{e.StackTrace}");
                }
            }
        }
    } else {
        if (argument == "start" && state != 1) {
            state = 1;
            nAlign = new nozzleAligment();
            if (!init()) shutdown();
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        } else if (argument == "stop" && state > 0) {
            shutdown();
            state = 0;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("offline"); wipe(); print("Manipulator shut down");
        } else if (argument.StartsWith("holder ")) {
            if      (argument.EndsWith(" attach")) holder?.attach();
            else if (argument.EndsWith(" detach")) holder?.detach();
            else if (argument.EndsWith(" toggle")) holder?.toggle();
            else if (argument.EndsWith(" next"))   holder?.selectNext();
        } else if (argument.StartsWith("arm ")) {
            if      (argument.EndsWith(" pack")) arm?.pack();
            else if (argument.EndsWith(" unpack")) arm?.unpack();
        }
    }
}