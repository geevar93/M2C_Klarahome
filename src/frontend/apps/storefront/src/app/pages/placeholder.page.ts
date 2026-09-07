import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { Button, EmptyState } from '@klarahome/ui-primitives';
import { map } from 'rxjs';

/** What a placeholder route declares about the page that will replace it. */
export interface PlaceholderDetail {
  /** The page's `<h1>` — the real one, because the heading is not what is missing. */
  readonly heading: string;
  /** The step of the implementation plan that builds this page's content, as a phrase. */
  readonly step: string;
}

/**
 * A routed page whose content has not been built yet.
 *
 * Step 23 owns the shell: the routes, the layout around them, and what happens on the way in and
 * out. The pages themselves are Steps 24 and 25. Rather than twenty near-identical files that
 * each say "not yet", there is one component and each route declares what will replace it — so
 * the route map is complete and navigable now, every URL in `05-frontend-architecture.md` §3.2
 * resolves, and the diff when a real page lands is a `loadComponent` line rather than a deletion.
 *
 * It renders the real heading and a plainly worded state. Nothing here pretends to be finished.
 */
@Component({
  selector: 'kh-placeholder-page',
  imports: [Button, EmptyState, RouterLink],
  template: `
    <h1>{{ detail().heading }}</h1>
    <kh-empty-state
      heading="Nothing to show here yet"
      [message]="
        'The route, the layout and the navigation around this page are built. Its content is built in ' +
        detail().step +
        '.'
      "
    >
      <a khButton variant="secondary" routerLink="/">Back to the home page</a>
    </kh-empty-state>
  `,
  styles: `
    h1 {
      margin-block: var(--space-6) 0;
      font-size: var(--text-2xl);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlaceholderPage {
  private readonly route = inject(ActivatedRoute);

  /**
   * From the route's data, as a signal.
   *
   * The router reuses this component between sibling placeholder routes rather than rebuilding
   * it, so reading `snapshot` once would leave the previous page's heading on screen.
   */
  protected readonly detail = toSignal(
    this.route.data.pipe(map((data) => data['placeholder'] as PlaceholderDetail)),
    { requireSync: true },
  );
}
