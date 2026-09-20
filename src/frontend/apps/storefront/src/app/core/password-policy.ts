/**
 * The password rule, stated once.
 *
 * The API's own policy (`KlaraHome.Modules.Identity`'s `PasswordOptions`) is a minimum length and
 * **no composition rules** — the Argon2id hash it feeds does not get stronger for a digit and an
 * uppercase letter, and a composition rule is what makes people write a password down. Ten is that
 * option's default, and it is repeated here as a constant rather than guessed independently by
 * three call sites, which is how a form ends up telling a shopper "at least 8 characters" for a
 * server that will reject anything under 10.
 *
 * Used by the register and forgot-password pages, both for the hint under the field and for the
 * client-side validator — one number, one place.
 */
export const PASSWORD_MIN_LENGTH = 10;

/** The hint shown under a new-password field. Matches {@link PASSWORD_MIN_LENGTH}. */
export const PASSWORD_HINT = `At least ${PASSWORD_MIN_LENGTH} characters. No particular mix of letters, numbers or symbols is required.`;
