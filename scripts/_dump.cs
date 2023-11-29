/*
 * cuVec - control's up vector
 * cgVec - -(control's total gravity vector) normalized
 * mat - IMyCubeGrid's WorldMatrix (MatrixD)
 */
var fVec = mat.Forward; var lVec = mat.Left; var uVec = mat.Up;

var fuVec = Vector3D.ProjectOnPlane(ref cuVec, ref fVec); //  \ - roll plane projection
var fgVec = Vector3D.ProjectOnPlane(ref cgVec, ref fVec); //  /
var luVec = Vector3D.ProjectOnPlane(ref cuVec, ref lVec); //  \ - pitch plane projection
var lgVec = Vector3D.ProjectOnPlane(ref cgVec, ref lVec); //  /
var uuVec = Vector3D.ProjectOnPlane(ref cuVec, ref uVec); //  \ - yaw plane projection
var ugVec = Vector3D.ProjectOnPlane(ref cgVec, ref uVec); //  /

var fMul = Vector3D.Dot(fuVec.Cross(fgVec), fVec); // roll clockwise check - dot product (on-vector projection) of a vector perpendicular to the roll plane vectors
var lMul = Vector3D.Dot(luVec.Cross(lgVec), lVec); // same for pitch
var uMul = Vector3D.Dot(uuVec.Cross(ugVec), uVec); // same for yaw
fMul = Math.Abs(fMul) > dEPS ? Math.Sign(fMul) : 0d; // check if roll adjustment needed
lMul = Math.Abs(lMul) > dEPS ? Math.Sign(lMul) : 0d; // same for pitch
uMul = Math.Abs(uMul) > dEPS ? Math.Sign(uMul) : 0d; // same for yaw
print($"fMul: {fMul.ToString("0.000")}    lMul: {lMul.ToString("0.000")}    uMul: {uMul.ToString("0.000")}");

var roll  = -(fMul * Vector3D.Angle(fuVec, fgVec) * radToDegMul); // roll error in degrees
var pitch =  (lMul * Vector3D.Angle(luVec, lgVec) * radToDegMul); // same for pitch
var yaw   =  (uMul * Vector3D.Angle(uuVec, ugVec) * radToDegMul); // same for yaw
roll  = Double.IsNaN(roll)  ? 0 : roll;
pitch = Double.IsNaN(pitch) ? 0 : pitch;
yaw   = Double.IsNaN(yaw)   ? 0 : yaw;