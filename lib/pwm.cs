
public class pwm {
    public float acc;
    public pwm(float initialAcc = 0f) { acc = initialAcc; }
    public bool get(float stren) { // stren - between 0.0f and 1.0f
        acc += stren;
        if (stren >= 1f) {
            stren -= 1f;
            return true;
        } else return false;
    }
}
