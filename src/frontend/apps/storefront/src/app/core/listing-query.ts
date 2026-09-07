import { Params } from '@angular/router';
import { FacetGroup, ProductSearchParams } from '@klarahome/data-access-catalog';
import { AppliedFilterView, FacetGroupView, SortOptionView } from '@klarahome/ui-patterns';

/**
 * The listing page's state, and its round trip through the URL.
 *
 * **The URL is the state.** Not a copy of it, not a projection of it — the filters, the sort and
 * the query live in the query string and nowhere else, so a shopper can share a filtered listing,
 * open one in a new tab, and use the back button to undo a filter. Everything here exists to make
 * that round trip exact.
 *
 * The **cursor is deliberately not in the URL.** Paging is keyset (Step 19), and a link carrying
 * `?cursor=` would open somebody else's page four with no page one above it. "Load more" is
 * component state; what a link carries is the query that produced the list.
 *
 * The facet keys the API answers with are not all the parameter names it accepts — `availability`
 * is written `inStock`, and a `price` band becomes `minPrice`/`maxPrice`. `applyFacet` is where
 * those two vocabularies are reconciled, once, rather than in every control that touches a filter.
 */

/** The facet keys the search projection produces (Step 19's `SearchSqlBuilder`). */
export const FACET_KEYS = {
  brand: 'brand',
  category: 'category',
  vendor: 'vendor',
  rating: 'rating',
  discount: 'discount',
  availability: 'availability',
  price: 'price',
} as const;

/** The prefix an attribute facet and an attribute filter both carry. */
export const ATTRIBUTE_PREFIX = 'attr.';

/** The orders the API accepts (`SearchSorts`), with the words a shopper reads. */
export const SORT_OPTIONS: readonly SortOptionView[] = [
  { value: 'relevance', label: 'Relevance' },
  { value: 'popularity', label: 'Most popular' },
  { value: 'newest', label: 'Newest first' },
  { value: 'price-asc', label: 'Price: low to high' },
  { value: 'price-desc', label: 'Price: high to low' },
  { value: 'discount', label: 'Biggest discount' },
  { value: 'rating', label: 'Best rated' },
];

const DEFAULT_SORT = 'relevance';

/** Reads a parameter that may be repeated or comma-separated, as the API accepts both. */
function list(value: string | readonly string[] | undefined): string[] {
  if (value === undefined) return [];
  const raw = Array.isArray(value) ? value : [value as string];
  return raw
    .flatMap((entry) => `${entry}`.split(','))
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}

function number(value: unknown): number | null {
  const parsed = Number.parseFloat(`${value ?? ''}`);
  return Number.isFinite(parsed) ? parsed : null;
}

/**
 * The search parameters a URL means.
 *
 * Unknown parameters are ignored rather than refused: a URL a shopper hand-edited, or one carrying
 * a campaign tag, should return a slightly different list and not an error page.
 */
export function fromQueryParams(params: Params, category?: string | null): ProductSearchParams {
  const attributes: Record<string, string[]> = {};

  for (const [key, value] of Object.entries(params)) {
    if (!key.startsWith(ATTRIBUTE_PREFIX)) continue;
    const code = key.slice(ATTRIBUTE_PREFIX.length);
    const values = list(value as string | string[]);
    if (code && values.length > 0) attributes[code] = values;
  }

  return {
    q: (params['q'] as string | undefined) ?? null,
    category: category ?? (params['category'] as string | undefined) ?? null,
    brand: list(params['brand'] as string | string[]),
    vendor: list(params['vendor'] as string | string[]),
    minPrice: number(params['minPrice']),
    maxPrice: number(params['maxPrice']),
    rating: number(params['rating']),
    discount: number(params['discount']),
    inStock: params['inStock'] === '1' || params['inStock'] === 'true',
    attributes,
    sort: (params['sort'] as string | undefined) ?? null,
  };
}

