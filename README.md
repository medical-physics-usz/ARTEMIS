# ARTEMIS

ARTEMIS: Adaptive Radiation Therapy Enhanced by Magnetic Resonance Imaging Systems.

This repository is a monorepo with two independent clinical software subprojects:

- `preprocessing/`: Python preprocessing pipeline and GUI, built as a standalone executable with PyInstaller.
- `eclipse_script/`: Eclipse Scripting API (ESAPI) project.

The Python pipeline also contains an Eclipse launcher script in `preprocessing/eclipse_launcher/`.

## Common Tasks

- Python development: see `preprocessing/README.md`.
- Python executable deployment: see `docs/deployment.md`.
- ESAPI development: see `eclipse_script/README.md`.
- macOS C# tests: run `dotnet test USZ_ARTEMIS.Offline.sln` from `eclipse_script/`.
- Data handling rules: see `docs/data_protection.md`.

## Repository Structure

```text
preprocessing/                      Python application, tests, configs, and PyInstaller files
preprocessing/eclipse_launcher/     Preprocessing app launcher
eclipse_script/                     ESAPI script
docs/                               Documentation
```
