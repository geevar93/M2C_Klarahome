import { Signal, WritableSignal, computed, signal } from '@angular/core';
import { isGstin, isMobile, isPincode, normaliseMobile } from '@klarahome/domain';

/**
 * Forms, without `@angular/forms`.
 *
 * The storefront has no `ReactiveFormsModule` anywhere in it, and this is the reason: the whole
 * application is zoneless and signal-based (docs/05-frontend-architecture.md §2), the forms it
 * needs are five short ones — an address, a profile, a sign-in, a question, a return — and
 * `@angular/forms` is 30 kB of framework, a second change-detection model and a `ControlValueAccessor`
 * contract to satisfy on every input, in an application whose initial bundle budget is 180 kB
 * gzipped and whose primitives are already signal inputs.
 *
 * What is actually needed is three things, and they are all here:
 *
 *  - a value that a template can bind both ways,
 *  - a validity rule that runs on that value,
 *  - and a message that appears **after** the field has been touched or the form submitted, never
 *    while somebody is still typing their first character.
 *
 * The last one is the part that is usually got wrong. A field that turns red at the second
 * keystroke of a ten-digit mobile number is not validating, it is nagging — so `error` stays null
 * until the field is blurred or the form is submitted, while `problem` is the unconditional truth
 * the submit button reads.
 *
 * Every rule here is also enforced by the API. This is what lets a form say "that PIN code has six
 * digits" without a round trip; it is not what makes the data trustworthy.
 */

/** Answers a message when the value is wrong, or null when it is fine. */
export type Validator = (value: string) => string | null;

export const required =
  (label = 'This'): Validator =>
  (value) =>
    value.trim().length > 0 ? null : `${label} is required.`;

export const minLength =
  (length: number, label = 'This'): Validator =>
  (value) =>
    value.trim().length === 0 || value.trim().length >= length
      ? null
      : `${label} must be at least ${length} characters.`;

export const maxLength =
  (length: number, label = 'This'): Validator =>
  (value) =>
    value.trim().length <= length ? null : `${label} must be ${length} characters or fewer.`;

/**
 * An email address, checked loosely on purpose.
 *
 * Something before an `@`, something after it, and a dot in the domain. The exhaustive RFC 5322
 * expression rejects addresses that genuinely deliver, and the only test that settles the question
 * is the verification mail the API sends anyway.
 */
export const email =
  (label = 'Email'): Validator =>
  (value) =>
    value.trim().length === 0 || /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(value.trim())
      ? null
      : `Enter a valid ${label.toLowerCase()}.`;

export const mobile: Validator = (value) =>
  value.trim().length === 0 || isMobile(value) ? null : 'Enter a 10-digit Indian mobile number.';

export const pincode: Validator = (value) =>
  value.trim().length === 0 || isPincode(value) ? null : 'Enter a six-digit PIN code.';

export const gstin: Validator = (value) =>
  value.trim().length === 0 || isGstin(value) ? null : 'Enter a valid 15-character GSTIN.';

/** Digits only, for an OTP box. `length` is what the API said it sent. */
export const numeric =
  (length: number): Validator =>
  (value) =>
    value.trim().length === 0 || new RegExp(`^[0-9]{${length}}$`).test(value.trim())
      ? null
      : `Enter the ${length}-digit code.`;

/** A second field that has to equal the first — a password confirmation. */
export const matches =
  (other: Signal<string>, label = 'The values'): Validator =>
  (value) =>
    value.length === 0 || value === other() ? null : `${label} do not match.`;

export interface FormField {
  readonly value: WritableSignal<string>;
  /** Whether the field has been blurred. Set by `khControl`'s blur handler. */
  readonly touched: WritableSignal<boolean>;
  /** The first failing rule, regardless of whether the field has been touched. */
  readonly problem: Signal<string | null>;
  /** What the field shows: the server's objection if there is one, else the client's. */
  readonly error: Signal<string | null>;
  set(value: string): void;
  markTouched(): void;
  reset(value?: string): void;
  /**
   * What the API said about this field, from a 422's `ProblemDetails.errors`.
   *
   * It outranks the client-side rules and is shown immediately rather than on blur: the value has
   * already been submitted, so there is nothing left to wait for. Cleared the moment the value
   * changes, because an objection to what was sent says nothing about what is being typed now.
   */
  setServerError(message: string | null): void;
}

/**
 * One field.
 *
 * `submitted` is passed in rather than held here, because "has the form been submitted" is a
 * property of the form and every field in it has to answer to the same one — otherwise a submit
 * reveals the message on the field that was touched and stays silent on the three that were not,
 * which is the exact set the user needs to see.
 */
