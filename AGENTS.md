# AGENTS.md

## Scope

These instructions apply to the whole repository. ARTEMIS is clinical software: prefer small, reviewable changes, preserve existing clinical behavior unless the task explicitly changes it, and make safety-relevant failure modes visible rather than silently recovering.

The repository contains two independent products:

- `preprocessing/`: Python 3.12 preprocessing pipeline, GUI, DICOM networking, and PyInstaller packaging.
- `eclipse_script/`: C#/.NET Eclipse Scripting API (ESAPI) project.

Do not create dependencies between these products. The C# launcher in `preprocessing/eclipse_launcher/` is a standalone, manually deployed bridge to the Python executable; it is not part of `eclipse_script/` and must not gain a project file.

## Read Before Changing Behavior

- Start with `README.md` and the README in the subproject being changed.
- Read `docs/data_protection.md` before working with configuration, logging, fixtures, or DICOM data.
- Read `docs/workflow.md` and `docs/deployment.md` before changing entry points, paths, packaging, or launcher behavior.
- Inspect nearby tests before changing clinical or DICOM behavior. Existing behavior encoded by regression tests is intentional unless the task says otherwise.

## Data Protection and Configuration

- Never commit real clinical data, patient identifiers, DICOM files, credentials, server details, logs, or generated clinical outputs.
- Use only synthetic, programmatically generated DICOM datasets in tests. Do not connect tests to ARIA, Eclipse, or any clinical endpoint.
- Keep `.env`, `AppPaths.local.json`, `USZ_ARTEMIS_Preprocessing.local.config`, and other local configuration untracked. Update only their example files with clearly fictitious values.
- The Python application currently reads `.env`; `preprocessing/configs/config.example.yaml` is documentation only. Do not make YAML authoritative accidentally.
- Do not edit or commit proprietary ESAPI assemblies, files under `reference/`, build outputs, packaged executables, or dependency caches.

## Python Development

Work from `preprocessing/` and use the project virtual environment:

```bash
python3.12 -m venv .venv
. .venv/bin/activate
python -m pip install -c constraints-dev.txt -e '.[dev]'
python -m pytest
```

- Application code belongs under `preprocessing/src/artemis_preprocessing/`; tests belong under `preprocessing/tests/`.
- Preserve both supported entry points: `python -m artemis_preprocessing` and the `artemis-preprocessing` console script.
- Keep GUI orchestration separate from reusable DICOM, registration, imaging, I/O, and database logic where practical.
- For a focused change, run the matching test module first, for example `python -m pytest tests/test_crop_series.py`, then run the full suite before handoff.
- Build the executable only when packaging or frozen-runtime behavior changes: `python packaging/pyinstaller/generate_exe.py`. The authoritative executable build is on Windows with Python 3.12.
- When dependencies change, keep `pyproject.toml`, `requirements.txt`, and `constraints-dev.txt` consistent with their current roles.

### DICOM Safety Invariants

- Preserve spatial geometry and patient-coordinate semantics; do not infer slice order from filenames or instance numbers when orientation and position data are available.
- Generate new Series and SOP Instance UIDs for derived objects, keep file-meta UIDs synchronized, and update all affected RTSTRUCT/REG references.
- Treat multi-file updates as transactions. Validate and stage outputs before replacing originals, and preserve rollback behavior on every failure path.
- Reject unsupported, inconsistent, or ambiguous inputs without partially mutating the source dataset.
- Add synthetic regression tests for geometry, reference integrity, metadata, rollback, and no-mutation failure behavior whenever those paths change.

## C# / ESAPI Development

- The production target for `eclipse_script/` is Varian ESAPI 18.1. Compile and validate against the ESAPI 18.1 assemblies and installed Online Help on the licensed Windows workstation. Do not treat the ESAPI 17.0 reference archive as the target runtime.
- Put ESAPI- and WPF-independent logic in `eclipse_script/USZ_ARTEMIS.Core/` and cover it in `USZ_ARTEMIS.Core.Tests/`.
- Keep Varian, WPF, and Eclipse-dependent code in `eclipse_script/USZ_ARTEMIS/`. Do not add those references to the cross-platform core or offline test solution.

### Core Assembly Deployment Boundary

