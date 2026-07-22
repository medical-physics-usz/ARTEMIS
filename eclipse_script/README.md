# Eclipse ESAPI Project

This folder contains the full ARTEMIS Eclipse Scripting API project.

The code is split into an Eclipse-dependent application and a small portable
core. This keeps the clinical integration in its required Windows environment
while allowing independent logic to be tested on any supported development
platform.

## Projects and solutions

| Component | Why it exists | How to use it |
| --- | --- | --- |
| `USZ_ARTEMIS/` | This is the main Eclipse plugin. It contains the ESAPI entry point, WPF user interface, and code that communicates with Eclipse. It targets .NET Framework 4.8 and x64 and depends on the proprietary Varian ESAPI assemblies. | Put ESAPI-, Eclipse-, and WPF-dependent code here. Build and test it through `USZ_ARTEMIS.sln` on the licensed Windows workstation. |
| `USZ_ARTEMIS.Core/` | This library holds logic that does not need ESAPI or WPF. It targets .NET Standard 2.0 so both the .NET Framework plugin and the cross-platform test project can reference it. Separating this logic makes fast offline testing possible without Eclipse. | Put reusable calculations, validation, rules, and other independently testable logic here. Do not add Varian, WPF, or other Windows-only references. The main plugin and the test project both reference this library. |
| `USZ_ARTEMIS.Core.Tests/` | This is the xUnit regression-test project for `USZ_ARTEMIS.Core`. It targets .NET 10 and intentionally has no reference to the main plugin, ESAPI, or WPF. | Add tests here whenever portable core behavior changes. Run them with `dotnet test USZ_ARTEMIS.Offline.sln`. These tests complement, but do not replace, Windows builds and Eclipse integration testing. |
| `USZ_ARTEMIS.Offline.sln` | This solution contains only `USZ_ARTEMIS.Core` and `USZ_ARTEMIS.Core.Tests`. It avoids loading the main plugin and therefore does not require Visual Studio, Windows, or local ESAPI assemblies. | Use this solution for routine cross-platform development and CI of portable logic. From `eclipse_script/`, run `dotnet test USZ_ARTEMIS.Offline.sln`. |
| `USZ_ARTEMIS.sln` | This is the production Visual Studio solution. It contains `USZ_ARTEMIS` and `USZ_ARTEMIS.Core`, matching the dependency needed to build the Eclipse plugin. It excludes the offline test project so the clinical build remains separate from the cross-platform test toolchain. | Open this solution in Visual Studio on Windows to restore packages and build the x64 Eclipse plugin. The authoritative build and all Eclipse integration testing use this solution and the intended ESAPI installation. |

The dependency direction is:

```text
USZ_ARTEMIS ----------> USZ_ARTEMIS.Core
USZ_ARTEMIS.Core.Tests -> USZ_ARTEMIS.Core
```

`USZ_ARTEMIS` and `USZ_ARTEMIS.Core.Tests` must not reference each other. The C#
namespaces and ESAPI assembly name are preserved from the previous project so
clinical behavior stays stable.

## Required Local Files

Varian ESAPI assemblies are proprietary and are not committed to Git. For local builds, place them here:

```text
eclipse_script/ESAPI 18.1/
├── VMS.TPS.Common.Model.API.dll
└── VMS.TPS.Common.Model.Types.dll
```

NuGet packages are restored into `eclipse_script/packages/`.

Deployment-specific paths are loaded from a JSON file beside the DLL with the
same filename stem, for example `USZ_ARTEMIS_v26.7.20.2.esapi.json` for
`USZ_ARTEMIS_v26.7.20.2.esapi.dll`. Start from
`USZ_ARTEMIS/Configuration/USZ_ARTEMIS.AppPaths.example.json` and keep the local
file out of Git. When a version-matched JSON file is present under
`Configuration/`, the Visual Studio build copies it beside the output DLL.

## Release identity

`ARTEMIS.Version.props` is the single source for the ESAPI release version. It
gives both the plugin and its embedded `USZ_ARTEMIS.Core` dependency
release-specific assembly names. This prevents a long-running Eclipse process
from reusing Core from an older ARTEMIS plugin release.

When preparing a release, update `ArtemisReleaseVersion`, clean the generated
`bin`, `obj`, and `plugins` directories, rebuild the complete solution, and
restart Eclipse before testing the new DLL. Costura uses the
`USZ_ARTEMIS.Core_v*` pattern, so its configuration does not need a matching
manual version edit.

## Build

Open `USZ_ARTEMIS.sln` in Visual Studio on Windows. Restore NuGet packages and
build the x64 configuration. Build outputs are generated under the main
project's `plugins/` folder and are ignored by Git.

## Offline development on macOS

Pure logic lives in `USZ_ARTEMIS.Core`, which targets .NET Standard 2.0 so the
existing .NET Framework 4.8 plugin can consume it. Cross-platform tests target
.NET 10 and are kept in a separate solution that does not load WPF or ESAPI:

```bash
dotnet test USZ_ARTEMIS.Offline.sln
```

Keep Varian and WPF-dependent code in `USZ_ARTEMIS`. Its authoritative build
and all Eclipse integration testing remain Windows-only.
