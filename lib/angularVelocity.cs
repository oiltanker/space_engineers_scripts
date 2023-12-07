
public static Vector3D toLocalAVel(Vector3D aVel, IMyTerminalBlock anchor) {
    var mat = anchor.WorldMatrix;
    return new Vector3D(
           mat.Left.Z * aVel.Z +    mat.Left.X * aVel.X +    mat.Left.Y * aVel.Y,
            -mat.Up.Z * aVel.Z -      mat.Up.X * aVel.X -      mat.Up.Y * aVel.Y,
        mat.Forward.Z * aVel.Z + mat.Forward.X * aVel.X + mat.Forward.Y * aVel.Y
    );
}
