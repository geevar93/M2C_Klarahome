import { ChangeDetectionStrategy, Component, effect, inject, input, output, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import {
  ReferenceDataService,
  ServiceableRegionPayload,
  VendorsAdminService,
} from '@klarahome/data-access-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError } from '../../core/describe-error';

/** One rule being edited. `scope` decides which of the two identifier fields is used. */
interface RegionDraft {
  scope: 'State' | 'PincodePrefix';
  stateId: string;
  pincodePrefix: string;
  isExcluded: boolean;
}

/**
 * Where a seller will ship to.
 *
 * **Two shapes of rule, and exclusions win.** A region is either a whole state or a PIN-code
 * prefix, and each is either an inclusion or an exclusion — so "everywhere in Telangana except
 * 5001xx" is two rows rather than a list of prefixes. The exclusion beating the inclusion is the
 * server's rule; the screen states it rather than sorting the rows to imply it.
 *
 * **"Serves all of India" makes the rows irrelevant, not wrong.** Turning it on is what most
 * sellers want and it is a single flag; the rows below stay so that turning it off again restores
 * the shape somebody worked out. That is why the whole set is sent as one write — the flag and the
 * rules are one decision on the server.
 *
 * This is a seller's own limit, and it is not the store's: Step 16A's delivery coverage decides
 * where the *platform* delivers, and a seller cannot widen it by adding a region here.
 */
@Component({
  selector: 'kh-serviceable-regions-panel',
  imports: [Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton],
  template: `
    <section class="panel">
      <h2>Where this seller ships</h2>
      <p class="hint">
        An exclusion always beats an inclusion. The store's own delivery coverage still applies on top of this
        — a seller cannot reach further than the platform does.
      </p>

      @if (error(); as message) {
        <kh-alert tone="danger" heading="Regions">{{ message }}</kh-alert>
      }

      @if (loading()) {
        <kh-skeleton height="6rem" />
      } @else {
        <kh-checkbox
          label="Ships anywhere in India"
          inputId="regions-all-india"
          [disabled]="!canManage()"
          [checked]="servesAllIndia()"
          (checkedChange)="servesAllIndia.set($event)"
        />

        @if (servesAllIndia()) {
          <p class="note">The rules below are kept but are not consulted while this is on.</p>
        }

        @for (region of regions(); track $index) {
          <div class="region">
            <kh-field [label]="'Rule'" [for]="'region-scope-' + $index">
              <select
                khControl
                [id]="'region-scope-' + $index"
                [disabled]="!canManage()"
                [value]="region.scope"
                (change)="setScope($index, $any($event.target).value)"
              >
                <option value="State">A whole state</option>
                <option value="PincodePrefix">A PIN-code prefix</option>
              </select>
            </kh-field>

            @if (region.scope === 'State') {
              <kh-field [label]="'State'" [for]="'region-state-' + $index">
                <select
                  khControl
                  [id]="'region-state-' + $index"
                  [disabled]="!canManage()"
                  [value]="region.stateId"
                  (change)="setField($index, 'stateId', $any($event.target).value)"
                >
                  <option value="">Choose a state…</option>
                  @for (state of states(); track state.id) {
                    <option [value]="state.id">{{ state.name }} ({{ state.code }})</option>
                  }
                </select>
              </kh-field>
            } @else {
              <kh-field [label]="'Prefix'" [for]="'region-prefix-' + $index" hint="500 covers 500001–500999.">
                <input
                  khControl
                  khNumeric
                  [id]="'region-prefix-' + $index"
                  type="text"
                  inputmode="numeric"
                  maxlength="6"
                  [disabled]="!canManage()"
                  [value]="region.pincodePrefix"
                  (input)="setField($index, 'pincodePrefix', $any($event.target).value)"
                />
              </kh-field>
            }

            <div class="flags">
              <kh-checkbox
                label="Exclude instead"
                [inputId]="'region-excluded-' + $index"
                [disabled]="!canManage()"
                [checked]="region.isExcluded"
                (checkedChange)="setExcluded($index, $event)"
              />
              @if (region.isExcluded) {
                <kh-badge tone="danger">Excluded</kh-badge>
              } @else {
                <kh-badge tone="success">Served</kh-badge>
              }
            </div>

            @if (canManage()) {
              <button khButton type="button" size="sm" variant="tertiary" (click)="removeRegion($index)">
                Remove
              </button>
            }
          </div>
        }

        @if (canManage()) {
          <div class="actions">
            <button khButton type="button" size="sm" (click)="addRegion()">
              <kh-icon name="plus" size="sm" />
              Add a rule
            </button>
            <button khButton type="button" variant="primary" [disabled]="busy()" (click)="save()">
              {{ busy() ? 'Saving…' : 'Save regions' }}
            </button>
          </div>
        }
      }
    </section>
  `,
  styles: `
    .panel {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    .hint,
    .note {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .hint {
      margin: 0 0 var(--space-3);
    }

    .note {
      margin: var(--space-2) 0 0;
    }

    .region {
      display: flex;
      gap: var(--space-3);
      align-items: flex-end;
      flex-wrap: wrap;
      margin-block-start: var(--space-3);
      padding-block-start: var(--space-3);
      border-block-start: 1px solid var(--color-border);
    }

    .region > kh-field {
      flex: 1 1 9rem;
    }

    .flags {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      flex: 1 1 12rem;
    }

    .actions {
      display: flex;
      gap: var(--space-2);
      justify-content: space-between;
      margin-block-start: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ServiceableRegionsPanel {
  private readonly vendors = inject(VendorsAdminService);
  private readonly toasts = inject(ToastService);

  /** The platform's states, so a rule names one rather than an identifier (Step 28B, deliverable 3). */
  protected readonly states = toSignal(inject(ReferenceDataService).states, { initialValue: [] });

  readonly vendorId = input.required<string>();
  readonly canManage = input(true);
  readonly changed = output<void>();

  protected readonly servesAllIndia = signal(false);
  protected readonly regions = signal<readonly RegionDraft[]>([]);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly error = signal<string | null>(null);

  constructor() {
    effect(() => {
      const id = this.vendorId();
      if (id) this.load(id);
    });
  }

  protected addRegion(): void {
    this.regions.update((current) => [
      ...current,
      { scope: 'State', stateId: '', pincodePrefix: '', isExcluded: false },
    ]);
  }

  protected removeRegion(index: number): void {
    this.regions.update((current) => current.filter((_region, position) => position !== index));
  }

  protected setScope(index: number, scope: string): void {
    this.regions.update((current) =>
      current.map((region, position) =>
        position === index
          ? // The other identifier is cleared, so a state id is never sent with a prefix rule.
            {
              ...region,
              scope: scope === 'PincodePrefix' ? 'PincodePrefix' : 'State',
              stateId: '',
              pincodePrefix: '',
            }
          : region,
      ),
    );
  }

  protected setField(index: number, field: 'stateId' | 'pincodePrefix', value: string): void {
    this.regions.update((current) =>
      current.map((region, position) => (position === index ? { ...region, [field]: value } : region)),
    );
  }

  protected setExcluded(index: number, value: boolean): void {
    this.regions.update((current) =>
      current.map((region, position) => (position === index ? { ...region, isExcluded: value } : region)),
    );
  }

  protected save(): void {
    if (this.busy()) return;

    const regions: ServiceableRegionPayload[] = this.regions()
      .filter((region) =>
        region.scope === 'State' ? region.stateId.trim().length > 0 : region.pincodePrefix.trim().length > 0,
      )
      .map((region) => ({
        scope: region.scope,
        stateId: region.scope === 'State' ? region.stateId.trim() : null,
        pincodePrefix: region.scope === 'PincodePrefix' ? region.pincodePrefix.trim() : null,
        isExcluded: region.isExcluded,
      }));

    const id = this.vendorId();
    this.busy.set(true);
    this.error.set(null);

    this.vendors.setServiceableRegions(id, { servesAllIndia: this.servesAllIndia(), regions }).subscribe({
      next: () => {
        this.busy.set(false);
        this.toasts.success('Regions saved.');
        this.load(id);
        this.changed.emit();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.error.set(describeError(error, 'They could not be saved.'));
      },
    });
  }

  private load(vendorId: string): void {
    this.loading.set(true);
    this.error.set(null);

    this.vendors.serviceableRegions(vendorId).subscribe({
      next: (answer) => {
        this.loading.set(false);
        this.servesAllIndia.set(answer.servesAllIndia);
        this.regions.set(
          answer.regions.map((region) => ({
            scope: region.scope === 'PincodePrefix' ? 'PincodePrefix' : 'State',
            stateId: region.stateId ?? '',
            pincodePrefix: region.pincodePrefix ?? '',
            isExcluded: region.isExcluded,
          })),
        );
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.error.set(describeError(error, 'They could not be loaded.'));
      },
    });
  }
}
