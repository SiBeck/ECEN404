"""pytest configuration for FinalCode tests.

Adds FinalCode and PythonScripts to sys.path so that:
  - cardiac_gating.*  (FinalCode/cardiac_gating/) is importable
  - cardiac_gating_bridge  (PythonScripts/) is importable
"""

import sys
from pathlib import Path

_FINALCODE_DIR = str(Path(__file__).resolve().parent.parent)
_SCRIPTS_DIR = str(Path(__file__).resolve().parent.parent.parent / "PythonScripts")

for _dir in (_FINALCODE_DIR, _SCRIPTS_DIR):
    if _dir not in sys.path:
        sys.path.insert(0, _dir)
