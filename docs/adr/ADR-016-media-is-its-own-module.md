# ADR-016 — Media is its own module, with its own `media` schema

- **Status:** ✅ Accepted
- **Raised at:** Step 8 (Media, file storage & Notifications module)
- **Supersedes:** —
- **Related:** ADR-002 (schema per module), ADR-010 (MinIO with the S3 API), ADR-013 (the one
  sanctioned cross-schema write)

---

## Context

Six specified tables reference a stored file by id and none of them says what it points at:
`vendor_kyc_documents.file_id`, `categories.image_file_id`, `brands.logo_file_id`,
`product_media.file_id`, `invoices.file_id`, `shipments.label_file_id`,
`banners.media_file_id`, `credit_notes.file_id`, `returns.evidence_file_ids`. `BrandingSettings`
carries `LogoRef`, `LogoDarkRef` and `FaviconRef` with a comment saying the Media module resolves
them from Step 8.

There is no Media module. `01-architecture.md` §4.1 lists seventeen modules and none of them is it;
`02-domain-model.md` §2 assigns file storage to no bounded context; `03-database-design.md` §2 has
no `media` schema. `08-integrations.md` §4 specifies the `IFileStorage` interface and the two
buckets, which is the *storage* half — not the registry that says a given file exists, what it
contains, whether it has been scanned, and who may see it.

So the file registry has to be given a home before anything can reference it. Three were available.

| Option | Assessment |
|---|---|
| **`platform.files`, owned by the Platform module** | Smallest documentation change: Platform already owns the cross-cutting tables, and no module list changes. Rejected because Platform's stated boundary is "tenant, settings, feature flags, audit, reference data — *anything transactional* excluded", and a file registry is neither configuration nor reference data. It has behaviour of its own — validation, scanning, derivative URLs, signed access, deletion — which would arrive as a second personality inside the module every other module already depends on. |
| **A shared service with no tables, files identified by their storage key** | Tempting: no schema, no module, no migration. Rejected because the interesting questions are not answerable from a key. "Has this been virus-scanned", "what are its dimensions", "is it public or private", "who uploaded it", "is it still referenced" are registry questions, and a bucket listing is a poor database. |
| **A Media module owning a `media` schema** | The choice. |

## Decision

**`KlaraHome.Modules.Media` is added as the eighteenth module and owns the `media` schema.**

It holds one table at Step 8 — `media.files` — and the behaviour around it: MIME and magic-number
validation, size and dimension limits, checksum, the virus-scan seam, imgproxy derivative URLs for
public images, signed short-lived URLs for private documents, and the document-rendering pipeline
that produces invoices and labels into `docs-private`.

Other modules never read `media.files`. They store a file id and resolve it through
`IMediaLibrary` in `KlaraHome.Contracts` — exactly as they read settings through `IStoreSettings`
and write audit entries through `IAuditLogger`. The eight columns listed above are therefore
**soft references**: no foreign key crosses a schema, as §2.1 requires, and a file id that has been
deleted resolves to nothing rather than breaking a join.

`01-architecture.md` §4.1, `02-domain-model.md` §2 and `03-database-design.md` §2 and §4 are
amended to include it.

## Why not simply infrastructure

`IFileStorage` **is** infrastructure and stays there: it is a thin S3 wrapper with no knowledge of
this business, and the migration path in `08-integrations.md` §4 — MinIO today, S3 or R2 later —
depends on it staying that thin. The Media module is what sits *on top*: it is the thing that knows
a 12 MB TIFF is not an acceptable product image, that a KYC document must never be served from a
public bucket, and that a logo has four sensible renditions. That is domain knowledge, and domain
knowledge in the shared infrastructure layer is how the shared layer becomes the place everything
ends up.

## Consequences

- **Good:** the boundary rules apply to media without an exception being written for it, and the
  architecture tests pick the new module up with no edit — they discover modules from the build
  output.
- **Good:** deleting a file, quarantining a file, or re-scanning every file is one module's job and
  has one place to live.
- **Cost:** an eighteenth module, an eighteenth schema, and a third specification document to keep
  current. Recorded here so the addition is a decision rather than a drift.
- **Cost:** a stored file has no referential integrity behind it. A file deleted while a product
  still points at it leaves a dangling id. Deletion is therefore soft by default and the registry
  keeps the row; a reference-counting sweep belongs with the other housekeeping jobs at Step 31.
- **Watch:** the temptation to let a later module reach into `media.files` for "just one join" —
  a listing page wanting a thumbnail URL, say. `IMediaLibrary` resolves ids in batches for exactly
  that case, and the architecture tests fail a cross-module project reference outright.
