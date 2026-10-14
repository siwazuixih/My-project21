// Non-Unity storage/display substitutes only; parsing is linked from production.
public partial class DobotController
{
    private double feedbackClockOffset = double.PositiveInfinity;
    private string currentRobotModeText;
    private double speedScaling, velocityRatio;
    private long feedbackSequence;
    private double[] actualJointSpeeds = new double[6];
    private void EnsureFeedbackArrays() { }
    private string RobotModeToText(int mode) => mode.ToString();
}
