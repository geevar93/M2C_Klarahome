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
 *   - domain and util stay framework-free leaves
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
              onlyDependOnLibsWithTags: ['type:ui', 'type:data-access', 'type:domain', 'type:util'],
            },
            {
              sourceTag: 'type:ui',
              onlyDependOnLibsWithTags: ['type:ui', 'type:domain', 'type:util'],
            },
            {
              sourceTag: 'type:data-access',
              onlyDependOnLibsWithTags: [
                'type:data-access',
                'type:data-access-api',
                'type:domain',
                'type:util',
              ],
            },
            {
              sourceTag: 'type:data-access-api',
              onlyDependOnLibsWithTags: ['type:domain', 'type:util'],
            },
            { sourceTag: 'type:domain', onlyDependOnLibsWithTags: ['type:util'] },
            { sourceTag: 'type:util', onlyDependOnLibsWithTags: ['type:util'] },

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
];
