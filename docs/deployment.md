# Deployment

## Python Executable

Build the executable on Windows:

```bash
cd preprocessing
python packaging/pyinstaller/generate_exe.py
```

Deploy `artemis_preprocessing.exe` and the local `.env` file to the approved server folder.
Set `REGISTRATION_LOG_FILE` in `.env` if registration results should be written to a shared log.

## Eclipse Launcher

Copy `preprocessing/eclipse_launcher/USZ_ARTEMIS_Preprocessing.cs` manually to the Varian Published Scripts folder.
Copy `USZ_ARTEMIS_Preprocessing.example.config` to `USZ_ARTEMIS_Preprocessing.local.config`
beside the deployed launcher script and set the executable path and working directory there.

## Full ESAPI Project

The production target is Varian ESAPI 18.1. Build
`eclipse_script/USZ_ARTEMIS.sln` against the ESAPI 18.1 assemblies in Visual
Studio on the licensed Windows workstation.

Deploy the generated ESAPI plugin to the Varian Published Scripts folder.

The production Costura configuration embeds `USZ_ARTEMIS.Core` in the generated
ESAPI plugin. When Core changes, rebuild the full solution before deployment so
the generated plugin contains the matching embedded Core code. Do not deploy a
standalone `USZ_ARTEMIS.Core.dll` unless the packaging configuration is
intentionally changed to require it. Remove stale standalone Core copies from
the Published Scripts folder and restart Eclipse before validation; otherwise
the runtime may load the stale file instead of the embedded version.

Copy `USZ_ARTEMIS.AppPaths.example.json` beside the deployed ESAPI assembly and
rename it by replacing the DLL's final `.dll` extension with `.json`. For
example, deploy `USZ_ARTEMIS_v26.7.20.2.esapi.json` beside
`USZ_ARTEMIS_v26.7.20.2.esapi.dll`. Alternatively, set
`USZ_ARTEMIS_APP_PATHS` to an explicit configuration path. The build copies a
source-local `$(AssemblyName).json` beside the output DLL when it is present.
