# Step 20 — CMS & Merchandising module

> Detail card for the Klara Home implementation plan.
> Tracker: [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) · Parking Lot: [`../PARKING_LOT.md`](../PARKING_LOT.md) · Test debt: [`../TEST_DEBT.md`](../TEST_DEBT.md)

> **MVP BUILD SPRINT STEP.** Build first, test last - see
> [`../IMPLEMENTATION_PLAN.md`](../IMPLEMENTATION_PLAN.md) section 3 before starting. Write
> production code; do not write integration tests. Record every deferred test in
> [`../TEST_DEBT.md`](../TEST_DEBT.md).

- **Phase:** E · **Depends on:** Step 19
- **Objective:** Let the business change the storefront without a deployment.
- **Deliverables:**
  - Block-based page composer (hero, banner grid, product carousel, category tiles, rich text,
    FAQ, testimonial, custom HTML) with typed block schemas.
  - Home page layout, landing pages, static/legal pages, navigation menus, footer builder.
  - Curated collections (manual + rule-based), product ranking/pinning per collection.
  - Banners with scheduling and targeting; announcement bar.
  - Draft → Preview → Scheduled → Published workflow with version history and rollback.
  - SEO: per-page meta, Open Graph, `sitemap.xml`, `robots.txt`, structured data (Product,
    Offer, BreadcrumbList, Organization), 301 redirect manager.
  - Blog/lookbook (optional, feature-flagged).
- **Build acceptance - what closes this step during the sprint:**
  1. Every item under **Deliverables** is written: entities, configuration, handlers, endpoints,
     module registration and DI wiring, and the EF migration for this module.
  2. The code compiles - `dotnet build src/backend/KlaraHome.sln` (backend) or
     `npx nx build <app>` (frontend) succeeds with no errors.
  3. The endpoints are registered and declare their permissions; the module is referenced by the
     host, and the migrator project compiles with the new migration.
  4. Every behaviour named in the full acceptance criteria below that has not been proved has a
     row in [`../TEST_DEBT.md`](../TEST_DEBT.md).
  5. Outcome / Notes filled in, Parking Lot updated, permission requested.
- **Full acceptance criteria (verified at Step 29, not now):** A home page is composed, previewed, scheduled and published; the
  storefront renders it server-side; sitemap and structured data validate.
- **Outcome / Notes:**

---

## Outcome — ✅ DONE (2026-09-06)

The storefront a merchandiser can change without a deployment. One module,
`KlaraHome.Modules.Content`, owning the `content` schema and nine tables; **51 endpoints**, five
permissions, five feature flags, three event subscriptions and two background workers. This module
compiles with **0 warnings**; **910 unit tests green** (102 new). A clean solution rebuild emits
eight warnings in total, all of them the pre-existing ones in `KlaraHome.Modules.Payments` that the
Parking Lot has carried since Step 19 and that **Step 28A** owns.

### What was built

**The page composer.** A page is an ordered list of typed blocks. Eight block types —
hero, banner grid, product carousel, category tiles, rich text, FAQ, testimonial, custom HTML —
declared in `BlockCatalog` as **data**, each with a schema of named fields, kinds, bounds and choices.
Three things read that table: the handler that validates a block, the admin screen that draws the
editor for one (`GET /admin/pages/block-types`), and the OpenAPI document the Angular workspace is
generated from. A discriminated union of C# records would have served the first and neither of the
others.

Configuration is checked **once, on the way in**, and stored canonically: declared fields in schema
order, absent ones as `null`, unknown properties **refused rather than dropped**, rich text sanitised,
numbers stored as numbers. A malformed block is therefore an error beside the field that is wrong, at
the moment an editor pressed save — not a component throwing during server-side rendering. Refusing an
unknown property is the deliberate half: a silently dropped field is a merchandiser who typed
`headLine`, saw no error, and cannot work out why the hero has no heading.

**The workflow.** `Draft → InReview → Scheduled → Published → Unpublished → Archived`, as one
transition table that says both which edges exist and who may take them — the same shape as the order,
shipment and return machines. Three callers read it and therefore cannot disagree: the handler that
moves a page, the scheduler that publishes one, and the admin screen, which draws its buttons from the
`allowedTransitions` the page itself came back with.

