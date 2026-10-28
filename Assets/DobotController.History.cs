public partial class DobotController
{
    // Read-only classification of an actual fresh robot feedback flag, never a UI/scene guess.
    public bool HistoryCollisionObserved
    {
        get { var state = CopyRobotState(); return !state.simulated && state.age_s >= 0 && state.age_s <= 0.25 && state.collision != 0; }
    }
}
