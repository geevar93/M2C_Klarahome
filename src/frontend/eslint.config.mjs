import nx from '@nx/eslint-plugin';

// ---------------------------------------------------------------------------------------------
// Local rule: no-hardcoded-color-in-styles (Step 30 stylelint guardrail).
//
// stylelint (.stylelintrc.json) covers every real .scss file in the workspace, but almost all
// component styling here lives inline, in the `styles:` template literal of an Angular
// `@Component` decorator — stylelint has no supported way to reach inside a plain (non-tagged)
// TS template literal (postcss-styled-syntax, the usual CSS-in-JS bridge, was tried against this
// codebase and does not pick up Angular's `styles:` property; it targets tagged templates like
// `styled.div\`...\``). This rule closes that gap with a plain AST check instead: it walks every
// `styles` property's template literal quasis and flags a hex/rgb()/hsl() colour literal, the
// same two things `.stylelintrc.json`'s `color-no-hex` / `function-disallowed-list` forbid in
// SCSS. It is not a general CSS parser — it does not understand `//` vs `/* */` context inside a
// template string the way stylelint understands a real stylesheet — so a rare accepted case
// (e.g. a third-party brand colour in an inline SVG `fill` in the *template*, not styles) is
// exempted by an eslint-disable comment with a reason, not by weakening this rule.
const HEX_COLOR = /#(?:[0-9a-fA-F]{3,4}){1,2}\b/;
const RGB_HSL_FN = /\b(?:rgba?|hsla?)\s*\(/;

const noHardcodedColorInStyles = {
  meta: {
    type: 'problem',
    docs: {
      description:
        'Forbid hard-coded hex/rgb/hsl colour literals in an Angular component styles: template literal — use a design token instead.',
    },
    schema: [],
  },
  create(context) {
    return {
      'Property[key.name="styles"] TemplateLiteral'(node) {
        for (const quasi of node.quasis) {
          const text = quasi.value.raw;
          if (HEX_COLOR.test(text) || RGB_HSL_FN.test(text)) {
            context.report({
              node: quasi,
              message:
                'Hard-coded colour literal in component styles — use var(--color-*) / var(--brand-*) from libs/ui/primitives/src/styles/_tokens.scss instead (docs/10-design-system.md §1).',
            });
          }
        }
      },
    };
  },
};

// ---------------------------------------------------------------------------------------------
// Local rule: no-hardcoded-spacing-in-styles (Step 30 follow-up guardrail).
//
// The colour rule above closes the hard-coded-colour gap; this closes the equivalent gap for
// spacing, type size and breakpoints, which a 2026-09-11 review found drifting in exactly the
// same way (`libs/ui/admin/src/lib/entity-picker.ts`'s `gap: 2px`, and admin's twenty-odd
// hand-picked `rem` breakpoints against the storefront's token scale). Same approach as
// `no-hardcoded-color-in-styles`: a plain AST walk over every `styles` template literal's quasis,
// not a CSS parser, so it does not understand `//` vs `/* */` context inside the template string —
// an accepted exception is an `eslint-disable` comment with a reason, not a weaker rule.
//
// Three things are flagged:
//   1. `font-size` set to a raw `px`/`rem`/`em` literal instead of a `--text-*` token.
//   2. `padding`/`margin`/`gap`/`row-gap`/`column-gap`/`inset*` set to a raw `px`/`rem`/`em`
//      literal instead of a `--space-*` token. `0` is always fine (there is no token for "none").
//      A hairline offset inside `calc()` alongside a token — `calc(var(--space-3) - 1px)`
//      (`order-timeline.ts`) — is fine too: that pattern exists because a hairline border needs to
//      come off a token value, not because the token was skipped.
//   3. An `@media` width that is not one of the six breakpoints
//      `libs/ui/primitives/src/styles/_breakpoints.scss` declares (480/768/1024/1280/1536px — `xs`
//      is 0 and has no query), in that file's own canonical unit (`px`). A `max-width` query is
//      always flagged regardless of its value: `_breakpoints.scss` is `min-width`-only by design
//      (mobile-first; see the comment on its `from()` mixin), and the fix is always to invert the
//      rule, never to change its value.
const TOKEN_BREAKPOINTS_PX = new Set([480, 768, 1024, 1280, 1536]);
const SPACING_PROPERTY = /^(?:padding|margin|gap|row-gap|column-gap|inset)(?:-[a-z]+)*$/;
const DECLARATION_RE = /([a-zA-Z-]+)\s*:\s*([^;{}]+);/g;
const LENGTH_RE = /(-?\d*\.?\d+)(px|rem|em)\b/g;
const MEDIA_RE = /@media\s*\(([^)]*)\)/g;

