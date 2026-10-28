"""Build-only extraction: compile the exact production offline coroutine, not a rewritten model.
Unity animation/servo references are stubbed by the harness; no project source is changed.
"""
from pathlib import Path
import sys

source = Path(sys.argv[1]).read_text()
types = source[:source.index('public partial class ConnectCommander')]
fields = source[source.index('    [Header("全流程离线验证")]'):source.index('    private bool chassisRouteRunning;')]
methods = source[source.index('    public void SetFineTuneTrajectoryProvider('):source.index('    private static string ExtractTargetPrefix(')]
chassis = source[source.index('    private IEnumerator SendChassisSegmentedRoutine('):source.index('    private IEnumerator SendArmRoutine(')]
assert 'GuardOfflineValidationRoutine' in methods and 'ExecuteActuatorPlanRoutine' in methods
Path(sys.argv[2]).write_text(types + '\npublic partial class ConnectCommander : MonoBehaviour\n{\n' + fields + methods + chassis + '\n}\n')
