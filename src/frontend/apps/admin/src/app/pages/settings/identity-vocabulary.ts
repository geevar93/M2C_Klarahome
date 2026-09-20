import { UserType } from '@klarahome/data-access-admin';

/**
 * The words the back office uses for an account's kind.
 *
 * Typed against the generated enum so a renamed value fails to compile here rather than printing
 * raw on a screen. "Vendor" is the API's word; "seller" is the product's (docs/10-design-system.md).
 */
export const USER_TYPES: readonly { value: UserType; label: string; hint: string }[] = [
  { value: 'Staff', label: 'Store staff', hint: 'Works for the platform.' },
  { value: 'Vendor', label: 'Seller user', hint: 'Needs the seller they belong to.' },
  { value: 'Customer', label: 'Customer', hint: 'Shops on the storefront; has no back office.' },
];

export function userTypeLabel(type: UserType | string | null | undefined): string {
  return USER_TYPES.find((entry) => entry.value === type)?.label ?? (type || '—');
}