/** Every non-zero px/rem/em literal in `value` that is not a hairline offset inside a `calc()`
 *  that also names a token (`var(--...)`). */
function disallowedLengths(value) {
  const found = [];
  let match;
  while ((match = LENGTH_RE.exec(value))) {
    if (parseFloat(match[1]) === 0) continue;

    const openCalc = value.lastIndexOf('calc(', match.index);
    if (openCalc !== -1) {
      let depth = 0;
      let closeIdx = -1;
      // `'calc('.length` is 5 — start just past the calc's own opening paren, at depth 0, so the
      // first unmatched ')' found is that paren's own close rather than one level short of it.
      for (let i = openCalc + 5; i < value.length; i += 1) {
        if (value[i] === '(') depth += 1;
        else if (value[i] === ')') {
          if (depth === 0) {
            closeIdx = i;
            break;
          }
          depth -= 1;
        }
      }
      if (closeIdx !== -1 && match.index < closeIdx && value.slice(openCalc, closeIdx + 1).includes('var(--')) {
        continue;
      }
    }

    found.push(match[0]);
  }
  return found;
}

const noHardcodedSpacingInStyles = {
  meta: {
    type: 'problem',
    docs: {
      description:
        'Forbid a raw px/rem/em literal for font-size/spacing, and an off-token @media width, inside an Angular component styles: template literal — use a design token instead.',
    },
    schema: [],
  },
  create(context) {
    return {
      'Property[key.name="styles"] TemplateLiteral'(node) {
        for (const quasi of node.quasis) {
          const text = quasi.value.raw;

          let declaration;
          DECLARATION_RE.lastIndex = 0;
          while ((declaration = DECLARATION_RE.exec(text))) {
            const [, property, value] = declaration;
            const isFontSize = property === 'font-size';
            if (!isFontSize && !SPACING_PROPERTY.test(property)) continue;

            for (const length of disallowedLengths(value)) {
              context.report({
                node: quasi,
                message: `Hard-coded ${length} on '${property}' in component styles — use ${
                  isFontSize ? 'a var(--text-*) type-scale token' : 'a var(--space-*) spacing token'
                } from libs/ui/primitives/src/styles/_tokens.scss instead (docs/10-design-system.md).`,
              });
            }
          }

          let media;
          MEDIA_RE.lastIndex = 0;
          while ((media = MEDIA_RE.exec(text))) {
            const condition = media[1];
            if (/max-width/.test(condition)) {
              context.report({
                node: quasi,
                message:
                  "@media (max-width: ...) — _breakpoints.scss is min-width-only, mobile-first, by design. Invert the rule (hidden/base by default, restored above the breakpoint) instead of matching downward.",
              });
              continue;
            }

            const widthMatch = condition.match(/min-width:\s*([\d.]+)(px|rem|em)/);
            if (!widthMatch) continue;

            const [, rawValue, unit] = widthMatch;
            const px = unit === 'px' ? parseFloat(rawValue) : parseFloat(rawValue) * 16;
            if (unit !== 'px' || !TOKEN_BREAKPOINTS_PX.has(px)) {
              context.report({
                node: quasi,
                message: `@media (min-width: ${rawValue}${unit}) is not one of the token breakpoints (480/768/1024/1280/1536px, libs/ui/primitives/src/styles/_breakpoints.scss) in that file's own unit (px) — round up to the nearest one instead of inventing a value.`,
              });
            }
          }
        }
      },
    };
  },
};

/**
 * Klara Home — workspace lint configuration.
 *
 * SCOPE: this file currently carries ONLY the Nx module-boundary constraints, which are a
 * structural property of the workspace (docs/05-frontend-architecture.md section 1).
 * The full Angular/TypeScript rule set, Prettier integration and CI wiring are owned by
 * Step 5 (CI pipeline & quality gates) and Step 22 (frontend foundations).
 *
 * Constraints encoded here:
 *   - an app may not import another app            (scope:storefront / scope:admin isolation)
 *   - ui may not import data-access                (presentation stays free of transport)
 *   - only data-access may import data-access/api  (the generated OpenAPI client is not
 *                                                   consumed directly by apps or ui)
 *   - domain is a true leaf: it imports nothing, so a model can be read anywhere
 *   - util may use domain (a pipe formats a Money), but never the other way round
 */
