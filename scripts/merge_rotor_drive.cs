@import lib.eps
@import lib.printFull
@import lib.grid
@import lib.pid
@import lib.alignment
@import lib.dockState
@import lib.activeStabilization

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@mrdrive(\s|$)");

public static pidCtrl newDefaultPid() => new pidCtrl(1d, 0.1d, 0.25d, 1d / ENG_UPS, 0.95d);
public class mrHead {
    public IMyTerminalBlock anchor;
    public List<IMyShipMergeBlock> merges;
    public IMyMotorStator rotor;
    public bool positive;
    public dir rotDir;
    public Dictionary<dir, Vector3D> dMap;
    public mrHead(IMyMotorStator rotor, List<IMyShipMergeBlock> merges, IMyTerminalBlock anchor) {
        this.rotor = rotor; this.merges = merges; this.anchor = anchor;
        positive = !(rotor.TargetVelocityRad < 0f);
        rotor.TargetVelocityRad = (float) (positive ? Math.PI : -Math.PI);

        var mat = anchor.WorldMatrix;
        rotDir = matchDir(rotor.WorldMatrix.Up, mat);
        dMap = new Dictionary<dir, Vector3D>();
        if        (rotDir == dir.forward) {
            dMap.Add(dir.left,  positive ? Vector3D.Normalize(mat.Right + mat.Down) : Vector3D.Normalize(mat.Right + mat.Up));
            dMap.Add(dir.right, positive ? Vector3D.Normalize(mat.Left  + mat.Up)   : Vector3D.Normalize(mat.Left  + mat.Down));

            dMap.Add(dir.up,   positive ? Vector3D.Normalize(mat.Down + mat.Left)  : Vector3D.Normalize(mat.Down + mat.Right));
            dMap.Add(dir.down, positive ? Vector3D.Normalize(mat.Up   + mat.Right) : Vector3D.Normalize(mat.Up   + mat.Left));
        } else if (rotDir == dir.backward) {
            dMap.Add(dir.left,  positive ? Vector3D.Normalize(mat.Right + mat.Up)   : Vector3D.Normalize(mat.Right + mat.Down));
            dMap.Add(dir.right, positive ? Vector3D.Normalize(mat.Left  + mat.Down) : Vector3D.Normalize(mat.Left  + mat.Up));

            dMap.Add(dir.up,   positive ? Vector3D.Normalize(mat.Down + mat.Right) : Vector3D.Normalize(mat.Down + mat.Left));
            dMap.Add(dir.down, positive ? Vector3D.Normalize(mat.Up   + mat.Left)  : Vector3D.Normalize(mat.Up   + mat.Right));
        } else if (rotDir == dir.left) {
            dMap.Add(dir.forward,  positive ? Vector3D.Normalize(mat.Backward + mat.Up)   : Vector3D.Normalize(mat.Backward + mat.Down));
            dMap.Add(dir.backward, positive ? Vector3D.Normalize(mat.Forward  + mat.Down) : Vector3D.Normalize(mat.Forward  + mat.Up));

            dMap.Add(dir.up,   positive ? Vector3D.Normalize(mat.Down + mat.Backward) : Vector3D.Normalize(mat.Down + mat.Forward));
            dMap.Add(dir.down, positive ? Vector3D.Normalize(mat.Up   + mat.Forward)  : Vector3D.Normalize(mat.Up  + mat.Backward));
        } else if (rotDir == dir.right) {
            dMap.Add(dir.forward,  positive ? Vector3D.Normalize(mat.Backward + mat.Down) : Vector3D.Normalize(mat.Backward + mat.Up));
            dMap.Add(dir.backward, positive ? Vector3D.Normalize(mat.Forward  + mat.Up)   : Vector3D.Normalize(mat.Forward  + mat.Down));

            dMap.Add(dir.up,   positive ? Vector3D.Normalize(mat.Down + mat.Forward)  : Vector3D.Normalize(mat.Down + mat.Backward));
            dMap.Add(dir.down, positive ? Vector3D.Normalize(mat.Up   + mat.Backward) : Vector3D.Normalize(mat.Up  + mat.Forward));
        } else if (rotDir == dir.up) {
            dMap.Add(dir.forward,  positive ? Vector3D.Normalize(mat.Backward + mat.Right) : Vector3D.Normalize(mat.Backward + mat.Left));
            dMap.Add(dir.backward, positive ? Vector3D.Normalize(mat.Forward  + mat.Left)  : Vector3D.Normalize(mat.Forward  + mat.Right));

            dMap.Add(dir.left,  positive ? Vector3D.Normalize(mat.Right + mat.Forward)  : Vector3D.Normalize(mat.Right + mat.Backward));
            dMap.Add(dir.right, positive ? Vector3D.Normalize(mat.Left  + mat.Backward) : Vector3D.Normalize(mat.Left  + mat.Forward));
        } else if (rotDir == dir.down) {
            dMap.Add(dir.forward,  positive ? Vector3D.Normalize(mat.Backward + mat.Left)  : Vector3D.Normalize(mat.Backward + mat.Right));
            dMap.Add(dir.backward, positive ? Vector3D.Normalize(mat.Forward  + mat.Right) : Vector3D.Normalize(mat.Forward  + mat.Left));

            dMap.Add(dir.left,  positive ? Vector3D.Normalize(mat.Right + mat.Backward) : Vector3D.Normalize(mat.Right + mat.Forward));
            dMap.Add(dir.right, positive ? Vector3D.Normalize(mat.Left  + mat.Forward)  : Vector3D.Normalize(mat.Left  + mat.Backward));
        } else throw new ArgumentException("Unsoported direction.");

        var tMat = MatrixD.Transpose(anchor.WorldMatrix);
        foreach (var d in dMap.Keys.ToList()) dMap[d] = Vector3D.TransformNormal(dMap[d], tMat);
    }

