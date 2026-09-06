/**
 * Identifier generation.
 *
 * Two callers need one: the correlation-id interceptor, which tags every request so a UI report
 * and a server log can be joined, and the checkout, which needs an idempotency key that survives
 * a retry (docs/04-api-specification.md §1).
 */

/**
 * A RFC 4122 v4 UUID.
 *
 * `crypto.randomUUID` needs a secure context, which the storefront always has in production and
 * does not always have in a developer's `http://localhost`. The fallback uses the same CSPRNG,
 * only assembled by hand; the last resort uses `Math.random`, which is fine because nothing
 * here is a secret — a correlation id only has to be unique, and an idempotency key only has to
 * be unique to one user's one intent.
 */
export function newUuid(): string {
  const cryptoObject = globalThis.crypto;

  if (typeof cryptoObject?.randomUUID === 'function') {
    return cryptoObject.randomUUID();
  }

  const bytes = new Uint8Array(16);
  if (typeof cryptoObject?.getRandomValues === 'function') {
    cryptoObject.getRandomValues(bytes);
  } else {
    for (let i = 0; i < bytes.length; i += 1) bytes[i] = Math.floor(Math.random() * 256);
  }

  // Version 4, variant 10xx — the two bits that make it a v4 rather than a random 128-bit string.
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;

  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0')).join('');
  return `${hex.slice(0, 8)}-${hex.slice(8, 12)}-${hex.slice(12, 16)}-${hex.slice(16, 20)}-${hex.slice(20)}`;
}
