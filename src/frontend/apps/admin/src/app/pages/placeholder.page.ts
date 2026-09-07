import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, RouterLink } from '@angular/router';
import { PageHeader } from '@klarahome/ui-admin';
import { Button, EmptyState } from '@klarahome/ui-primitives';
import { map } from 'rxjs';

/** What a placeholder route declares about the screen that will replace it. */
export interface PlaceholderDetail {
  /** The screen's real heading — the title is not what is missing. */
  readonly heading: string;
  /** The step of the implementation plan that builds it. */
  readonly step: string;
}

/**
 * A route whose screen belongs to a later step.
 *
 * Step 26 owns the shell, the navigation and the permissions around every back-office screen; the
 * screens themselves are Steps 27 and 28. One component serves all of them, and each route says
 * what will replace it — so the whole route map from `05-frontend-architecture.md` §4.2 resolves
 * today, the guards on it can be exercised, and the diff when a real screen lands is one `load`
 * line in `navigation.ts` rather than a deletion.
 *
 * Reaching this page is itself meaningful: it means the guard let the user through, which is half
 * of what Step 26 has to get right.
 */
@Component({
  selector: 'kh-placeholder-page',
  imports: [Button, EmptyState, PageHeader, RouterLink],
  template: `
    <kh-page-header [heading]="detail().heading" />
    <kh-empty-state
      heading="This screen is not built yet"
      [message]="
        'Its route, its permissions and its place in the navigation are. The screen itself is built in ' +
        detail().step +
        '.'
      "
    >
      <a khButton variant="secondary" routerLink="/dashboard">Back to the dashboard</a>
    </kh-empty-state>
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PlaceholderPage {
  private readonly route = inject(ActivatedRoute);

  /**
   * From the route's data, as a signal.
   *
   * The router reuses this component between sibling placeholder routes rather than rebuilding it,
   * so reading `snapshot` once would leave the previous screen's heading on this one.
   */
  protected readonly detail = toSignal(
    this.route.data.pipe(map((data) => data['placeholder'] as PlaceholderDetail)),
    { requireSync: true },
  );
}