Two properties are load-bearing. **An editor cannot publish** — `content.content.manage` takes a page
as far as review and `content.page.publish` takes it further — which is the only thing that makes the
`InReview` state mean anything. And **only the scheduler** may take `Scheduled → Published`; a
publisher who wants it live now publishes it from draft, which is a different edge and a different
line in the audit trail.

**Version history and rollback.** Every publish snapshots the whole page — title, SEO block and blocks
— into an append-only `page_versions` row. A snapshot rather than a diff, because the one operation
this table exists for is restoring a page somebody broke, at four o'clock, quickly. A rollback writes a
**new** version whose content is an old one's, restores the content and *not* the status, and
re-validates the snapshot's blocks on the way in — a version that no longer satisfies the current
schema is refused with the field named rather than restored as a rendered gap.

**Preview** is `GET /admin/pages/{id}/preview`, which renders a page exactly as the storefront would,
whatever its status, optionally at a named version. It is on the admin surface and not the store one,
and that is a security decision: a preview token is a URL that grants access to unpublished content and
gets pasted into chat threads.

**Menus and the footer.** One aggregate. A footer is columns of links, a column is a heading with
children, and that is the two-level menu this already describes. Items carry a **discriminated target**
— page, category, collection or literal URL — rather than a bare href, which is what lets a menu
survive a rename; the target is resolved into a path at render time, and an item whose target has been
deleted or unpublished is dropped along with everything beneath it.

**Banners.** Placement, schedule, priority and a coarse audience (anonymous / signed in / everyone).
The **announcement bar is a banner** with a message and no image, at its own placement, because it
answers the same three questions — what does it say, when does it run, who sees it — and a second table
would have needed its own scheduling, targeting and admin screen to answer them again. `POST
/admin/banners/{id}/active` exists so that taking a campaign down in the next ninety seconds does not
mean sending the whole banner back and passing its validation on the way.

**Collections, and the decision behind them.** A rule-based collection is **materialised into
`collection_items`**, never evaluated on demand — every condition a merchandiser can write is about a
fact in `catalog`, `pricing` or `vendors`, and no query may cross a schema. It is kept current two
ways, and both are necessary: catalogue events re-evaluate every rule for the one product that changed
within seconds, and a periodic sweep rebuilds a whole collection, covering the two cases no event fires
for — a rule that was *edited*, and a "published within N days" condition that stops being true purely
because time passed. `is_from_rule` is what lets the two coexist: a refresh deletes only the rows the
rule wrote, so a merchandiser's pinned three survive every rebuild and survive even after the rule
stops matching them.

**SEO.** `robots.txt` generated from settings rather than shipped as a static asset, because the single
most consequential line in it — `Disallow: /` or not — is a decision an operator occasionally has to
reverse in a hurry. A paginated **sitemap index** across five sections, every `loc` absolute and built
from the configured canonical origin. Per-page meta and Open Graph on the page and the collection. And
the `schema.org` **`@graph`** for one path: Organization, WebSite with its SearchAction, BreadcrumbList
from the real category ancestry, and Product + Offer + AggregateRating whose price and availability come
from the *same* buy-box resolution the product page renders — which is the one SEO mistake with a direct
commercial penalty attached. A **301 redirect manager** with normalised paths, `410 Gone` as a
first-class answer, and a hit counter that tells a merchandiser which of four hundred rules still matter.

**Blog**, behind `content.blog`, which ships off: a page type with a byline, an excerpt, tags and its
own index.

### Files and folders

```
src/backend/modules/KlaraHome.Modules.Content/
  ContentModule.cs
  Domain/            PageLifecycle · BlockCatalog · SeoMetadata · ContentPage (+ ContentBlock,
                     PageVersion, BlockDraft) · Menu (+ MenuItem) · Banner · Collection
                     (+ CollectionItem, CollectionRuleSet) · Redirect
  Application/       ContentContracts · ContentBodies · ContentErrors · ContentQueries ·
                     Validation/ContentFormats · Pages/{PageFeature, PageWorkflowFeature} ·
                     Menus · Banners · Collections · Redirects · Storefront · Seo
  Endpoints/         ContentPermissions · AdminContentEndpoints · StoreContentEndpoints
  Infrastructure/    ContentOptions · ContentScope ·
                     Blocks/{BlockValidator, BlockReferenceReader, HtmlSanitizer, PageBlockBinder} ·
                     Collections/{CollectionRules, CollectionMaterializer} ·
                     Rendering/{ContentRenderer, ContentProjection, PageComposer, MenuComposer} ·
                     Seo/{StorefrontRoutes, RobotsBuilder, SitemapBuilder, StructuredDataBuilder} ·
                     Events/CatalogContentHandlers ·
                     Jobs/{ContentSchedulerWorker, CollectionRefreshWorker} ·
                     Features/ContentFeatures · Persistence/ (+ Configurations, Migrations)
src/backend/tests/KlaraHome.UnitTests/Content/
  PageLifecycleTests · HtmlSanitizerTests · ContentFormatTests ·
  CollectionRuleEvaluatorTests · BlockValidatorTests
docs/adr/ADR-020-materialised-collections-and-declared-block-types.md
```

