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
- Applying a template **replaces the home page's blocks**. A published page has no separate draft (its blocks are what the storefront serves, as the composer's "Save & update live site" already says), so on a published home page the template goes live at once; the previous published snapshot stays in version history and can be rolled back. On a draft page nothing reaches shoppers until publish. The palette step's warning says which of the two applies.
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

## Template review (2026-10-08, after the first build)

Design read: four home page templates for a premium DTC home-goods store in India, trust-first commerce. Typography (Fraunces display, system sans) and the eight palettes are fixed brand decisions and are not under review. What a template may vary: block arrangement, block configuration, header and footer treatment. Reviewed on the live storefront at 1366 and 390 wide, screenshots in the session scratchpad under `shots/templates/`.

### What is wrong

1. **Every hero is the same dark slab.** With no picture, the hero renders as a 300px block of ink with text on it, in all four templates. Loom's "full-width" hero and Atelier's "split" hero differ only in text alignment, and the "full-width" one is an inset rounded card, not full width. The template gallery promises four different openers and delivers one.
2. **Shoppers see "No image" placeholders.** Atelier and Bazaar ship a BannerGrid whose two tiles are 400px tall grey boxes with a broken-image glyph and the words "No image", directly under the hero. That is an admin-only state leaking onto the public site.
3. **Category tiles are blank white cards.** "Shop by room" / "Browse the shop" draws two text-only white boxes in a tinted band, filling a third of the row. It reads unfinished rather than minimal.
4. **The product carousel is not a carousel and ends ragged.** The block wraps as a grid; eight products at six per row leave an orphan row of two on every template. Bazaar stacks two identical rails ("Trending now" and "Under ₹999" show the same eight products).
5. **Grids do not match their content.** Edit's testimonials put two quotes in a three-column grid; Loom's tiles put two tiles in a row sized for three or more. A grid has exactly as many cells as it has content.
6. **Rich text floats.** "Made slowly, sent quickly" and Edit's opener are a left-aligned paragraph inside a centred 52ch column, so the text sits at an odd offset with uneven margins. Either centre the column and its text (Loom, centred language) or align it to the page gutter (Edit, editorial language).
7. **Band rhythm is accidental.** Some sections sit in a tinted rounded band (tiles, testimonials, FAQ), others on the bare page (carousel, rich text), in an order that depends on which blocks happened to land next to each other. Two bands never touch today only by luck.
8. **The newsletter footer breaks its own grid.** At desktop the Newsletter column wraps under the five link columns as a lone row with a 220px input and a disabled Join button; it was meant to be a column.
9. **Template identity is only header deep.** Loom, Atelier, Bazaar and Edit share identical block styling and density. Beyond the opener and the header, a shopper could not tell them apart.

### What to change

Frontend (`libs/ui/patterns`, storefront, admin canvas follows automatically):
- **Hero**: `align=centre` renders full-bleed (edge to edge, no card radius, no inset) with the picture as the ground and the scrim as today; `align=left` renders a split card: copy on the left, picture on the right at 4:5. Without a picture, a hero is at most 240px tall at desktop, sits on `--color-surface` (not ink), uses ink text and a primary CTA. Never a 300px ink slab with nothing in it.
- **Banner grid and category tiles without an image**: a tinted tile (`--color-surface-sunken`) with the caption set in the display face inside it, no glyph, no "No image" text. The "No image" wording belongs in the admin editor only (the canvas may label it).
- **Category tiles**: when the tile has an image (or an image override) show it; the grid is `auto-fit` with a minimum so two tiles fill the row at desktop as two halves, not as two-thirds.
- **Product carousel**: at `md` and above it is a true horizontal scroll-snap rail with a visible "View all" and no orphan row; on phone it stays a two-column grid of at most the first four, then "View all". Honour the block's `limit`.
- **Testimonials**: grid is `auto-fit` so two quotes become two columns; three stay three.
- **Rich text**: a `centred` presentation (centre the column and its text) and a `start` presentation (page gutter, 65ch). Pick per block via its existing `align`-style field if the catalog has one; otherwise add one to the block schema and default to `start`.
- **Section bands**: a band gets its tint from the block type as today, but two bands in a row collapse their shared gap so bands read as one surface; a band never sits directly under the hero with a page-ground gap of less than one section gap.
- **Newsletter footer**: the newsletter is a full-width row above the copyright line, email field and Join inline, not a sixth column.

