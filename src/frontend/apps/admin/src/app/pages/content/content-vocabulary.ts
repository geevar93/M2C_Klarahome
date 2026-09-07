/**
 * The words the content endpoints accept.
 *
 * Read off `KlaraHome.Modules.Content`'s own enums — `PageType`, `PageStatus`, `BannerPlacement`,
 * `BannerAudience`, `MenuLinkType`, `CollectionKind`, `RuleField`, `RuleOperator`,
 * `CollectionSort` — and typed as plain `string` in the OpenAPI document, so nothing here is
 * checked by a compiler. The same gap the Step 24, 26 and 27 parking-lot rows describe, recorded
 * again for this step.
 *
 * The exception, and it is the important one: **block types are not in this file.** Their schemas
 * are served by `GET /admin/content/block-types` and the composer builds its form from them, so a
 * block type added on the server needs no change here. That is what the rest of this file would
 * look like if the contract typed these too.
 */

export interface Choice {
  readonly value: string;
  readonly label: string;
  readonly hint?: string;
}

export const PAGE_TYPES: readonly Choice[] = [
  { value: 'Landing', label: 'Landing page', hint: 'A campaign page with its own URL.' },
  { value: 'Static', label: 'Static page', hint: 'About us, delivery, contact.' },
  { value: 'Legal', label: 'Legal page', hint: 'Terms, privacy, returns policy.' },
  { value: 'Blog', label: 'Blog post', hint: 'Listed on the journal, and tagged.' },
  { value: 'Home', label: 'Home page', hint: 'There is one, and it cannot be deleted.' },
];

export const PAGE_STATUSES: readonly Choice[] = [
  { value: 'Draft', label: 'Draft' },
  { value: 'InReview', label: 'In review' },
  { value: 'Scheduled', label: 'Scheduled' },
  { value: 'Published', label: 'Published' },
  { value: 'Unpublished', label: 'Unpublished' },
  { value: 'Archived', label: 'Archived' },
];

/** What each transition means, in the words of the person taking it. */
export const TRANSITION_LABELS: Readonly<Record<string, string>> = {
  Draft: 'Back to draft',
  InReview: 'Send for review',
  Scheduled: 'Schedule',
  Published: 'Publish now',
  Unpublished: 'Take down',
  Archived: 'Archive',
};

export const BANNER_PLACEMENTS: readonly Choice[] = [
  { value: 'AnnouncementBar', label: 'Announcement bar', hint: 'The strip above the header.' },
  { value: 'HomeHero', label: 'Home hero' },
  { value: 'HomeStrip', label: 'Home strip' },
  { value: 'CategoryHeader', label: 'Category header' },
  { value: 'ListingSidebar', label: 'Listing sidebar' },
  { value: 'ProductStrip', label: 'Product page strip' },
  { value: 'CartStrip', label: 'Cart strip' },
];

export const BANNER_AUDIENCES: readonly Choice[] = [
  { value: 'None', label: 'Everybody' },
  { value: 'Anonymous', label: 'Signed-out visitors only' },
  { value: 'SignedIn', label: 'Signed-in customers only' },
];

/**
 * What a menu item points at.
 *
 * `Page`, `Category` and `Collection` take a target id and survive a rename, which is the reason
 * the model has link types at all; `Url` is the escape hatch and does not.
 */
export const MENU_LINK_TYPES: readonly Choice[] = [
  { value: 'Page', label: 'A page', hint: 'Survives the page being renamed.' },
  { value: 'Category', label: 'A category', hint: 'Survives the category being renamed.' },
  { value: 'Collection', label: 'A collection', hint: 'Survives the collection being renamed.' },
  { value: 'Url', label: 'A URL', hint: 'Breaks if whatever it points at moves.' },
  { value: 'None', label: 'Nothing — a heading', hint: 'A label with children under it.' },
];

export const COLLECTION_KINDS: readonly Choice[] = [
  { value: 'Manual', label: 'Chosen by hand' },
  { value: 'Rule', label: 'Filled by a rule' },
];

/** The facts a collection rule may be about. Each is a column the module can actually query. */
export const RULE_FIELDS: readonly Choice[] = [
  { value: 'Category', label: 'Category' },
  { value: 'Brand', label: 'Brand' },
  { value: 'Vendor', label: 'Seller' },
  { value: 'Price', label: 'Price' },
  { value: 'DiscountPercent', label: 'Discount %' },
  { value: 'Rating', label: 'Rating' },
  { value: 'PublishedWithinDays', label: 'Published within (days)' },
  { value: 'Attribute', label: 'An attribute' },
];

export const RULE_OPERATORS: readonly Choice[] = [
  { value: 'In', label: 'is any of' },
  { value: 'NotIn', label: 'is none of' },
  { value: 'GreaterThan', label: 'is more than' },
  { value: 'AtLeast', label: 'is at least' },
  { value: 'LessThan', label: 'is less than' },
  { value: 'AtMost', label: 'is at most' },
];

export const COLLECTION_SORTS: readonly Choice[] = [
  { value: 'Newest', label: 'Newest first' },
  { value: 'PriceAscending', label: 'Cheapest first' },
  { value: 'PriceDescending', label: 'Dearest first' },
  { value: 'Discount', label: 'Biggest saving first' },
  { value: 'Rating', label: 'Best reviewed first' },
];