/**
 * The query string these parameters mean.
 *
 * A parameter at its default is **absent**, not written out: `?sort=relevance&inStock=false` is
 * the same page as `/`, and two URLs for one page is a duplicate in an index and a cache miss at
 * the edge. Lists are comma-joined, which is shorter than repeating the key and is one of the two
 * spellings the endpoint accepts.
 *
 * The category is carried by the *path* on a category page, so it is never written here — passing
 * it as a query parameter as well would produce `/c/lighting?category=<uuid>`.
 */
export function toQueryParams(params: ProductSearchParams): Params {
  const query: Params = {};

  if (params.q?.trim()) query['q'] = params.q.trim();
  if (params.brand?.length) query['brand'] = params.brand.join(',');
  if (params.vendor?.length) query['vendor'] = params.vendor.join(',');
  if (params.minPrice !== null && params.minPrice !== undefined) query['minPrice'] = params.minPrice;
  if (params.maxPrice !== null && params.maxPrice !== undefined) query['maxPrice'] = params.maxPrice;
  if (params.rating) query['rating'] = params.rating;
  if (params.discount) query['discount'] = params.discount;
  if (params.inStock) query['inStock'] = '1';
  if (params.sort && params.sort !== DEFAULT_SORT) query['sort'] = params.sort;

  for (const [code, values] of Object.entries(params.attributes ?? {})) {
    if (values.length > 0) query[`${ATTRIBUTE_PREFIX}${code}`] = values.join(',');
  }

  return query;
}

/** Adds or removes one value from a multi-valued filter. */
function toggleValue(current: readonly string[] | undefined, value: string, selected: boolean): string[] {
  const values = current ?? [];
  if (selected) return values.includes(value) ? [...values] : [...values, value];
  return values.filter((entry) => entry !== value);
}

/**
 * Applies one facet toggle.
 *
 * This is where the facet vocabulary meets the filter vocabulary. Four of the seven groups are
 * ordinary multi-valued filters; three are not, and each is a deliberate decision:
 *
 *  - **`price`** is a band whose bounds arrive on the facet value (`from`/`to`), so selecting one
 *    writes `minPrice`/`maxPrice` and selecting another *replaces* them. Two bands at once would
 *    mean an interval the API cannot express.
 *  - **`rating`** and **`discount`** are thresholds — "4 & up" — so they replace rather than
 *    accumulate, and toggling the selected one off clears the filter.
 *  - **`availability`** is a single boolean spelled `inStock`.
 */
export function applyFacet(
  params: ProductSearchParams,
  key: string,
  value: string,
  selected: boolean,
  bounds?: { readonly from?: number | null; readonly to?: number | null },
): ProductSearchParams {
  if (key.startsWith(ATTRIBUTE_PREFIX)) {
    const code = key.slice(ATTRIBUTE_PREFIX.length);
    const attributes = { ...(params.attributes ?? {}) };
    const values = toggleValue(attributes[code], value, selected);
    if (values.length > 0) attributes[code] = values;
    else delete attributes[code];
    return { ...params, attributes };
  }

  switch (key) {
    case FACET_KEYS.brand:
      return { ...params, brand: toggleValue(params.brand, value, selected) };
    case FACET_KEYS.vendor:
      return { ...params, vendor: toggleValue(params.vendor, value, selected) };
    case FACET_KEYS.category:
      return { ...params, category: selected ? value : null };
    case FACET_KEYS.rating:
      return { ...params, rating: selected ? number(value) : null };
    case FACET_KEYS.discount:
      return { ...params, discount: selected ? number(value) : null };
    case FACET_KEYS.availability:
      return { ...params, inStock: selected };
    case FACET_KEYS.price:
      return selected
        ? { ...params, minPrice: bounds?.from ?? null, maxPrice: bounds?.to ?? null }
        : { ...params, minPrice: null, maxPrice: null };
    default:
      // A facet key this build does not know about. Ignored rather than guessed at — the panel
      // will simply not reflect it, which is better than writing a parameter the API refuses.
      return params;
  }
}

