import { RenderMode, ServerRoute } from '@angular/ssr';

/**
 * Server rendering strategy.
 *
 * Everything is server-rendered for now. **Not prerendered**: a prerender runs at build time,
 * where there is no API and no runtime configuration, so a prerendered catalogue page would ship
 * whatever the build machine could not fetch.
 *
 * The real per-route split — SSR for the indexable pages, CSR behind auth for cart, checkout and
 * account (docs/05-frontend-architecture.md §3.1) — belongs to Step 23, which is where those
 * routes come into existence.
 */
export const serverRoutes: ServerRoute[] = [
  {
    path: '**',
    renderMode: RenderMode.Server,
  },
];
