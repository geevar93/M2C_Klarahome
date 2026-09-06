# ADR-020 — A rule-based collection is materialised, and a block type is a declared schema

- **Status:** ✅ Accepted
- **Raised at:** Step 20, by the deliverables *"block-based page composer … with typed block
  schemas"* and *"curated collections (manual + rule-based)"*
- **Supersedes:** —
- **Related:** ADR-019 (a projection is the expensive half, and it is not the engine's),
  ADR-016 (Media is its own module, and a `file_id` is resolved rather than joined),
  ADR-003 (the outbox: an event is written in the transaction of the fact it describes)

---

## Context

The CMS is the first module whose *input* is written by somebody who is not a developer and whose
*output* is rendered by a component nobody has seen yet. Two of its deliverables look like ordinary
CRUD and are not, and both were decided before the code was written.

**A block is arbitrary configuration that a compiled component has to render.** The page composer's
promise is that a merchandiser changes the storefront without a deployment. That promise is only
worth anything if the storefront knows in advance how to render whatever it is handed — otherwise
"without a deployment" means "until the first page that renders as a blank space". So the question is
not whether block configuration is open-shaped JSON; it is *where* the shape is decided and *when* it
is checked.

**A rule-based collection is a query over data this module may not query.** Every condition a
merchandiser would want to write — a category, a brand, a price band, a discount, an attribute, "new
in the last thirty days" — is about a fact in the `catalog`, `pricing` or `vendors` schemas, and
`01-architecture.md` §2.1 forbids a query that crosses a schema. There is no version of "evaluate the
rule when the page is rendered" that does not either break that rule or make the home page read the
whole catalogue on every request.

There is a third, smaller question with a disproportionate consequence. One block type —
custom HTML — is arbitrary markup executed in every shopper's browser, which is the textbook shape of
a stored cross-site-scripting vulnerability (`07-security-compliance.md` §3). A CMS without one is a
CMS a marketing team routes around; a CMS with an unguarded one is a liability.

## Decision

### 1. Block types are a closed, declared catalogue, and configuration is validated on the way in

`BlockCatalog` is a table of block types, each with a schema: named fields, each with a kind
(`Text`, `RichText`, `Html`, `Link`, `Integer`, `Boolean`, `MediaRef`, `ProductRef`, `CategoryRef`,
`CollectionRef`, `Choice`), whether it is required, whether it is a list, its bound, and its choices.
Adding a block type is a release — this table and an Angular component, together — and not an
afternoon.

The schema is **data, not a discriminated union of C# records**, because three things read it: the
handler that validates a block, the admin screen that draws the editor for one, and the OpenAPI
document the Angular workspace is generated from. A union serves the first well and the other two not
at all. `GET /admin/pages/block-types` serves the table, so the editor's form and the validator that
judges it cannot drift.

Configuration is checked **once, when it is saved**, and stored canonically: declared fields in schema
order, absent ones written as `null`, unknown properties **refused rather than dropped**, rich text
sanitised, numbers stored as numbers. A malformed block is therefore an error message beside the field
that is wrong, at the moment an editor pressed save — rather than a component throwing during
server-side rendering, which is a blank home page and a stack trace nobody can attribute to whoever
caused it. Refusing an unknown property rather than ignoring it is deliberate: a silently dropped
field is a merchandiser who typed `headLine`, saw no error, and cannot work out why the hero has no
heading.

### 2. Custom HTML is gated by a permission **and** a feature flag, and is deliberately not sanitised

`content.custom-html.write` says *who* may write one. `content.custom-html` says whether this
deployment permits them *at all*. Both are checked, and the flag is checked again at render time — so
turning it off is a remedy and not merely a prohibition on writing new ones.

Its content is stored exactly as written. Sanitising it would make the block useless and leave nothing
that could carry an embed, which is the reason it exists. The control is who may write one, not what
one may contain. Every *other* text field, including rich text, goes through an allow-list sanitiser:
a small set of formatting tags and attributes, no `style`, no `on*` handler, no scheme but `http`,
`https`, `mailto` and `tel`.

The `merchandiser` role bundle gets `content.content.manage`, `content.page.publish`,
`content.redirect.manage` and `content.seo.read`, and does **not** get the custom-HTML permission.

### 3. A rule-based collection is materialised into `collection_items`, never evaluated on demand

The rule is the input; the rows are the answer. A background pass walks the catalogue over
`IProductProjectionSource`, evaluates the rule in memory against each product's winning offer, sorts
what matched, takes the limit, and rewrites the rows the rule owns. The storefront then reads one
indexed table.

It is kept current two ways, and both are necessary:

- **Catalogue events** (`ListingPublished`, `ListingUpdated`, `ListingDeactivated`) re-evaluate every
  rule for the one product that changed, within seconds. This path *appends* rather than re-sorting,
  because getting a new product into the right position means re-evaluating the whole collection — and
  a product at the end of a carousel for half an hour is a far better outcome than one that does not
  appear at all.
- **A periodic sweep** rebuilds a whole collection. It covers the two cases no event will ever fire
  for: a rule that was *edited* (the products have not changed, the rule has), and a "published within
  N days" condition that stops being true purely because time passed. It also restores the order the
  event path let drift.

`collection_items.is_from_rule` is what makes the two coexist: a refresh deletes only the rows the
rule wrote. A merchandiser's pinned three survive every rebuild, and survive even after the rule stops
matching them.

`RuleField` is a **closed** enum, and every value in it is a field `ProductProjection` already
carries. A condition that could name something outside the projection would be a condition nothing
could evaluate, and the failure would be a silently empty collection rather than an error anybody
could act on.

## Consequences

**Good.**

- A merchandiser changes the storefront without a deployment, and the storefront always knows how to
  render what it is handed.
- A bad block is caught at save time, by the person who wrote it, with the field named.
- The home page reads two indexed tables and no catalogue at all. Rule evaluation happens in a worker
  where nobody is waiting for it.
- The admin block editor cannot drift from the validator, because both read one table served over the
  API.
- Custom HTML exists, and needs two deliberate acts by two different kinds of person to reach a
  shopper.

**Bad, and accepted.**

- **A new block type is a release.** Two artefacts have to ship together, and a mismatch between them
  is a rendered gap. Mitigated by serving the schema rather than compiling it into the admin app, so
  at least the editor is never ahead of the backend.
- **Rule membership is eventually consistent**, with a lag bounded by the sweep interval (thirty
  minutes by default) for ordering and by event delivery (seconds) for membership. A merchandiser who
  writes a rule and expects an instantly perfect page gets one — `PUT /admin/collections/{id}/rule`
  evaluates it synchronously — but a product that goes live afterwards arrives at the end of the
  carousel until the next sweep.
- **The full walk is O(catalogue) per rule.** Bounded by `MaxWalkedVariants` and by an early exit once
  the limit is satisfied, and paid in a worker. A store far larger than this platform's target would
  need the walk replaced by a query against the search projection — which is possible without changing
  anything outside `CollectionMaterializer`, and is deliberately not built now.
- **The custom-HTML block is a real, permanent risk surface.** It is guarded, logged and switchable,
  and it is still arbitrary markup. Step 29 owns an adversarial test suite for the sanitiser; nothing
  can make the unsanitised block safe except restraint about who holds the permission.
