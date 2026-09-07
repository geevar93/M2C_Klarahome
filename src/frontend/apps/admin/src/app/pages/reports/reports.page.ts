import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  ReportDefinition,
  ReportRunResponse,
  ReportScheduleResponse,
  ReportingAdminService,
  ScheduleBody,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  CellTemplate,
  ConfirmDialog,
  DataTable,
  DataTableColumn,
  Modal,
  PageHeader,
  StatusBadge,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';
import { tableDateTime } from '../../core/format';

const FREQUENCIES = [
  { value: 'Daily', label: 'Every day' },
  { value: 'Weekly', label: 'Every week' },
  { value: 'Monthly', label: 'Every month' },
] as const;

const WEEKDAYS = [
  { value: '1', label: 'Monday' },
  { value: '2', label: 'Tuesday' },
  { value: '3', label: 'Wednesday' },
  { value: '4', label: 'Thursday' },
  { value: '5', label: 'Friday' },
  { value: '6', label: 'Saturday' },
  { value: '7', label: 'Sunday' },
] as const;

/**
 * Every report the platform produces.
 *
 * **The catalogue is the API's, not this file's.** `GET /admin/reports` answers each report with
 * its columns, its groupings and whether a seller may run it, and the tiles below are built from
 * that — so a report added on the server appears here with no change to this screen, and a seller
 * is offered only the reports they may actually run because the server has already excluded the
 * rest. Nothing here names a report except by the key the server gave it.
 *
 * **A schedule is a standing instruction, and its report is not editable.** Changing which report
 * a schedule runs would silently change what a recipient has been receiving for months, so the
 * API refuses it and the form only offers the timetable and the recipients. Producing the report
 * is the same code path either way — a scheduled run and one somebody asked for are the same
 * artefact in the same log, which is what makes the runs list below worth reading.
 *
 * The runs list includes failures, deliberately: a report that has been failing quietly every
 * Monday is exactly the thing a list of successes would hide.
 */
