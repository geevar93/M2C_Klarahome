const nxPreset = require('@nx/jest/preset').default;

/**
 * Packages inside node_modules that must go through the transform.
 *
 * Jest runs CommonJS here. These are published as ES modules — some, like `until-async`, with no
 * CommonJS build at all — so skipping them the way the rest of node_modules is skipped means the
 * first `export` statement is a syntax error before a single test runs.
 *
 * The list is explicit rather than "transform everything in node_modules", which would add minutes
 * to every run for the benefit of these few. Add to it when a new ESM-only dependency appears; the
 * symptom is always `SyntaxError: Unexpected token 'export'` naming the package.
 */
const ESM_PACKAGES = [
  // MSW and its dependency tree.
  'msw',
  '@mswjs',
  'until-async',
  '@bundled-es-modules',
  'outvariant',
  'strict-event-emitter',
  'is-node-process',
  'headers-polyfill',
  'tough-cookie',
  'graphql',
  // Angular ships its locale data as ESM only. `en-IN` is loaded by @klarahome/i18n, so any test
  // that stands up the app's providers reaches it.
  '@angular/common/locales',
];

module.exports = {
  ...nxPreset,

  // jsdom does not implement the fetch API — no `Request`, `Response`, `ReadableStream` — and MSW
  // is built on it. `jest-fixed-jsdom` is jsdom with those globals restored from Node's own
  // implementation, which is what a browser would have provided anyway.
  testEnvironment: 'jest-fixed-jsdom',

  // Without this, jsdom asks for the `browser` export condition and MSW answers with its ESM
  // build. Clearing the list falls through to the CommonJS entry points, which is what MSW
  // documents for Jest.
  testEnvironmentOptions: { customExportConditions: [''] },

  transformIgnorePatterns: [`node_modules/(?!(?:${ESM_PACKAGES.join('|')})/)(?!.*\\.mjs$)`],
};
