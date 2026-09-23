import { Session } from '@klarahome/data-access-auth';

import { DESTINATIONS, canReach } from './navigation';

/**
 * One thing the "+ New" sheet can offer.
 *
 * `guardPath` names the route in `navigation.ts` whose guard actually protects `targetPath` — for
 * the create shortcuts that is the detail/builder route (`catalog/products/:id`,
 * `promotions/:id`), because `/catalog/products/new` and `/promotions/new` are that same route
 * with a literal instead of a GUID (see the comments in `navigation.ts`). Checking `canReach`
 * against the route that actually guards the destination — rather than re-declaring a permission
 * here — is what makes it impossible for the sheet to offer a link its own guard would then refuse.
 */
export interface QuickAction {
  readonly label: string;
  /** What it makes, in a word or two, for the line under the label. */
  readonly hint: string;
  readonly icon: string;
  readonly targetPath: string;
  readonly guardPath: string;
  /**
   * A sheet to open instead of navigating. `targetPath` still names where the thing is made, and
   * `guardPath` still decides whether it is offered; the sheet is the short way in.
   */
  readonly opens?: 'product-quick-add';
}

/**
 * What a session most often starts a day by making.
 *
 * These used to be a row of four buttons across the top of the dashboard, which on a phone pushed
 * the first work queue below the fold. They are now behind one "+" in the top bar, reachable from
 * every screen, in the order a merchant reaches for them.
 */
export const QUICK_ACTIONS: readonly QuickAction[] = [
  {
    label: 'Product',
    hint: 'Add something to the catalogue',
    icon: 'package',
    targetPath: '/catalog/products/new',
    guardPath: 'catalog/products/:id',
    opens: 'product-quick-add',
  },
  {
    label: 'Promotion',
    hint: 'A discount, offer or coupon',
    icon: 'megaphone',
    targetPath: '/promotions/new',
    guardPath: 'promotions/:id',
  },
  {
    label: 'Purchase order',
    hint: 'Stock coming in from a supplier',
    icon: 'download',
    targetPath: '/inventory/purchase-orders',
    guardPath: 'inventory/purchase-orders',
  },
  {
    label: 'Page',
    hint: 'A storefront page or the homepage',
    icon: 'edit',
    targetPath: '/content/pages',
    guardPath: 'content/pages',
  },
  {
    label: 'User',
    hint: 'Someone who signs in to the back office',
    icon: 'user',
    targetPath: '/settings/users',
    guardPath: 'settings/users',
  },
];

/** The quick actions this session's guards would actually let it open. */
export function quickActionsFor(session: Session | null): readonly QuickAction[] {
  return QUICK_ACTIONS.filter((action) => {
    const destination = DESTINATIONS.find((entry) => entry.path === action.guardPath);
    return destination !== undefined && canReach(session, destination);
  });
}