/** Whether a facet value is currently applied. The panel's tick comes from here. */
export function isFacetSelected(
  params: ProductSearchParams,
  key: string,
  value: string,
  from?: number | null,
): boolean {
  if (key.startsWith(ATTRIBUTE_PREFIX)) {
    return (params.attributes?.[key.slice(ATTRIBUTE_PREFIX.length)] ?? []).includes(value);
  }

  switch (key) {
    case FACET_KEYS.brand:
      return (params.brand ?? []).includes(value);
    case FACET_KEYS.vendor:
      return (params.vendor ?? []).includes(value);
    case FACET_KEYS.category:
      return params.category === value;
    case FACET_KEYS.rating:
      return params.rating === number(value);
    case FACET_KEYS.discount:
      return params.discount === number(value);
    case FACET_KEYS.availability:
      return params.inStock === true;
    case FACET_KEYS.price:
      // A band is identified by its lower bound: the labels are generated and could change, the
      // bounds are the filter itself.
      return params.minPrice !== null && params.minPrice === (from ?? null);
    default:
      return false;
  }
}

/** The API's facets, with this URL's selections marked. */
export function toFacetGroups(facets: readonly FacetGroup[], params: ProductSearchParams): FacetGroupView[] {
  return facets.map((facet) => {
    const values = facet.values.map((value) => ({
      value: value.value,
      label: value.label,
      count: value.count,
      selected: isFacetSelected(params, facet.key, value.value, value.from),
    }));

    return {
      key: facet.key,
      label: facet.label,
      values,
      selectedCount: values.filter((value) => value.selected).length,
    };
  });
}

/**
 * The filters that are on, as removable chips.
 *
 * Derived from the *facets* rather than from the parameters, so every chip carries the label the
 * panel used — a brand chip reading "Brand: 7f3c…" would be a filter nobody can identify. A filter
 * whose facet is no longer in the response (because applying it narrowed the set that produces the
 * facets) falls back to the raw value, which is rare and still removable.
 */
export function appliedFilters(
  params: ProductSearchParams,
  facets: readonly FacetGroupView[],
): AppliedFilterView[] {
  const applied: AppliedFilterView[] = [];

  for (const group of facets) {
    for (const value of group.values) {
      if (value.selected) {
        applied.push({ key: group.key, value: value.value, label: `${group.label}: ${value.label}` });
      }
    }
  }

  const known = new Set(applied.map((filter) => `${filter.key}:${filter.value}`));

  // The three filters that can be set without a facet value being present — a hand-edited URL, or
  // a price band that this result set no longer offers.
  if (params.inStock && !known.has(`${FACET_KEYS.availability}:in-stock`)) {
    applied.push({ key: FACET_KEYS.availability, value: 'in-stock', label: 'In stock' });
  }
  if (
    (params.minPrice !== null && params.minPrice !== undefined) ||
    (params.maxPrice !== null && params.maxPrice !== undefined)
  ) {
    if (![...known].some((entry) => entry.startsWith(`${FACET_KEYS.price}:`))) {
      applied.push({ key: FACET_KEYS.price, value: 'custom', label: priceLabel(params) });
    }
  }
  if (params.rating && !applied.some((filter) => filter.key === FACET_KEYS.rating)) {
    applied.push({ key: FACET_KEYS.rating, value: `${params.rating}`, label: `${params.rating} & up` });
  }

  return applied;
}

function priceLabel(params: ProductSearchParams): string {
  const from = params.minPrice;
  const to = params.maxPrice;
  if (from !== null && from !== undefined && to !== null && to !== undefined) return `₹${from} – ₹${to}`;
  if (from !== null && from !== undefined) return `₹${from} and above`;
  return `Up to ₹${to}`;
}

/** Clears every filter, keeping the query, the sort and the category the page is for. */
export function clearFilters(params: ProductSearchParams): ProductSearchParams {
  return { q: params.q, category: params.category, sort: params.sort, attributes: {}, brand: [], vendor: [] };
}
