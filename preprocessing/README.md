# ARTEMIS Preprocessing

Python GUI and preprocessing pipeline for the ARTEMIS workflow.

## Layout

- `src/artemis_preprocessing/`: application code.
- `src/artemis_preprocessing/cli.py`: console entry point.
- `src/artemis_preprocessing/__main__.py`: module entry point for `python -m artemis_preprocessing`.
- `configs/`: example configuration files.
- `packaging/pyinstaller/`: executable build files.
- `eclipse_launcher/`: single manually deployed Eclipse launcher script.
- `tests/`: automated smoke and regression tests.

## Local Setup

```bash
cd preprocessing
python -m venv .venv
. .venv/bin/activate
python -m pip install -e .
```

Runtime configuration is still loaded from `.env`. Start from `configs/env.example` and keep real clinical paths, registration log locations, and credentials out of Git.

## Run

```bash
python -m artemis_preprocessing <patient_id> <rtplan_label> <rtplan_uid>
```

The Eclipse launcher passes those values when the executable is started from Eclipse.

## Build Executable

```bash
cd preprocessing
python packaging/pyinstaller/generate_exe.py
```

The generated executable is named `artemis_preprocessing.exe` on Windows.
