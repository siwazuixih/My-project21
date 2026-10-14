#!/usr/bin/env python3
"""Read-only device-free deployment check; never import the actuator server."""
import importlib
import json
import os
from pathlib import Path
import sys
import tempfile


def check_runtime():
    root = Path(__file__).resolve().parent
    checks = []
    def record(name, ok, detail):
        checks.append(dict(name=name, ok=bool(ok), detail=str(detail)))
    record('python', sys.version_info >= (3, 8), sys.executable + ' ' + sys.version.split()[0])
    for name in ('numpy', 'matplotlib'):
        try:
            module = importlib.import_module(name)
            record(name, True, getattr(module, '__version__', 'imported'))
        except Exception as exc:
            record(name, False, exc)
    for name in ('servo_tcp_client_fault_control_v26_28Nm_abnormal_stop_only.py',
                 'servo_contact_cycle.py', 'unity_robot_client.py', 'cr10af_hybrid_x.py'):
        path = root / name
        try:
            compile(path.read_text(encoding='utf-8-sig'), str(path), 'exec')
            record(name, True, 'syntax OK (not executed)')
        except Exception as exc:
            record(name, False, exc)
    font = root.parent / 'Assets' / '微软雅黑.ttf'
    record('curve_font', font.is_file(), font)
    # Matplotlib实际需要可写缓存；临时目录测试不向设备发任何命令。
    try:
        with tempfile.TemporaryDirectory(prefix='commission-check-') as cache:
            os.environ['MPLCONFIGDIR'] = cache
            from matplotlib.figure import Figure
            from matplotlib.backends.backend_agg import FigureCanvasAgg
            figure = Figure(figsize=(2, 1))
            canvas = FigureCanvasAgg(figure)
            figure.add_subplot(111).plot([0, 1], [0, 1])
            canvas.draw()
        record('headless_curve', True, 'Agg render OK')
    except Exception as exc:
        record('headless_curve', False, exc)
    return dict(ok=all(item['ok'] for item in checks), checks=checks,
                devices_connected=False,
                note='仅检查运行环境；不证明端口可用、设备连接或运动安全')


if __name__ == '__main__':
    report = check_runtime()
    print(json.dumps(report, ensure_ascii=False, indent=2))
    sys.exit(0 if report['ok'] else 1)
