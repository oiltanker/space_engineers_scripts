@import lib.alignment

public static float chooseRPY(align align, dir dir, float roll, float pitch, float yaw) { // possibly faster than multiplying by dot products
    switch (dir) {
        case dir.forward:
            if (align.up == dir.forward || align.up == dir.backward) return -roll;
            else                                                     return  roll;
        case dir.backward:
            if (align.up == dir.forward || align.up == dir.backward) return  roll;
            else                                                     return -roll;
        case dir.left:
            if (align.up == dir.left    || align.up == dir.right)    return -pitch;
            else                                                     return  pitch;
        case dir.right:
            if (align.up == dir.left    || align.up == dir.right)    return  pitch;
            else                                                     return -pitch;
        case dir.up:
            if (align.up == dir.up      || align.up == dir.down)     return  yaw;
            else                                                     return -yaw;
        case dir.down:
            if (align.up == dir.up      || align.up == dir.down)     return -yaw;
            else                                                     return  yaw;
        default: return Single.NaN;
    }
}