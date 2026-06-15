# Eclipse Launcher

`USZ_ARTEMIS_Preprocessing.cs` is a single Eclipse/Varian script file that starts the generated Python executable.

It is intentionally not a Visual Studio project:

- no `.csproj` belongs in this folder,
- no CI workflow builds this file,
- deployment is a manual copy to the Varian Published Scripts folder.

Before clinical deployment, copy `USZ_ARTEMIS_Preprocessing.example.config` to
`USZ_ARTEMIS_Preprocessing.local.config` beside the deployed launcher script.
Put the real executable path and working directory in the local config file, not
in tracked source.
