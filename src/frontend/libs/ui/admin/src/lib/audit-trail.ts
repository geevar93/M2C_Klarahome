import { ChangeDetectionStrategy, Component, input } from '@angular/core';
import { KhDatePipe } from '@klarahome/i18n';
import { Disclosure, EmptyState, Skeleton } from '@klarahome/ui-primitives';

import { AuditEntryView } from './admin.model';

/**
 * Who changed this, when, and to what.
 *
 * Embeddable on any entity detail page (`docs/05-frontend-architecture.md` §4.3) and also the body
 * of the audit-log screen, because the two are the same list with a different filter — one entity,
 * or everything.
 *
 * The design decision worth naming is that a change **shows both sides**. An audit line reading
 * "Priya changed the price" is not an audit trail; "₹2,499 → ₹1,999" is, and it is the difference
 * between an entry that answers a dispute and one that starts another. The API stores `before` and
 * `after` as whole documents, and the app's mapper reduces them to the fields that actually moved
 * — a diff of eighty unchanged fields hides the one that did.
 *
 * Entries are collapsed by default and each opens on demand: an order with sixty events is a wall
 * of JSON otherwise, and the question is nearly always "what happened", not "what were all the
 * values".
 */
@Component({
  selector: 'kh-audit-trail',
  imports: [Disclosure, EmptyState, KhDatePipe, Skeleton],
  template: `
    @if (loading() && entries().length === 0) {
      <div class="loading">
        <kh-skeleton [lines]="4" height="1rem" />
      </div>
    } @else if (entries().length === 0) {
      <kh-empty-state
        heading="Nothing recorded yet"
        message="Changes to this record will appear here as they happen."
      />
    } @else {
      <ol>
        @for (entry of entries(); track entry.id) {
          <li>
            <div class="line">
              <span class="action">{{ entry.action }}</span>
              <span class="meta">
                {{ entry.actor }} ·
                <time [attr.datetime]="entry.occurredAt">{{ entry.occurredAt | khDate }}</time>
              </span>
            </div>

            @if (entry.changes.length > 0) {
              <kh-disclosure heading="What changed" [hint]="entry.changes.length + ' field(s)'">
                <table>
                  <thead>
                    <tr>
                      <th scope="col">Field</th>
                      <th scope="col">Before</th>
                      <th scope="col">After</th>
                    </tr>
                  </thead>
                  <tbody>
                    @for (change of entry.changes; track change.field) {
                      <tr>
                        <th scope="row">{{ change.field }}</th>
                        <td class="before">{{ change.before ?? '—' }}</td>
                        <td class="after">{{ change.after ?? '—' }}</td>
                      </tr>
                    }
                  </tbody>
                </table>
              </kh-disclosure>
            }

            @if (showTechnical()) {
              <p class="technical">
                {{ entry.entityType }}
                @if (entry.entityId) {
                  <span> · {{ entry.entityId }}</span>
                }
                @if (entry.ip) {
                  <span> · {{ entry.ip }}</span>
                }
                @if (entry.correlationId) {
                  <span> · {{ entry.correlationId }}</span>
                }
              </p>
            }
          </li>
        }
      </ol>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    ol {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    li {
      padding: var(--space-3) 0;
      border-block-end: 1px solid var(--color-border);
    }

    .line {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      align-items: baseline;
      justify-content: space-between;
    }

    .action {
      font-weight: var(--weight-medium);
      font-size: var(--text-sm);
    }

    .meta,
    .technical {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .technical {
      margin: var(--space-1) 0 0;
      font-family: var(--font-mono);
      overflow-wrap: anywhere;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      font-size: var(--text-xs);
    }

    th,
    td {
      padding: var(--space-1) var(--space-2);
      text-align: start;
      vertical-align: top;
      border-block-end: 1px solid var(--color-border);
      overflow-wrap: anywhere;
    }

    .before {
      color: var(--color-text-muted);
      text-decoration: line-through;
    }

    .after {
      font-weight: var(--weight-medium);
    }

    .loading {
      padding: var(--space-4) 0;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuditTrail {
  readonly entries = input<readonly AuditEntryView[]>([]);
  readonly loading = input(false);
  /**
   * Whether the entity id, the caller's IP and the correlation id are shown.
   *
   * On for the audit-log screen, where somebody is investigating; off for the panel on an entity
   * page, where the entity is already known and an IP address is noise.
   */
  readonly showTechnical = input(false);
}
