"""Compile/test the same portable C marker detector embedded in the iOS plugin."""
import os
from pathlib import Path
import shutil
import subprocess
from generate_glove_markers import generate

ROOT = Path(__file__).resolve().parent.parent
PLUGIN = ROOT / "Assets/ARVisualizer/Plugins/iOS"
BUILD = ROOT / "Builds/MarkerValidation"


def main():
    compiler = os.environ.get("CC") or shutil.which("gcc") or shutil.which("clang") or shutil.which("cc")
    if not compiler:
        raise SystemExit("Install a C99 compiler or set CC to its path.")
    patterns = generate()
    binary = BUILD / ("marker-tests.exe" if os.name == "nt" else "marker-tests")
    sources = [PLUGIN / "AVMarkerDetector.c", *(PLUGIN / "AprilTag").glob("*.c"),
               *(PLUGIN / "AprilTag/common").glob("*.c"), ROOT / "Tools/Tests/marker_detector_tests.c"]
    subprocess.run([compiler, "-std=gnu99", "-O2", "-I", str(PLUGIN), "-I", str(PLUGIN / "AprilTag"),
                    *map(str, sources), "-o", str(binary), "-lm", "-pthread"], check=True)
    subprocess.run([str(binary), str(patterns)], check=True)


if __name__ == "__main__":
    main()