- `USZ_ARTEMIS` and `USZ_ARTEMIS.Core` are separate projects, but the production Costura configuration embeds `USZ_ARTEMIS.Core` in the generated ESAPI plugin. Adding, changing, or removing a Core type or member used by the main project is therefore a deployment-impacting change, even when the source change is small.
- When the main project starts using a new or changed Core API, rebuild the full ESAPI solution and deploy the newly generated main ESAPI assembly so it contains the matching embedded Core code.
- Do not deploy a standalone `USZ_ARTEMIS.Core.dll` unless the packaging configuration is intentionally changed to require it. Remove stale standalone Core copies from deployment locations and restart Eclipse before validation; otherwise the runtime may load the stale file instead of the embedded version.
- During review and handoff, explicitly identify new main-to-Core references and confirm that the production ESAPI assembly was rebuilt after the Core change.
- Small predicates used only within one ESAPI-dependent workflow may remain in the main project when extracting them solely for testability would create unnecessary cross-assembly deployment coupling.

### Using the Local ESAPI References

The repository-local, read-only references are under `reference/` (singular). Read each directory's `README.md` before relying on it.

- Use `reference/esapi-docs-17.0/catalog.jsonl` to locate exact ESAPI 17.0 type names, member names, UIDs, assemblies, and signatures. Then open the matching file under `reference/esapi-docs-17.0/docs/` for declarations, remarks, parameters, return values, exceptions, and inherited members.
- Use `reference/esapi-guide-18.1/` for ESAPI/Eclipse compatibility, upgrade guidance, scripting and deployment workflows, Plan Checker behavior, and the selected 18.1 additions documented by the vendor guide. Start with its `README.md`, then follow the topic files it indexes.
- Search from the repository root instead of scanning the whole archive manually. For example:

```bash
rg -n 'GetDVHCumulativeData|PlanSetup' reference/esapi-docs-17.0/catalog.jsonl
rg -n 'GetDVHCumulativeData' reference/esapi-docs-17.0/docs
rg -n 'Plan Checker|Department' reference/esapi-guide-18.1
```

- Keep the version boundary explicit in reasoning and handoff notes. The 17.0 archive is authoritative only for ESAPI 17.0, while the 18.1 guide is a curated overlay rather than a complete member-level reference.
- Do not guess that a 17.0 signature is unchanged in 18.1 or implement a new 18.1 member from a guide summary alone. Verify exact 18.1 signatures and behavior against the installed ESAPI 18.1 Online Help and assemblies on the licensed Windows workstation; if that is unavailable, state the uncertainty and required verification.
- Treat source links, version labels, and printed guide page references in these files as provenance. Cite the specific local reference and version when it materially informs an implementation decision.
- Do not modify, regenerate, redistribute, or add files under `reference/` as part of normal feature work. These references inform implementation but do not replace compilation against the intended assemblies, Eclipse integration tests, commissioning, or clinical validation.

- Run cross-platform tests from `eclipse_script/`:

```bash
dotnet test USZ_ARTEMIS.Offline.sln
```

- The full `USZ_ARTEMIS.sln` build and Eclipse integration testing are Windows-only and require Visual Studio, .NET Framework 4.8 targeting support, and the local proprietary ESAPI assemblies. State explicitly when those checks could not be run.
- Preserve public namespaces, assembly names, and ESAPI-facing signatures unless a coordinated deployment change is explicitly requested.
- Treat `Script.cs` clinical-mode behavior, application paths, process launches, and Published Scripts deployment as safety-sensitive. Never replace local configuration with hard-coded deployment paths.

## Change and Validation Rules

- Keep edits scoped to the requested subproject; do not reformat unrelated legacy code.
- Respect `.gitattributes`: Python/Markdown/configuration files use LF, while C#, project, solution, XAML, and XML files use CRLF.
- Add or update regression tests for behavior changes and bug fixes. Prefer precise assertions about observable outputs and failure behavior.
- Run the smallest relevant check during development, followed by the broadest locally supported suite for the changed subproject.
- For changes spanning the launcher boundary, verify the argument contract on both sides and document any required manual Eclipse test.
- Before handoff, inspect `git diff`, confirm that no sensitive or generated files were added, and report which tests ran plus any Windows/clinical validation still required.
