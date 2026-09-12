import { StoreMenuItemResponse } from '@klarahome/data-access-content';
import { NavItem } from '@klarahome/ui-patterns';

/**
 * The CMS menu, as the header and footer want it.
 *
 * The mapping exists because `ui` may not import `data-access` — and that boundary earns its keep
 * right here: menu items are recursive, and a header that took the transport type would carry the
 * whole content contract into the presentation layer for the sake of three fields.
 */
export function toNavItems(items: readonly StoreMenuItemResponse[] | undefined): NavItem[] {
  return (items ?? []).map((item) => ({
    label: item.label,
    href: item.href,
    opensInNewTab: item.opensInNewTab,
    badge: item.badge,
    children: item.children?.length ? toNavItems(item.children) : undefined,
  }));
}

/**
 * The menu codes the storefront asks for.
 *
 * A menu's code is free text an editor chooses, so these two are a convention rather than a
 * contract the API enforces. The API answers by code first and by *placement* second, so a menu an
 * editor named "cosmetics" but placed in the header still arrives here. Named once, so there is
 * somewhere to look.
 */
export const MENU_CODES = {
  header: 'header',
  footer: 'footer',
} as const;
