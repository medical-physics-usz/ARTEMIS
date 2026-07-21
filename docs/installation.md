# Installation

## ARTEMIS Preprocessing

Install Python 3.12.

```bash
cd preprocessing
python3.12 -m venv .venv
. .venv/bin/activate
python -m pip install -c constraints-dev.txt -e '.[dev]'
python -m pytest
```

Create a local `.env` file from `configs/env.example`. Do not commit `.env`.

## Visual Studio / ESAPI

Install Visual Studio with .NET Framework 4.8 targeting support.

Place the Varian ESAPI DLLs under:

```text
eclipse_script/ESAPI 18.1/
```

Open `eclipse_script/USZ_ARTEMIS.sln` and restore NuGet packages before building.

Copy `eclipse_script/USZ_ARTEMIS/Configuration/USZ_ARTEMIS.AppPaths.example.json`
to a JSON file whose stem matches the ESAPI DLL and replace the placeholder
paths for the clinical deployment. For example,
`USZ_ARTEMIS_v26.7.20.2.esapi.dll` uses
`USZ_ARTEMIS_v26.7.20.2.esapi.json`. The JSON may sit beside the deployed ESAPI
assembly or be referenced with the `USZ_ARTEMIS_APP_PATHS` environment
variable. When a JSON matching `$(AssemblyName).json` exists under the source
`Configuration/` directory, the Visual Studio build copies it beside the
output DLL.