    private double getMov(dir nDir, Vector3D mov) {
        switch (nDir) {
            case dir.forward:  return  mov.Z;
            case dir.backward: return -mov.Z;
            case dir.left:     return  mov.X;
            case dir.right:    return -mov.X;
            case dir.up:       return -mov.Y;
            case dir.down:     return  mov.Y;
            default: throw new ArgumentException("Unsoported direction.");
        }
    }
    private Vector3D getAVec(dir nDir) {
        var mat = anchor.WorldMatrix;
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

    private static readonly double maxDist = Math.Sqrt(2d) / 2d;
    private static readonly double nearDist = Math.Sqrt(2d) / 16d;
    public void update(Vector3D mov, Vector3D vel, bool dampen) {
        var tMat = MatrixD.Transpose(anchor.WorldMatrix);
        var uVec = rotor.WorldMatrix.Up; var dVec = rotor.WorldMatrix.Down;

        vel = Vector3D.TransformNormal(Vector3D.ProjectOnPlane(ref vel, ref uVec), tMat) * 10d;
        var velMag = vel.Length();

        var mVecs = merges.Select(m => {
            var mergeVec = m.WorldMatrix.Translation - rotor.Top.WorldMatrix.Translation;
            return Vector3D.Normalize(Vector3D.ProjectOnPlane(ref mergeVec, ref uVec));
        }).ToList();

        var eaVec = getAVec(dMap.Keys.First());
        var emVec = Vector3D.Zero; double eDist = double.PositiveInfinity;
        for (var i = 0; i < mVecs.Count; i++) {
            var cDist = (mVecs[i] - eaVec).Length();
            if (cDist < eDist) { eDist = cDist; emVec = mVecs[i]; }
        }
        var eMul = (positive ? uVec : dVec).Dot(emVec.Cross(eaVec)); eMul = double.IsNaN(eMul) ? 0d : Math.Sign(eMul);

        if (-eMul * eDist > eMul * nearDist) rotor.TargetVelocityRad = (float) (positive ? Math.PI : -Math.PI);
        else if (
            rotDir == dir.forward  && (Math.Abs(mov.X) > dEPS || Math.Abs(mov.Y) > dEPS) ||
            rotDir == dir.backward && (Math.Abs(mov.X) > dEPS || Math.Abs(mov.Y) > dEPS) ||
            rotDir == dir.left     && (Math.Abs(mov.Z) > dEPS || Math.Abs(mov.Y) > dEPS) ||
            rotDir == dir.right    && (Math.Abs(mov.Z) > dEPS || Math.Abs(mov.Y) > dEPS) ||
            rotDir == dir.up       && (Math.Abs(mov.Z) > dEPS || Math.Abs(mov.X) > dEPS) ||
            rotDir == dir.down     && (Math.Abs(mov.Z) > dEPS || Math.Abs(mov.X) > dEPS)
        ) rotor.TargetVelocityRad = (float) (positive ? Math.PI : -Math.PI);
        else if (velMag < 10d) rotor.TargetVelocityRad = (float) (Math.Min(velMag / 10d, 1d) * Math.PI * (positive ? 1d : -1d));

        foreach (var dv in dMap) {
            var aVec = getAVec(dv.Key);
            IMyShipMergeBlock merge = null; var mVec = Vector3D.Zero; double dist = double.PositiveInfinity;
            for (var i = 0; i < mVecs.Count; i++) {
                var cDist = (mVecs[i] - aVec).Length();
                if (cDist < dist) { dist = cDist; merge = merges[i]; mVec = mVecs[i]; }
            }
            var mul = (positive ? uVec : dVec).Dot(mVec.Cross(aVec)); mul = double.IsNaN(mul) ? 0d : Math.Sign(mul);
            var forceVec = dv.Value;

            var pMov = -mov.Dot(forceVec);
            if (pMov < -dEPS) { // accelerate
                if (dist < nearDist || mul > 0d && dist < maxDist) merge.Enabled = true;
                else merge.Enabled = false;
            } else if (dampen && Math.Abs(pMov) < dEPS && vel.Dot(forceVec) < -dEPS) { // dampen
                if (dist < nearDist || mul > 0d && dist < maxDist) merge.Enabled = true;
                else merge.Enabled = false;
            } else merge.Enabled = false; // no action
        }
    }
    public void shutdown() => merges.ForEach(m => { if (m.IsFunctional) m.Enabled = false; });
}

public int state = 0;
public IMyShipController controller = null;
public List<mrHead> mrHeads = null;
public Vector3D sPos = Vector3D.Zero;
public pidCtrl fPid = newDefaultPid();
public pidCtrl lPid = newDefaultPid();
public pidCtrl uPid = newDefaultPid();
public gStableArr gStabilizator = null;

public void update(double delta) {
    if (isCurrentlyDocked()) {
        print("Docked");
        mrHeads.ForEach(h => h.shutdown());
        gStabilizator?.standby();
        Runtime.UpdateFrequency = UpdateFrequency.Update10;
        return;
    } else if (Runtime.UpdateFrequency != UpdateFrequency.Update1) Runtime.UpdateFrequency = UpdateFrequency.Update1;

    gStabilizator?.update();

    var mov = controller.MoveIndicator;
    var vel = controller.GetShipVelocities().LinearVelocity;

    print($"mov: {mov.X.ToString("0.000")}, {mov.Y.ToString("0.000")}, {mov.Z.ToString("0.000")}");
    print($"vel: {vel.X.ToString("0.000")}, {vel.Y.ToString("0.000")}, {vel.Z.ToString("0.000")}");

    var mat = controller.WorldMatrix;
    var fVec = mat.Forward; var lVec = mat.Left; var uVec = mat.Up;
    if (vel.Length() < 3d && mov.Length() < dEPS) {
        if (sPos.IsZero()) sPos = mat.Translation;
        vel = (mat.Translation - sPos) * 0.25f;
        vel = fVec * fPid.control(vel.Dot(fVec), delta) + lVec * lPid.control(vel.Dot(lVec), delta) + uVec * uPid.control(vel.Dot(uVec), delta);
        print($"fine adjusting to {vel.X.ToString("0.000")}, {vel.Y.ToString("0.000")}, {vel.Z.ToString("0.000")}");
    } else sPos = mat.Translation;
    vel += controller.GetNaturalGravity() * delta;

    mrHeads.ForEach(h => h.update(mov, vel, controller.DampenersOverride));
}

public bool init() {
    var blocks = getBlocks(b => b.IsSameConstructAs(Me));

    findDebugLcd(blocks, tagRegex);
    wipe(); print("tag: @mrdrive");

    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName)) as IMyShipController;
    if (controller != null) {
        try {
            initDockState(blocks);

            gStabilizator?.release();
            var gyros = blocks.Where(b => b is IMyGyro && b.CubeGrid == controller.CubeGrid && b.IsWorking && tagRegex.IsMatch(b.CustomName)).Cast<IMyGyro>();
            if (gyros.Count() > 0) { gStabilizator = new gStableArr(gyros, controller); gStabilizator.capture(); }
            else gStabilizator = null;

            var merges = blocks.Where(b => b is IMyShipMergeBlock).Cast<IMyShipMergeBlock>();
            var rotors = blocks.Where(b => b is IMyMotorStator && b.IsWorking && tagRegex.IsMatch(b.CustomName)).Cast<IMyMotorStator>();
            if (mrHeads != null) mrHeads.ForEach(h => h.shutdown());
            mrHeads = rotors.Select(r => new mrHead(r, merges.Where(m => m.CubeGrid == r.TopGrid).ToList(), controller)).ToList();

            foreach (var b in blocks.Where(b => b is IMyThrust && b.CubeGrid == controller.CubeGrid && tagRegex.IsMatch(b.CustomName))) (b as IMyThrust).Enabled = false;
        } catch (Exception e) {
            print($"Wrongly built merge rotor drive: could not evaluate components\n{e.Message}\n{e.StackTrace}"); Echo("error");
            return false;
        }

        if (mrHeads.Count > 0) {
            Echo($"ok");
            return true;
        } else {
            print("Wrong merge rotor drive configuration");
            print("  No merge drive rotors found.");
            Echo("error");
        }
    } else { print("No main controller"); Echo("error"); }

    return false;
}

