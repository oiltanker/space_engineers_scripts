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
    public IMyMotorStator rotor1;
    public IMyMotorStator rotor2;
    public List<IMyPistonBase> pistons;
    public List<IMyTerminalBlock> measreTo;
    public Vector3D endPos { get { return measreTo.Select(b => b.WorldMatrix.Translation).Aggregate((v1, v2) => v1 + v2) / (double) measreTo.Count; } }
    public Vector3D beginPos { get { return (rotor1.WorldMatrix.Translation + rotor2.WorldMatrix.Translation) / 2d; } }
    public double length { get { return measreTo.Count <= 0 ? 0d : (endPos - beginPos).Length(); } }
    public manSegment(IMyMotorStator rotor1, IMyMotorStator rotor2, List<IMyPistonBase> pistons, List<IMyTerminalBlock> measreTo) {
        if (measreTo == null) throw new ArgumentNullException("End measure block list cannot be null");
        this.rotor1 = rotor1;
        this.rotor2 = rotor2;
        this.pistons = pistons;
        this.measreTo = measreTo;
    }
    public void pack() => pistons.ForEach(p => p.Velocity = -5f);
    public void unpack() => pistons.ForEach(p => p.Velocity = 5f);
    public bool isPacked() => pistons.Sum(p => p.CurrentPosition - p.LowestPosition) < 0.01d;
    public bool isUnpacked() => pistons.Sum(p => p.HighestPosition - p.CurrentPosition) < 0.01d;
    public void shutdown() {
        pistons.ForEach(p => { if (p.IsFunctional) p.Velocity = 0f; });
        shutdownStator(rotor1);  shutdownStator(rotor2);
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
public class manArm {
    public enum armState { none, packed, unpacked, packing, unpacking, fail }
    public armState state;
    public dir xDir;
    public manSegment seg1;
    public manSegment seg2;
    public manNozzle seg3;
    public IMyMotorStator rotorBase;
    public IMyShipController controller;
    public nozzleAligment nAlign;
    public failStateMgr failMgr;
    public manArm(dir xDir, IMyShipController controller, manSegment seg1, manSegment seg2, manNozzle seg3, IMyMotorStator rotorBase,
                  nozzleAligment nAlign = null, armState state = armState.none, failStateMgr failMgr = null) {
        var errStr = "";
        if (seg3.rotorRoll == null || seg3.hingePitch == null || seg3.hingeYaw == null) errStr += "Wrong segment 3 configuration\n";
        if (seg2.rotor1 == null || seg2.rotor2 == null || seg2.measreTo.Count == 0) errStr += "Wrong segment 2 configuration\n";
        if (seg1.rotor1 == null || seg1.rotor2 == null || seg1.measreTo.Count == 0) errStr += "Wrong segment 1 configuration\n";
        if (!String.IsNullOrEmpty(errStr)) throw new ArgumentException(errStr);

        this.state = state;
        this.xDir = xDir;
        this.controller = controller;
        this.seg1 = seg1; this.seg2 = seg2; this.seg3 = seg3; this.rotorBase = rotorBase;
        this.nAlign = nAlign != null ? nAlign : new nozzleAligment();
        this.failMgr = failMgr != null ? failMgr : new failStateMgr();
        this.failMgr.failAction = () => { this.state = armState.fail; this.shutdown(); this.nAlign.resetRot(); };
    }

    public static double signSquare(double x) => Math.Sign(x)*x*x;
    public void setHeadPos() {
        var offVec = seg2.endPos - seg1.beginPos;
        var uVec = seg1.rotor1.WorldMatrix.Up;
        offVec = Vector3D.ProjectOnPlane(ref offVec, ref uVec);
        nAlign.pos = offVec + seg1.beginPos; // remove drift
    }

    private static Vector3D getVec(dir nDir, MatrixD mat) {
        switch (nDir) {
            case dir.forward:  return mat.Forward;
            case dir.backward: return mat.Backward;
            case dir.left:     return mat.Left;
            case dir.right:    return mat.Right;
            case dir.up:       return mat.Up;
            case dir.down:     return mat.Down;
            default: throw new ArgumentException("Unsoported direction.");
        }
    }
    private static double getAngle(IMyMotorStator rotor, int angleMul = 1) {
        var aMul = rotor.WorldMatrix.Up.Dot(rotor.WorldMatrix.Backward.Cross(rotor.Top.WorldMatrix.Forward));
        var angle = double.IsNaN(aMul) ? 0d : angleMul * Math.Sign(aMul) * Vector3D.Angle(rotor.WorldMatrix.Backward, rotor.Top.WorldMatrix.Forward);
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
        var pHead = nAlign.pos + mov;

        var xVec = getVec(xDir, rotorBase.Top.WorldMatrix);
        var uVec = rotorBase.WorldMatrix.Up;
        var bPos = seg1.beginPos;
        var bVec = pHead - bPos;
        bVec = Vector3D.Normalize(Vector3D.ProjectOnPlane(ref bVec, ref uVec));

        var tMat = new MatrixD();
        tMat.Left =    bVec;
        tMat.Up =      uVec;
        tMat.Forward = Vector3D.Normalize(bVec.Cross(uVec));
        tMat = MatrixD.Transpose(tMat);

        var brMul = uVec.Dot(xVec.Cross(bVec));
        var brAngle = double.IsNaN(brMul) ? 0d : Math.Sign(-brMul) * Vector3D.Angle(bVec, xVec);
        print($"brAngle: {(brAngle * radToDegMul).ToString("0.000")}");
        setStatorVel(rotorBase, signSquare(brAngle * 1d) * 1d);

        var dest = Vector3D.TransformNormal(pHead - bPos, tMat); dest.Z = 0d;

        var dist = dest.Length();
        print($"dist: {dist.ToString("0.000")}");
        var theta = Math.PI + Math.Atan2(dest.X, dest.Y);

        print($"theta: {(theta * radToDegMul).ToString("0.000")}");

        var beta = -Math.Acos((dist*dist - seg1.length*seg1.length - seg2.length*seg2.length) / (2d * seg1.length * seg2.length));
        print($"beta: {((beta + Math.PI) * radToDegMul).ToString("0.000")}    lenTotal: {(seg1.length + seg2.length).ToString("0.000")}");
        var innerAngle = beta + Math.PI;
        
        double nS1Angle = getAngle(seg1.rotor1, 1);
        double nS2Angle = getAngle(seg2.rotor1, 1);
        print($"seg1.rotor1 angle: {(nS1Angle * radToDegMul).ToString("0.000")}    seg2.rotor1 angle: {(nS2Angle * radToDegMul).ToString("0.000")}");
        if (innerAngle > Math.PI / 6d) {
            var alphaDiv = seg1.length + seg2.length * Math.Cos(beta);
            var alpha = theta - Math.Atan((seg2.length * Math.Sin(beta)) / alphaDiv) - (alphaDiv >= 0d ? 0d : Math.PI);
            print($"alpha: {(alpha * radToDegMul).ToString("0.000")}");

            nS1Angle = alpha;
            nS2Angle = innerAngle;
        }

        adjustAngle(seg1, nS1Angle, 2d);
        adjustAngle(seg2, nS2Angle, 2d);
        if (!double.IsNaN(nS1Angle + nS2Angle)) nAlign.pos = pHead;

        var cMat = controller.WorldMatrix;
        var iPitch =  rot.X * cMat.Up.Dot(seg3.hingePitch.Top.WorldMatrix.Forward)   + rot.Y * cMat.Left.Dot(seg3.hingeYaw.Top.WorldMatrix.Up);
        var iYaw   = -rot.X * cMat.Left.Dot(seg3.hingePitch.Top.WorldMatrix.Forward) + rot.Y * cMat.Up.Dot(seg3.hingeYaw.Top.WorldMatrix.Up);
        var roll  = getHingeAngleDelta(seg3.rotorRoll,  controller.WorldMatrix.Left,    ref nAlign.roll,  rot.Z);
        var pitch = getHingeAngleDelta(seg3.hingePitch, controller.WorldMatrix.Forward, ref nAlign.pitch, iPitch);
        var yaw   = getHingeAngleDelta(seg3.hingeYaw,   controller.WorldMatrix.Forward, ref nAlign.yaw,   iYaw);
        print($"pitch: {(pitch * radToDegMul).ToString("0.000")}    yaw: {(yaw * radToDegMul).ToString("0.000")}");

        seg3.setRPY(roll, pitch, yaw);
    }

    private double getHingePackAngle(IMyMotorStator hinge, Vector3D measure) {
        var uVec = hinge.WorldMatrix.Up; var rVec = hinge.WorldMatrix.Right;
        var pMeasure = Vector3D.ProjectOnPlane(ref measure, ref uVec);

        var mul = uVec.Dot(pMeasure.Cross(rVec));
        var diff = double.IsNaN(mul) ? 0d : Math.Sign(mul) * Vector3D.Angle(pMeasure, rVec);
        return double.IsNaN(diff) ? 0d : signSquare(diff * 10f) * 10f;
    }
    private bool anglesAdjusted(double nS1Angle, double nS2Angle, double eps = 0.1d) =>
        Math.Abs(adjustAngle(seg1, nS1Angle)) + Math.Abs(adjustAngle(seg2, nS2Angle)) < eps;
    private static double adjustAngle(manSegment seg, double nAngle, double mul = 2d) {
        var sAngle = getAngle(seg.rotor1);
        var diff = double.IsNaN(nAngle) ? 0f : (float) (sAngle - nAngle);

        var nVel = signSquare(diff * mul) * mul;
        setStatorVel(seg.rotor1, nVel);
        setStatorVel(seg.rotor2, -nVel);

        return diff;
    }
    private static bool angleAdjusted(manSegment seg, double nAngle, double eps = 0.1d) => Math.Abs(adjustAngle(seg, nAngle, 2d)) < eps;
    private bool segmentsPacked() {
        seg1.pack(); seg2.pack();
        return seg1.isPacked() && seg2.isPacked();
    }
    private bool segmentsUnpacked() {
        seg1.unpack(); seg2.unpack();
        return seg1.isUnpacked() && seg2.isUnpacked();
    }
    private bool nozzleAdjusted() {
        var pitch = signSquare(seg3.hingePitch.Angle * -2f) * 10f;
        var yaw   = signSquare(seg3.hingeYaw.Angle   * -2f) * 10f;
        var roll  = signSquare(getPiAngle(seg3.rotorRoll)  * -2f) * 10f;
        seg3.setRPY(roll, pitch, yaw);
        return Math.Abs(pitch) + Math.Abs(yaw) + Math.Abs(roll) < 0.1d;
    }
    private bool adjusted = false;
    private static readonly List<Func<manArm, bool>> packActions = new List<Func<manArm, bool>>{
        (ma) => ma.nozzleAdjusted(), (ma) => ma.segmentsPacked(),
        (ma) => manArm.angleAdjusted(ma.seg1, Math.PI * 5d / 4d), (ma) => manArm.angleAdjusted(ma.seg2, Math.PI / 8d)
    };
    private static readonly List<Func<manArm, bool>> unpackActions = new List<Func<manArm, bool>>{
        (ma) => manArm.angleAdjusted(ma.seg2, Math.PI / 2d), (ma) => manArm.angleAdjusted(ma.seg1, Math.PI),
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
        var allPistons = seg1.pistons.Concat(seg1.pistons).Concat(seg2.pistons);
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
            if (!adjusted && anglesAdjusted(Math.PI * 5d / 4d, Math.PI / 8d, 0.05d)) {
                adjusted = true;
                shutdown();
            }
        } else if (state != armState.fail) failMgr.reset();
        return state == armState.unpacked || state == armState.fail;
    }

    public void pack()   { if (state == armState.unpacked || state == armState.fail) { state = armState.packing;   failMgr.reset(); } }
    public void unpack() { if (state == armState.packed || state == armState.fail)   { state = armState.unpacking; failMgr.reset(); } }
    public void shutdown() {
        seg1?.shutdown(); seg2?.shutdown(); seg3?.shutdown();
        shutdownStator(rotorBase);
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
    arm.update(mat.Backward * mov.Z * 0.1d + mat.Right * mov.X * 0.1d + mat.Up * mov.Y * 0.1d, new Vector3D(pitch * -0.01f, yaw * 0.01f, roll * -0.5f));
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
public manSegment findSegment(string sIdx, List<IMyTerminalBlock> measreTo, IEnumerable<IMyMotorStator> rotors, IEnumerable<IMyPistonBase> pistons) {
    print($"  segment {sIdx}");
    var manpulRegex = new @Regex($"(\\s|^)@manpul-{sIdx}-(p|n)(\\s|$)");
    IMyMotorStator rotor1 = null; IMyMotorStator rotor2 = null;
    foreach (var r in rotors) {
        var match = manpulRegex.Match(r.CustomName);
        if (!match.Success) continue;
        if      (match.Groups[2].Value == "p") rotor1 = r;
        else if (match.Groups[2].Value == "n") rotor2 = r;
    }
    if (rotor1 != null || rotor2 != null) print($"  - rotor1: '{rotor1.CustomName}'    rotor2: '{rotor2.CustomName}'");
    else throw new ArgumentException($"Did not find rotor pair (tag: '@manpul-{sIdx}-<p|n>')");
    return new manSegment(rotor1, rotor2, walkPistons(rotor1.TopGrid, pistons).Concat(walkPistons(rotor2.TopGrid, pistons)).Distinct().ToList(), measreTo);
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
    findNonStandardLcd(blocks, "manpul", 0.6f);
    wipe();

    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName)) as IMyShipController;
    if (controller != null) {
        try {
            var prevActionQueue = holder?.actionQueue;
            var prevArmState = arm != null ? arm.state : manArm.armState.none;
            var failMgr = arm?.failMgr;
            shutdown();

            var stators = blocks.Where(b => b is IMyMotorStator).Cast<IMyMotorStator>();
            var pistons = blocks.Where(b => b is IMyPistonBase).Cast<IMyPistonBase>();

            var rBaseRegex = new @Regex($"(\\s|^)@manpul-base-([a-z]+)(\\s|$)");

            var rotors = stators.Where(s => isRotor(s));
            print("Evaluating base rotor ...");
            IMyMotorStator rotorBase = null; var xDir = dir.forward;
            foreach (var r in rotors) {
                var match = rBaseRegex.Match(r.CustomName);
                if (match.Success) {
                    rotorBase = r;
                    var dirStr = match.Groups[2].Value;
                    if      (dirStr == "forward") xDir = dir.forward;
                    else if (dirStr == "backward") xDir = dir.backward;
                    else if (dirStr == "left") xDir = dir.left;
                    else if (dirStr == "right") xDir = dir.right;
                    else throw new ArgumentException($"unsupported direction {dirStr}, choose (forward|backward|left|right)");
                }
            }
            if (rotorBase == null) {
                var rbStr = rotorBase != null ? "present" : "missing";
                throw new ArgumentException($"@manpul-base-<dir> {rbStr}");
            }

            var hinges = stators.Where(s => isHinge(s));
            print("Evaluating nozzle ...");
            manNozzle  seg3 =  findNozzle("3", hinges, rotors);
            print("Evaluating segments ...");
            manSegment seg2 = findSegment("2", new List<IMyTerminalBlock>{ seg3.hingePitch.TopGrid == seg3.hingeYaw.CubeGrid ? seg3.hingePitch : seg3.hingeYaw }, rotors, pistons);
            manSegment seg1 = findSegment("1", new List<IMyTerminalBlock>{ seg2.rotor1, seg2.rotor2 }, rotors, pistons);

            print("Evaluating arm ...");
            arm = new manArm(xDir, controller, seg1, seg2, seg3, rotorBase, nAlign, prevArmState, failMgr);

            print("Evaluating holder ...");
            var nozzleRegex = new @Regex($"(\\s|^)@manpul-nozzle(\\s|$)");
            var holderRegex = new @Regex($"(\\s|^)@manpul-holder(\\s|$)");
            var nRotor = stators.FirstOrDefault(s => nozzleRegex.IsMatch(s.CustomName));
            if (nRotor == null) throw new ArgumentException($"nozzle rotor (tag: @manpul-nozzle) not found");
            IMyPistonBase hPiston = pistons.FirstOrDefault(p => holderRegex.IsMatch(p.CustomName));
            if (hPiston == null) throw new ArgumentException($"holder piston (tag: @manpul-holder) not found");
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