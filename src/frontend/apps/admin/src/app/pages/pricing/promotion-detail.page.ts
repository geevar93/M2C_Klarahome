import { ChangeDetectionStrategy, Component, WritableSignal, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { ActivatedRoute, Router } from '@angular/router';
import {
  CatalogAdminService,
  CategoryNode,
  PricingAdminService,
  PromotionApplication,
  PromotionBody,
  PromotionConditionsPayload,
  PromotionResponse,
  PromotionScopePayload,
  PromotionTierPayload,
  PromotionType,
  QuoteLinePayload,
  QuotePaymentMethod,
  QuoteResult,
  ReferenceDataService,
  StackingMode,
  VendorsAdminService,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  ConfirmDialog,
  DataTable,
  DataTableColumn,
  EntityOption,
  EntityPicker,
  FormShell,
  PageHeader,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { Observable, map } from 'rxjs';

import { ToastService, formField, formGroup, required } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';
import { tableDateTime, tableMoney } from '../../core/format';
import {
  PAYMENT_METHODS,
  PROMOTION_APPLICATIONS,
  PROMOTION_TYPES,
  STACKING_MODES,
} from './promotion-vocabulary';

/** A basket the merchandiser invents to try the rule on. */
interface SimulationLine {
  listingId: string;
  quantity: string;
}

/** The literal path segment that means "this promotion does not exist yet". */
const NEW = 'new';

/**
 * The rule builder, and the simulator that keeps it honest.
 *
 * **The simulator is the point of this screen.** A promotion is a small program — a mechanic, a
 * scope, a set of conditions, a stacking rule and a priority — and the only way to know what it
 * does is to run it. `POST /admin/promotions/simulate` runs it through **the same quote engine
 * that will run at checkout** (Step 12's `IPriceQuoteEngine`), so what this screen shows is not a
 * client-side approximation of the discount: it is the discount. Nothing is saved by simulating,
 * and the promotion does not have to exist, which is what makes it usable *while* writing a rule
 * rather than after.
 *
 * The quote comes back whole — every line, every promotion the engine considered **with its reason
 * for not applying**, the tax and the totals — and the reasons are rendered, because "my coupon
 * did nothing" is the question this screen exists to answer and the engine already knows why.
 *
 * **The form shows the fields the chosen mechanic actually uses.** Buy-one-get-one has a buy
 * quantity and a get quantity; a tiered discount has tiers; a bundle has a listing set and a
 * price. They all live in one `conditions` object on the API, and showing all of it at once would
 * be a form of thirty fields of which twenty-four are ignored. What is hidden is not sent.
 *
 * **A new promotion and an existing one are the same screen**, reached as `/promotions/new`. A
 * builder that could not be used until after a create dialogue had captured six fields would be
 * two forms with one set of rules between them.
 */
@Component({
  selector: 'kh-promotion-detail-page',
  imports: [
    Alert,
    Badge,
    Button,
    Checkbox,
    ConfirmDialog,
    Control,
    DataTable,
    EntityPicker,
    Field,
    FormShell,
    HasPermission,
    Icon,
    PageHeader,
    Skeleton,
  ],
  template: `
    <kh-page-header
      [heading]="isNew() ? 'New promotion' : (promotion()?.name ?? 'Promotion')"
      [description]="subtitle()"
      [crumbs]="[{ label: 'Promotions', path: '/promotions' }]"
    >
      @if (promotion(); as current) {
        <kh-badge [tone]="current.isActive ? 'success' : 'neutral'">
          {{ current.isActive ? 'Switched on' : 'Switched off' }}
        </kh-badge>
        <ng-container *khHasPermission="'pricing.promotion.manage'">
          <button khButton type="button" size="sm" [disabled]="busy()" (click)="toggle(current)">
            {{ current.isActive ? 'Switch off' : 'Switch on' }}
          </button>
          <button khButton type="button" size="sm" variant="danger" (click)="deleting.set(true)">
            Delete
          </button>
        </ng-container>
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This promotion could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="20rem" />
    } @else {
      <div class="layout">
        <kh-form-shell
          heading="The rule"
          description="What it takes off, from what, and when."
          [summary]="summary()"
          [saving]="busy()"
          [dirty]="true"
          [submitLabel]="isNew() ? 'Create promotion' : 'Save changes'"
          (submitted)="save()"
          (cancelled)="back()"
        >
          <kh-field label="Name" for="promo-name" [error]="form.fields.name.error()">
            <input
              khControl
              id="promo-name"
              type="text"
              maxlength="160"
              [value]="form.fields.name.value()"
              (input)="form.fields.name.set($any($event.target).value)"
              (touched)="form.fields.name.markTouched()"
            />
          </kh-field>

          <kh-field
            label="Coupon code"
            for="promo-code"
            [optional]="true"
            hint="Left blank, the rule applies on its own with nothing to type."
            [error]="form.fields.code.error()"
          >
            <input
              khControl
              id="promo-code"
              type="text"
              maxlength="40"
              [value]="form.fields.code.value()"
              (input)="form.fields.code.set($any($event.target).value)"
            />
          </kh-field>

          <kh-field label="Description" for="promo-description" [optional]="true">
            <textarea
              khControl
              id="promo-description"
              rows="2"
              [value]="form.fields.description.value()"
              (input)="form.fields.description.set($any($event.target).value)"
            ></textarea>
          </kh-field>

          <kh-field label="Mechanic" for="promo-type" [hint]="typeHint()">
            <select khControl id="promo-type" [value]="type()" (change)="type.set($any($event.target).value)">
              @for (choice of types; track choice.value) {
                <option [value]="choice.value">{{ choice.label }}</option>
              }
            </select>
          </kh-field>

          <kh-field label="Applies to" for="promo-applies" [hint]="applicationHint()">
            <select
              khControl
              id="promo-applies"
              [value]="appliesTo()"
              (change)="appliesTo.set($any($event.target).value)"
            >
              @for (choice of applications; track choice.value) {
                <option [value]="choice.value">{{ choice.label }}</option>
              }
            </select>
          </kh-field>

          @if (type() !== 'FreeShipping') {
            <kh-field
              [label]="type() === 'Percentage' ? 'Percentage off' : 'Amount off'"
              for="promo-value"
              [error]="form.fields.value.error()"
            >
              <input
                khControl
                id="promo-value"
                type="number"
                min="0"
                step="0.01"
                [value]="form.fields.value.value()"
                (input)="form.fields.value.set($any($event.target).value)"
              />
            </kh-field>
          }

          <!-- ---- The mechanic's own fields ---- -->

          @if (type() === 'Bogo') {
            <div class="row">
              <kh-field label="Buy this many" for="promo-buy">
                <input
                  khControl
                  id="promo-buy"
                  type="number"
                  min="1"
                  [value]="buyQuantity()"
                  (input)="buyQuantity.set($any($event.target).value)"
                />
              </kh-field>
              <kh-field label="Get this many" for="promo-get">
                <input
                  khControl
                  id="promo-get"
                  type="number"
                  min="1"
                  [value]="getQuantity()"
                  (input)="getQuantity.set($any($event.target).value)"
                />
              </kh-field>
              <kh-field label="At this discount (%)" for="promo-get-pct" hint="100 makes them free.">
                <input
                  khControl
                  id="promo-get-pct"
                  type="number"
                  min="0"
                  max="100"
                  [value]="getDiscountPercent()"
                  (input)="getDiscountPercent.set($any($event.target).value)"
                />
              </kh-field>
            </div>
          }

          @if (type() === 'Bundle') {
            <kh-field
              label="Listings in the bundle"
              for="promo-bundle"
              hint="One listing id per line. Every one of them must be in the basket."
            >
              <textarea
                khControl
                id="promo-bundle"
                rows="3"
                [value]="bundleListingIds()"
                (input)="bundleListingIds.set($any($event.target).value)"
              ></textarea>
            </kh-field>
            <kh-field label="Bundle price" for="promo-bundle-price">
              <input
                khControl
                id="promo-bundle-price"
                type="number"
                min="0"
                step="0.01"
                [value]="bundlePrice()"
                (input)="bundlePrice.set($any($event.target).value)"
              />
            </kh-field>
          }

          @if (type() === 'Tiered') {
            <fieldset>
              <legend>Tiers</legend>
              <p class="hint">
                The highest tier the basket reaches is the one that applies. Values are
                {{ tiersArePercentage() ? 'percentages' : 'rupee amounts' }}.
              </p>
              <kh-checkbox
                label="Tier values are percentages"
                inputId="promo-tier-pct"
                [checked]="tiersArePercentage()"
                (checkedChange)="tiersArePercentage.set($event)"
              />
              @for (tier of tiers(); track $index) {
                <div class="row">
                  <kh-field [label]="'From basket value'" [for]="'tier-min-' + $index">
                    <input
                      khControl
                      [id]="'tier-min-' + $index"
                      type="number"
                      min="0"
                      step="0.01"
                      [value]="tier.minAmount"
                      (input)="setTier($index, 'minAmount', $any($event.target).value)"
                    />
                  </kh-field>
                  <kh-field [label]="'Take off'" [for]="'tier-value-' + $index">
                    <input
                      khControl
                      [id]="'tier-value-' + $index"
                      type="number"
                      min="0"
                      step="0.01"
                      [value]="tier.value"
                      (input)="setTier($index, 'value', $any($event.target).value)"
                    />
                  </kh-field>
                  <button khButton type="button" size="sm" variant="tertiary" (click)="removeTier($index)">
                    Remove
                  </button>
                </div>
              }
              <button khButton type="button" size="sm" (click)="addTier()">
                <kh-icon name="plus" size="sm" />
                Add a tier
              </button>
            </fieldset>
          }

          <!-- ---- Conditions ---- -->

          <fieldset>
            <legend>Conditions</legend>

            <div class="row">
              <kh-field label="Minimum basket value" for="promo-min-order" [optional]="true">
                <input
                  khControl
                  id="promo-min-order"
                  type="number"
                  min="0"
                  step="0.01"
                  [value]="form.fields.minOrderValue.value()"
                  (input)="form.fields.minOrderValue.set($any($event.target).value)"
                />
              </kh-field>
              <kh-field
                label="Most it may take off"
                for="promo-max-discount"
                [optional]="true"
                hint="Blank means uncapped."
              >
                <input
                  khControl
                  id="promo-max-discount"
                  type="number"
                  min="0"
                  step="0.01"
                  [value]="maxDiscount()"
                  (input)="maxDiscount.set($any($event.target).value)"
                />
              </kh-field>
            </div>

            <div class="row">
              <kh-field label="Minimum units" for="promo-min-qty" [optional]="true">
                <input
                  khControl
                  id="promo-min-qty"
                  type="number"
                  min="0"
                  [value]="minQuantity()"
                  (input)="minQuantity.set($any($event.target).value)"
                />
              </kh-field>
              <kh-field label="Most units it discounts" for="promo-max-qty" [optional]="true">
                <input
                  khControl
                  id="promo-max-qty"
                  type="number"
                  min="0"
                  [value]="maxQuantityPerOrder()"
                  (input)="maxQuantityPerOrder.set($any($event.target).value)"
                />
              </kh-field>
            </div>

            <kh-checkbox
              label="First order only"
              inputId="promo-first-order"
              [checked]="firstOrderOnly()"
              (checkedChange)="firstOrderOnly.set($event)"
            />

            <fieldset class="inline">
              <legend>Payment methods</legend>
              <p class="hint">None ticked means any method.</p>
              @for (method of paymentMethods; track method.value) {
                <kh-checkbox
                  [label]="method.label"
                  [inputId]="'promo-pm-' + method.value"
                  [checked]="selectedPaymentMethods().includes(method.value)"
                  (checkedChange)="togglePaymentMethod(method.value, $event)"
                />
              }
            </fieldset>
          </fieldset>

          <!-- ---- Scope ---- -->

          <fieldset>
            <legend>What it applies to</legend>
            <p class="hint">
              Everything left empty means "no restriction". An exclusion always beats an inclusion.
            </p>

            <kh-field label="Categories" for="promo-categories" [optional]="true">
              <select
                khControl
                id="promo-categories"
                multiple
                size="6"
                (change)="setCategoryIds($any($event.target))"
              >
                @for (node of categoryOptions(); track node.id) {
                  <option [value]="node.id" [selected]="categoryIds().includes(node.id)">
                    {{ node.label }}
                  </option>
                }
              </select>
            </kh-field>

            <kh-field label="Brands" for="promo-brands" [optional]="true">
              <select
                khControl
                id="promo-brands"
                multiple
                size="6"
                (change)="setBrandIds($any($event.target))"
              >
                @for (brand of brands.rows(); track brand.id) {
                  <option [value]="brand.id" [selected]="brandIds().includes(brand.id)">
                    {{ brand.name }}
                  </option>
                }
              </select>
            </kh-field>

            <!--
              A picker beside the list rather than instead of it. A scope is genuinely a *set* of
              ids, and a control that held one would be the wrong shape; the picker appends, and the
              list stays editable for the operator who has forty of them on a clipboard
              (Step 28B, deliverable 15).
            -->
            <kh-entity-picker
              label="Find a listing to include"
              inputId="promo-listing-picker"
              [optional]="true"
              hint="Adds it to the list below."
              [search]="listingSearch"
              (chose)="append(listingIds, $event?.id)"
            />

            <kh-field label="Listings" for="promo-listings" [optional]="true" hint="One listing id per line.">
              <textarea
                khControl
                id="promo-listings"
                rows="3"
                [value]="listingIds()"
                (input)="listingIds.set($any($event.target).value)"
              ></textarea>
            </kh-field>

            <kh-entity-picker
              label="Find a listing to exclude"
              inputId="promo-excluded-picker"
              [optional]="true"
              hint="Adds it to the exclusions below."
              [search]="listingSearch"
              (chose)="append(excludedListingIds, $event?.id)"
            />

            <kh-field
              label="Listings to exclude"
              for="promo-excluded"
              [optional]="true"
              hint="One listing id per line."
            >
              <textarea
                khControl
                id="promo-excluded"
                rows="2"
                [value]="excludedListingIds()"
                (input)="excludedListingIds.set($any($event.target).value)"
              ></textarea>
            </kh-field>

            <kh-entity-picker
              label="Find a seller"
              inputId="promo-vendor-picker"
              [optional]="true"
              hint="Adds them to the list below."
              [search]="vendorSearch"
              (chose)="append(vendorIds, $event?.id)"
            />

            <kh-field label="Sellers" for="promo-vendors" [optional]="true" hint="One seller id per line.">
              <textarea
                khControl
                id="promo-vendors"
                rows="2"
                [value]="vendorIds()"
                (input)="vendorIds.set($any($event.target).value)"
              ></textarea>
            </kh-field>
          </fieldset>

          <!-- ---- Schedule, stacking and limits ---- -->

          <fieldset>
            <legend>When, and how often</legend>

            <div class="row">
              <kh-field label="Starts" for="promo-starts" [error]="form.fields.startsAt.error()">
                <input
                  khControl
                  id="promo-starts"
                  type="datetime-local"
                  [value]="form.fields.startsAt.value()"
                  (input)="form.fields.startsAt.set($any($event.target).value)"
                />
              </kh-field>
              <kh-field label="Ends" for="promo-ends" [optional]="true" hint="Blank runs until switched off.">
                <input
                  khControl
                  id="promo-ends"
                  type="datetime-local"
                  [value]="endsAt()"
                  (input)="endsAt.set($any($event.target).value)"
                />
              </kh-field>
            </div>

            <div class="row">
              <kh-field label="Stacking" for="promo-stacking" [hint]="stackingHint()">
                <select
                  khControl
                  id="promo-stacking"
                  [value]="stacking()"
                  (change)="stacking.set($any($event.target).value)"
                >
                  @for (choice of stackingModes; track choice.value) {
                    <option [value]="choice.value">{{ choice.label }}</option>
                  }
                </select>
              </kh-field>
              <kh-field label="Priority" for="promo-priority" hint="Lower runs first.">
                <input
                  khControl
                  id="promo-priority"
                  type="number"
                  min="0"
                  [value]="form.fields.priority.value()"
                  (input)="form.fields.priority.set($any($event.target).value)"
                />
              </kh-field>
            </div>

            <div class="row">
              <kh-field label="Total uses allowed" for="promo-limit-total" [optional]="true">
                <input
                  khControl
                  id="promo-limit-total"
                  type="number"
                  min="0"
                  [value]="usageLimitTotal()"
                  (input)="usageLimitTotal.set($any($event.target).value)"
                />
              </kh-field>
              <kh-field label="Uses per customer" for="promo-limit-customer" [optional]="true">
                <input
                  khControl
                  id="promo-limit-customer"
                  type="number"
                  min="0"
                  [value]="usageLimitPerCustomer()"
                  (input)="usageLimitPerCustomer.set($any($event.target).value)"
                />
              </kh-field>
            </div>
          </fieldset>
        </kh-form-shell>

        <!-- ---- The simulator ---- -->

        <aside>
          <section class="panel">
            <h2>Try it on a basket</h2>
            <p class="hint">
              Run through the same engine that prices a real checkout. Nothing is saved, and the promotion
              does not have to exist yet — save it first only if you want the stored version tried rather than
              what is on screen.
            </p>

            @for (line of simulationLines(); track $index) {
              <div class="row">
                <kh-field [label]="'Listing id'" [for]="'sim-listing-' + $index">
                  <input
                    khControl
                    [id]="'sim-listing-' + $index"
                    type="text"
                    [value]="line.listingId"
                    (input)="setSimulationLine($index, 'listingId', $any($event.target).value)"
                  />
                </kh-field>
                <kh-field [label]="'Units'" [for]="'sim-qty-' + $index">
                  <input
                    khControl
                    [id]="'sim-qty-' + $index"
                    type="number"
                    min="1"
                    [value]="line.quantity"
                    (input)="setSimulationLine($index, 'quantity', $any($event.target).value)"
                  />
                </kh-field>
                <button
                  khButton
                  type="button"
                  size="sm"
                  variant="tertiary"
                  (click)="removeSimulationLine($index)"
                >
                  Remove
                </button>
              </div>
            }

            <button khButton type="button" size="sm" (click)="addSimulationLine()">
              <kh-icon name="plus" size="sm" />
              Add a line
            </button>

            <div class="row">
              <kh-field label="Coupon typed" for="sim-coupon" [optional]="true">
                <input
                  khControl
                  id="sim-coupon"
                  type="text"
                  [value]="simulationCoupon()"
                  (input)="simulationCoupon.set($any($event.target).value)"
                />
              </kh-field>
              <kh-field label="Delivery charge" for="sim-shipping">
                <input
                  khControl
                  id="sim-shipping"
                  type="number"
                  min="0"
                  step="0.01"
                  [value]="simulationShipping()"
                  (input)="simulationShipping.set($any($event.target).value)"
                />
              </kh-field>
            </div>

            <div class="row">
              <kh-field label="Payment method" for="sim-method">
                <select
                  khControl
                  id="sim-method"
                  [value]="simulationMethod()"
                  (change)="simulationMethod.set($any($event.target).value)"
                >
                  @for (method of paymentMethods; track method.value) {
                    <option [value]="method.value">{{ method.label }}</option>
                  }
                </select>
              </kh-field>
              <kh-field label="Place of supply" for="sim-state" [optional]="true">
                <select
                  khControl
                  id="sim-state"
                  [value]="simulationStateId()"
                  (change)="simulationStateId.set($any($event.target).value)"
                >
                  <option value="">The store's own state</option>
                  @for (state of states(); track state.id) {
                    <option [value]="state.id">{{ state.name }} ({{ state.code }})</option>
                  }
                </select>
              </kh-field>
            </div>

            <kh-checkbox
              label="Treat as the customer's first order"
              inputId="sim-first"
              [checked]="simulationFirstOrder()"
              (checkedChange)="simulationFirstOrder.set($event)"
            />

            <button khButton type="button" variant="primary" [disabled]="simulating()" (click)="simulate()">
              {{ simulating() ? 'Pricing…' : 'Price this basket' }}
            </button>

            @if (simulationError(); as message) {
              <kh-alert tone="danger" heading="It could not be priced">{{ message }}</kh-alert>
            }

            @if (quote(); as result) {
              <dl class="totals">
                <dt>Goods</dt>
                <dd>{{ money(result.subtotal, result.currencyCode) }}</dd>
                <dt>Discount</dt>
                <dd>−{{ money(result.discountTotal, result.currencyCode) }}</dd>
                <dt>Tax</dt>
                <dd>{{ money(result.taxTotal, result.currencyCode) }}</dd>
                <dt>Delivery</dt>
                <dd>{{ money(result.shipping, result.currencyCode) }}</dd>
                @if (result.codFee > 0) {
                  <dt>Cash handling</dt>
                  <dd>{{ money(result.codFee, result.currencyCode) }}</dd>
                }
                <dt class="grand">Customer pays</dt>
                <dd class="grand">{{ money(result.grandTotal, result.currencyCode) }}</dd>
              </dl>

              @if (result.couponRejection; as rejection) {
                <kh-alert tone="warning" heading="The coupon did nothing">{{ rejection }}</kh-alert>
              }

              <h3>What the engine considered</h3>
              @if (result.promotions.length === 0) {
                <p class="hint">No promotion matched this basket at all.</p>
              } @else {
                <ul class="considered">
                  @for (entry of result.promotions; track entry.promotionId) {
                    <li>
                      <kh-badge [tone]="entry.applied ? 'success' : 'neutral'">
                        {{ entry.applied ? 'Applied' : 'Not applied' }}
                      </kh-badge>
                      <span class="name">{{ entry.name }}</span>
                      @if (entry.applied) {
                        <span class="amount">−{{ money(entry.discountAmount, result.currencyCode) }}</span>
                      } @else if (entry.reason) {
                        <span class="reason">{{ entry.reason }}</span>
                      }
                    </li>
                  }
                </ul>
              }
            }
          </section>

          @if (!isNew()) {
            <section class="panel">
              <h2>Who has used it</h2>
              @if (redemptions(); as list) {
                <kh-data-table
                  label="Redemptions"
                  [columns]="redemptionColumns"
                  [rows]="list.rows()"
                  [rowKey]="redemptionKey"
                  [loading]="list.loading()"
                  [page]="redemptionPage()"
                  emptyMessage="Nobody has used this promotion yet."
                  (nextPage)="list.next()"
                  (previousPage)="list.previous()"
                />
              }
            </section>
          }
        </aside>
      </div>
    }

    <kh-confirm-dialog
      [open]="deleting()"
      heading="Delete this promotion"
      message="A promotion that has been redeemed cannot be deleted; switch it off instead. This cannot be undone."
      confirmLabel="Delete"
      [confirmPhrase]="promotion()?.name ?? null"
      [busy]="busy()"
      (confirmed)="remove()"
      (cancelled)="deleting.set(false)"
    />
  `,
  styles: `
    kh-alert {
      margin-block: var(--space-4);
    }

    .layout {
      display: grid;
      gap: var(--space-6);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 60rem) {
      .layout {
        grid-template-columns: minmax(0, 3fr) minmax(22rem, 2fr);
        align-items: start;
      }
    }

    aside {
      display: flex;
      flex-direction: column;
      gap: var(--space-4);
    }

    .panel {
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .panel h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
    }

    .panel h3 {
      margin: var(--space-4) 0 var(--space-2);
      font-size: var(--text-base);
    }

    fieldset {
      margin-block: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    fieldset.inline {
      margin-block-end: 0;
    }

    legend {
      padding-inline: var(--space-2);
      font-weight: var(--weight-medium);
    }

    .row {
      display: flex;
      gap: var(--space-3);
      align-items: flex-end;
      flex-wrap: wrap;
    }

    .row > kh-field {
      flex: 1 1 10rem;
    }

    .hint {
      margin: 0 0 var(--space-3);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .totals {
      display: grid;
      grid-template-columns: 1fr auto;
      gap: var(--space-1) var(--space-4);
      margin-block: var(--space-4) 0;
    }

    .totals dt {
      color: var(--color-text-muted);
    }

    .totals dd {
      margin: 0;
      font-variant-numeric: tabular-nums;
      text-align: end;
    }

    .totals .grand {
      padding-block-start: var(--space-2);
      border-block-start: 1px solid var(--color-border);
      font-weight: var(--weight-bold);
      color: var(--color-text);
    }

    .considered {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .considered li {
      display: flex;
      gap: var(--space-2);
      align-items: baseline;
      padding-block: var(--space-2);
      border-block-end: 1px solid var(--color-border);
    }

    .considered .name {
      flex: 1;
      font-weight: var(--weight-medium);
    }

    .considered .reason {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
      text-align: end;
    }

    .considered .amount {
      font-variant-numeric: tabular-nums;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class PromotionDetailPage {
  private readonly pricing = inject(PricingAdminService);
  private readonly catalog = inject(CatalogAdminService);
  private readonly vendors = inject(VendorsAdminService);

  /** The platform's states, for the place of supply. */
  protected readonly states = toSignal(inject(ReferenceDataService).states, { initialValue: [] });
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly types = PROMOTION_TYPES;
  protected readonly applications = PROMOTION_APPLICATIONS;
  protected readonly stackingModes = STACKING_MODES;
  protected readonly paymentMethods = PAYMENT_METHODS;

  private readonly id = this.route.snapshot.paramMap.get('id') ?? NEW;

  protected readonly promotion = signal<PromotionResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly summary = signal<readonly string[]>([]);
  protected readonly deleting = signal(false);

  // The mechanic and everything that hangs off it. Held apart from the text form because a select
  // decides which of the fields below are rendered at all, and a `formField` is a string.
  protected readonly type = signal<PromotionType>('Percentage');
  protected readonly appliesTo = signal<PromotionApplication>('Line');
  protected readonly stacking = signal<StackingMode>('Exclusive');
  protected readonly endsAt = signal('');
  protected readonly maxDiscount = signal('');
  protected readonly usageLimitTotal = signal('');
  protected readonly usageLimitPerCustomer = signal('');

  protected readonly minQuantity = signal('');
  protected readonly maxQuantityPerOrder = signal('');
  protected readonly firstOrderOnly = signal(false);
  protected readonly selectedPaymentMethods = signal<readonly QuotePaymentMethod[]>([]);
  protected readonly buyQuantity = signal('');
  protected readonly getQuantity = signal('');
  protected readonly getDiscountPercent = signal('');
  protected readonly bundleListingIds = signal('');
  protected readonly bundlePrice = signal('');
  protected readonly tiers = signal<readonly PromotionTierPayload[]>([]);
  protected readonly tiersArePercentage = signal(false);

  protected readonly categoryIds = signal<readonly string[]>([]);
  protected readonly brandIds = signal<readonly string[]>([]);
  protected readonly vendorIds = signal('');

  /**
   * Adds a chosen id to one of the scope lists.
   *
   * A no-op for an id already there — choosing the same product twice is a mis-click, not an
   * instruction to list it twice, and the API would collapse the duplicate anyway.
   */
  protected append(target: WritableSignal<string>, id: string | undefined): void {
    if (!id) return;

    const lines = target()
      .split('\n')
      .map((line) => line.trim())
      .filter((line) => line.length > 0);

    if (lines.includes(id)) return;

    target.set([...lines, id].join('\n'));
  }

  /** Finds listings for the two scope pickers. */
  protected readonly listingSearch = (term: string): Observable<readonly EntityOption[]> =>
    this.catalog.searchListings(term).pipe(
      map((listings) =>
        listings.map((listing) => ({
          id: listing.id,
          label: listing.productName,
          hint: `${listing.sku} · ${listing.status}`,
        })),
      ),
    );

  /** Finds sellers for the scope picker. */
  protected readonly vendorSearch = (term: string): Observable<readonly EntityOption[]> =>
    this.vendors
      .searchVendors(term)
      .pipe(
        map((sellers) =>
          sellers.map((seller) => ({ id: seller.id, label: seller.displayName, hint: seller.code })),
        ),
      );
  protected readonly listingIds = signal('');
  protected readonly excludedListingIds = signal('');

  /** Read as a plain array: a scope picker with two hundred brands needs no paging control. */
  protected readonly brands = this.catalog.brands({}, 200);
  protected readonly categoryOptions = signal<readonly { id: string; label: string }[]>([]);

  protected readonly simulationLines = signal<readonly SimulationLine[]>([{ listingId: '', quantity: '1' }]);
  protected readonly simulationCoupon = signal('');
  protected readonly simulationShipping = signal('0');
  protected readonly simulationMethod = signal<QuotePaymentMethod>('Prepaid');
  /**
   * Where the simulated basket is delivered, which decides the GST split.
   *
   * A list rather than a typed identifier since Step 28B (deliverable 3). Blank means the store's
   * own state, which is what the quote engine falls back to.
   */
  protected readonly simulationStateId = signal('');
  protected readonly simulationFirstOrder = signal(false);
  protected readonly simulating = signal(false);
  protected readonly simulationError = signal<string | null>(null);
  protected readonly quote = signal<QuoteResult | null>(null);

  protected readonly redemptions = signal<ReturnType<PricingAdminService['redemptions']> | null>(null);

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('The name')], this.submitted),
    code: formField('', [], this.submitted),
    description: formField('', [], this.submitted),
    value: formField('0', [], this.submitted),
    minOrderValue: formField('0', [], this.submitted),
    priority: formField('100', [], this.submitted),
    startsAt: formField('', [required('A start date')], this.submitted),
  });

  protected readonly isNew = computed(() => this.id === NEW);

  protected readonly subtitle = computed(() => {
    const current = this.promotion();
    if (!current) return 'What it takes off, from what, and when.';
    return `${current.type} · used ${current.usageCount} time${current.usageCount === 1 ? '' : 's'} · created ${tableDateTime(current.createdAt)}`;
  });

  protected readonly typeHint = computed(() => hintFor(PROMOTION_TYPES, this.type()));
  protected readonly applicationHint = computed(() => hintFor(PROMOTION_APPLICATIONS, this.appliesTo()));
  protected readonly stackingHint = computed(() => hintFor(STACKING_MODES, this.stacking()));

  protected readonly redemptionPage = computed(() => {
    const list = this.redemptions();
    if (!list) return null;
    return {
      nextCursor: list.nextCursor(),
      hasPrevious: list.hasPrevious(),
      size: list.size(),
      total: list.total(),
    };
  });

  protected readonly redemptionKey = (row: { id: string }) => row.id;

  protected readonly redemptionColumns: readonly DataTableColumn<{
    orderId: string;
    discountAmount: number;
    status: string;
    redeemedAt: string;
    reversedAt: string | null;
  }>[] = [
    { key: 'orderId', label: 'Order', value: (row) => row.orderId },
    { key: 'discountAmount', label: 'Saved', kind: 'number', value: (row) => tableMoney(row.discountAmount) },
    { key: 'status', label: 'Status', value: (row) => row.status, width: '8rem' },
    { key: 'redeemedAt', label: 'When', kind: 'date', value: (row) => tableDateTime(row.redeemedAt) },
    {
      key: 'reversedAt',
      label: 'Reversed',
      kind: 'date',
      value: (row) => tableDateTime(row.reversedAt),
      hiddenByDefault: true,
    },
  ];

  constructor() {
    this.form.fields.startsAt.reset(localInput(new Date().toISOString()));
    this.loadScopeOptions();

    if (!this.isNew()) {
      this.load();
      const list = this.pricing.redemptions(this.id);
      list.load();
      this.redemptions.set(list);
    }
  }

  protected money(amount: number, currency: string): string {
    return tableMoney(amount, currency);
  }

  protected back(): void {
    void this.router.navigate(['/promotions']);
  }

  // ---- Tiers ------------------------------------------------------------------------------------

  protected addTier(): void {
    this.tiers.update((current) => [...current, { minAmount: 0, value: 0 }]);
  }

  protected removeTier(index: number): void {
    this.tiers.update((current) => current.filter((_tier, position) => position !== index));
  }

  protected setTier(index: number, field: 'minAmount' | 'value', raw: string): void {
    const parsed = Number(raw);
    this.tiers.update((current) =>
      current.map((tier, position) =>
        position === index ? { ...tier, [field]: Number.isFinite(parsed) ? parsed : 0 } : tier,
      ),
    );
  }

  // ---- Scope pickers ----------------------------------------------------------------------------

  protected setCategoryIds(select: HTMLSelectElement): void {
    this.categoryIds.set(selectedValues(select));
  }

  protected setBrandIds(select: HTMLSelectElement): void {
    this.brandIds.set(selectedValues(select));
  }

  protected togglePaymentMethod(method: QuotePaymentMethod, on: boolean): void {
    this.selectedPaymentMethods.update((current) =>
      on ? [...new Set([...current, method])] : current.filter((entry) => entry !== method),
    );
  }

  // ---- The simulator ----------------------------------------------------------------------------

  protected addSimulationLine(): void {
    this.simulationLines.update((current) => [...current, { listingId: '', quantity: '1' }]);
  }

  protected removeSimulationLine(index: number): void {
    this.simulationLines.update((current) => current.filter((_line, position) => position !== index));
  }

  protected setSimulationLine(index: number, field: keyof SimulationLine, value: string): void {
    this.simulationLines.update((current) =>
      current.map((line, position) => (position === index ? { ...line, [field]: value } : line)),
    );
  }

  protected simulate(): void {
    const lines: QuoteLinePayload[] = this.simulationLines()
      .filter((line) => line.listingId.trim().length > 0)
      .map((line) => ({
        lineId: null,
        listingId: line.listingId.trim(),
        quantity: Math.max(1, Number(line.quantity) || 1),
      }));

    if (lines.length === 0) {
      this.simulationError.set('Add at least one listing to price.');
      return;
    }

    this.simulating.set(true);
    this.simulationError.set(null);

    this.pricing
      .simulate({
        lines,
        customerId: null,
        stateId: this.simulationStateId().trim() || null,
        couponCode: this.simulationCoupon().trim() || null,
        paymentMethod: this.simulationMethod(),
        isFirstOrder: this.simulationFirstOrder(),
        shippingAmount: Number(this.simulationShipping()) || 0,
      })
      .subscribe({
        next: (result) => {
          this.simulating.set(false);
          this.quote.set(result);
        },
        error: (error: unknown) => {
          this.simulating.set(false);
          this.quote.set(null);
          this.simulationError.set(describeError(error, 'The engine refused this basket.'));
        },
      });
  }

  // ---- Save and the lifecycle -------------------------------------------------------------------

  protected save(): void {
    if (!this.form.submit() || this.busy()) return;

    this.busy.set(true);
    this.summary.set([]);

    const body = this.toBody();
    const existing = this.promotion();
    const request = existing
      ? this.pricing.updatePromotion(existing.id, body)
      : this.pricing.createPromotion(body);

    request.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.promotion.set(saved);
        this.toasts.success(existing ? 'Promotion saved.' : 'Promotion created.');
        if (!existing) void this.router.navigate(['/promotions', saved.id]);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        const errors = fieldErrors(error);
        this.summary.set(
          errors ? this.form.applyServerErrors(errors) : [describeError(error, 'It could not be saved.')],
        );
      },
    });
  }

  protected toggle(current: PromotionResponse): void {
    this.busy.set(true);
    const request = current.isActive
      ? this.pricing.deactivatePromotion(current.id)
      : this.pricing.activatePromotion(current.id);

    request.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.promotion.set(saved);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.summary.set([describeError(error, 'That could not be changed.')]);
      },
    });
  }

  protected remove(): void {
    const current = this.promotion();
    if (!current) return;

    this.busy.set(true);
    this.pricing.deletePromotion(current.id).subscribe({
      next: () => {
        this.busy.set(false);
        this.deleting.set(false);
        this.toasts.success('Promotion deleted.');
        void this.router.navigate(['/promotions']);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.deleting.set(false);
        this.summary.set([describeError(error, 'It could not be deleted.')]);
      },
    });
  }

  // ---- Loading ----------------------------------------------------------------------------------

  private load(): void {
    this.loading.set(true);
    this.pricing.promotion(this.id).subscribe({
      next: (promotion) => {
        this.loading.set(false);
        this.promotion.set(promotion);
        this.fill(promotion);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'That promotion could not be loaded.'));
      },
    });
  }

  private loadScopeOptions(): void {
    // Both are for the scope pickers, and neither is fatal: a scope that cannot offer a category
    // list is a scope somebody types listing ids into, which the form also allows.
    this.catalog.categoryTree(false).subscribe({
      next: (tree) => this.categoryOptions.set(flattenCategories(tree, 0)),
      error: () => this.categoryOptions.set([]),
    });

    this.brands.load();
  }

  private fill(promotion: PromotionResponse): void {
    this.form.reset({
      name: promotion.name,
      code: promotion.code ?? '',
      description: promotion.description ?? '',
      value: String(promotion.value),
      minOrderValue: String(promotion.minOrderValue),
      priority: String(promotion.priority),
      startsAt: localInput(promotion.startsAt),
    });

    this.type.set(promotion.type);
    this.appliesTo.set(promotion.appliesTo);
    this.stacking.set(promotion.stacking);
    this.endsAt.set(localInput(promotion.endsAt));
    this.maxDiscount.set(promotion.maxDiscount === null ? '' : String(promotion.maxDiscount));
    this.usageLimitTotal.set(promotion.usageLimitTotal === null ? '' : String(promotion.usageLimitTotal));
    this.usageLimitPerCustomer.set(
      promotion.usageLimitPerCustomer === null ? '' : String(promotion.usageLimitPerCustomer),
    );

    const conditions = promotion.conditions;
    this.minQuantity.set(conditions.minQuantity === null ? '' : String(conditions.minQuantity));
    this.maxQuantityPerOrder.set(
      conditions.maxQuantityPerOrder === null ? '' : String(conditions.maxQuantityPerOrder),
    );
    this.firstOrderOnly.set(conditions.firstOrderOnly ?? false);
    this.selectedPaymentMethods.set(conditions.paymentMethods ?? []);
    this.buyQuantity.set(conditions.buyQuantity === null ? '' : String(conditions.buyQuantity));
    this.getQuantity.set(conditions.getQuantity === null ? '' : String(conditions.getQuantity));
    this.getDiscountPercent.set(
      conditions.getDiscountPercent === null ? '' : String(conditions.getDiscountPercent),
    );
    this.bundleListingIds.set((conditions.bundleListingIds ?? []).join('\n'));
    this.bundlePrice.set(conditions.bundlePrice === null ? '' : String(conditions.bundlePrice));
    this.tiers.set(conditions.tiers ?? []);
    this.tiersArePercentage.set(conditions.tiersArePercentage ?? false);

    const scope = promotion.scope;
    this.categoryIds.set(scope.categoryIds ?? []);
    this.brandIds.set(scope.brandIds ?? []);
    this.vendorIds.set((scope.vendorIds ?? []).join('\n'));
    this.listingIds.set((scope.listingIds ?? []).join('\n'));
    this.excludedListingIds.set((scope.excludedListingIds ?? []).join('\n'));
  }

  /**
   * The body, with the fields the chosen mechanic does not use left null.
   *
   * Sending a stale `buyQuantity` on a promotion that has been switched from BOGO to a percentage
   * would leave a condition nobody can see on screen and the engine would still read it.
   */
  private toBody(): PromotionBody {
    const values = this.form.values();
    const type = this.type();

    const conditions: PromotionConditionsPayload = {
      minQuantity: optionalNumber(this.minQuantity()),
      firstOrderOnly: this.firstOrderOnly() ? true : null,
      paymentMethods: this.selectedPaymentMethods().length > 0 ? [...this.selectedPaymentMethods()] : null,
      maxQuantityPerOrder: optionalNumber(this.maxQuantityPerOrder()),
      buyQuantity: type === 'Bogo' ? optionalNumber(this.buyQuantity()) : null,
      getQuantity: type === 'Bogo' ? optionalNumber(this.getQuantity()) : null,
      getDiscountPercent: type === 'Bogo' ? optionalNumber(this.getDiscountPercent()) : null,
      bundleListingIds: type === 'Bundle' ? lines(this.bundleListingIds()) : null,
      bundlePrice: type === 'Bundle' ? optionalNumber(this.bundlePrice()) : null,
      tiers: type === 'Tiered' ? [...this.tiers()] : null,
      tiersArePercentage: type === 'Tiered' ? this.tiersArePercentage() : null,
    };

    const scope: PromotionScopePayload = {
      categoryIds: this.categoryIds().length > 0 ? [...this.categoryIds()] : null,
      brandIds: this.brandIds().length > 0 ? [...this.brandIds()] : null,
      vendorIds: lines(this.vendorIds()),
      listingIds: lines(this.listingIds()),
      excludedListingIds: lines(this.excludedListingIds()),
      segments: null,
    };

    return {
      code: values.code || null,
      name: values.name,
      description: values.description || null,
      type,
      appliesTo: this.appliesTo(),
      value: Number(values.value) || 0,
      scope,
      conditions,
      stacking: this.stacking(),
      priority: Number(values.priority) || 0,
      startsAt: new Date(values.startsAt).toISOString(),
      endsAt: this.endsAt() ? new Date(this.endsAt()).toISOString() : null,
      usageLimitTotal: optionalNumber(this.usageLimitTotal()),
      usageLimitPerCustomer: optionalNumber(this.usageLimitPerCustomer()),
      minOrderValue: Number(values.minOrderValue) || 0,
      maxDiscount: optionalNumber(this.maxDiscount()),
    };
  }
}

function hintFor(choices: readonly { value: string; hint?: string }[], value: string): string {
  return choices.find((choice) => choice.value === value)?.hint ?? '';
}

function selectedValues(select: HTMLSelectElement): readonly string[] {
  return Array.from(select.selectedOptions).map((option) => option.value);
}

/** A textarea of identifiers, one per line. Null rather than an empty array means "unrestricted". */
function lines(value: string): string[] | null {
  const entries = value
    .split('\n')
    .map((entry) => entry.trim())
    .filter((entry) => entry.length > 0);
  return entries.length > 0 ? entries : null;
}

function optionalNumber(value: string): number | null {
  const trimmed = value.trim();
  if (trimmed.length === 0) return null;
  const parsed = Number(trimmed);
  return Number.isFinite(parsed) ? parsed : null;
}

/**
 * An ISO instant as `datetime-local` wants it.
 *
 * The input has no time zone, so the value has to be the local wall clock — which is the browser's
 * and, for this platform's operators, `Asia/Kolkata`. `new Date(value)` on the way back converts
 * it to the instant the server stores.
 */
function localInput(value: string | null): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

/** The category tree flattened into a select, with depth shown by indentation. */
function flattenCategories(nodes: readonly CategoryNode[], depth: number): { id: string; label: string }[] {
  return nodes.flatMap((node) => [
    { id: node.id, label: `${'— '.repeat(depth)}${node.name}` },
    ...flattenCategories(node.children ?? [], depth + 1),
  ]);
}
