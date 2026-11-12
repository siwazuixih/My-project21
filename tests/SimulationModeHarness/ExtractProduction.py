"""Compile the exact production entry guard, not a reimplementation. No Unity scene or IO."""
from pathlib import Path
import sys

source = Path(sys.argv[1]).read_text()
start = source.index('    private bool PrepareSimulationPlayback()')
body = source.index('{', start)
depth = 1
end = body + 1
while depth:
    if source[end] == '{': depth += 1
    if source[end] == '}': depth -= 1
    end += 1
# Call-site checks keep this focused harness honest about the real UI wiring.
normal = source[source.index('    private void OnPathPlanClicked()'):source.index('    private void OnResetClicked()')]
assert normal.index('if (!PrepareSimulationPlayback()) return;') < normal.index('RebuildRuntimeNavMesh()')
offline = source[source.index('    private void OnFullProcessValidationChanged('):source.index('    private void LoadRunParams()')]
assert 'enabled && !PrepareSimulationPlayback()' in offline
assert 'RealRobotFollower.followEnabled = false' not in source
assert 'RealRobotFollower.SetRealRobotFollow(!m_isSimulationMode && param.IsSync)' in source
Path(sys.argv[2]).write_text('using UnityEngine;\npublic partial class Simulation {\n' + source[start:end] + '\n}\n')