public void shutdown() {
    if (mrHeads != null) mrHeads.ForEach(h => h.shutdown());
    gStabilizator?.release();

    mrHeads = null; gStabilizator = null;
}

const string pName = "@mrdrive program";
public Program() {
    Echo("");
    if (!Me.CustomName.StartsWith(pName)) Me.CustomName = pName;
    initMeLcd();

    if (!string.IsNullOrEmpty(Storage)) state = int.Parse(Storage);
    if (state == 1) {
        if (!init()) shutdown();
        Runtime.UpdateFrequency = UpdateFrequency.Update1;
    } else {
        findDebugLcd(getBlocks(b => b.IsSameConstructAs(Me)), tagRegex);
        Echo("offline"); wipe(); print("merge rotor drive shut down");
    }
}

public void Save() => Storage = state.ToString();

public int refreshTick = 0;
public void Main(string argument, UpdateType updateSource) {
    if (updateSource == UpdateType.Update1 || updateSource == UpdateType.Update10) {
        if (state == 1) {
            refreshTick++;
            if (refreshTick >= 200) {
                if (!init()) shutdown();
                refreshTick = 0;
            }
            if (mrHeads != null) {
                try {
                    wipe();
                    update(Runtime.TimeSinceLastRun.TotalSeconds);
                } catch (Exception e) {
                    shutdown();
                    Echo("error"); wipe(); print($"Exception occcured while execution:\n{e.Message}\n{e.StackTrace}");
                }
            }
        }
    } else {
        if (argument == "start" && state != 1) {
            state = 1;
            if (!init()) shutdown();
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        } else if (argument == "stop" && state > 0) {
            shutdown();
            state = 0;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("offline"); wipe(); print("merge rotor drive shut down");
        }
    }
}