# Storefront designer (Grow → Storefront)

Agreed 2026-10-08. Prototype: https://claude.ai/artifact/HFtXKfMobxFJ1XUHBWHP5G

A merchant opens Grow → Storefront and sees **Your storefront**: a thumbnail of the home page,
the template and palette in use, and three doors: *Change the look* (template → palette),
*Edit the home page* (a full-page visual editor), *Advanced* (the existing Pages, Menus, Banners,
Collections screens, unchanged routes).

## Decisions (settled, do not reopen)

- Typography never changes. Fraunces headings + system sans stay as docs/10-design-system.md fixes them.
- A template covers the **home page blocks, the header treatment and the footer treatment**. Product and category pages are untouched.
- Platform staff only (`content.content.manage`, `platformOnly`). Sellers get nothing.
- Applying a template **replaces the home page draft**; the last published version stays in version history (rollback already exists).
- Palettes are **curated only** from the eight presets in `libs/util/src/lib/theme-presets.ts`. Each template names 3 or 4. No per-palette tweaking in this flow; the token JSON stays under Advanced for staff.
- Preview is **embedded in the admin**: the admin renders the shared `CmsBlockRenderer` (`libs/ui/patterns`) against the unsaved draft, resolved through a draft-preview API call. No signed storefront preview URL.

## Templates (static catalog, server side)

| id | name | suits | header | footer | palettes | blocks (type, default heading) |
|---|---|---|---|---|---|---|
| loom | Loom | Textiles and soft goods | `centred` | `columns` | sand, terracotta, berry, emerald | Hero (full-width, "Handwoven for the home"), CategoryTiles "Shop by room", ProductCarousel "New this week", Testimonials "What people say", RichText "Made slowly, sent quickly" |
| atelier | Atelier | Furniture, photography-led | `inline` | `slim` | slate, sand, ocean | Hero (split, "Furniture that stays"), BannerGrid, ProductCarousel "Bestsellers", Faq "Good to know" |
| bazaar | Bazaar | Many categories, catalogue-first | `search` | `columnsNewsletter` | coral, indigo, ocean, emerald | CategoryTiles "Browse the shop", BannerGrid, ProductCarousel "Trending now", ProductCarousel "Under ₹999", RichText "Free delivery over ₹1,499" |
| edit | Edit | A small, curated range | `wordmark` | `slim` | slate, violet, terracotta | RichText "Twelve things we would buy again", Hero (split, "The linen edit"), ProductCarousel "The twelve", Testimonials "From the kitchen table" |

Block type keys are the server's (`Hero`, `BannerGrid`, `ProductCarousel`, `CategoryTiles`, `RichText`, `Faq`, `Testimonials`, `CustomHtml`; check `BlockCatalog.cs` for the exact spelling) and each default config must validate against `BlockCatalog`. For the hero's full-width versus split look, use whatever `align`/layout field the catalog already has; if there is none, use `align` and say so in the outcome notes.