Backend (`StorefrontTemplateFiller`, `StorefrontTemplateCatalog`):
- **Hero picture**: fill the hero's `imageFileId` with the first product image from the catalogue (newest product with an image) so no template opens with an empty hero; the merchant replaces it in the editor.
- **Banner grid pictures**: each banner takes the first product image found in its category; if none, the banner is dropped rather than shipped empty. A grid with one banner becomes a single full-width banner.
- **Category tile pictures**: use the category's image when it has one, else the image override to the first product image in that category (the `ReadCategoryImageOverrides` mechanism already exists).
- **Carousels**: `limit` per template (Atelier 4 large, Loom 6, Bazaar 6, Edit 6) and the filler respects it. Bazaar's "Under ₹999" is filled with products whose price is at or under ₹999, newest first, and is dropped from the applied page if fewer than 3 qualify. Two carousels on one template never share the same set when the catalogue has enough products.
- **Testimonials**: Edit ships three quotes, not two.

Identity per template (the point of having four):
- **Loom**: full-bleed hero, tiles in a band, carousel of 6, testimonials in a band, centred rich text. Centred header, columns footer.
- **Atelier**: split hero, one full-width banner (the biggest category) then one half banner pair only if images exist, carousel of 4 large cards, FAQ. Inline header, slim footer. Section gap one step larger than the others.
- **Bazaar**: no hero; tiles first (up to 6), banner pair, two distinct carousels of 6, delivery promise as start-aligned rich text. Search header, columns-with-newsletter footer. Section gap one step smaller.
- **Edit**: start-aligned editorial opener (65ch at the gutter), split hero, carousel of 6, three testimonials. Wordmark header, slim footer.

### Second pass (same day, after the first fixes)

Seen on the live storefront after the first round. The four pages now differ in opener, rhythm and density, and nothing says "No image" any more. What remains:

1. **Product shots are the wrong pictures for heroes, tiles and banners.** The filler takes the newest product image, which is a small square studio shot with the product name printed along its bottom edge. Blown up to a full-bleed hero it is blurry and its caption strip shows through the scrim on a phone; as a 4:5 split-hero picture its caption reads as a second headline; in tiles and banners the product's name is bigger than the category's. The demo seed already has a wide hero picture and a picture per category (`DemoMediaCatalogue`, keys `demo-hero` and `demo-category-<slug>`, which `DemoHomePageSeeder` already looks up). A real store has the same shapes: the existing home page's hero picture and the category's own picture.
2. **Pictures do not fill their tiles.** Category tiles and banners draw the image at its intrinsic width centred in a tinted box; the single full-width banner in Atelier becomes a 1000px-tall image.
3. **Bazaar shows the same two pictures twice in a row.** The tiles and the banner pair both resolve to the same two categories and the same two product shots.
4. **Rail cards change size between rails on one page.** The rail fills the row exactly, so Bazaar's six-card rail and its four-card rail have different card widths.
5. **The split hero is mostly empty panel.** The picture column is a fixed 20rem, so at desktop the copy column is a wide flat panel with a small headline floating in it.

Fixes:

Frontend:
- Tiles and banners: the picture fills the tile (`object-fit: cover`), tiles at 4:3, a banner pair at 3:2, a single full-width banner at 21:9 capped at 420px on desktop and 4:3 on a phone. The category or banner caption is the primary label, set in the display face at the card-title size, below the picture; no secondary tiny label.
- Product rail: the card width comes from the block's `limit` (4 or fewer means four across, otherwise six across at desktop), not from the number of cards; fewer cards leave space on the right. Phone unchanged.
- Split hero: picture column 42% at desktop, copy column uses the display size one step larger than today, copy max-width 36ch, vertically centred. Phone unchanged.
- Full-bleed hero on a phone: the scrim under the copy goes opaque enough that nothing of the picture shows through the text block (today the picture's bottom edge shows behind the CTA).

Backend (`StorefrontTemplateFiller`):
- Hero picture, in order: the current home page's hero picture if the page already has a hero; else the media file the demo seeder calls `demo-hero` (reuse the same lookup the seeder uses); else the newest product picture.
- Tile picture, in order: the category's own picture; else the current home page's tile override for that category; else `demo-category-<slug>`; else the first product picture in the category.
- Banner picture: the category's own picture; else `demo-category-<slug>`; else the second product picture in the category, so a banner never repeats the tile beside it.
- A banner grid skips categories already shown by a CategoryTiles block on the same template; if none remain it is dropped. (Bazaar with two categories therefore has tiles and no banners; Atelier, which has no tiles, keeps its banner.)
