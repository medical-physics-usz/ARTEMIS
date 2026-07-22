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

Set `ArtemisReleaseVersion` in `eclipse_script/ARTEMIS.Version.props`, then clean
the generated `bin`, `obj`, and `plugins` directories and build
`eclipse_script/USZ_ARTEMIS.sln` in Visual Studio. The shared release property
gives the plugin and its embedded Core dependency distinct assembly identities,
preventing Eclipse from reusing Core from an older ARTEMIS release.

Deploy the generated ESAPI plugin to the Varian Published Scripts folder and
restart Eclipse before testing it. A file replacement alone does not unload
assemblies that the current Eclipse process has already loaded.

Copy `USZ_ARTEMIS.AppPaths.example.json` beside the deployed ESAPI assembly and
rename it by replacing the DLL's final `.dll` extension with `.json`. For
example, deploy `USZ_ARTEMIS_v26.7.20.2.esapi.json` beside
`USZ_ARTEMIS_v26.7.20.2.esapi.dll`. Alternatively, set
`USZ_ARTEMIS_APP_PATHS` to an explicit configuration path. The build copies a
source-local `$(AssemblyName).json` beside the output DLL when it is present.
