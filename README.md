# ARTEMIS

ARTEMIS: Adaptive Radiation Therapy Enhanced by Magnetic Resonance Imaging Systems.

This repository is a monorepo with two independent clinical software subprojects:

- `preprocessing/`: Python preprocessing pipeline and GUI, built as a standalone executable with PyInstaller.
- `eclipse_script/`: full Visual Studio / Eclipse Scripting API project.

The Python executable workflow also contains a small Eclipse launcher script in `preprocessing/eclipse_launcher/`. That launcher is copied manually to the Varian Published Scripts folder and starts the generated Python executable. It is not a C# project and is not built by CI.

## Common Tasks

- Python development: see `preprocessing/README.md`.
- Python executable deployment: see `docs/deployment.md`.
- ESAPI development: see `eclipse_script/README.md`.
- Data handling rules: see `docs/data_protection.md`.

## Repository Layout

```text
preprocessing/           Python application, tests, configs, and PyInstaller files
preprocessing/eclipse_launcher/
                           Single manually deployed Eclipse launcher script
eclipse_script/             Visual Studio ESAPI solution and project
docs/                      Shared workflow and deployment documentation
.github/workflows/         Separate Python, executable, and ESAPI CI workflows
```
