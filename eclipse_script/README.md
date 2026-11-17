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
namespaces and ESAPI entry point remain stable. The production target is
ESAPI 19.0 / Eclipse 19.0, .NET Framework 4.8, and x64. The current plugin is
`USZ_ARTEMIS_v26.11.17.1.esapi.dll`, with assembly, file, and informational
version `26.11.17.1`.

## Required Local Files

Varian ESAPI assemblies are proprietary and are not committed to Git. For local builds, place them here:

```text
eclipse_script/ESAPI-19.0/
├── VMS.TPS.Common.Model.API.dll
└── VMS.TPS.Common.Model.Types.dll
```

NuGet packages are restored into `eclipse_script/packages/`.
Both ESAPI references are pinned to the supplied assemblies' strong-name
version `1.1.0.24` and are not copied to the output or embedded in the plugin.

Deployment-specific paths are loaded from `USZ_ARTEMIS.esapi.json` beside the
DLL, independent of the versioned DLL filename. Start from
`USZ_ARTEMIS/Configuration/USZ_ARTEMIS.AppPaths.example.json` and keep the local
file out of Git. When `USZ_ARTEMIS.esapi.json` is present under
`Configuration/`, the Visual Studio build copies it beside the output DLL.

The same local JSON file accepts `CouchConfiguration`. Leave it empty only when
the selected Eclipse couch model has a single configuration, as documented in
the ESAPI 19.0 [StructureSet reference](reference/esapi-19/API/StructureSet.md#addcouchstructures).
If the model has multiple configurations, set the exact commissioned configuration
name, preserving the previous rails-in geometry where applicable. ARTEMIS does
not guess a configuration name or retry with another configuration.

ESAPI 19.0 replaces the two `RailPosition` arguments and three HU arguments of
`AddCouchStructures` with a configuration name and an HU-override dictionary.
The supplied reference does not specify the dictionary keys. ARTEMIS therefore
passes an empty dictionary, then assigns and reads back the existing clinical HU
overrides on the returned structures by ID: `CouchSurface = -432` for
`Exact IGRT Couch, medium`; `CouchSurface = -900` and `CouchInterior = -700`
for `Exact_Couch_Top_with_Flat_panel`. Other components retain their Eclipse
model values. Missing or ambiguous override targets and failed assignments
raise errors and trigger removal of the incomplete couch. The existing BODY
restoration and couch repositioning rollback remain in place.
If the initial replacement fails after removing an existing couch, the error
explicitly instructs the user to discard the modifications; that original
couch is not automatically restored by the initial creation path.

## Preprocessing launcher

The **Launch preprocessing** button above **Plan Adaptation** starts the
separately deployed Python executable. Set `PreprocessingExecutablePath` and
`PreprocessingWorkingDirectory` to absolute paths in `USZ_ARTEMIS.esapi.json`
(or the configuration selected by `USZ_ARTEMIS_APP_PATHS`). Use the existing
standalone launcher's `ExecutablePath` and `WorkingDirectory` values when
migrating. Keep `.env` beside the executable, even when the configured working
directory is different.
No Python build or source dependency is added to the ESAPI solution.

The button passes four positional arguments: patient ID, open plan ID, plan
DICOM UID, and Windows username, matching the standalone launcher. The Python
GUI currently consumes the first three arguments. A patient and plan must be
open; incomplete context, missing or invalid configuration, unavailable paths,
process-start failures, and nonzero exit codes produce an error dialog. The
launch is asynchronous and does not save or modify the plan. The standalone
launcher remains available for independent deployment.

Build and deploy the updated main ESAPI plugin. On the licensed Eclipse 19.0
workstation, confirm the button location and launch a commissioned test case;
verify the patient/plan shown in preprocessing and `.env` loading from beside
the executable with a different working directory. Verify paths containing
spaces, missing configuration, unavailable executable/working directory, and
Windows denying process startup. On a test deployment, omit a required `.env`
setting and verify that Eclipse reports the startup failure.
The launch helper is tested offline through linked source, adding no new
main-to-Core API references or standalone Core deployment requirement.

## Plan copying

Plan copying checks whether the destination structure set can use physical
material table `AcurosXB-13.5`, assigns it immediately after copying, and reads
it back. An already assigned matching table is left in place. If the check,
assignment, or verification fails, ARTEMIS shows a warning and continues plan
copying; assign or verify the table manually in Eclipse before dose calculation.
This setting belongs to the destination structure set and therefore also affects
other plans sharing it. It does not select the plan's dose-calculation model.
The implementation uses the ESAPI 19.0
[physical material table methods](reference/esapi-19/API/StructureSet.md#canusephysicalmaterialtable).

After copying optimization objectives, an information dialog reminds the planner
to manually enable **Avoid entry dose** for `OR_Hip_Water_Ph` when that structure
is present in the destination structure set, including when it is empty.
For present structures, a separate warning requires manual verification that
`OR_Hip_Water` and `OR_Hip_Water_Ph` have material override **Water**, and
`HighDensity_Ph` has material override **Titanium Alloy**, before optimization
or dose calculation. Absent structures do not produce reminders.

These named material overrides cannot be read through the public ESAPI 19.0
`Structure` API, as checked against the supplied 19.0 assembly and
[Structure reference](reference/esapi-19/API/Structure.md#getassignedhu).
HU overrides and the structure set's physical material table do not confirm
the named material assignment. ARTEMIS therefore requests manual verification
and does not report these overrides as automatically verified or change them.
The reminder logic is tested offline through linked source and adds no new
main-to-Core references. Rebuild and deploy the main ESAPI assembly, then verify
the information and warning dialogs on Eclipse 19.0 with each target structure
present and absent, both Water structure IDs present, an empty target structure,
and correct, missing, or incorrect named material overrides.

## Build

Open `USZ_ARTEMIS.sln` in Visual Studio on Windows. Restore NuGet packages and
build the x64 configuration. Build outputs are generated under the main
project's `plugins/` folder and are ignored by Git.

```powershell
MSBuild.exe USZ_ARTEMIS.sln /restore /p:RestorePackagesConfig=true /p:Configuration=Release /p:Platform=x64
```

The migration audit also checked every other modified or removed member listed
in the ESAPI 19.0 [breaking changes](reference/esapi-19/guide/02-eclipse-scripting-api.md#breaking-changes-in-esapi-190).
None has an active call site in this project. The exact couch signature was
verified against the supplied 19.0 API DLL as well as the member reference.

Before clinical deployment, validate on the licensed Eclipse 19.0 workstation:

- Configure and verify the selected couch configuration, component IDs, rail
  positions, HU assignments, image orientation, and automatic positioning.
- Check BODY restoration, retry success, and rollback after creation or HU
  assignment failures. If cleanup or restoration fails, discard the changes.
- Exercise plan copying, structure creation and metadata rules, clinical-goal
  evaluation, and QA/reporting against commissioned test cases.
- Verify automatic material table assignment, an already matching table, and
  warning-only continuation when the table is unavailable or assignment fails.
  Confirm `AcurosXB-13.5` is commissioned for the intended workflow and that
  other plans sharing the destination structure set are accounted for.
- Deploy the rebuilt main plugin, remove stale standalone `USZ_ARTEMIS.Core.dll`
  copies, restart Eclipse, and confirm the displayed version. Core remains
  embedded through Costura; this migration adds no main-to-Core API references.

## Offline development on macOS

Pure logic lives in `USZ_ARTEMIS.Core`, which targets .NET Standard 2.0 so the
existing .NET Framework 4.8 plugin can consume it. Cross-platform tests target
.NET 10 and are kept in a separate solution that does not load WPF or ESAPI:

```bash
dotnet test USZ_ARTEMIS.Offline.sln
```

The couch HU tests compile the ESAPI-independent `CouchDataExtractor.cs` as a
linked source file. They check the production mapping and failure behavior
without adding ESAPI/WPF dependencies or a new Core deployment dependency.

Keep Varian and WPF-dependent code in `USZ_ARTEMIS`. Its authoritative build
and all Eclipse integration testing remain Windows-only.
