import {
  BannerAudience,
  BannerPlacement,
  CollectionKind,
  CollectionSort,
  MenuLinkType,
  PageStatus,
  PageType,
  RuleField,
  RuleOperator,
} from '@klarahome/data-access-admin';

/**
 * The labels the content screens put on the words the content endpoints accept.
 *
 * **Every value below is a generated enum**, so the arrays are checked by the compiler and a value
 * renamed on the server breaks the build rather than a request. That was not true until Step 28B:
 * these nine enumerations were declared as plain `string` in the OpenAPI document, and this file
 * was a copy of a server vocabulary that nothing verified (deliverable 11).
 *
 * What is left here is the part a compiler cannot check and should not: the wording an editor
 * reads, which is deliberately not always the enum's own word.
 *
 * Block types are still not in this file, and that is the model the rest of it now follows one step
 * behind: their schemas are served by `GET /admin/content/block-types` and the composer builds its
 * form from them, so a block type added on the server needs no change here at all.
 */

export interface Choice<T extends string = string> {
  readonly value: T;
  readonly label: string;
  readonly hint?: string;
}

export const PAGE_TYPES: readonly Choice<PageType>[] = [
  { value: 'Landing', label: 'Landing page', hint: 'A campaign page with its own URL.' },
  { value: 'Static', label: 'Static page', hint: 'About us, delivery, contact.' },
  { value: 'Legal', label: 'Legal page', hint: 'Terms, privacy, returns policy.' },
  { value: 'Blog', label: 'Blog post', hint: 'Listed on the journal, and tagged.' },
  { value: 'Home', label: 'Home page', hint: 'There is one, and it cannot be deleted.' },
];

export const PAGE_STATUSES: readonly Choice<PageStatus>[] = [
  { value: 'Draft', label: 'Draft' },
  { value: 'InReview', label: 'In review' },
  { value: 'Scheduled', label: 'Scheduled' },
  { value: 'Published', label: 'Published' },
  { value: 'Unpublished', label: 'Unpublished' },
  { value: 'Archived', label: 'Archived' },
];

/** What each transition means, in the words of the person taking it. */
export const TRANSITION_LABELS: Readonly<Record<PageStatus, string>> = {
  Draft: 'Back to draft',
  InReview: 'Send for review',
  Scheduled: 'Schedule',
  Published: 'Publish now',
  Unpublished: 'Take down',
  Archived: 'Archive',
};

export const BANNER_PLACEMENTS: readonly Choice<BannerPlacement>[] = [
  { value: 'AnnouncementBar', label: 'Announcement bar', hint: 'The strip above the header.' },
  { value: 'HomeHero', label: 'Home hero' },
  { value: 'HomeStrip', label: 'Home strip' },
  { value: 'CategoryHeader', label: 'Category header' },
  { value: 'ListingSidebar', label: 'Listing sidebar' },
  { value: 'ProductStrip', label: 'Product page strip' },
  { value: 'CartStrip', label: 'Cart strip' },
];

export const BANNER_AUDIENCES: readonly Choice<BannerAudience>[] = [
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
export const MENU_LINK_TYPES: readonly Choice<MenuLinkType>[] = [
  { value: 'Page', label: 'A page', hint: 'Survives the page being renamed.' },
  { value: 'Category', label: 'A category', hint: 'Survives the category being renamed.' },
  { value: 'Collection', label: 'A collection', hint: 'Survives the collection being renamed.' },
  { value: 'Url', label: 'A URL', hint: 'Breaks if whatever it points at moves.' },
  { value: 'None', label: 'Nothing — a heading', hint: 'A label with children under it.' },
];

/**
 * Where the storefront draws a menu.
 *
 * The storefront asks for `header` and `footer` by code, and falls back to a menu *placed* there
 * whatever its code — so an editor who names their navigation "Cosmetics" still gets a header.
 */
export const MENU_PLACEMENTS: readonly Choice[] = [
  { value: 'header', label: 'Header', hint: 'The primary navigation, and the mobile drawer.' },
  { value: 'footer', label: 'Footer', hint: 'Columns of links: each top-level item is a heading.' },
];

export const COLLECTION_KINDS: readonly Choice<CollectionKind>[] = [
  { value: 'Manual', label: 'Chosen by hand' },
  { value: 'Rule', label: 'Filled by a rule' },
];

/** The facts a collection rule may be about. Each is a column the module can actually query. */
export const RULE_FIELDS: readonly Choice<RuleField>[] = [
  { value: 'Category', label: 'Category' },
  { value: 'Brand', label: 'Brand' },
  { value: 'Vendor', label: 'Seller' },
  { value: 'Price', label: 'Price' },
  { value: 'DiscountPercent', label: 'Discount %' },
  { value: 'Rating', label: 'Rating' },
  { value: 'PublishedWithinDays', label: 'Published within (days)' },
  { value: 'Attribute', label: 'An attribute' },
];

export const RULE_OPERATORS: readonly Choice<RuleOperator>[] = [
  { value: 'In', label: 'is any of' },
  { value: 'NotIn', label: 'is none of' },
  { value: 'GreaterThan', label: 'is more than' },
  { value: 'AtLeast', label: 'is at least' },
  { value: 'LessThan', label: 'is less than' },
  { value: 'AtMost', label: 'is at most' },
];

export const COLLECTION_SORTS: readonly Choice<CollectionSort>[] = [
  { value: 'Newest', label: 'Newest first' },
  { value: 'PriceAscending', label: 'Cheapest first' },
  { value: 'PriceDescending', label: 'Dearest first' },
  { value: 'Discount', label: 'Biggest saving first' },
  { value: 'Rating', label: 'Best reviewed first' },
];
