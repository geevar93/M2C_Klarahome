import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import {
  DeliveryCoverageResponse,
  PincodeRangeModel,
  RateBody,
  ServiceabilityResponse,
  ShippingRateResponse,
  ShippingZonesService,
  ZoneBody,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import { EntityDrawer, FormShell, PageHeader } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableMoney } from '../../core/format';

/** A zone with the rate bands that belong to it, as the screen renders them. */
interface ZoneView {
  readonly id: string;
  readonly code: string;
  readonly name: string;
  readonly states: readonly string[];
  readonly pincodeRanges: readonly PincodeRangeModel[];
  readonly priority: number;
  readonly isActive: boolean;
  readonly isCatchAll: boolean;
  readonly rates: readonly ShippingRateResponse[];
}

/**
 * Shipping zones and the rate card on each of them.
 *
 * **Zones are ordered and the first match wins**, so `priority` is the most consequential field on
 * the form and the zones are listed in that order rather than alphabetically. The catch-all is
 * marked, because a store without one has PIN codes that match no zone and therefore have no
 * price — which presents at checkout as an address that cannot be delivered to, for no visible
 * reason.
 *
 * **Rates are shown grouped under their zone, with per-seller overrides beside the defaults.** A
 * band is (weight range × order-value range) and the engine picks the narrowest match, so two rows
 * that differ only by `vendorId` are not a duplicate — flattening them into one table is what makes
 * them look like one.
 *
 * **Coverage is a different refusal from serviceability, and the tester says which.** Step 16A
 * keeps `DELIVERY_AREA_NOT_COVERED` (our decision — Hyderabad by default) distinct from
 * `PINCODE_NOT_SERVICEABLE` (the courier's), and "why can this customer not order" is answered by
 * knowing which of the two it is.
 *
 * **Rates are editable here, and they had to be somewhere.** Until Step 28B nothing on this
 * platform called `createRate` or `updateRate`, so a store could not change its shipping prices
 * without a seeder — which is a first-run path blocked by an absent screen rather than by a
 * decision (deliverable 14). It is a drawer of eleven numeric fields rather than an inline table
 * because a band is a *rule*, and a rule that can be half-edited in a grid is one that prices a
 * parcel wrongly between two keystrokes.
 */
