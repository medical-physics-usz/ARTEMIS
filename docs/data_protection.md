# Data Protection

Do not commit clinical data, patient identifiers, credentials, local server secrets, or generated clinical outputs.

Never commit:

- DICOM files.
- `.env`, local config files, passwords, tokens, or ARIA connection details.
- Version-matched ARTEMIS ESAPI configuration files such as
  `USZ_ARTEMIS_v26.7.20.2.esapi.json`,
  `USZ_ARTEMIS_Preprocessing.local.config`, or other config files with real
  server paths.
- Generated logs.
