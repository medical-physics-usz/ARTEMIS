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

Do not create a `.csproj` for the launcher.

## Full ESAPI Project

Build `eclipse_script/USZ_ARTEMIS.sln` in Visual Studio using `Release|x64`. Deploy the generated ESAPI plugin according to the local clinical release procedure.
Copy `AppPaths.example.json` to `AppPaths.local.json` beside the deployed ESAPI assembly, or set `USZ_ARTEMIS_APP_PATHS` to the local config file path.
