import { Money } from '@klarahome/domain';

/**
 * What the shell needs to know about a navigation item — and nothing more.
 *
 * Deliberately **not** the API's `StoreMenuItemResponse`. The `ui` layer may not import
 * `data-access` (the lint boundary enforces it), and that rule is not bureaucracy here: a header
 * that took the transport type would have to change every time the CMS menu contract gained a
 * field, and could not be rendered in a test without a mock server. The app maps the response to
 * this on its way in.
 */
export interface NavItem {
  readonly label: string;
  /**
   * An internal path (`/c/lighting`) or an absolute URL. Absent for a heading that only groups
   * its children — which the CMS allows, and which must not render as a link to nowhere.
   */
  readonly href?: string | null;
  readonly opensInNewTab?: boolean;
  /** A short marker: `New`, `Sale`. Text, never a colour on its own. */
  readonly badge?: string | null;
  readonly children?: readonly NavItem[];
}

/** One line of the mini-cart. The same reasoning as `NavItem`: a view model, not a DTO. */
export interface MiniCartLine {
  readonly id: string;
  readonly name: string;
  readonly quantity: number;
  /**
   * The line total **as the API computed it**. A `Money` and not a number, so it cannot be
   * rendered without its currency, and not a pre-formatted string, so the one place that knows how
   * to write ₹1,23,456 stays the `khMoney` pipe (docs/05-frontend-architecture.md §2).
   */
  readonly lineTotal: Money;
  /** SKU or another short identifier, shown inside the placeholder image box until Step 30. */
  readonly reference?: string;
}

/** True when a href should be handled by the router rather than by the browser. */
export function isInternalHref(href: string | null | undefined): boolean {
  return !!href && href.startsWith('/');
}
