import nx from '@nx/eslint-plugin';

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
];