export default [
  ...nx.configs['flat/base'],
  ...nx.configs['flat/typescript'],
  ...nx.configs['flat/javascript'],
  {
    ignores: ['**/dist', '**/node_modules', '**/.angular', '**/coverage', '**/test-output'],
  },
  {
    files: ['**/*.ts', '**/*.tsx', '**/*.js', '**/*.jsx', '**/*.mjs', '**/*.cjs'],
    rules: {
      '@nx/enforce-module-boundaries': [
        'error',
        {
          enforceBuildableLibDependency: true,
          allow: ['^.*/eslint(\.base)?\.config\.[cm]?js$'],
          depConstraints: [
            // ---- layer constraints ----
            {
              sourceTag: 'type:app',
              onlyDependOnLibsWithTags: [
                'type:ui',
                'type:data-access',
                'type:domain',
                'type:util',
                'type:testing',
              ],
            },
            {
              sourceTag: 'type:ui',
              onlyDependOnLibsWithTags: ['type:ui', 'type:domain', 'type:util', 'type:testing'],
            },
            {
              sourceTag: 'type:data-access',
              onlyDependOnLibsWithTags: [
                'type:data-access',
                'type:data-access-api',
                'type:domain',
                'type:util',
                'type:testing',
              ],
            },
            {
              sourceTag: 'type:data-access-api',
              onlyDependOnLibsWithTags: ['type:domain', 'type:util', 'type:testing'],
            },
            // `domain` imports nothing. It is the one layer every other may read, which only
            // stays true while it depends on none of them.
            { sourceTag: 'type:domain', onlyDependOnLibsWithTags: [] },
            { sourceTag: 'type:util', onlyDependOnLibsWithTags: ['type:util', 'type:domain'] },

            // The test harness is the one library that reaches across every layer: it wires the real
            // interceptor chain so a component test exercises it rather than stubbing past it.
            // Nothing depends on it in production — the no-restricted-imports rule below is what
            // enforces that, because a tag cannot tell a spec file from the code it tests.
            { sourceTag: 'type:testing', onlyDependOnLibsWithTags: ['*'] },

            // ---- scope constraints (keeps the two apps independent) ----
            {
              sourceTag: 'scope:storefront',
              onlyDependOnLibsWithTags: ['scope:storefront', 'scope:shared'],
            },
            {
              sourceTag: 'scope:admin',
              onlyDependOnLibsWithTags: ['scope:admin', 'scope:shared'],
            },
            { sourceTag: 'scope:shared', onlyDependOnLibsWithTags: ['scope:shared'] },
          ],
        },
      ],
    },
  },
  {
    // The test harness must never reach a production bundle. The module-boundary tags cannot
    // enforce this on their own — they see a project, not a file, and a spec file lives inside the
    // very library it tests. This rule draws the line where it actually is.
    files: ['**/*.ts'],
    ignores: [
      '**/*.spec.ts',
      '**/*.test.ts',
      '**/test-setup.ts',
      '**/*.e2e.ts',
      'libs/testing/**',
      'apps/*-e2e/**',
    ],
    rules: {
      'no-restricted-imports': [
        'error',
        {
          patterns: [
            {
              group: ['@klarahome/testing', '@klarahome/testing/*', 'msw', 'msw/*'],
              message:
                'The test harness and MSW belong to spec files only. Importing them from production code ships a mock server to customers.',
            },
          ],
        },
      ],
    },
  },
  {
    // Step 30 stylelint guardrail, TS half — see the comment on noHardcodedColorInStyles above.
    files: ['**/*.ts'],
    ignores: ['**/*.spec.ts', '**/*.test.ts'],
    plugins: {
      local: {
        rules: {
          'no-hardcoded-color-in-styles': noHardcodedColorInStyles,
          'no-hardcoded-spacing-in-styles': noHardcodedSpacingInStyles,
        },
      },
    },
    rules: {
      'local/no-hardcoded-color-in-styles': 'error',
      'local/no-hardcoded-spacing-in-styles': 'error',
    },
  },
];
