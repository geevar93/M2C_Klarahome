import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute } from '@angular/router';
import { ReportDefinition, ReportingAdminService } from '@klarahome/data-access-admin';
import { SessionStore } from '@klarahome/data-access-auth';
import { Alert, Skeleton } from '@klarahome/ui-primitives';

import { describeError } from '../../core/describe-error';
import { ReportRunner } from './report-runner';

/**
 * One report.
 *
 * The screen's only work is finding the definition for the key in the URL and handing it to the
 * runner — everything else is the runner's, because a seller's performance page needs the same
 * thing and two copies of "how to format a Percent column" is one copy too many.
 *
 * The catalogue is fetched rather than the definition being asked for directly, because there is no
 * `GET /admin/reports/{key}/definition`: the list is the declaration. That has one useful
 * consequence — **a key a seller may not run simply is not in the list they are served**, so the
 * "no such report" message covers both a typo and an unauthorised key without this screen having to
 * tell the two apart, which is what an authorisation surface should do.
 */
@Component({
  selector: 'kh-report-detail-page',
  imports: [Alert, ReportRunner, Skeleton],
  template: `
    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="The report could not be found">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="20rem" />
    } @else if (definition(); as report) {
      <kh-report-runner
        [reportKey]="report.key"
        [definition]="report"
        [showVendorFilter]="!isVendor()"
        [crumbs]="[{ label: 'Reports', path: '/reports' }]"
      />
    } @else {
      <kh-alert tone="warning" heading="No such report">
        Nothing is declared under <code>{{ key }}</code> that you may run. It may have been renamed, or it may
        be one of the store's own reports rather than a seller's.
      </kh-alert>
    }
  `,
  styles: `
    code {
      font-family: var(--font-mono);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportDetailPage {
  private readonly reporting = inject(ReportingAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly session = inject(SessionStore);

  protected readonly key = this.route.snapshot.paramMap.get('key') ?? '';

  protected readonly definition = signal<ReportDefinition | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);

  protected readonly isVendor = computed(() => (this.session.session()?.vendorId ?? null) !== null);

  constructor() {
    this.loading.set(true);
    this.reporting.definitions().subscribe({
      next: (definitions) => {
        this.loading.set(false);
        this.definition.set(definitions.find((entry) => entry.key === this.key) ?? null);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'The catalogue could not be loaded.'));
      },
    });
  }
}
