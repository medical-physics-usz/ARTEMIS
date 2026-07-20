# Eclipse ESAPI Project

This folder contains the full ARTEMIS Eclipse Scripting API project.

Open `USZ_ARTEMIS.sln` in Visual Studio. The C# namespaces and ESAPI assembly name are preserved from the previous project so clinical behavior stays stable.

## Required Local Files

Varian ESAPI assemblies are proprietary and are not committed to Git. For local builds, place them here:

```text
eclipse_script/ESAPI 18.1/
├── VMS.TPS.Common.Model.API.dll
└── VMS.TPS.Common.Model.Types.dll
```

NuGet packages are restored into `eclipse_script/packages/`.

Deployment-specific paths are loaded from `AppPaths.local.json`. Start from
`USZ_ARTEMIS/Configuration/AppPaths.example.json` and keep the local file out of
Git.

## Build

Use Visual Studio. Build outputs are generated under the project `plugins/` folder and are ignored by Git.

## Offline development on macOS

Pure logic lives in `USZ_ARTEMIS.Core`, which targets .NET Standard 2.0 so the
existing .NET Framework 4.8 plugin can consume it. Cross-platform tests target
.NET 10 (the current LTS SDK installed on the Mac) and are kept in a separate
solution that does not load WPF or ESAPI:

```bash
dotnet test USZ_ARTEMIS.Offline.sln
```

Keep Varian and WPF-dependent code in `USZ_ARTEMIS`. Its authoritative build
and all Eclipse integration testing remain Windows-only.