@Component({
  selector: 'kh-reports-page',
  imports: [
    Alert,
    Badge,
    Button,
    CellTemplate,
    Checkbox,
    ConfirmDialog,
    Control,
    DataTable,
    Field,
    HasPermission,
    Icon,
    Modal,
    PageHeader,
    RouterLink,
    Skeleton,
    StatusBadge,
  ],
  template: `
    <kh-page-header
      heading="Reports"
      description="What the store can tell you, and what it sends on a timetable."
    >
      <button
        khButton
        type="button"
        *khHasPermission="'reporting.schedule.manage'"
        (click)="startSchedule(null)"
      >
        <kh-icon name="clock" size="sm" />
        New schedule
      </button>
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Reports could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not take">{{ message }}</kh-alert>
    }

    @if (loading()) {
      <kh-skeleton height="14rem" />
    } @else {
      @for (group of groups(); track group.name) {
        <section class="group">
          <h2>{{ group.name }}</h2>
          <div class="tiles">
            @for (report of group.reports; track report.key) {
              <a class="tile" [routerLink]="['/reports', report.key]">
                <span class="name">{{ report.name }}</span>
                <span class="description">{{ report.description }}</span>
                <span class="meta">
                  {{ report.columns.length }} column{{ report.columns.length === 1 ? '' : 's' }}
                  @if (report.groupBy.length > 0) {
                    · grouped by {{ report.groupBy.join(' or ') }}
                  }
                </span>
                @if (!report.isVendorScoped) {
                  <kh-badge tone="info">The store's own</kh-badge>
                }
              </a>
            }
          </div>
        </section>
      }
    }

    <section class="section">
      <h2>Scheduled reports</h2>
      <p class="hint">
        Produced on a timetable and emailed as a short-lived link. Which report a schedule runs cannot be
        changed — make a new one instead.
      </p>

      @if (schedules().length === 0) {
        <p class="empty">Nothing is scheduled.</p>
      } @else {
        <ul class="schedules">
          @for (schedule of schedules(); track schedule.id) {
            <li>
              <div>
                <span class="name">
                  {{ schedule.name }}
                  @if (!schedule.isActive) {
                    <kh-badge tone="neutral">Paused</kh-badge>
                  }
                </span>
                <span class="meta">
                  {{ schedule.reportName ?? schedule.reportKey }} · {{ describeSchedule(schedule) }} · next
                  {{ dateTime(schedule.nextRunAt) }}
                </span>
                <span class="meta">
                  {{
                    schedule.recipients.length === 0 ? 'Filed, not emailed' : schedule.recipients.join(', ')
                  }}
                </span>
              </div>

              <div class="actions" *khHasPermission="'reporting.schedule.manage'">
                <button khButton type="button" size="sm" variant="tertiary" (click)="startSchedule(schedule)">
                  Edit
                </button>
                <button khButton type="button" size="sm" variant="tertiary" (click)="deleting.set(schedule)">
                  Delete
                </button>
              </div>
            </li>
          }
        </ul>
      }
    </section>

    <section class="section">
      <h2>What has been produced</h2>

      @if (runs.error(); as message) {
        <kh-alert tone="danger" heading="Runs could not be loaded">{{ message }}</kh-alert>
      }

      <kh-data-table
        label="Report runs"
        [columns]="runColumns"
        [rows]="runs.rows()"
        [rowKey]="runKey"
        [loading]="runs.loading()"
        [page]="runPage()"
        emptyMessage="No report has been produced yet."
        (nextPage)="runs.next()"
        (previousPage)="runs.previous()"
      >
        <ng-template khCell="status" let-row>
          <kh-status-badge [status]="row.status" />
          @if (row.error) {
            <span class="meta warn">{{ row.error }}</span>
          }
        </ng-template>

        <ng-template khCell="download" let-row>
          @if (row.status === 'Completed') {
            <button
              khButton
              type="button"
              size="sm"
              variant="tertiary"
              [disabled]="downloadingId() === row.id"
              (click)="download(row)"
            >
              {{ downloadingId() === row.id ? 'Opening…' : 'Download' }}
            </button>
          }
        </ng-template>
      </kh-data-table>
    </section>

    <kh-modal
      [open]="scheduling()"
      [heading]="editingSchedule() ? 'Edit schedule' : 'New schedule'"
      (closed)="scheduling.set(false)"
    >
      @if (scheduleError(); as message) {
        <kh-alert tone="danger" heading="It could not be saved">{{ message }}</kh-alert>
      }

      <kh-field label="Name" for="schedule-name">
        <input
          khControl
          id="schedule-name"
          type="text"
          maxlength="120"
          [value]="scheduleName()"
          (input)="scheduleName.set($any($event.target).value)"
        />
      </kh-field>

      @if (!editingSchedule()) {
        <kh-field label="Report" for="schedule-report" hint="Not editable afterwards.">
          <select
            khControl
            id="schedule-report"
            [value]="scheduleReportKey()"
            (change)="scheduleReportKey.set($any($event.target).value)"
          >
            @for (report of definitions(); track report.key) {
              <option [value]="report.key">{{ report.name }}</option>
            }
          </select>
        </kh-field>
      }

      <kh-field label="How often" for="schedule-frequency">
        <select
          khControl
          id="schedule-frequency"
          [value]="frequency()"
          (change)="frequency.set($any($event.target).value)"
        >
          @for (choice of frequencies; track choice.value) {
            <option [value]="choice.value">{{ choice.label }}</option>
          }
        </select>
      </kh-field>

      @if (frequency() === 'Weekly') {
        <kh-field label="On" for="schedule-weekday">
          <select
            khControl
            id="schedule-weekday"
            [value]="dayOfWeek()"
            (change)="dayOfWeek.set($any($event.target).value)"
          >
            @for (day of weekdays; track day.value) {
              <option [value]="day.value">{{ day.label }}</option>
            }
          </select>
        </kh-field>
      }

      @if (frequency() === 'Monthly') {
        <kh-field label="On day" for="schedule-monthday" hint="1 to 28, so every month has one.">
          <input
            khControl
            id="schedule-monthday"
            type="number"
            min="1"
            max="28"
            [value]="dayOfMonth()"
            (input)="dayOfMonth.set($any($event.target).value)"
          />
        </kh-field>
      }

      <kh-field label="At (UTC hour)" for="schedule-hour" hint="0 to 23. UTC, not IST.">
        <input
          khControl
          id="schedule-hour"
          type="number"
          min="0"
          max="23"
          [value]="hourUtc()"
          (input)="hourUtc.set($any($event.target).value)"
        />
      </kh-field>

      <kh-field
        label="Send to"
        for="schedule-recipients"
        [optional]="true"
        hint="Comma separated. Left blank, the report is filed and not emailed."
      >
        <input
          khControl
          id="schedule-recipients"
          type="text"
          [value]="recipients()"
          (input)="recipients.set($any($event.target).value)"
        />
      </kh-field>

      <kh-checkbox
        label="Running"
        inputId="schedule-active"
        [checked]="scheduleActive()"
        (checkedChange)="scheduleActive.set($event)"
      />

      <div slot="footer">
        <button khButton type="button" variant="tertiary" (click)="scheduling.set(false)">Cancel</button>
        <button khButton type="button" variant="primary" [disabled]="busy()" (click)="saveSchedule()">
          {{ busy() ? 'Saving…' : 'Save schedule' }}
        </button>
      </div>
    </kh-modal>

    <kh-confirm-dialog
      [open]="deleting() !== null"
      heading="Delete this schedule"
      message="It stops running. Everything it has already produced stays."
      confirmLabel="Delete"
      [busy]="busy()"
      (confirmed)="deleteSchedule()"
      (cancelled)="deleting.set(null)"
    />
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .group,
    .section {
      margin-block-end: var(--space-6);
    }

    h2 {
      margin: 0 0 var(--space-3);
      font-size: var(--text-lg);
    }

    .tiles {
      display: grid;
      gap: var(--space-3);
      grid-template-columns: repeat(auto-fill, minmax(16rem, 1fr));
    }

    .tile {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      color: inherit;
      text-decoration: none;
    }

    .tile:hover {
      border-color: var(--color-border-strong);
    }

    .name {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      font-weight: var(--weight-medium);
    }

    .description {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .meta {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .meta.warn {
      color: var(--color-danger);
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .empty {
      margin: 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .schedules {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .schedules li {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      justify-content: space-between;
      padding-block: var(--space-3);
      border-block-end: 1px solid var(--color-border);
    }

    .actions {
      display: flex;
      gap: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ReportsPage {
  private readonly reporting = inject(ReportingAdminService);
  private readonly toasts = inject(ToastService);

  protected readonly frequencies = FREQUENCIES;
  protected readonly weekdays = WEEKDAYS;
  protected readonly dateTime = tableDateTime;

  protected readonly definitions = signal<readonly ReportDefinition[]>([]);
  protected readonly schedules = signal<readonly ReportScheduleResponse[]>([]);
  protected readonly runs = this.reporting.runs();

  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly downloadingId = signal<string | null>(null);

  protected readonly scheduling = signal(false);
  protected readonly scheduleError = signal<string | null>(null);
  protected readonly editingSchedule = signal<ReportScheduleResponse | null>(null);
  protected readonly deleting = signal<ReportScheduleResponse | null>(null);
  protected readonly scheduleName = signal('');
  protected readonly scheduleReportKey = signal('');
  protected readonly frequency = signal('Weekly');
  protected readonly dayOfWeek = signal('1');
  protected readonly dayOfMonth = signal('1');
  protected readonly hourUtc = signal('2');
  protected readonly recipients = signal('');
  protected readonly scheduleActive = signal(true);

  /** The catalogue, filed under the headings the server gave each report. */
  protected readonly groups = computed(() => {
    const byGroup = new Map<string, ReportDefinition[]>();
    for (const report of this.definitions()) {
      const existing = byGroup.get(report.group);
      if (existing) existing.push(report);
      else byGroup.set(report.group, [report]);
    }
    return [...byGroup.entries()].map(([name, reports]) => ({ name, reports }));
  });

  protected readonly runPage = computed(() => ({
    nextCursor: this.runs.nextCursor(),
    hasPrevious: this.runs.hasPrevious(),
    size: this.runs.size(),
    total: this.runs.total(),
  }));

  protected readonly runKey = (row: ReportRunResponse) => row.id;

  protected readonly runColumns: readonly DataTableColumn<ReportRunResponse>[] = [
    { key: 'reportKey', label: 'Report', value: (row) => row.reportKey },
    {
      key: 'period',
      label: 'Period',
      value: (row) => `${tableDateTime(row.periodStart)} → ${tableDateTime(row.periodEnd)}`,
    },
    { key: 'status', label: 'Status', kind: 'custom', width: '16rem' },
    { key: 'rowCount', label: 'Rows', kind: 'number', value: (row) => row.rowCount ?? '—', width: '7rem' },
    {
      key: 'startedAt',
      label: 'Started',
      kind: 'date',
      value: (row) => tableDateTime(row.startedAt),
      width: '13rem',
    },
    { key: 'download', label: '', kind: 'custom', width: '8rem' },
  ];

  constructor() {
    this.load();
    this.runs.load();
  }

  protected describeSchedule(schedule: ReportScheduleResponse): string {
    const at = `${String(schedule.hourUtc).padStart(2, '0')}:00 UTC`;
    if (schedule.frequency === 'Daily') return `every day at ${at}`;
    if (schedule.frequency === 'Weekly') {
      const day = WEEKDAYS.find((entry) => entry.value === String(schedule.dayOfWeek))?.label ?? 'a weekday';
      return `every ${day} at ${at}`;
    }
    return `on day ${schedule.dayOfMonth ?? 1} of the month at ${at}`;
  }

  // ---- Runs -------------------------------------------------------------------------------------

  /**
   * Fetches a short-lived signed link and follows it.
   *
   * The link is not cached: it expires, and a stored one would fail silently the second time it
   * was used. Opened in a new tab rather than downloaded, because the browser decides what to do
   * with a CSV and a tab is the outcome that works either way.
   */
  protected download(run: ReportRunResponse): void {
    this.downloadingId.set(run.id);
    this.actionError.set(null);

    this.reporting.download(run.id).subscribe({
      next: (link) => {
        this.downloadingId.set(null);
        window.open(link.url, '_blank', 'noopener');
      },
      error: (error: unknown) => {
        this.downloadingId.set(null);
        this.actionError.set(describeError(error, 'That file could not be fetched.'));
      },
    });
  }

  // ---- Schedules --------------------------------------------------------------------------------

  protected startSchedule(schedule: ReportScheduleResponse | null): void {
    this.scheduleError.set(null);
    this.editingSchedule.set(schedule);

    if (schedule) {
      this.scheduleName.set(schedule.name);
      this.scheduleReportKey.set(schedule.reportKey);
      this.frequency.set(schedule.frequency);
      this.dayOfWeek.set(String(schedule.dayOfWeek ?? 1));
      this.dayOfMonth.set(String(schedule.dayOfMonth ?? 1));
      this.hourUtc.set(String(schedule.hourUtc));
      this.recipients.set(schedule.recipients.join(', '));
      this.scheduleActive.set(schedule.isActive);
    } else {
      this.scheduleName.set('');
      this.scheduleReportKey.set(this.definitions()[0]?.key ?? '');
      this.frequency.set('Weekly');
      this.dayOfWeek.set('1');
      this.dayOfMonth.set('1');
      this.hourUtc.set('2');
      this.recipients.set('');
      this.scheduleActive.set(true);
    }

    this.scheduling.set(true);
  }

  protected saveSchedule(): void {
    if (this.busy()) return;

    const frequency = this.frequency();
    const body: ScheduleBody = {
      reportKey: this.scheduleReportKey(),
      name: this.scheduleName(),
      frequency,
      hourUtc: clamp(Number(this.hourUtc()), 0, 23),
      dayOfWeek: frequency === 'Weekly' ? clamp(Number(this.dayOfWeek()), 1, 7) : null,
      dayOfMonth: frequency === 'Monthly' ? clamp(Number(this.dayOfMonth()), 1, 28) : null,
      recipients: this.recipients()
        .split(',')
        .map((entry) => entry.trim())
        .filter((entry) => entry.length > 0),
      isActive: this.scheduleActive(),
    };

    this.busy.set(true);
    this.scheduleError.set(null);

    const existing = this.editingSchedule();
    const request = existing
      ? this.reporting.updateSchedule(existing.id, body)
      : this.reporting.createSchedule(body);

    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.scheduling.set(false);
        this.toasts.success(existing ? 'Schedule saved.' : 'Schedule created.');
        this.loadSchedules();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.scheduleError.set(describeError(error, 'It could not be saved.'));
      },
    });
  }

  protected deleteSchedule(): void {
    const schedule = this.deleting();
    if (!schedule) return;

    this.busy.set(true);
    this.reporting.deleteSchedule(schedule.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.deleting.set(null);
        this.loadSchedules();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.deleting.set(null);
        this.actionError.set(describeError(error, 'It could not be deleted.'));
      },
    });
  }

  // ---- Loading ----------------------------------------------------------------------------------

  private load(): void {
    this.loading.set(true);
    this.reporting.definitions().subscribe({
      next: (definitions) => {
        this.loading.set(false);
        this.definitions.set(definitions);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'The catalogue could not be loaded.'));
      },
    });

    this.loadSchedules();
  }

  private loadSchedules(): void {
    this.reporting.schedules(false).subscribe({
      next: (schedules) => this.schedules.set(schedules),
      // Not fatal: the catalogue above is still usable without the timetable.
      error: () => this.schedules.set([]),
    });
  }
}

function clamp(value: number, low: number, high: number): number {
  if (!Number.isFinite(value)) return low;
  return Math.min(high, Math.max(low, Math.round(value)));
}
