/*
 * NOT POSSIBLE DUE TO BAD PHYSICS
 */


@import lib.eps
@import lib.printFull
@import lib.grid
@import lib.alignment
@import lib.pid

public const double radToDegMul = 180 / Math.PI;
public static readonly @Regex tagRegex = new @Regex(@"(\s|^)@test(\s|$)");

public int state = 0;
public IMyShipController controller = null;
bool allOk = false;

int angleMul1 = 0; int angleMul2 = 0;
IMyMotorStator rotor1 = null;
IMyMotorStator rotor2 = null;
IMyTerminalBlock bEnd = null;

Vector3D pEnd = Vector3D.Zero;

public void adjust(Vector3D mov, Vector3D rot) {
    pEnd += mov;

    var bMat = rotor1.WorldMatrix;
    var uVec = bMat.Up;
    var dest = pEnd - bMat.Translation;
    dest = Vector3D.ProjectOnPlane(ref dest, ref uVec);
    var dist = dest.Length();
    print($"dist: {dist.ToString("0.000")}");

    var vec12 = rotor2.WorldMatrix.Translation - bMat.Translation;
    var len1 = (Vector3D.ProjectOnPlane(ref vec12, ref uVec)).Length();
    var vec2e = bEnd.WorldMatrix.Translation - rotor2.WorldMatrix.Translation;
    var len2 = (Vector3D.ProjectOnPlane(ref vec2e, ref uVec)).Length();
    print($"len1: {len1.ToString("0.000")}    len2: {len2.ToString("0.000")}");

    var tMul = bMat.Down.Dot(dest.Cross(bMat.Right));
    var theta = double.IsNaN(tMul) ? 0d : Math.Sign(tMul) * Vector3D.Angle(dest, bMat.Right);
    print($"theta: {(theta * radToDegMul).ToString("0.000")}");
    var beta = -Math.Acos((dist*dist - len1*len1 - len2*len2) / (2d * len1 * len2));
    print($"beta: {(beta * radToDegMul).ToString("0.000")}");
    var alphaDiv = len1 + len2 * Math.Cos(beta);
    print($"alphaDiv: {alphaDiv.ToString("0.000")}");
    var alpha = theta - Math.Atan((len2 * Math.Sin(beta)) / alphaDiv) - (alphaDiv >= 0d ? 0d : Math.PI);
    print($"alpha: {(alpha * radToDegMul).ToString("0.000")}");

    var r1Mul = bMat.Up.Dot(bMat.Backward.Cross(rotor1.Top.WorldMatrix.Forward));
    var r1Angle = double.IsNaN(r1Mul) ? 0d : Math.Sign(r1Mul) *  Vector3D.Angle(bMat.Backward, rotor1.Top.WorldMatrix.Forward);
    if (r1Angle < 0d) r1Angle = 2d*Math.PI + r1Angle;
    var r2Mul = rotor2.WorldMatrix.Up.Dot(rotor2.WorldMatrix.Backward.Cross(rotor2.Top.WorldMatrix.Forward));
    var r2Angle = double.IsNaN(r2Mul) ? 0d : Math.Sign(r2Mul) *  Vector3D.Angle(rotor2.WorldMatrix.Backward, rotor2.Top.WorldMatrix.Forward);
    if (r2Angle < 0d) r2Angle = 2d*Math.PI + r2Angle;
    print($"r1Angle: {(r1Angle * radToDegMul).ToString("0.000")}");
    print($"r2Angle: {(r2Angle * radToDegMul).ToString("0.000")}");
    print($"rotor1 vel: {((angleMul1 * alpha - r1Angle) * radToDegMul).ToString("0.000")}");
    print($"rotor2 vel: {((angleMul2 * (beta + Math.PI) - r2Angle) * radToDegMul).ToString("0.000")}");
    rotor1.TargetVelocityRad = 1f * (float) -(angleMul1 * alpha - r1Angle);
    rotor2.TargetVelocityRad = 1f * (float) -(angleMul2 * (beta + Math.PI) - r2Angle);
}

public void update() {
    var rotInd = controller.RotationIndicator;
    var roll = controller.RollIndicator; var pitch = rotInd.X; var yaw = rotInd.Y;
    print($"roll: {roll.ToString("0.000")}    pitch: {pitch.ToString("0.000")}    yaw: {yaw.ToString("0.000")}");

    var mat = controller.WorldMatrix;
    var mov = controller.MoveIndicator;
    print($"mov: {mov.X.ToString("0.0")}, {mov.Y.ToString("0.0")}, {mov.Z.ToString("0.0")}");

    adjust(mat.Left * mov.X * 0.1d + mat.Up * -mov.Z * 0.1d, new Vector3D(pitch * 0.01f, yaw * -0.01f, roll * -0.5f));
}

public bool init() {
    var blocks = getBlocks(b => b.IsSameConstructAs(Me));

    findDebugLcd(blocks, tagRegex);
    wipe();

    controller = blocks.FirstOrDefault(b => b is IMyShipController && tagRegex.IsMatch(b.CustomName)) as IMyShipController;
    if (controller != null) {
        try {
            shutdown();

            var stators = blocks.Where(b => b is IMyMotorStator).Cast<IMyMotorStator>();

            var rotRegex = new @Regex($"(\\s|^)@test-(1|2)-(p|n)(\\s|$)");
            var endRegex = new @Regex($"(\\s|^)@test-end(\\s|$)");

            print("Evaluating rotors ...");
            foreach (var s in stators) {
                var match = rotRegex.Match(s.CustomName);
                if (!match.Success) continue;
                if        (match.Groups[2].Value == "1") {
                    if      (match.Groups[3].Value == "p") angleMul1 =  1;
                    else if (match.Groups[3].Value == "n") angleMul1 = -1;
                    rotor1 = s;
                } else if (match.Groups[2].Value == "2") {
                    if      (match.Groups[3].Value == "p") angleMul2 =  1;
                    else if (match.Groups[3].Value == "n") angleMul2 = -1;
                    rotor2 = s;
                }
            }
            print("Evaluating end block ...");
            bEnd = blocks.FirstOrDefault(b => endRegex.IsMatch(b.CustomName));
            if (bEnd == null || rotor1 == null || rotor2 == null) {
                var r1Str = rotor1 != null ? "present" : "missing";
                var r2Str = rotor2 != null ? "present" : "missing";
                var ebStr = bEnd != null ? "present" : "missing";
                throw new ArgumentException($"@test-1-(p|n) {r1Str}, @test-2-(p|n) {r2Str}, @test-end {ebStr}");
            }
            allOk = true;
            pEnd = bEnd.WorldMatrix.Translation;
        } catch (Exception e) {
            print($"Wrongly built manipulator: could not evaluate components\n{e.Message}"); Echo("error");
            return false;
        }

        if (allOk) {
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
    if (rotor1 != null && rotor1.IsFunctional) rotor1.TargetVelocityRad = 0f;
    if (rotor2 != null && rotor2.IsFunctional) rotor2.TargetVelocityRad = 0f;
    pEnd = Vector3D.Zero;
    rotor1 = null; rotor2 = null; bEnd = null;
}

const string pName = "@test program";
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
            if (allOk) {
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
            if (!init()) shutdown();
            Runtime.UpdateFrequency = UpdateFrequency.Update1;
        } else if (argument == "stop" && state > 0) {
            shutdown();
            state = 0;
            Runtime.UpdateFrequency = UpdateFrequency.None;
            Echo("offline"); wipe(); print("Manipulator shut down");
        }
    }
}