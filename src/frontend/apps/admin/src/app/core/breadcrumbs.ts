import { AdminCrumb } from '@klarahome/ui-admin';

import { AdminDestination, DESTINATIONS } from './navigation';

/** Pages that are real routes but are not in `DESTINATIONS`. */
const EXTRA: Readonly<Record<string, string>> = {
  more: 'All screens',
  profile: 'Your profile',
};

/**
 * The trail above a page: Home, the section it is filed under, its list when it is a detail
 * screen, then the page itself.
 *
 * Derived from `DESTINATIONS` and the current URL so there is nothing to keep in step: a screen
 * added to the declaration gets its trail for free. A detail route (`orders/:id`) is matched by
 * pattern and its parent is the declared destination one segment up, so it reads
 * Home / Orders / Order and "Orders" links back to the list. The section is plain text rather than
 * a link because a section is a heading, not a page.
 *
 * The dashboard has no trail at all (an empty array), and so does anything unrecognised: a trail
 * that guesses is worse than none.
 */
export function breadcrumbsFor(url: string): readonly AdminCrumb[] {
  const path = url.split('?')[0].split('#')[0].replace(/^\/+|\/+$/g, '');
  if (path === '' || path === 'dashboard') return [];

  const home: AdminCrumb = { label: 'Home', path: '/dashboard' };
  const extra = EXTRA[path];
  if (extra) return [home, { label: extra }];

  const destination = match(path);
  if (!destination) return [];

  const parent = destination.hidden ? parentOf(destination) : null;
  const leading = parent ?? destination;
  const crumbs: AdminCrumb[] = [home];

  if (destination.section !== 'Home' && destination.section !== leading.label) {
    crumbs.push({ label: destination.section });
  }
  if (parent) crumbs.push({ label: parent.label, path: `/${parent.path}` });
  crumbs.push({ label: destination.label });
  return crumbs;
}

/** The most specific declared destination for a URL path: a literal route beats a `:param` one. */
function match(path: string): AdminDestination | null {
  const segments = path.split('/');
  let best: { destination: AdminDestination; literals: number } | null = null;

  for (const destination of DESTINATIONS) {
    const pattern = destination.path.split('/');
    if (pattern.length !== segments.length) continue;

    let literals = 0;
    const fits = pattern.every((part, index) => {
      if (part.startsWith(':')) return true;
      literals += 1;
      return part === segments[index];
    });

    if (fits && (!best || literals > best.literals)) best = { destination, literals };
  }
  return best?.destination ?? null;
}

function parentOf(destination: AdminDestination): AdminDestination | null {
  const parts = destination.path.split('/');
  parts.pop();
  const parentPath = parts.join('/');
  return DESTINATIONS.find((candidate) => candidate.path === parentPath) ?? null;
}