@Component({
  selector: 'kh-shipping-zones-page',
  imports: [
    Alert,
    Badge,
    Button,
    Checkbox,
    Control,
    EntityDrawer,
    Field,
    FormShell,
    HasPermission,
    Icon,
    PageHeader,
    Skeleton,
  ],
  template: `
    <kh-page-header
      heading="Shipping zones"
      description="Where a parcel can go, and what it costs to send it."
    >
      <button
        khButton
        type="button"
        variant="primary"
        *khHasPermission="'shipping.rate.manage'"
        (click)="startCreate()"
      >
        <kh-icon name="plus" size="sm" />
        New zone
      </button>
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Zones could not be loaded">{{ message }}</kh-alert>
    }

    @if (coverage(); as current) {
      <kh-alert [tone]="current.enabled ? 'warning' : 'info'" heading="Delivery coverage">
        @if (current.enabled) {
          This deployment delivers to
          {{ current.allowedCities.length > 0 ? current.allowedCities.join(', ') : 'the listed PIN codes' }}
          only. Everything outside it is refused before serviceability is even asked about.
        } @else {
          Coverage is not restricted; every PIN code the courier serves can be delivered to.
        }
      </kh-alert>
    }

    <section class="tester">
      <h2>Can we deliver to a PIN code?</h2>
      <p class="hint">
        Two different refusals: we do not cover the area, or the courier does not serve it. The answer says
        which.
      </p>

      <div class="row">
        <kh-field label="PIN code" for="test-pincode">
          <input
            khControl
            khNumeric
            id="test-pincode"
            type="text"
            inputmode="numeric"
            maxlength="6"
            [value]="testPincode()"
            (input)="testPincode.set($any($event.target).value)"
          />
        </kh-field>
        <button khButton type="button" [disabled]="testing()" (click)="test()">
          {{ testing() ? 'Asking…' : 'Test it' }}
        </button>
      </div>

      @if (testError(); as message) {
        <kh-alert tone="danger" heading="It could not be tested">{{ message }}</kh-alert>
      }

      @if (tested(); as answer) {
        <dl>
          <dt>Deliverable</dt>
          <dd>
            <kh-badge [tone]="answer.deliverable ? 'success' : 'danger'">
              {{ answer.deliverable ? 'Yes' : 'No' }}
            </kh-badge>
          </dd>
          <dt>Covered by this store</dt>
          <dd>{{ answer.covered ? 'Yes' : 'No — outside our delivery area' }}</dd>
          <dt>Served by the courier</dt>
          <dd>{{ answer.isServiceable ? 'Yes' : 'No' }}</dd>
          <dt>Cash on delivery</dt>
          <dd>{{ answer.codOk ? 'Available' : 'Not available' }}</dd>
          <dt>Where</dt>
          <dd>{{ answer.city ?? '—' }}{{ answer.state ? ', ' + answer.state : '' }}</dd>
          @if (answer.etaDays !== null) {
            <dt>Usually takes</dt>
            <dd>{{ answer.etaDays }} day{{ answer.etaDays === 1 ? '' : 's' }}</dd>
          }
          @if (answer.message) {
            <dt>Reason</dt>
            <dd>{{ answer.message }}</dd>
          }
        </dl>
      }
    </section>

    @if (loading()) {
      <kh-skeleton height="16rem" />
    } @else {
      @for (zone of zones(); track zone.id) {
        <section class="zone">
          <header>
            <div>
              <span class="name">
                {{ zone.name }}
                <kh-badge [tone]="zone.isActive ? 'success' : 'neutral'">
                  {{ zone.isActive ? 'Active' : 'Off' }}
                </kh-badge>
                @if (zone.isCatchAll) {
                  <kh-badge tone="primary">Catch-all</kh-badge>
                }
              </span>
              <span class="note">
                <code>{{ zone.code }}</code> · priority {{ zone.priority }} ·
                {{ describeScope(zone) }}
              </span>
            </div>

            <div class="zone-actions" *khHasPermission="'shipping.rate.manage'">
              <button khButton type="button" size="sm" variant="tertiary" (click)="startEdit(zone)">
                Edit zone
              </button>
              <button khButton type="button" size="sm" (click)="startCreateRate(zone)">Add a band</button>
            </div>
          </header>

          @if (zone.rates.length === 0) {
            <p class="note warn">
              No rate band on this zone — an address matching it has no price and cannot be checked out.
            </p>
          } @else {
            <table>
              <caption class="kh-visually-hidden">
                Rate bands for
                {{
                  zone.name
                }}
              </caption>
              <thead>
                <tr>
                  <th scope="col">Service</th>
                  <th scope="col">Weight</th>
                  <th scope="col">Basket</th>
                  <th scope="col">Base</th>
                  <th scope="col">Per kg</th>
                  <th scope="col">Free above</th>
                  <th scope="col">COD</th>
                  <th scope="col">ETA</th>
                  <th scope="col"><span class="kh-visually-hidden">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (rate of zone.rates; track rate.id) {
                  <tr [class.inactive]="!rate.isActive">
                    <td>
                      {{ rate.method }}
                      @if (rate.vendorId) {
                        <span class="note">seller override</span>
                      }
                    </td>
                    <td>{{ rate.minWeightGrams }}–{{ rate.maxWeightGrams }} g</td>
                    <td>
                      {{ money(rate.minOrderValue, rate.currencyCode) }}–{{
                        rate.maxOrderValue === null ? 'any' : money(rate.maxOrderValue, rate.currencyCode)
                      }}
                    </td>
                    <td>{{ money(rate.baseRate, rate.currencyCode) }}</td>
                    <td>{{ money(rate.perKgRate, rate.currencyCode) }}</td>
                    <td>{{ rate.freeAbove === null ? '—' : money(rate.freeAbove, rate.currencyCode) }}</td>
                    <td>
                      {{ rate.isCodAllowed ? money(rate.codFee, rate.currencyCode) : 'Not allowed' }}
                    </td>
                    <td>{{ rate.etaMinDays }}–{{ rate.etaMaxDays }} days</td>
                    <td>
                      <button
                        khButton
                        type="button"
                        size="sm"
                        variant="tertiary"
                        *khHasPermission="'shipping.rate.manage'"
                        (click)="startEditRate(zone, rate)"
                      >
                        Edit
                      </button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </section>
      }
    }

    @if (drawerOpen()) {
      <kh-entity-drawer
        [heading]="editing() ? 'Edit zone' : 'New zone'"
        [subtitle]="editing()?.code ?? null"
        (closed)="drawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Zone"
          description="Zones are matched in priority order and the first match wins."
          [summary]="summary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editing() ? 'Save' : 'Create'"
          (submitted)="save()"
          (cancelled)="drawerOpen.set(false)"
        >
          <kh-field label="Name" for="zone-name" [error]="form.fields.name.error()">
            <input
              khControl
              id="zone-name"
              type="text"
              maxlength="120"
              [value]="form.fields.name.value()"
              (input)="form.fields.name.set($any($event.target).value)"
              (touched)="form.fields.name.markTouched()"
            />
          </kh-field>

          @if (!editing()) {
            <kh-field
              label="Code"
              for="zone-code"
              hint="Stable, and referred to by the rate card."
              [error]="form.fields.code.error()"
            >
              <input
                khControl
                id="zone-code"
                type="text"
                maxlength="40"
                [value]="form.fields.code.value()"
                (input)="form.fields.code.set($any($event.target).value)"
              />
            </kh-field>
          }

          <kh-field label="Priority" for="zone-priority" hint="Lower is matched first.">
            <input
              khControl
              id="zone-priority"
              type="number"
              min="0"
              [value]="form.fields.priority.value()"
              (input)="form.fields.priority.set($any($event.target).value)"
            />
          </kh-field>

          <kh-field label="States" for="zone-states" [optional]="true" hint="State ids, one per line.">
            <textarea
              khControl
              id="zone-states"
              rows="4"
              [value]="states()"
              (input)="states.set($any($event.target).value)"
            ></textarea>
          </kh-field>

          <kh-field
            label="PIN-code ranges"
            for="zone-ranges"
            [optional]="true"
            hint="One per line, as from-to — for example 500001-500099."
          >
            <textarea
              khControl
              id="zone-ranges"
              rows="4"
              [value]="ranges()"
              (input)="ranges.set($any($event.target).value)"
            ></textarea>
          </kh-field>

          <kh-checkbox
            label="Active"
            inputId="zone-active"
            [checked]="isActive()"
            (checkedChange)="isActive.set($event)"
          />
        </kh-form-shell>
      </kh-entity-drawer>
    }

    @if (rateDrawerOpen()) {
      <kh-entity-drawer
        [heading]="editingRate() ? 'Edit rate band' : 'New rate band'"
        [subtitle]="rateZone()?.name ?? null"
        (closed)="rateDrawerOpen.set(false)"
      >
        <kh-form-shell
          heading="Rate band"
          description="A band is a weight range and a basket range. The engine picks the narrowest one that matches."
          [summary]="rateSummary()"
          [saving]="saving()"
          [dirty]="true"
          [submitLabel]="editingRate() ? 'Save' : 'Create'"
          (submitted)="saveRate()"
          (cancelled)="rateDrawerOpen.set(false)"
        >
          <kh-field
            label="Service"
            for="rate-method"
            hint="Which delivery service this prices. It cannot be changed on an existing band."
          >
            <select
              khControl
              id="rate-method"
              [disabled]="!!editingRate()"
              [value]="rateForm.fields.method.value()"
              (change)="rateForm.fields.method.set($any($event.target).value)"
            >
              <option value="Standard">Standard</option>
              <option value="Express">Express</option>
            </select>
          </kh-field>

          <div class="pair">
            <kh-field label="Minimum weight (g)" for="rate-min-weight">
              <input
                khControl
                id="rate-min-weight"
                type="number"
                min="0"
                [value]="rateForm.fields.minWeightGrams.value()"
                (input)="rateForm.fields.minWeightGrams.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Maximum weight (g)" for="rate-max-weight">
              <input
                khControl
                id="rate-max-weight"
                type="number"
                min="0"
                [value]="rateForm.fields.maxWeightGrams.value()"
                (input)="rateForm.fields.maxWeightGrams.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <div class="pair">
            <kh-field label="Minimum basket" for="rate-min-value">
              <input
                khControl
                id="rate-min-value"
                type="number"
                min="0"
                step="0.01"
                [value]="rateForm.fields.minOrderValue.value()"
                (input)="rateForm.fields.minOrderValue.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field
              label="Maximum basket"
              for="rate-max-value"
              [optional]="true"
              hint="Leave blank for no ceiling."
            >
              <input
                khControl
                id="rate-max-value"
                type="number"
                min="0"
                step="0.01"
                [value]="rateForm.fields.maxOrderValue.value()"
                (input)="rateForm.fields.maxOrderValue.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <div class="pair">
            <kh-field label="Base rate" for="rate-base">
              <input
                khControl
                id="rate-base"
                type="number"
                min="0"
                step="0.01"
                [value]="rateForm.fields.baseRate.value()"
                (input)="rateForm.fields.baseRate.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Per kilogram" for="rate-per-kg">
              <input
                khControl
                id="rate-per-kg"
                type="number"
                min="0"
                step="0.01"
                [value]="rateForm.fields.perKgRate.value()"
                (input)="rateForm.fields.perKgRate.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <kh-field
            label="Free above"
            for="rate-free-above"
            [optional]="true"
            hint="Blank means delivery is never free on this band."
          >
            <input
              khControl
              id="rate-free-above"
              type="number"
              min="0"
              step="0.01"
              [value]="rateForm.fields.freeAbove.value()"
              (input)="rateForm.fields.freeAbove.set($any($event.target).value)"
            />
          </kh-field>

          <kh-checkbox
            label="Cash on delivery allowed"
            description="A band that refuses it takes COD off the payment step for every address it prices."
            inputId="rate-cod-allowed"
            [checked]="isCodAllowed()"
            (checkedChange)="isCodAllowed.set($event)"
          />

          @if (isCodAllowed()) {
            <kh-field label="COD handling fee" for="rate-cod-fee">
              <input
                khControl
                id="rate-cod-fee"
                type="number"
                min="0"
                step="0.01"
                [value]="rateForm.fields.codFee.value()"
                (input)="rateForm.fields.codFee.set($any($event.target).value)"
              />
            </kh-field>
          }

          <div class="pair">
            <kh-field label="Fastest (days)" for="rate-eta-min">
              <input
                khControl
                id="rate-eta-min"
                type="number"
                min="0"
                [value]="rateForm.fields.etaMinDays.value()"
                (input)="rateForm.fields.etaMinDays.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Slowest (days)" for="rate-eta-max">
              <input
                khControl
                id="rate-eta-max"
                type="number"
                min="0"
                [value]="rateForm.fields.etaMaxDays.value()"
                (input)="rateForm.fields.etaMaxDays.set($any($event.target).value)"
              />
            </kh-field>
          </div>

          <kh-checkbox
            label="Active"
            description="An inactive band is kept and not used. Nothing on a past order changes."
            inputId="rate-active"
            [checked]="rateIsActive()"
            (checkedChange)="rateIsActive.set($event)"
          />
        </kh-form-shell>
      </kh-entity-drawer>
    }
  `,
  styles: `
    .zone-actions {
      display: flex;
      gap: var(--space-2);
    }

    .pair {
      display: grid;
      gap: var(--space-3);
    }

    @media (min-width: 768px) {
      .pair {
        grid-template-columns: 1fr 1fr;
      }
    }

    kh-alert {
      margin-block-end: var(--space-4);
    }

    .tester,
    .zone {
      margin-block-end: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .row {
      display: flex;
      gap: var(--space-3);
      align-items: flex-end;
    }

    .row > kh-field {
      flex: 0 1 12rem;
    }

    header {
      display: flex;
      gap: var(--space-3);
      align-items: flex-start;
      justify-content: space-between;
      margin-block-end: var(--space-3);
    }

    .name {
      display: flex;
      gap: var(--space-2);
      align-items: center;
      flex-wrap: wrap;
      font-weight: var(--weight-medium);
    }

    .note {
      display: block;
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .note.warn {
      color: var(--color-warning);
    }

    code {
      font-family: var(--font-mono);
    }

    dl {
      display: grid;
      grid-template-columns: auto 1fr;
      gap: var(--space-2) var(--space-4);
      margin-block-start: var(--space-4);
    }

    dt {
      color: var(--color-text-muted);
    }

    dd {
      margin: 0;
    }

    table {
      inline-size: 100%;
      border-collapse: collapse;
      font-size: var(--text-sm);
    }

    th,
    td {
      padding: var(--space-2);
      border-block-end: 1px solid var(--color-border);
      text-align: start;
      white-space: nowrap;
    }

    th {
      color: var(--color-text-muted);
      font-weight: var(--weight-medium);
    }

    tr.inactive {
      opacity: 0.55;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ShippingZonesPage {
  private readonly shipping = inject(ShippingZonesService);
  private readonly toasts = inject(ToastService);

  protected readonly zones = signal<readonly ZoneView[]>([]);
  protected readonly coverage = signal<DeliveryCoverageResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saving = signal(false);
  protected readonly summary = signal<readonly string[]>([]);

  protected readonly drawerOpen = signal(false);
  protected readonly editing = signal<ZoneView | null>(null);
  protected readonly states = signal('');
  protected readonly ranges = signal('');
  protected readonly isActive = signal(true);

  protected readonly testPincode = signal('');
  protected readonly testing = signal(false);
  protected readonly testError = signal<string | null>(null);
  protected readonly tested = signal<ServiceabilityResponse | null>(null);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('A name')], this.submitted),
    code: formField('', [required('A code')], this.submitted),
    priority: formField('100', [], this.submitted),
  });

  // ---- The rate-card editor (Step 28B, deliverable 14) -------------------------------------------

  protected readonly rateDrawerOpen = signal(false);
  protected readonly rateZone = signal<ZoneView | null>(null);
  protected readonly editingRate = signal<ShippingRateResponse | null>(null);
  protected readonly rateSummary = signal<readonly string[]>([]);
  protected readonly isCodAllowed = signal(true);
  protected readonly rateIsActive = signal(true);

  private readonly rateSubmitted = signal(false);

  /**
   * Eleven numbers, and none of them is required in the "must not be blank" sense.
   *
   * Every field has a meaningful zero — a band with no base rate is free delivery, and one with no
   * per-kilogram rate is flat — so a required-field rule here would refuse the most ordinary band
   * a store writes. The two that must be *absent* rather than zero are the ceilings, and blank is
   * how they say so.
   */
  protected readonly rateForm = formGroup(this.rateSubmitted, {
    method: formField('Standard', [], this.rateSubmitted),
    minWeightGrams: formField('0', [], this.rateSubmitted),
    maxWeightGrams: formField('500', [], this.rateSubmitted),
    minOrderValue: formField('0', [], this.rateSubmitted),
    maxOrderValue: formField('', [], this.rateSubmitted),
    baseRate: formField('0', [], this.rateSubmitted),
    perKgRate: formField('0', [], this.rateSubmitted),
    freeAbove: formField('', [], this.rateSubmitted),
    codFee: formField('0', [], this.rateSubmitted),
    etaMinDays: formField('2', [], this.rateSubmitted),
    etaMaxDays: formField('5', [], this.rateSubmitted),
  });

  constructor() {
    this.load();
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  protected describeScope(zone: ZoneView): string {
    if (zone.isCatchAll) return 'everything not matched by a zone above';
    const parts: string[] = [];
    if (zone.states.length > 0)
      parts.push(`${zone.states.length} state${zone.states.length === 1 ? '' : 's'}`);
    if (zone.pincodeRanges.length > 0) {
      parts.push(`${zone.pincodeRanges.length} PIN range${zone.pincodeRanges.length === 1 ? '' : 's'}`);
    }
    return parts.length > 0 ? parts.join(', ') : 'nothing — it matches no address';
  }

  protected test(): void {
    const pincode = this.testPincode().trim();
    if (pincode.length !== 6) {
      this.testError.set('A PIN code is six digits.');
      return;
    }

    this.testing.set(true);
    this.testError.set(null);

    this.shipping.testPincode(pincode).subscribe({
      next: (answer) => {
        this.testing.set(false);
        this.tested.set(answer);
      },
      error: (error: unknown) => {
        this.testing.set(false);
        this.tested.set(null);
        this.testError.set(describeError(error, 'It could not be tested.'));
      },
    });
  }

  protected startCreate(): void {
    this.editing.set(null);
    this.states.set('');
    this.ranges.set('');
    this.isActive.set(true);
    this.summary.set([]);
    this.form.reset({ name: '', code: '', priority: '100' });
    this.drawerOpen.set(true);
  }

  // ---- The rate card ----------------------------------------------------------------------------

  protected startCreateRate(zone: ZoneView): void {
    this.rateZone.set(zone);
    this.editingRate.set(null);
    this.isCodAllowed.set(true);
    this.rateIsActive.set(true);
    this.rateSummary.set([]);
    this.rateForm.reset({
      method: 'Standard',
      minWeightGrams: '0',
      maxWeightGrams: '500',
      minOrderValue: '0',
      maxOrderValue: '',
      baseRate: '0',
      perKgRate: '0',
      freeAbove: '',
      codFee: '0',
      etaMinDays: '2',
      etaMaxDays: '5',
    });
    this.rateDrawerOpen.set(true);
  }

  protected startEditRate(zone: ZoneView, rate: ShippingRateResponse): void {
    this.rateZone.set(zone);
    this.editingRate.set(rate);
    this.isCodAllowed.set(rate.isCodAllowed);
    this.rateIsActive.set(rate.isActive);
    this.rateSummary.set([]);
    this.rateForm.reset({
      method: rate.method,
      minWeightGrams: String(rate.minWeightGrams),
      maxWeightGrams: String(rate.maxWeightGrams),
      minOrderValue: String(rate.minOrderValue),
      maxOrderValue: rate.maxOrderValue === null ? '' : String(rate.maxOrderValue),
      baseRate: String(rate.baseRate),
      perKgRate: String(rate.perKgRate),
      freeAbove: rate.freeAbove === null ? '' : String(rate.freeAbove),
      codFee: String(rate.codFee),
      etaMinDays: String(rate.etaMinDays),
      etaMaxDays: String(rate.etaMaxDays),
    });
    this.rateDrawerOpen.set(true);
  }

  /**
   * Writes the band.
   *
   * The zone and the seller are not on the form. A band belongs to the zone it was opened from, and
   * a per-seller override is a different decision on a different screen — offering a seller field
   * here would let an operator create one by accident and then wonder why the default stopped
   * applying to everybody.
   */
  protected saveRate(): void {
    if (!this.rateForm.submit() || this.saving()) return;

    const zone = this.rateZone();
    if (!zone) return;

    const values = this.rateForm.values();
    const existing = this.editingRate();

    const body: RateBody = {
      zoneId: zone.id,
      method: values.method,
      vendorId: existing?.vendorId ?? null,
      terms: {
        minWeightGrams: Number(values.minWeightGrams) || 0,
        maxWeightGrams: Number(values.maxWeightGrams) || 0,
        minOrderValue: Number(values.minOrderValue) || 0,

        // Blank is "no ceiling", which is a different band from one capped at zero. An empty string
        // coerced through Number() would be exactly that mistake.
        maxOrderValue: values.maxOrderValue.trim() === '' ? null : Number(values.maxOrderValue),
        baseRate: Number(values.baseRate) || 0,
        perKgRate: Number(values.perKgRate) || 0,
        freeAbove: values.freeAbove.trim() === '' ? null : Number(values.freeAbove),
        codFee: this.isCodAllowed() ? Number(values.codFee) || 0 : 0,
        isCodAllowed: this.isCodAllowed(),
        etaMinDays: Number(values.etaMinDays) || 0,
        etaMaxDays: Number(values.etaMaxDays) || 0,
      },
      isActive: this.rateIsActive(),
    };

    this.saving.set(true);
    this.rateSummary.set([]);

    const request = existing ? this.shipping.updateRate(existing.id, body) : this.shipping.createRate(body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.rateDrawerOpen.set(false);
        this.toasts.success(existing ? 'Rate band saved.' : 'Rate band created.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const errors = fieldErrors(error);
        this.rateSummary.set(
          errors
            ? this.rateForm.applyServerErrors(errors)
            : [describeError(error, 'That band could not be saved.')],
        );
      },
    });
  }

  protected startEdit(zone: ZoneView): void {
    this.editing.set(zone);
    this.states.set(zone.states.join('\n'));
    this.ranges.set(zone.pincodeRanges.map((range) => `${range.from}-${range.to}`).join('\n'));
    this.isActive.set(zone.isActive);
    this.summary.set([]);
    this.form.reset({ name: zone.name, code: zone.code, priority: String(zone.priority) });
    this.drawerOpen.set(true);
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const body: ZoneBody = {
      code: values.code,
      name: values.name,
      priority: Number(values.priority) || 0,
      states: lines(this.states()),
      pincodeRanges: parseRanges(this.ranges()),
      isActive: this.isActive(),
    };

    this.saving.set(true);
    this.summary.set([]);

    const existing = this.editing();
    const request = existing ? this.shipping.updateZone(existing.id, body) : this.shipping.createZone(body);

    request.subscribe({
      next: () => {
        this.saving.set(false);
        this.drawerOpen.set(false);
        this.toasts.success(existing ? 'Zone saved.' : 'Zone created.');
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const errors = fieldErrors(error);
        this.summary.set(
          errors ? this.form.applyServerErrors(errors) : [describeError(error, 'It could not be saved.')],
        );
      },
    });
  }

  /**
   * Fetches the zones and the whole rate card, then joins them.
   *
   * Two requests rather than one per zone: the rate endpoint answers every band when it is asked
   * without a zone, and a store with eight zones would otherwise make nine requests to draw one
   * page.
   */
  private load(): void {
    this.loading.set(true);
    this.loadError.set(null);

    this.shipping.zones(true).subscribe({
      next: (zones) => {
        this.shipping.rates(undefined, true).subscribe({
          next: (rates) => {
            this.loading.set(false);
            this.zones.set(
              [...zones]
                .sort((left, right) => left.priority - right.priority)
                .map((zone) => ({
                  ...zone,
                  rates: rates.filter((rate) => rate.zoneId === zone.id),
                })),
            );
          },
          error: (error: unknown) => {
            this.loading.set(false);
            // The zones are still worth drawing; the bands are what could not be fetched.
            this.zones.set(zones.map((zone) => ({ ...zone, rates: [] })));
            this.loadError.set(describeError(error, 'The rate card could not be loaded.'));
          },
        });
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'They could not be loaded.'));
      },
    });

    this.shipping.coverage().subscribe({
      next: (coverage) => this.coverage.set(coverage),
      error: () => this.coverage.set(null),
    });
  }
}

function lines(value: string): string[] {
  return value
    .split('\n')
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
}

/** `500001-500099` per line. A line that is not a pair is dropped rather than sent malformed. */
function parseRanges(value: string): PincodeRangeModel[] {
  return lines(value)
    .map((entry) => entry.split('-').map((part) => part.trim()))
    .filter((parts) => parts.length === 2 && parts[0].length > 0 && parts[1].length > 0)
    .map(([from, to]) => ({ from, to }));
}