Touched outside the module: `KlaraHome.Contracts` (two methods on `IProductProjectionSource`, the new
`ICatalogTaxonomy`, the new `SeoSettings` section), Catalog (`CatalogTaxonomyDirectory`, two methods on
`ProductProjectionSource`), Platform (`SettingsCatalog`, `SeoSettingsValidator`), Identity
(`PermissionCatalog`), the solution, the three hosts, `ModuleAssemblies.cs`, both `appsettings.json`
and the unit-test project.

### Shared-layer changes

| Change | Why it could not be avoided |
|---|---|
| `IProductProjectionSource.FindByProductsAsync` | A collection holds **product** ids — that is what a person chooses and what §4.14 stores — and every read-model on this platform is keyed on the **variant**. Doing the translation in the consumer would put a second copy of "which variant represents a product on a card" in the CMS |
| `IProductProjectionSource.FindBySlugAsync` | A crawler asks for a URL and a URL carries a slug, not an id. The alternative was a second contract and a second round trip for the same question |
| `ICatalogTaxonomy` (new) | The sitemap has to list every browsable URL and a breadcrumb has to name the ancestors above a node. Neither is answerable from the search projection, which holds a leaf's name and a path of **ids** but none of the names along it |
| `SeoSettings` (new settings section) | The canonical origin and the indexing switch are facts about a *deployment*, and a staging site that has leaked into an index is a problem measured in minutes. `AllowIndexing` ships **off** |

### Deviations from the specification

1. **`banners` gained a `message` column and an `AnnouncementBar` placement.** §4.14 named neither, and
   the announcement bar was a deliverable with no table. §4.14 rewritten; `CHANGE_LOG.md` updated.
2. **`collection_items` gained `is_from_rule`.** Without it a refresh either deletes an editor's pins or
   accumulates every product that ever matched.
3. **`pages` gained `content_changed_at`, `summary`, `author` and `tags`**, and `page_versions` gained
   `note` and `restored_from`. The first is what `lastmod` carries — a crawler told that every page
   changed because somebody renamed one menu learns to ignore `lastmod` entirely — and the rest are what
   the blog deliverable and a legible history need.
4. **Brands are absent from the sitemap.** They have slugs; the storefront route map has no route that
   serves one, and listing them would put 404s in a sitemap.
5. **`/blog` and `/blog/{slug}` are not in the storefront route map.** Recorded for Step 23/24; the
   sitemap omits the section entirely while the flag is off, so nothing points at a route that does not
   exist.

### Known gaps and technical debt

- **Nothing is proved against a database, a browser or a crawler.** Thirty-three `TEST_DEBT.md` rows,
  including both headline criteria — a home page composed, previewed, scheduled and published, and the
  sitemap and structured data validating.
- **The HTML sanitiser is a regular-expression pass, not an HTML5 parser.** Conservative in the
  direction that matters, and Step 29 owns an adversarial corpus for it. If that corpus finds a hole,
  an allow-list parser is the fix.
- **The custom-HTML block is stored unsanitised, by design.** Guarded by a permission *and* a flag, and
  the flag is re-checked at render time so switching it off is a remedy rather than only a prohibition.
  The `merchandiser` role bundle deliberately does not hold the permission.
- **Rule membership is eventually consistent**, and the event path appends rather than re-sorting; the
  sweep restores the order within thirty minutes.
- **The sitemap's products section walks the catalogue** on every generation, bounded and cached. The
  honest fix, if it ever hurts, is to read the search projection, and it is contained to one class.
- **Ratings are null until Step 21.** `AggregateRating` and the rating on a product card are copied from
  the catalogue, which has no reviews yet — the same gap Step 19 recorded.
- **No storefront renders any of this yet.** Steps 23–25 do, and the headline criterion is not provable
  until they exist.
