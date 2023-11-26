public const int ENG_UPS = 60;

public class pidCtrl {
    // component constants
    public double constP;
    public double constI;
    public double constD;
    public double integralFalloff;
    // PID variables
    double timeStep;
    double invTimeStep;
    double errorSum;
    double lastError;
    bool firstRun;
    public pidCtrl(double constP = 1d, double constI = 0.25d, double constD = 0.1d, double timeStep = 1d / ENG_UPS, double integralFalloff = 0.95d) {
        this.constP = constP;
        this.constI = constI;
        this.constD = constD;
        setTimeStep(timeStep);
        this.integralFalloff = integralFalloff;
        reset();
    }
    public void setTimeStep(double timeStep) {
        if (timeStep != this.timeStep) {
            this.timeStep = timeStep;
            invTimeStep = 1d / this.timeStep;
        }
    }
    public void reset() {
        errorSum = 0d;
        lastError = 0d;
        firstRun = true;
    }
    public double getIntegral(double currentError) => errorSum * integralFalloff + currentError * timeStep; // error for integral component
    public double control(double error) { // for assumed constant time
        double errorDerivative = (error - lastError) * invTimeStep;
        if (firstRun) {
            errorDerivative = 0d;
            firstRun = false;
        }
        errorSum = getIntegral(error);
        lastError = error;
        return constP * error + constI * errorSum + constD * errorDerivative;
    }
    public double control(double error, double timeStep) { // for exact time
        setTimeStep(timeStep);
        return control(error);
    }
}