**Filling.** When a template is previewed or applied, the server fills reference fields so the page is alive at once: `CategoryTiles` get up to 6 top-level active categories; each `ProductCarousel` gets up to 8 recent active products (the second carousel in Bazaar may reuse the same set); `BannerGrid` gets two banners with a headline and a link to the first two categories and no image. The hero image stays empty (the editor's picker fills it). If the catalogue is empty, reference fields stay empty and the page must still validate.

## API (Content module, `/api/v1/admin`, permission `content.content.manage`)

Implemented. Paths are under `/api/v1/admin` (there is no `/content` segment; pages live at `/admin/pages`).

```
GET  /api/v1/admin/storefront-templates
     -> StorefrontTemplateResponse[]
        { id, name, suits, description, headerStyle, footerStyle, paletteIds: string[] }

GET  /api/v1/admin/storefront-templates/{id}/preview
     -> StorePageResponse   (the template, filled, bound and resolved; NOT persisted)

POST /api/v1/admin/storefront-templates/{id}/apply
     -> PageResponse        (the home page: created if absent (slug "home", PageType.Home),
                             otherwise its draft blocks replaced by the filled template.
                             A published page is not transitioned; only its draft changed.)

POST /api/v1/admin/pages/{id}/preview-draft
     body: { blocks: BlockBody[] }   (the same block shape UpdatePageBody carries)
     -> StorePageResponse   (bound with PageBlockBinder, composed with PageComposer against the
                             page's metadata but THESE blocks; untracked; NOT persisted)
```

Operation names: `adminListStorefrontTemplates`, `adminPreviewStorefrontTemplate`,
`adminApplyStorefrontTemplate`, `adminPreviewDraftPage`. An unknown template id or page id is 404.

**Validation errors are 422, not 400.** A bad block returns HTTP 422 with code `CONTENT_INVALID_BLOCK`
and the field paths in `errors` (for example `blocks[0].imageFileId`), exactly as the page save does.
`preview-draft` returns the same response.

**Lenient save, strict publish.** A draft is allowed to be incomplete: a hero with no picture, a
banner with no image, a carousel that names no products (an empty catalogue). Therefore template
preview and apply, `preview-draft` and the page save (`PUT /admin/pages/{id}`) all bind in draft mode
(`allowIncomplete`), which relaxes only a missing required media reference and the carousel "needs a
collection or products" rule. Every other rule still applies. Going live is strict:
`POST /admin/pages/{id}/transition` to `Published` or `Scheduled` re-validates the page's current blocks
strictly and returns the same 422 shape with the block named, leaving the page where it was. Custom
HTML blocks are skipped by that re-check (they are gated by their own permission and flag at write
time). Rollback binds strictly too.

**Block keys.** The server keys are `Hero`, `BannerGrid`, `ProductCarousel`, `CategoryTiles`,
`RichText`, `Faq`, `Testimonial` (singular, not `Testimonials`) and `CustomHtml`.

**Hero full-width versus split.** The hero has no layout field, only `align` (`left`, `centre`,
`right`). Templates use `align: "centre"` for the full-width hero (Loom) and `align: "left"` for the
split hero (Atelier, Edit). `right` is unused. The storefront and the editor canvas should read it
that way.

## Platform settings (`branding` section, public, part of `/store/config`)

New fields on `BrandingSettings` with defaults that reproduce today's storefront:

```
TemplateId   string   ""           (a catalog id or empty; informational)
HeaderStyle  string   "inline"     one of: inline | centred | search | wordmark
FooterStyle  string   "columns"    one of: columns | slim | columnsNewsletter
```

Validated against those sets. The palette continues to be `ThemeTokens = { "--kh-theme": "<preset id>" }`.

## Storefront

`kh-site-header` gains a `style` input (`HeaderStyle`) and `kh-site-footer` a `style` input
(`FooterStyle`); the shell passes `branding().headerStyle` and `branding().footerStyle`. Treatments:

- header `inline`: today's header, unchanged.
- header `centred`: logo centred on its own row, the menu centred beneath it, search, cart and account at the row ends.
- header `search`: logo left, a prominent search field filling the middle, cart and account right; the menu as a strip beneath.
- header `wordmark`: like `centred` but the store name set as a large uppercase display wordmark (Fraunces, letter-spaced); no logo image even if one is set.
- footer `columns`: today's footer, unchanged.
- footer `slim`: one row: copyright left, the footer menu inline right, social icons after it.
- footer `columnsNewsletter`: today's columns plus a fourth column with an email field and a "Join" button. There is no backend for it; the control is present and disabled with a short hint unless a newsletter mechanism already exists.

All four headers keep the mobile drawer, the announcement bar and the mini-cart exactly as today.

## Admin

Routes (all `platformOnly`, `content.content.manage`, section `Storefront`, category Grow):

```
storefront                 "Your storefront"   (the only visible item in the section)
storefront/templates       hidden   step 1
storefront/palette         hidden   step 2
storefront/editor          hidden   step 3, guardUnsavedChanges
```

Pages, Menus, Banners, Collections move to a new section **"Storefront tools"** in the Grow
category, after Storefront. Paths unchanged. The nav spec that checks every section sits in
exactly one category must be updated.

- **Your storefront**: thumbnail = `CmsBlockRenderer` of `GET /pages/{home}/preview` scaled down, inside a wrapper carrying the chosen preset's tokens; template name from `branding.templateId`, palette from `--kh-theme`, status from the home page. Three door buttons as in the prototype.
- **Templates**: one row per template (name, suits, description left; desktop render middle; phone render right at about 390px in a narrow frame), rendered from `GET /storefront-templates/{id}/preview` through `CmsBlockRenderer`, with the header and footer drawn by `kh-site-header` and `kh-site-footer` in the template's styles. Selecting then *Continue* goes to the palette step. Nothing is written yet.
- **Palette**: the template's curated presets as swatch cards (reuse `ThemePicker` filtered to those ids, or the same card style); the preview re-colours live. *Continue* calls `POST apply`, then PUTs the `branding` section with `{ themeTokens: {"--kh-theme": id}, templateId, headerStyle, footerStyle }` merged over the current section value, then navigates to the editor.
- **Editor**: full-page canvas (desktop and phone toggle) of the home page draft, each block wrapped in a selectable frame with its label; click opens a side panel with that block's fields through the existing `SchemaField` and `ReferenceTags` controls (same as the composer); a `+` between blocks adds from the block library; keyboard: arrow keys move, Delete removes. Text is typed in the side panel (in-canvas contenteditable is a later nicety, not required). The canvas re-renders from `POST /pages/{id}/preview-draft`, debounced about 400 ms after a change, while the local draft stays the source of truth. Save draft and Publish use the existing `updatePage` and transition calls. The old composer remains at `content/pages/:id` for Advanced.
- The canvas wrapper sets the storefront palette tokens inline (`--color-*` from the preset, see `theme-presets.ts`) and `--font-display` to Fraunces if the admin can load the face from the shared primitives assets; otherwise the system face is acceptable inside the canvas.
