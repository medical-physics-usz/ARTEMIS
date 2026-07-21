# Workflow

ARTEMIS has two separate software workflows in one repository.

## Python Executable Workflow

The Python pipeline lives in `preprocessing/`. It is developed as a normal Python application using the `src/` layout, then packaged into a standalone executable with PyInstaller.

The Eclipse launcher in `preprocessing/eclipse_launcher/USZ_ARTEMIS_Preprocessing.cs` is copied manually to the Varian Published Scripts folder. Its deployed executable path is read from `USZ_ARTEMIS_Preprocessing.local.config`, which is not committed. When started from Eclipse, it passes the patient ID, plan label, plan UID, and username to the Python executable.

## Full ESAPI Workflow

The full Eclipse Scripting API project lives in `eclipse_script/`. It is a normal Visual Studio solution and is built separately from the Python executable.
Deployment-specific ESAPI paths are read from a JSON file beside the DLL with
the same filename stem and are not committed. For example,
`USZ_ARTEMIS_v26.7.20.2.esapi.dll` uses
`USZ_ARTEMIS_v26.7.20.2.esapi.json`.

The Eclipse launcher is not part of the ESAPI solution. It belongs to the Python executable deployment workflow.
