# Data Protection

Do not commit clinical data, patient identifiers, credentials, local server secrets, or generated clinical outputs.

Never commit:

- DICOM, RTSTRUCT, RTPLAN, RTDOSE, NIfTI, MHA, or NRRD files.
- `.env`, local YAML/JSON config files, passwords, tokens, or ARIA connection details.
- `AppPaths.local.json`, `USZ_ARTEMIS_Preprocessing.local.config`, or other deployment config files with real server paths.
- Generated exports, logs, screenshots with identifiers, temporary folders, or build outputs.
- Proprietary Varian ESAPI DLLs.

Use anonymized fixtures only when test data is needed. Keep clinical deployment paths in local configuration or documented deployment procedures, not in reusable test data.
