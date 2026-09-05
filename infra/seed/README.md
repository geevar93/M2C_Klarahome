# Operator-supplied reference data

Data the product needs but does not ship. This directory is mounted read-only into the migrator
container at `/app/seed`, so a file dropped here is imported by the next migrator run.

Nothing here is committed except this file: the datasets are large, they change without notice from
their publishers, and a stale copy baked into a release is worse than an empty table an operator
knows to fill.

## `pincodes.csv` — India Post PIN codes

Roughly nineteen thousand rows. Imported by `Platform.Pincodes` when
`Platform__PincodeDataPath=/app/seed/pincodes.csv` (`PINCODE_DATA_PATH` in `.env`).

Format — a header row, then one row per PIN code:

```csv
pincode,city,district,stateCode,zone
400001,Mumbai,Mumbai City,27,West
110001,New Delhi,New Delhi,07,North
```

| Column | Meaning |
|---|---|
| `pincode` | Six digits, no leading zero |
| `city` | City or town the code serves |
| `district` | Revenue district |
| `stateCode` | Two-digit **GST state code**, not the postal circle. See `platform.states` |
| `zone` | Logistics zone. Free text; the Shipping module bands rates on it from Step 16 |

The import is idempotent: a row is matched on its PIN code and updated in place, so re-running the
migrator after refreshing the file corrects the data rather than duplicating it. Rows whose
`stateCode` is not a jurisdiction in `platform.states` are counted and skipped — the migrator logs
how many — so one bad row cannot fail a deploy.

Until the file exists, keep the `platform.pincode-lookup` feature flag off:

```
PUT /api/v1/admin/feature-flags/platform.pincode-lookup   { "enabled": false }
```

Otherwise `GET /api/v1/store/pincodes/{pincode}` answers `PINCODE_NOT_FOUND` to every valid code,
which looks like a bug rather than missing data.
