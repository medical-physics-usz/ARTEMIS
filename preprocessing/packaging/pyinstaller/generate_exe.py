from pathlib import Path

import PyInstaller.__main__

project_root = Path(__file__).resolve().parents[2]
spec_file = project_root / "packaging" / "pyinstaller" / "artemis_preprocessing.spec"

PyInstaller.__main__.run([
    str(spec_file),
    "--clean",
    "--noconfirm",
    "--distpath",
    str(project_root / "dist"),
    "--workpath",
    str(project_root / "build"),
])
