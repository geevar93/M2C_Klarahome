# ADR-015 — PDFsharp + MigraDoc for generated documents, not QuestPDF

- **Status:** ✅ Accepted
- **Raised at:** Step 8 (Media, file storage & Notifications module)
- **Supersedes:** the `PDF | QuestPDF` row of `01-architecture.md` §7
- **Related:** ADR-009 (avoid commercially licensed libraries), ADR-011/012 (the same question,
  answered for the test stack)

---

## Context

`01-architecture.md` §7 names **QuestPDF** for invoices, credit notes and manifests. ADR-009,
accepted at Step 0, says this product does not take a dependency that carries a commercial licence
for commercial use — the reason MediatR and AutoMapper were ruled out and an in-house dispatcher
was written instead.

QuestPDF is dual-licensed. Its **Community** licence is free, but only for organisations under
**USD 1 M annual gross revenue**; above that threshold a paid licence is required, and the library
enforces the choice through a `QuestPDF.Settings.License` assignment the developer makes.

For a single application that would be a footnote. This one is **redistributable**: the whole point
of the tenancy and settings work at Step 6 is that a second business is onboarded by changing
configuration, not by rebuilding the product. A licence whose applicability depends on the revenue
of whoever the deployment belongs to is therefore not a decision we can take once — it is an
obligation that travels with every copy, and that somebody has to re-check for each client.

The client this is first built for is comfortably under the threshold. That is not the question.
The question is what a redistributed copy is permitted to do without anyone reading a licence.

### Options considered

| Option | Licence | Assessment |
|---|---|---|
| **QuestPDF** | Dual: Community (< USD 1 M revenue) / commercial | The nicest API in .NET for this job, and the spec's original choice. Rejected: the obligation is per-deployment, invisible in code, and directly contradicts ADR-009. |
| **PDFsharp 6 + MigraDoc** | **MIT** | Same repository, same maintainers, MIT since 6.0. MigraDoc supplies a document model — sections, paragraphs, tables with repeating headers, page numbering — which is what an invoice actually needs; PDFsharp renders it. More verbose than QuestPDF's fluent API. |
| **iText 7** | AGPL / commercial | AGPL is worse than QuestPDF's arrangement for a redistributed product, not better. |
| **Headless Chromium (HTML → PDF)** | Free | Reuses the notification templating for layout, which is genuinely attractive. Rejected for Step 8: it adds a browser container to every deployment, roughly 400 MB, for three documents — and a rendering pipeline whose output depends on a browser version is a poor fit for a *statutory* document like a GST invoice. |
| **A hand-rolled PDF writer** | — | Not seriously considered. Tables, fonts, page breaks and PDF/A are a project, not a file. |

## Decision

**Generated documents are rendered with PDFsharp 6 and MigraDoc, both MIT.**

`01-architecture.md` §7 is amended to name them. QuestPDF is not used, and the licensing guard
comment in `Directory.Packages.props` gains it as a third named example, alongside MediatR and
AutoMapper, so the next person to reach for it finds the answer where they are working rather than
in this file.

The renderer sits behind `IDocumentRenderer` in `KlaraHome.Infrastructure`, taking a
document model this codebase owns and returning bytes. No MigraDoc type appears in a module, in a
domain model or in an endpoint — the same rule every other integration follows
(`08-integrations.md` preamble). Swapping the renderer later, including for the headless-browser
option if the design ambitions of Step 30 demand it, is one class.

## Consequences

- **Good:** no licence obligation travels with a redistributed deployment, which is what ADR-009
  exists to guarantee.
- **Good:** MigraDoc's table model — repeating header rows, keep-with-next, automatic page breaks —
  is exactly the shape of an invoice, and is the part that would be most tedious to write against a
  raw PDF API.
- **Cost:** more code per document than QuestPDF would need, and a document model that has to be
  designed rather than expressed inline. Step 8 pays that cost once by building the model; Steps 14,
  16 and 17 fill it in.
- **Cost:** MigraDoc's API is older in style — `Document`, `Section`, `Row`, `Cell` — and reads
  less well than a fluent builder. The wrapper is what the rest of the codebase sees.
- **Watch:** fonts. PDFsharp needs a font resolver on Linux containers, where the framework fonts a
  Windows developer takes for granted are absent. The renderer registers an explicit resolver over
  fonts embedded in the assembly, so a document renders identically on a developer's machine and in
  the container — a difference that would otherwise appear only in production, in a statutory
  document.