export function formField(
  initial = '',
  validators: readonly Validator[] = [],
  submitted: Signal<boolean> = signal(false),
): FormField {
  const value = signal(initial);
  const touched = signal(false);
  const serverError = signal<string | null>(null);

  const problem = computed<string | null>(() => {
    const current = value();
    for (const validate of validators) {
      const message = validate(current);
      if (message) return message;
    }
    return null;
  });

  return {
    value,
    touched,
    problem,
    error: computed(() => serverError() ?? (touched() || submitted() ? problem() : null)),
    set: (next) => {
      value.set(next);
      serverError.set(null);
    },
    markTouched: () => touched.set(true),
    reset: (next = initial) => {
      value.set(next);
      touched.set(false);
      serverError.set(null);
    },
    setServerError: (message) => serverError.set(message),
  };
}

export interface FormGroup<T extends Record<string, FormField>> {
  readonly fields: T;
  /** True when no field has a problem. What a submit button is disabled by. */
  readonly isValid: Signal<boolean>;
  /** The trimmed values, keyed as the fields are. */
  values(): Record<keyof T, string>;
  /** Called on submit; makes every message visible at once. Answers whether the form is valid. */
  submit(): boolean;
  reset(values?: Partial<Record<keyof T, string>>): void;
  /** Set by `submit`, read by every field's `error`. */
  readonly submitted: Signal<boolean>;
  /**
   * Applies a 422's field errors, and answers the messages that belong to no field.
   *
   * The API validates everything the client does and several things it cannot — a coupon that has
   * been used, a SKU another seller has claimed — and answers `ProblemDetails.errors`, a map of
   * member name to messages (`docs/04-api-specification.md` §1.2). Its keys arrive in the
   * server's casing (`RecipientName`, sometimes `body.recipientName`); the match here is on the
   * last dotted segment, case-insensitively, so neither side has to know the other's convention.
   *
   * Anything that matches no field is returned rather than dropped, for the caller to show in a
   * summary. **A validation message the user never sees is a form that will not submit and will
   * not say why**, which is the single worst outcome this helper exists to prevent.
   */
  applyServerErrors(errors: Readonly<Record<string, readonly string[]>> | null | undefined): string[];
  /** Clears every server message. Called before a resubmit. */
  clearServerErrors(): void;
}

/**
 * A group of fields, and the one `submitted` flag they share.
 *
 * ```ts
 * private readonly submitted = signal(false);
 * protected readonly form = formGroup(this.submitted, {
 *   recipientName: formField('', [required('Name')], this.submitted),
 * });
 * ```
 *
 * The flag is passed to both because a field can be used on its own — a coupon box, a PIN code —
 * and forcing it into a group for the sake of one input would be ceremony.
 */
export function formGroup<T extends Record<string, FormField>>(
  submitted: WritableSignal<boolean>,
  fields: T,
): FormGroup<T> {
  const entries = Object.entries(fields) as [keyof T, FormField][];

  const normalise = (key: string): string => {
    const last = key.split('.').pop() ?? key;
    return last.toLowerCase();
  };

  return {
    fields,
    submitted,
    isValid: computed(() => entries.every(([, field]) => field.problem() === null)),
    applyServerErrors: (errors) => {
      const unmatched: string[] = [];
      if (!errors) return unmatched;

      const byName = new Map(entries.map(([key, field]) => [normalise(`${String(key)}`), field]));
      for (const [key, messages] of Object.entries(errors)) {
        const message = messages?.[0];
        if (!message) continue;

        const field = byName.get(normalise(key));
        if (field) field.setServerError(message);
        else unmatched.push(message);
      }
      return unmatched;
    },
    clearServerErrors: () => {
      for (const [, field] of entries) field.setServerError(null);
    },
    values: () =>
      Object.fromEntries(entries.map(([key, field]) => [key, field.value().trim()])) as Record<
        keyof T,
        string
      >,
    submit: () => {
      submitted.set(true);
      for (const [, field] of entries) {
        field.markTouched();
        // A stale objection to the last submission must not block this one, nor sit under a
        // field the user has since corrected.
        field.setServerError(null);
      }
      return entries.every(([, field]) => field.problem() === null);
    },
    reset: (values) => {
      submitted.set(false);
      for (const [key, field] of entries) field.reset(values?.[key]);
    },
  };
}

/** Re-exported so a form can normalise a pasted `+91 98765 43210` before it is sent. */
export { normaliseMobile };
