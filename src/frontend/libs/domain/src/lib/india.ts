/**
 * The Indian formats the UI validates before it bothers the server.
 *
 * Every one of these is checked server-side too — this is what makes a form say "that PIN code
 * has five digits" without a round trip, not what makes the data trustworthy.
 */

/** Six digits, first one non-zero. `500081` is Hyderabad; `012345` is not a PIN code. */
export const PINCODE_PATTERN = /^[1-9][0-9]{5}$/;

/** Ten digits starting 6–9, which is every mobile series the TRAI has allocated. */
export const MOBILE_PATTERN = /^[6-9][0-9]{9}$/;

/** Two state digits, five PAN characters, one entity digit, `Z`, one checksum character. */
export const GSTIN_PATTERN = /^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$/;

/** Five letters, four digits, one letter. */
export const PAN_PATTERN = /^[A-Z]{5}[0-9]{4}[A-Z]$/;

/** Four bank letters, `0`, then six branch characters. */
export const IFSC_PATTERN = /^[A-Z]{4}0[A-Z0-9]{6}$/;

export const isPincode = (value: string): boolean => PINCODE_PATTERN.test(value.trim());
export const isMobile = (value: string): boolean => MOBILE_PATTERN.test(normaliseMobile(value));
export const isGstin = (value: string): boolean => GSTIN_PATTERN.test(value.trim().toUpperCase());
export const isPan = (value: string): boolean => PAN_PATTERN.test(value.trim().toUpperCase());
export const isIfsc = (value: string): boolean => IFSC_PATTERN.test(value.trim().toUpperCase());

/**
 * Strips what people actually type — `+91`, `0`, spaces, dashes — down to the ten digits the API
 * wants. A customer who pastes their number from a contact card should not be told it is invalid.
 */
export function normaliseMobile(value: string): string {
  const digits = value.replace(/\D/g, '');
  if (digits.length === 12 && digits.startsWith('91')) return digits.slice(2);
  if (digits.length === 11 && digits.startsWith('0')) return digits.slice(1);
  return digits;
}

/**
 * The state a GSTIN was issued in, as its two-digit code.
 *
 * Place of supply is decided by the server — it is a tax determination, not a string operation —
 * but the code is what a vendor form shows back to confirm the number was read correctly.
 */
export function stateCodeFromGstin(gstin: string): string | null {
  const trimmed = gstin.trim().toUpperCase();
  return GSTIN_PATTERN.test(trimmed) ? trimmed.slice(0, 2) : null;
}

/** Masks all but the last four digits of an account number for display. */
export function maskAccountNumber(accountNumber: string): string {
  const digits = accountNumber.replace(/\s/g, '');
  return digits.length <= 4 ? digits : `${'•'.repeat(digits.length - 4)}${digits.slice(-4)}`;
}
