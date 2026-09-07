/**
 * The spacing scale, as a type.
 *
 * A layout primitive takes a step on the 4px scale — never a length — so there is no way to write
 * `gap="13px"` in a template and no way for a component to hold a spacing value the theme cannot
 * change. Every step maps to a custom property declared in `10-design-system-placeholder.md` §2,
 * which is what makes Step 30 a token swap.
 */
export type SpaceStep = 0 | 1 | 2 | 3 | 4 | 5 | 6 | 8 | 10 | 12 | 16 | 20;

/** Resolves a step to the custom property that carries its value. */
export function spaceToken(step: SpaceStep): string {
  return `var(--space-${step})`;
}
