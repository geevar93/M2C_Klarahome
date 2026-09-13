import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import {
  AttributeResponse,
  AttributeValuePayload,
  CatalogAdminService,
  CategoryNode,
  ListingResponse,
  MediaPayload,
  ProductBody,
  ProductResponse,
  SpecificationPayload,
  VariantResponse,
  VendorsAdminService,
} from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import {
  AuditTrail,
  ConfirmDialog,
  EntityOption,
  EntityPicker,
  FormShell,
  HasUnsavedChanges,
  PageHeader,
  StatusBadge,
  toneFor,
} from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Icon, Skeleton } from '@klarahome/ui-primitives';
import { AuditLogService } from '@klarahome/data-access-admin';
import { ToastService, formField, formGroup, required } from '@klarahome/util';
import { Observable, catchError, forkJoin, map, of, switchMap } from 'rxjs';

import { toAuditEntry } from '../../core/audit.mapper';
import { describeError, fieldErrors } from '../../core/describe-error';
import { tableMoney } from '../../core/format';
import { MediaManager } from './media-manager';
import { VariantEditor } from './variant-editor';

/** A category flattened out of the tree, with its depth, for a `<select>`. */
interface CategoryOption {
  readonly id: string;
  readonly label: string;
}

/**
 * The product editor.
 *
 * One screen for the whole aggregate — details, compliance, attributes, media, variants and the
 * sellers' offers against it — because they are one record and a tab per section would make
 * "what state is this product in" a question you have to click four times to answer.
 *
 * The parts that are decisions:
 *
 * **The compliance fields are not optional and are not buried.** HSN code, GST rate, country of
 * origin and the manufacturer/packer/importer details are Legal Metrology and GST requirements for
 * anything sold in India (docs/02-domain-model.md), and `ProductResponse.complianceGaps` is the
 * API's own list of what is still missing. It is rendered as a warning at the top rather than as
 * an error at save time, because a draft is allowed to be incomplete and a *published* product is
 * not — which is exactly when the API will refuse it.
 *
 * **The lifecycle buttons come off the status, and each is a different permission.** Submit,
 * approve, reject, publish, unpublish and archive are six endpoints; the header shows only the
 * ones the product's current status allows (`Product.IsTransitionAllowed`), and `*khHasPermission`
 * hides the ones this user cannot use. Six buttons at once, most of them answering 409 or 403, is
 * what this screen used to be.
 *
 * **One save.** The images go to their own endpoint, but "Save changes" sends them after the
 * details when they changed. A separate "Save images" button was the one that got missed.
 *
 * **A new product is the same screen with less of it.** Variants, media and offers need a product
 * id, so they appear once there is one. The alternative — a separate "create" page — is the same
 * ninety fields written twice.
 */
@Component({
  selector: 'kh-product-detail-page',
  imports: [
    Alert,
    AuditTrail,
    Badge,
    Button,
    Checkbox,
    ConfirmDialog,
    Control,
    EntityPicker,
    Field,
    FormShell,
    HasPermission,
    Icon,
    MediaManager,
    PageHeader,
    Skeleton,
    StatusBadge,
    VariantEditor,
  ],
  template: `
    <kh-page-header
      [heading]="product()?.name || (isNew() ? 'New product' : 'Product')"
      [crumbs]="[{ label: 'Products', path: '/catalog/products' }]"
      [description]="product()?.slug ?? null"
    >
      @if (product(); as current) {
        <kh-status-badge [status]="current.status" />

        <!-- One row of buttons, and only the moves the state machine allows from here
             (Product.IsTransitionAllowed): a draft is submitted or published, a submission is
             approved or rejected, a live product is unpublished, a withdrawn one re-published. -->
        @if (current.status === 'Draft') {
          <button
            *khHasPermission="'catalog.product.manage'"
            khButton
            type="button"
            size="sm"
            variant="primary"
            [disabled]="busy()"
            (click)="run(catalog.submitProduct(current.id), 'Sent for review.')"
          >
            Submit for review
          </button>
        }

        @if (current.status === 'PendingApproval') {
          <button
            *khHasPermission="'catalog.product.moderate'"
            khButton
            type="button"
            size="sm"
            variant="primary"
            [disabled]="busy()"
            (click)="run(catalog.approveProduct(current.id, null), 'Approved.')"
          >
            Approve
          </button>

          <button
            *khHasPermission="'catalog.product.moderate'"
            khButton
            type="button"
            size="sm"
            variant="danger"
            [disabled]="busy()"
            (click)="rejecting.set(true)"
          >
            Reject
          </button>
        }

        @if (current.status === 'Draft' || current.status === 'Inactive') {
          <button
            *khHasPermission="'catalog.product.moderate'"
            khButton
            type="button"
            size="sm"
            [variant]="current.status === 'Inactive' ? 'primary' : 'secondary'"
            [disabled]="busy()"
            (click)="run(catalog.publishProduct(current.id), 'Live on the storefront.')"
          >
            {{ current.status === 'Inactive' ? 'Publish again' : 'Publish now' }}
          </button>
        }

        @if (current.status === 'Active') {
          <button
            *khHasPermission="'catalog.product.moderate'"
            khButton
            type="button"
            size="sm"
            [disabled]="busy()"
            (click)="run(catalog.unpublishProduct(current.id), 'Taken off the storefront.')"
          >
            Unpublish
          </button>
        }

        @if (current.status !== 'Archived') {
          <button
            *khHasPermission="'catalog.product.manage'"
            khButton
            type="button"
            size="sm"
            variant="tertiary"
            [disabled]="busy()"
            (click)="archiving.set(true)"
          >
            Archive
          </button>
        }
      }
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This product could not be loaded">{{ message }}</kh-alert>
    }

    @if (actionError(); as message) {
      <kh-alert tone="danger" heading="That did not work" [dismissible]="true">{{ message }}</kh-alert>
    }

    @if (product()?.complianceGaps?.length) {
      <kh-alert tone="warning" heading="This product cannot be published yet">
        <ul class="gaps">
          @for (gap of product()!.complianceGaps; track gap) {
            <li>{{ gap }}</li>
          }
        </ul>
      </kh-alert>
    }

    @if (loading()) {
      <kh-skeleton height="20rem" />
    } @else {
      <kh-form-shell
        heading="Product details"
        description="What the product is, and everything the law requires us to say about it."
        [summary]="summary()"
        [saving]="saving()"
        [dirty]="dirty()"
        [submitLabel]="isNew() ? 'Create product' : 'Save changes'"
        (submitted)="save()"
        (cancelled)="back()"
      >
        <kh-field label="Name" for="product-name" [error]="form.fields.name.error()">
          <input
            khControl
            id="product-name"
            type="text"
            maxlength="200"
            [value]="form.fields.name.value()"
            (input)="onEdit(); form.fields.name.set($any($event.target).value)"
            (touched)="form.fields.name.markTouched()"
          />
        </kh-field>

        <kh-field
          label="URL slug"
          for="product-slug"
          [optional]="true"
          hint="Left blank, it is made from the name. Changing it changes the product's address."
        >
          <input
            khControl
            id="product-slug"
            type="text"
            [value]="form.fields.slug.value()"
            (input)="onEdit(); form.fields.slug.set($any($event.target).value)"
          />
        </kh-field>

        <div class="pair">
          <kh-field label="Category" for="product-category" [error]="form.fields.categoryId.error()">
            <select
              khControl
              id="product-category"
              [value]="form.fields.categoryId.value()"
              (change)="onEdit(); form.fields.categoryId.set($any($event.target).value)"
            >
              <option value="">Choose a category</option>
              @for (option of categoryOptions(); track option.id) {
                <option [value]="option.id" [selected]="option.id === form.fields.categoryId.value()">
                  {{ option.label }}
                </option>
              }
            </select>
          </kh-field>

          <!-- \`[selected]\` on each option as well as \`[value]\` on the select: the brands and
               categories arrive after the product does, and a \`<select>\` given a value it has
               no option for yet drops it — the product looked brandless on every reload. -->
          <kh-field label="Brand" for="product-brand" [optional]="true">
            <select
              khControl
              id="product-brand"
              [value]="form.fields.brandId.value()"
              (change)="onEdit(); form.fields.brandId.set($any($event.target).value)"
            >
              <option value="">No brand</option>
              @for (brand of brandOptions(); track brand.id) {
                <option [value]="brand.id" [selected]="brand.id === form.fields.brandId.value()">
                  {{ brand.name }}
                </option>
              }
            </select>
          </kh-field>
        </div>

        <kh-field label="Short description" for="product-short" [optional]="true">
          <textarea
            khControl
            id="product-short"
            rows="2"
            maxlength="500"
            [value]="form.fields.shortDescription.value()"
            (input)="onEdit(); form.fields.shortDescription.set($any($event.target).value)"
          ></textarea>
        </kh-field>

        <kh-field label="Description" for="product-description" [optional]="true">
          <textarea
            khControl
            id="product-description"
            rows="6"
            [value]="form.fields.description.value()"
            (input)="onEdit(); form.fields.description.set($any($event.target).value)"
          ></textarea>
        </kh-field>

        <fieldset>
          <legend>Tax and origin</legend>
          <div class="pair">
            <kh-field label="HSN code" for="product-hsn" [error]="form.fields.hsnCode.error()">
              <input
                khControl
                khNumeric
                id="product-hsn"
                type="text"
                maxlength="8"
                [value]="form.fields.hsnCode.value()"
                (input)="onEdit(); form.fields.hsnCode.set($any($event.target).value)"
                (touched)="form.fields.hsnCode.markTouched()"
              />
            </kh-field>

            <kh-field label="GST rate (%)" for="product-gst" [error]="form.fields.gstRate.error()">
              <input
                khControl
                khNumeric
                id="product-gst"
                type="number"
                min="0"
                max="28"
                step="0.01"
                [value]="form.fields.gstRate.value()"
                (input)="onEdit(); form.fields.gstRate.set($any($event.target).value)"
                (touched)="form.fields.gstRate.markTouched()"
              />
            </kh-field>
          </div>

          <kh-field
            label="Country of origin"
            for="product-origin"
            [error]="form.fields.countryOfOrigin.error()"
          >
            <input
              khControl
              id="product-origin"
              type="text"
              maxlength="60"
              [value]="form.fields.countryOfOrigin.value()"
              (input)="onEdit(); form.fields.countryOfOrigin.set($any($event.target).value)"
              (touched)="form.fields.countryOfOrigin.markTouched()"
            />
          </kh-field>
        </fieldset>

        <fieldset>
          <legend>Who made it, packed it and imported it</legend>
          <p class="hint">
            Legal Metrology requires the manufacturer for anything made here and the importer for anything
            brought in. One line each: name, address, contact.
          </p>

          @for (party of parties; track party.key) {
            <div class="triple">
              <kh-field
                [label]="party.label + ' name'"
                [for]="'party-' + party.key + '-name'"
                [optional]="partyOptional(party.key, 'name')"
              >
                <input
                  khControl
                  [id]="'party-' + party.key + '-name'"
                  type="text"
                  [value]="partyValue(party.key, 'name')"
                  (input)="setParty(party.key, 'name', $any($event.target).value)"
                />
              </kh-field>
              <kh-field
                [label]="party.label + ' address'"
                [for]="'party-' + party.key + '-address'"
                [optional]="partyOptional(party.key, 'address')"
              >
                <input
                  khControl
                  [id]="'party-' + party.key + '-address'"
                  type="text"
                  [value]="partyValue(party.key, 'address')"
                  (input)="setParty(party.key, 'address', $any($event.target).value)"
                />
              </kh-field>
              <kh-field
                [label]="party.label + ' contact'"
                [for]="'party-' + party.key + '-contact'"
                [optional]="true"
              >
                <input
                  khControl
                  [id]="'party-' + party.key + '-contact'"
                  type="text"
                  [value]="partyValue(party.key, 'contact')"
                  (input)="setParty(party.key, 'contact', $any($event.target).value)"
                />
              </kh-field>
            </div>
          }
        </fieldset>

        <fieldset>
          <legend>Returns and warranty</legend>
          <kh-checkbox
            label="This product can be returned"
            inputId="product-returnable"
            [checked]="isReturnable()"
            (checkedChange)="onEdit(); isReturnable.set($event)"
          />

          <div class="pair">
            <kh-field
              label="Return window (days)"
              for="product-window"
              [optional]="true"
              hint="Blank uses the store's own window."
            >
              <input
                khControl
                khNumeric
                id="product-window"
                type="number"
                min="0"
                [disabled]="!isReturnable()"
                [value]="form.fields.returnWindowDays.value()"
                (input)="onEdit(); form.fields.returnWindowDays.set($any($event.target).value)"
              />
            </kh-field>
            <kh-field label="Warranty" for="product-warranty" [optional]="true">
              <input
                khControl
                id="product-warranty"
                type="text"
                [value]="form.fields.warranty.value()"
                (input)="onEdit(); form.fields.warranty.set($any($event.target).value)"
              />
            </kh-field>
          </div>
        </fieldset>

        @if (attributes().length > 0) {
          <fieldset>
            <legend>Attributes</legend>
            @for (attribute of attributes(); track attribute.id) {
              <kh-field
                [label]="attribute.name + (attribute.unit ? ' (' + attribute.unit + ')' : '')"
                [for]="'attr-' + attribute.id"
                [optional]="!attribute.isRequired"
              >
                @if (attribute.dataType === 'Select' || attribute.dataType === 'MultiSelect') {
                  <select
                    khControl
                    [id]="'attr-' + attribute.id"
                    [value]="attributeValue(attribute.id)"
                    (change)="setAttribute(attribute, $any($event.target).value)"
                  >
                    <option value="">Not set</option>
                    @for (option of attribute.options; track option.id) {
                      <option [value]="option.id">{{ option.label }}</option>
                    }
                  </select>
                } @else {
                  <input
                    khControl
                    [id]="'attr-' + attribute.id"
                    [type]="
                      attribute.dataType === 'Number'
                        ? 'number'
                        : attribute.dataType === 'Date'
                          ? 'date'
                          : 'text'
                    "
                    [value]="attributeValue(attribute.id)"
                    (input)="setAttribute(attribute, $any($event.target).value)"
                  />
                }
              </kh-field>
            }
          </fieldset>
        }

        <fieldset>
          <legend>Specifications</legend>
          <p class="hint">The table on the product page. Group is optional and sorts them together.</p>

          @for (spec of specifications(); track $index; let index = $index) {
            <div class="triple">
              <kh-field label="Label" [for]="'spec-label-' + index">
                <input
                  khControl
                  [id]="'spec-label-' + index"
                  type="text"
                  [value]="spec.label"
                  (input)="setSpec(index, 'label', $any($event.target).value)"
                />
              </kh-field>
              <kh-field label="Value" [for]="'spec-value-' + index">
                <input
                  khControl
                  [id]="'spec-value-' + index"
                  type="text"
                  [value]="spec.value"
                  (input)="setSpec(index, 'value', $any($event.target).value)"
                />
              </kh-field>
              <div class="spec-remove">
                <kh-field label="Group" [for]="'spec-group-' + index" [optional]="true">
                  <input
                    khControl
                    [id]="'spec-group-' + index"
                    type="text"
                    [value]="spec.group ?? ''"
                    (input)="setSpec(index, 'group', $any($event.target).value)"
                  />
                </kh-field>
                <button khButton type="button" size="sm" variant="danger" (click)="removeSpec(index)">
                  <kh-icon name="trash" size="sm" />
                </button>
              </div>
            </div>
          }

          <button khButton type="button" size="sm" (click)="addSpec()">
            <kh-icon name="plus" size="sm" />
            Add a specification
          </button>
        </fieldset>

        <fieldset>
          <legend>Search engines</legend>
          <kh-field label="Meta title" for="product-meta-title" [optional]="true">
            <input
              khControl
              id="product-meta-title"
              type="text"
              maxlength="120"
              [value]="form.fields.metaTitle.value()"
              (input)="onEdit(); form.fields.metaTitle.set($any($event.target).value)"
            />
          </kh-field>
          <kh-field label="Meta description" for="product-meta-description" [optional]="true">
            <textarea
              khControl
              id="product-meta-description"
              rows="2"
              maxlength="320"
              [value]="form.fields.metaDescription.value()"
              (input)="onEdit(); form.fields.metaDescription.set($any($event.target).value)"
            ></textarea>
          </kh-field>
          <kh-checkbox
            label="Keep this product out of search engines"
            inputId="product-noindex"
            [checked]="noIndex()"
            (checkedChange)="onEdit(); noIndex.set($event)"
          />
        </fieldset>
      </kh-form-shell>

      @if (product(); as current) {
        <section class="panel">
          <h2>Images</h2>
          <p class="hint">Saved together with the details above by "Save changes".</p>
          <kh-media-manager
            [media]="media()"
            (mediaChange)="onMediaChange($event)"
            [urls]="mediaUrls()"
            idPrefix="product"
            ownerType="Product"
            [ownerId]="current.id"
          />
        </section>

        <section class="panel">
          <div class="panel-head">
            <h2>Variants</h2>
            <button khButton type="button" size="sm" (click)="editVariant(null)">
              <kh-icon name="plus" size="sm" />
              Add a variant
            </button>
          </div>

          @if (current.variants.length === 0) {
            <p class="hint">
              No variants. A product needs at least one before a seller can list it — a product with a single
              size is one variant, not none.
            </p>
          } @else {
            <table>
              <thead>
                <tr>
                  <th scope="col">SKU</th>
                  <th scope="col">Name</th>
                  <th scope="col">Status</th>
                  <th scope="col" class="numeric">MRP</th>
                  <th scope="col" class="numeric">Weight</th>
                  <th scope="col"><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (variant of current.variants; track variant.id) {
                  <tr>
                    <td>{{ variant.sku }}</td>
                    <td>{{ variant.nameSuffix || '—' }}</td>
                    <td>
                      <kh-badge [tone]="tone(variant.status)">{{ variant.status }}</kh-badge>
                    </td>
                    <td class="numeric">{{ money(variant.mrp) }}</td>
                    <td class="numeric">{{ variant.weightGrams }} g</td>
                    <td>
                      <button khButton type="button" size="sm" (click)="editVariant(variant)">Edit</button>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </section>

        <section class="panel">
          <div class="panel-head">
            <h2>Offers</h2>
            <button
              *khHasPermission="'catalog.listing.manage'"
              khButton
              type="button"
              size="sm"
              (click)="addingOffer() && !editingOffer() ? cancelOffer() : startOffer()"
            >
              <kh-icon name="plus" size="sm" />
              Add an offer
            </button>
          </div>
          <p class="hint">
            What sellers are charging for this product. The buy box picks one of them. The storefront shows
            the winning offer's selling price, so this is where a listed price is changed.
          </p>

          @if (addingOffer()) {
            <div class="offer-form">
              @if (editingOffer(); as listing) {
                <p class="hint">
                  Editing the offer on <strong>{{ listing.sku }}</strong>. The variant and the seller stay as
                  they are; a live offer's new price reaches the storefront as soon as it is saved.
                </p>
              } @else {
                <kh-field label="Variant" for="offer-variant">
                  <select
                    khControl
                    id="offer-variant"
                    [value]="offerVariantId()"
                    (change)="offerVariantId.set($any($event.target).value)"
                  >
                    <option value="">Choose a variant</option>
                    @for (variant of current.variants; track variant.id) {
                      <option [value]="variant.id">
                        {{ variant.sku }}{{ variant.nameSuffix ? ' — ' + variant.nameSuffix : '' }}
                      </option>
                    }
                  </select>
                </kh-field>

                <kh-entity-picker
                  label="Seller"
                  inputId="offer-vendor-picker"
                  hint="Who is making this offer. Only an active seller can put an offer on the storefront."
                  [search]="vendorSearch"
                  [value]="offerVendor()"
                  (chose)="offerVendor.set($event)"
                />
              }

              <div class="pair">
                <kh-field label="Selling price" for="offer-price">
                  <input
                    khControl
                    khNumeric
                    id="offer-price"
                    type="number"
                    min="0"
                    step="0.01"
                    [value]="offerPrice()"
                    (input)="offerPrice.set($any($event.target).value)"
                  />
                </kh-field>
                <kh-field
                  label="MRP"
                  for="offer-mrp"
                  [optional]="!editingOffer()"
                  [hint]="editingOffer() ? 'The selling price may not exceed it.' : 'Blank uses the variant’s own MRP.'"
                >
                  <input
                    khControl
                    khNumeric
                    id="offer-mrp"
                    type="number"
                    min="0"
                    step="0.01"
                    [value]="offerMrp()"
                    (input)="offerMrp.set($any($event.target).value)"
                  />
                </kh-field>
              </div>

              <div class="pair">
                <kh-field label="Handling time (hours)" for="offer-handling">
                  <input
                    khControl
                    khNumeric
                    id="offer-handling"
                    type="number"
                    min="1"
                    [value]="offerHandlingHours()"
                    (input)="offerHandlingHours.set($any($event.target).value)"
                  />
                </kh-field>
                <kh-field label="Max order quantity" for="offer-max-qty" [optional]="true">
                  <input
                    khControl
                    khNumeric
                    id="offer-max-qty"
                    type="number"
                    min="1"
                    [value]="offerMaxQuantity()"
                    (input)="offerMaxQuantity.set($any($event.target).value)"
                  />
                </kh-field>
              </div>

              <kh-checkbox
                label="Accepts cash on delivery"
                inputId="offer-cod"
                [checked]="offerCodAllowed()"
                (checkedChange)="offerCodAllowed.set($event)"
              />

              @if (offerError(); as message) {
                <kh-alert
                  tone="danger"
                  [heading]="editingOffer() ? 'That offer could not be saved' : 'That offer could not be opened'"
                  [dismissible]="true"
                  >{{ message }}</kh-alert
                >
              }

              <div class="panel-actions">
                <button khButton type="button" size="sm" [disabled]="busy()" (click)="cancelOffer()">
                  Cancel
                </button>
                <button
                  khButton
                  type="button"
                  size="sm"
                  variant="primary"
                  [disabled]="busy()"
                  (click)="submitOffer()"
                >
                  {{ editingOffer() ? 'Save offer' : 'Open offer' }}
                </button>
              </div>
            </div>
          }

          @if (listings.rows().length === 0) {
            <p class="hint">No seller has listed this product yet.</p>
          } @else {
            <table>
              <thead>
                <tr>
                  <th scope="col">SKU</th>
                  <th scope="col">Status</th>
                  <th scope="col" class="numeric">MRP</th>
                  <th scope="col" class="numeric">Selling price</th>
                  <th scope="col">COD</th>
                  <th scope="col"><span class="sr-only">Actions</span></th>
                </tr>
              </thead>
              <tbody>
                @for (listing of listings.rows(); track listing.id) {
                  <tr>
                    <td>{{ listing.sku }}</td>
                    <td>
                      <kh-badge [tone]="tone(listing.status)">{{ listing.status }}</kh-badge>
                    </td>
                    <td class="numeric">{{ money(listing.mrp) }}</td>
                    <td class="numeric">{{ money(listing.sellingPrice) }}</td>
                    <td>{{ listing.isCodAllowed ? 'Yes' : 'No' }}</td>
                    <td>
                      <div class="row-actions" *khHasPermission="'catalog.listing.manage'">
                        <button
                          khButton
                          type="button"
                          size="sm"
                          [disabled]="busy()"
                          (click)="editOffer(listing)"
                        >
                          Edit
                        </button>
                        <button
                          khButton
                          type="button"
                          size="sm"
                          [disabled]="busy()"
                          (click)="toggleListing(listing)"
                        >
                          {{ listing.status === 'Active' ? 'Deactivate' : 'Activate' }}
                        </button>
                      </div>
                    </td>
                  </tr>
                }
              </tbody>
            </table>
          }
        </section>

        <section class="panel">
          <h2>History</h2>
          <kh-audit-trail [entries]="auditEntries()" [loading]="audit.loading()" />
        </section>
      }
    }

    @if (variantOpen()) {
      <kh-variant-editor
        [productId]="product()!.id"
        [editing]="variantBeingEdited()"
        [axes]="variantAxes()"
        (saved)="onVariantSaved()"
        (cancelled)="variantOpen.set(false)"
      />
    }

    <kh-confirm-dialog
      [open]="rejecting()"
      heading="Reject this product"
      message="The seller is told why. They can correct it and submit it again."
      confirmLabel="Reject"
      [requireReason]="true"
      [busy]="busy()"
      (confirmed)="reject($event.reason)"
      (cancelled)="rejecting.set(false)"
    />

    <kh-confirm-dialog
      [open]="archiving()"
      heading="Archive this product"
      [message]="'Archiving takes it off the storefront and out of search. Its orders and invoices are kept.'"
      confirmLabel="Archive"
      [confirmPhrase]="product()?.name ?? null"
      [busy]="busy()"
      (confirmed)="archive()"
      (cancelled)="archiving.set(false)"
    />
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .gaps {
      margin: 0;
      padding-inline-start: var(--space-4);
    }

    fieldset {
      margin: 0 0 var(--space-4);
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    legend {
      padding-inline: var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .hint {
      margin-block-start: 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .pair,
    .triple {
      display: grid;
      gap: var(--space-3);
    }

    .spec-remove {
      display: flex;
      gap: var(--space-2);
      align-items: flex-start;
    }

    .spec-remove kh-field {
      flex: 1;
    }

    @media (min-width: 768px) {
      .pair {
        grid-template-columns: 1fr 1fr;
      }

      .triple {
        grid-template-columns: repeat(3, 1fr);
      }
    }

    .panel {
      margin-block-start: var(--space-5);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .panel-head {
      display: flex;
      align-items: center;
      justify-content: space-between;
    }

    .offer-form {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      margin-block-end: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface);
    }

    .panel-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--space-2);
      margin-block-start: var(--space-2);
    }

    .row-actions {
      display: flex;
      justify-content: flex-end;
      gap: var(--space-2);
    }

    .panel h2 {
      margin: 0 0 var(--space-2);
      font-size: var(--text-lg);
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
    }

    .numeric {
      text-align: end;
      font-variant-numeric: tabular-nums;
    }

    .sr-only {
      position: absolute;
      inline-size: 1px;
      block-size: 1px;
      overflow: hidden;
      clip-path: inset(50%);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class ProductDetailPage implements HasUnsavedChanges {
  protected readonly catalog = inject(CatalogAdminService);
  private readonly auditLog = inject(AuditLogService);
  private readonly vendors = inject(VendorsAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  private readonly id = this.route.snapshot.paramMap.get('id') ?? 'new';
  protected readonly isNew = signal(this.id === 'new');

  protected readonly product = signal<ProductResponse | null>(null);
  protected readonly loading = signal(false);
  protected readonly saving = signal(false);
  protected readonly busy = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly actionError = signal<string | null>(null);
  protected readonly summary = signal<readonly string[]>([]);
  protected readonly dirty = signal(false);

  protected readonly attributes = signal<readonly AttributeResponse[]>([]);
  protected readonly categories = signal<readonly CategoryNode[]>([]);
  protected readonly brands = this.catalog.brands({ activeOnly: true }, 200);
  protected readonly listings = this.catalog.listings({ productId: this.id }, 50);
  protected readonly audit = this.auditLog.forEntity('Product', this.id);

  protected readonly addingOffer = signal(false);
  protected readonly offerVariantId = signal('');
  protected readonly offerVendor = signal<EntityOption | null>(null);
  protected readonly offerPrice = signal('');
  protected readonly offerMrp = signal('');
  protected readonly offerHandlingHours = signal('24');
  protected readonly offerMaxQuantity = signal('');
  protected readonly offerCodAllowed = signal(true);
  protected readonly offerError = signal<string | null>(null);
  /** The offer whose terms the form is editing, or null when it is opening a new one. */
  protected readonly editingOffer = signal<ListingResponse | null>(null);

  protected readonly media = signal<readonly MediaPayload[]>([]);
  protected readonly specifications = signal<readonly SpecificationPayload[]>([]);
  protected readonly isReturnable = signal(true);
  protected readonly noIndex = signal(false);

  protected readonly rejecting = signal(false);
  protected readonly archiving = signal(false);
  protected readonly variantOpen = signal(false);
  protected readonly variantBeingEdited = signal<VariantResponse | null>(null);

  private readonly attributeValues = signal<Readonly<Record<string, AttributeValuePayload>>>({});
  private readonly partyValues = signal<
    Readonly<Record<string, { name: string; address: string; contact: string }>>
  >({
    manufacturer: { name: '', address: '', contact: '' },
    packer: { name: '', address: '', contact: '' },
    importer: { name: '', address: '', contact: '' },
  });

  protected readonly parties = [
    { key: 'manufacturer', label: 'Manufacturer' },
    { key: 'packer', label: 'Packer' },
    { key: 'importer', label: 'Importer' },
  ] as const;

  private readonly submitted = signal(false);

  protected readonly form = formGroup(this.submitted, {
    name: formField('', [required('The name')], this.submitted),
    slug: formField('', [], this.submitted),
    categoryId: formField('', [required('A category')], this.submitted),
    brandId: formField('', [], this.submitted),
    shortDescription: formField('', [], this.submitted),
    description: formField('', [], this.submitted),
    hsnCode: formField('', [required('The HSN code')], this.submitted),
    gstRate: formField('', [required('The GST rate')], this.submitted),
    countryOfOrigin: formField('', [required('The country of origin')], this.submitted),
    returnWindowDays: formField('', [], this.submitted),
    warranty: formField('', [], this.submitted),
    metaTitle: formField('', [], this.submitted),
    metaDescription: formField('', [], this.submitted),
  });

  /** The tree flattened, indented by depth, so one `<select>` can offer the whole taxonomy. */
  protected readonly categoryOptions = computed<readonly CategoryOption[]>(() =>
    flatten(this.categories(), 0),
  );

  /**
   * The brands to offer. The list is the active ones; a product already on a brand that has since
   * been retired keeps it selectable, or the select would silently read "No brand" and the next
   * save would clear it.
   */
  protected readonly brandOptions = computed<readonly { id: string; name: string }[]>(() => {
    const rows = this.brands.rows();
    const current = this.product()?.brandId;
    if (!current || rows.some((brand) => brand.id === current)) return rows;
    return [...rows, { id: current, name: 'Current brand (no longer active)' }];
  });

  /** The image URLs the product response resolved, so a saved image renders without a resizer. */
  protected readonly mediaUrls = computed<Readonly<Record<string, string | null>>>(() =>
    Object.fromEntries((this.product()?.media ?? []).map((item) => [item.fileId, item.url])),
  );

  /** The images as last saved, so a save knows whether they need sending at all. */
  private readonly savedMedia = signal<string>('[]');

  /** Only variant-defining attributes may be a variant axis, and the API says which those are. */
  protected readonly variantAxes = computed(() =>
    this.attributes().filter((attribute) => attribute.isVariantDefining),
  );

  /** "Imported goods" per `Product.ComplianceGaps` — a country of origin declared and not India. */
  protected readonly isImported = computed(() => {
    const country = this.form.fields.countryOfOrigin.value().trim().toUpperCase();
    return country.length > 0 && country !== 'IN';
  });

  protected readonly auditEntries = computed(() => this.audit.rows().map(toAuditEntry));

  constructor() {
    this.brands.load();

    forkJoin({
      attributes: this.catalog.attributes(),
      categories: this.catalog.categoryTree(),
    }).subscribe({
      next: (reference) => {
        this.attributes.set(reference.attributes);
        this.categories.set(reference.categories);
      },
      error: (error: unknown) =>
        this.actionError.set(describeError(error, 'The categories and attributes could not be loaded.')),
    });

    if (!this.isNew()) {
      this.listings.load();
      this.audit.load();
      this.load();
    }
  }

  /** The unsaved-changes guard's question. See `unsavedChangesGuard` in `ui-admin`. */
  hasUnsavedChanges(): boolean {
    return this.dirty() && !this.saving();
  }

  protected tone(status: string) {
    return toneFor(status);
  }

  protected money(amount: number): string {
    return tableMoney(amount);
  }

  protected onEdit(): void {
    this.dirty.set(true);
  }

  protected partyValue(key: string, field: 'name' | 'address' | 'contact'): string {
    return this.partyValues()[key]?.[field] ?? '';
  }

  /**
   * Whether this party's field is safe to label "(optional)".
   *
   * Mirrors `Product.ComplianceGaps` exactly, not the general rule of thumb: the manufacturer's
   * name is required to publish regardless of origin (its address is not — `Manufacturer.IsEmpty`
   * only fires when both are blank), and the importer's name and address are required together,
   * but only once the product declares a foreign country of origin.
   */
  protected partyOptional(key: string, field: 'name' | 'address' | 'contact'): boolean {
    if (field === 'contact' || key === 'packer') return true;
    if (key === 'manufacturer') return field === 'address';
    if (key === 'importer') return !this.isImported();
    return true;
  }

  protected setParty(key: string, field: 'name' | 'address' | 'contact', value: string): void {
    this.dirty.set(true);
    this.partyValues.update((current) => ({ ...current, [key]: { ...current[key], [field]: value } }));
  }

  protected attributeValue(attributeId: string): string {
    const held = this.attributeValues()[attributeId];
    return held?.optionId ?? held?.text ?? '';
  }

  /**
   * Writes one attribute value.
   *
   * A select writes `optionId` and everything else writes `text`, because that is the shape
   * `AttributeValuePayload` has and the shape the API validates against: an option id in the text
   * field is a string that matches no option, and the product silently loses the value.
   */
  protected setAttribute(attribute: AttributeResponse, value: string): void {
    this.dirty.set(true);
    const isOption = attribute.dataType === 'Select' || attribute.dataType === 'MultiSelect';

    this.attributeValues.update((current) => {
      const next = { ...current };
      if (!value) {
        delete next[attribute.id];
        return next;
      }
      next[attribute.id] = {
        attributeId: attribute.id,
        text: isOption ? null : value,
        optionId: isOption ? value : null,
      };
      return next;
    });
  }

  protected addSpec(): void {
    this.dirty.set(true);
    this.specifications.update((current) => [...current, { label: '', value: '', group: null }]);
  }

  protected setSpec(index: number, field: 'label' | 'value' | 'group', value: string): void {
    this.dirty.set(true);
    this.specifications.update((current) =>
      current.map((spec, at) =>
        at === index ? { ...spec, [field]: field === 'group' ? value || null : value } : spec,
      ),
    );
  }

  protected removeSpec(index: number): void {
    this.dirty.set(true);
    this.specifications.update((current) => current.filter((_spec, at) => at !== index));
  }

  protected save(): void {
    if (!this.form.submit() || this.saving()) return;

    const values = this.form.values();
    const parties = this.partyValues();
    const body: ProductBody = {
      name: values.name,
      slug: values.slug || null,
      categoryId: values.categoryId,
      brandId: values.brandId || null,
      // The seller a product belongs to is the token's, and the API decides it. A platform user
      // creating a catalogue product leaves it null; a vendor cannot set it to somebody else.
      vendorId: null,
      shortDescription: values.shortDescription || null,
      description: values.description || null,
      hsnCode: values.hsnCode || null,
      gstRate: Number(values.gstRate),
      countryOfOrigin: values.countryOfOrigin || null,
      manufacturer: toParty(parties['manufacturer']),
      packer: toParty(parties['packer']),
      importer: toParty(parties['importer']),
      isReturnable: this.isReturnable(),
      returnWindowDays: values.returnWindowDays ? Number(values.returnWindowDays) : null,
      warranty: values.warranty || null,
      specifications: this.specifications().filter((spec) => spec.label && spec.value),
      seo: {
        metaTitle: values.metaTitle || null,
        metaDescription: values.metaDescription || null,
        metaKeywords: null,
        canonicalUrl: null,
        noIndex: this.noIndex(),
      },
      attributes: Object.values(this.attributeValues()),
    };

    this.saving.set(true);
    this.summary.set([]);

    const current = this.product();
    const request = current ? this.catalog.updateProduct(current.id, body) : this.catalog.createProduct(body);

    // The images are their own endpoint, but one "Save changes" is the only button an editor
    // should need: a product is one record to them, and a separate "Save images" was the button
    // that got missed — pictures chosen, form saved, reload, pictures gone.
    const withMedia: Observable<{ saved: ProductResponse; mediaFailed: boolean }> =
      current && this.mediaChanged()
        ? request.pipe(
            switchMap((saved) =>
              this.catalog.setProductMedia(saved.id, { media: [...this.media()] }).pipe(
                map((withImages) => ({ saved: withImages, mediaFailed: false })),
                catchError((error: unknown) => {
                  // The details did save; say precisely what did not, and keep the chosen images
                  // on screen and dirty so the next save sends them again.
                  this.actionError.set(
                    describeError(error, 'The details were saved, but the images were not.'),
                  );
                  return of({ saved, mediaFailed: true });
                }),
              ),
            ),
          )
        : request.pipe(map((saved) => ({ saved, mediaFailed: false })));

    withMedia.subscribe({
      next: ({ saved, mediaFailed }) => {
        this.saving.set(false);
        this.toasts.success(current ? 'Product saved.' : 'Product created.');

        if (current) {
          this.apply(saved, { keepMedia: mediaFailed });
          if (mediaFailed) this.dirty.set(true);
          return;
        }
        this.dirty.set(false);
        // A created product changes the URL, because everything below the form needs an id.
        this.router.navigate(['/catalog/products', saved.id], { replaceUrl: true });
      },
      error: (error: unknown) => {
        this.saving.set(false);
        const errors = fieldErrors(error);
        this.summary.set(
          errors
            ? this.form.applyServerErrors(errors)
            : [describeError(error, 'The product could not be saved.')],
        );
      },
    });
  }

  protected onMediaChange(media: readonly MediaPayload[]): void {
    this.dirty.set(true);
    this.media.set(media);
  }

  /** Whether the images differ from what the server last confirmed. Position is part of it. */
  private mediaChanged(): boolean {
    return JSON.stringify(this.media()) !== this.savedMedia();
  }

  protected editVariant(variant: VariantResponse | null): void {
    this.variantBeingEdited.set(variant);
    this.variantOpen.set(true);
  }

  protected onVariantSaved(): void {
    this.variantOpen.set(false);
    this.load();
  }

  /**
   * Finds sellers for the "Add an offer" picker. Only the active ones: `CreateListingCommandHandler`
   * refuses a seller who is not trading, so offering an applied or suspended seller here is a
   * choice that can only end in the server's refusal.
   */
  protected readonly vendorSearch = (term: string): Observable<readonly EntityOption[]> =>
    this.vendors
      .searchVendors(term, 10, 'Active')
      .pipe(
        map((sellers) =>
          sellers.map((seller) => ({ id: seller.id, label: seller.displayName, hint: seller.code })),
        ),
      );

  protected cancelOffer(): void {
    this.addingOffer.set(false);
    this.editingOffer.set(null);
    this.offerVariantId.set('');
    this.offerVendor.set(null);
    this.offerPrice.set('');
    this.offerMrp.set('');
    this.offerHandlingHours.set('24');
    this.offerMaxQuantity.set('');
    this.offerCodAllowed.set(true);
    this.offerError.set(null);
  }

  /** Opens the form empty, for a new offer — also when it was showing an existing one. */
  protected startOffer(): void {
    this.cancelOffer();
    this.addingOffer.set(true);
  }

  /**
   * Opens the form on an existing offer's terms. The same form as "Add an offer", because the
   * fields are the same; only the variant and the seller are fixed, since an offer is the pair.
   */
  protected editOffer(listing: ListingResponse): void {
    this.cancelOffer();
    this.editingOffer.set(listing);
    this.offerVariantId.set(listing.variantId);
    this.offerPrice.set(String(listing.sellingPrice));
    this.offerMrp.set(String(listing.mrp));
    this.offerHandlingHours.set(String(listing.handlingTimeHours));
    this.offerMaxQuantity.set(listing.maxOrderQuantity === null ? '' : String(listing.maxOrderQuantity));
    this.offerCodAllowed.set(listing.isCodAllowed);
    this.addingOffer.set(true);
  }

  /**
   * Opens the offer as a Draft — `CreateListingCommandHandler` never activates one. Activating is
   * the (now-fixed) button already in the offers table, which is also where the invariants that
   * only matter for a live offer (seller active, variant sellable, product published) are checked.
   *
   * For an existing offer the same button saves its terms instead. A live offer's new price goes
   * out as `ListingUpdated`, so the search cards and the buy box follow without a republish.
   */
  protected submitOffer(): void {
    const editing = this.editingOffer();
    if (editing) {
      this.saveOffer(editing);
      return;
    }

    const variantId = this.offerVariantId();
    const vendorId = this.offerVendor()?.id;
    const sellingPrice = Number(this.offerPrice());

    if (!variantId || !vendorId || !sellingPrice) {
      this.offerError.set('Pick a variant, a seller and a selling price.');
      return;
    }

    this.busy.set(true);
    this.offerError.set(null);

    this.catalog
      .createListing({
        variantId,
        vendorId,
        mrp: this.offerMrp() ? Number(this.offerMrp()) : null,
        sellingPrice,
        vendorSku: null,
        handlingTimeHours: Number(this.offerHandlingHours()) || 24,
        isCodAllowed: this.offerCodAllowed(),
        maxOrderQuantity: this.offerMaxQuantity() ? Number(this.offerMaxQuantity()) : null,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.toasts.success('Offer opened as a draft. Activate it below to put it on the storefront.');
          this.cancelOffer();
          this.listings.refresh();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.offerError.set(describeError(error, 'That offer could not be opened.'));
        },
      });
  }

  private saveOffer(listing: ListingResponse): void {
    const sellingPrice = Number(this.offerPrice());
    const mrp = Number(this.offerMrp());

    if (!sellingPrice || !mrp) {
      this.offerError.set('Give the offer a selling price and an MRP.');
      return;
    }

    if (sellingPrice > mrp) {
      this.offerError.set('The selling price may not exceed the MRP.');
      return;
    }

    this.busy.set(true);
    this.offerError.set(null);

    this.catalog
      .updateListing(listing.id, {
        mrp,
        sellingPrice,
        vendorSku: listing.vendorSku,
        handlingTimeHours: Number(this.offerHandlingHours()) || listing.handlingTimeHours,
        isCodAllowed: this.offerCodAllowed(),
        maxOrderQuantity: this.offerMaxQuantity() ? Number(this.offerMaxQuantity()) : null,
      })
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.toasts.success(
            listing.status === 'Active'
              ? 'Offer saved. The storefront shows the new price.'
              : 'Offer saved.',
          );
          this.cancelOffer();
          this.listings.refresh();
          this.audit.refresh();
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.offerError.set(describeError(error, 'That offer could not be saved.'));
        },
      });
  }

  protected toggleListing(listing: ListingResponse): void {
    this.busy.set(true);
    const request =
      listing.status === 'Active'
        ? this.catalog.deactivateListing(listing.id, null)
        : this.catalog.activateListing(listing.id);

    request.subscribe({
      next: () => {
        this.busy.set(false);
        this.listings.refresh();
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That offer could not be changed.'));
      },
    });
  }

  protected reject(reason: string): void {
    const current = this.product();
    if (!current) return;
    this.rejecting.set(false);
    this.run(this.catalog.rejectProduct(current.id, { notes: reason }), 'Rejected, and the seller told.');
  }

  protected archive(): void {
    const current = this.product();
    if (!current) return;
    this.archiving.set(false);
    this.run(this.catalog.archiveProduct(current.id), 'Archived.');
  }

  /** Every lifecycle button runs through here, so one place owns the busy flag and the reload. */
  protected run(request: ReturnType<CatalogAdminService['publishProduct']>, message: string): void {
    if (this.busy()) return;
    this.busy.set(true);
    this.actionError.set(null);

    request.subscribe({
      next: (saved) => {
        this.busy.set(false);
        this.apply(saved);
        this.audit.refresh();
        this.toasts.success(message);
      },
      error: (error: unknown) => {
        this.busy.set(false);
        this.actionError.set(describeError(error, 'That could not be done.'));
      },
    });
  }

  protected back(): void {
    this.router.navigate(['/catalog/products']);
  }

  private load(): void {
    this.loading.set(true);
    this.catalog.product(this.id).subscribe({
      next: (loaded) => {
        this.loading.set(false);
        this.apply(loaded);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'That product could not be found.'));
      },
    });
  }

  /**
   * Puts a server response into the form. The one place the two representations meet.
   *
   * `keepMedia` leaves the image list as the editor has it — for the save where the details went
   * through and the images did not, so their choice is not thrown away with the error.
   */
  private apply(loaded: ProductResponse, { keepMedia = false } = {}): void {
    this.product.set(loaded);
    this.isNew.set(false);
    this.dirty.set(false);

    this.form.reset({
      name: loaded.name,
      slug: loaded.slug,
      categoryId: loaded.categoryId,
      brandId: loaded.brandId ?? '',
      shortDescription: loaded.shortDescription ?? '',
      description: loaded.description ?? '',
      hsnCode: loaded.hsnCode ?? '',
      gstRate: String(loaded.gstRate),
      countryOfOrigin: loaded.countryOfOrigin ?? '',
      returnWindowDays: loaded.returnWindowDays === null ? '' : String(loaded.returnWindowDays),
      warranty: loaded.warranty ?? '',
      metaTitle: loaded.seo.metaTitle ?? '',
      metaDescription: loaded.seo.metaDescription ?? '',
    });

    this.isReturnable.set(loaded.isReturnable);
    this.noIndex.set(loaded.seo.noIndex);
    this.specifications.set(loaded.specifications);
    const media = loaded.media.map((item) => ({
      fileId: item.fileId,
      kind: item.kind,
      altText: item.altText,
      position: item.position,
    }));
    this.savedMedia.set(JSON.stringify(media));
    if (!keepMedia) this.media.set(media);
    this.partyValues.set({
      manufacturer: fromParty(loaded.manufacturer),
      packer: fromParty(loaded.packer),
      importer: fromParty(loaded.importer),
    });
    // `AttributeValueResponse.value` is the rendered text and `optionId` the chosen option; the
    // payload names the first of those `text`, so the two shapes are mapped rather than spread.
    this.attributeValues.set(
      Object.fromEntries(
        loaded.attributes.map((value) => [
          value.attributeId,
          {
            attributeId: value.attributeId,
            text: value.optionId ? null : value.value,
            optionId: value.optionId,
          },
        ]),
      ),
    );
  }
}

function flatten(nodes: readonly CategoryNode[], depth: number): CategoryOption[] {
  return nodes.flatMap((node) => [
    { id: node.id, label: `${'— '.repeat(depth)}${node.name}` },
    ...flatten(node.children, depth + 1),
  ]);
}

/** An entirely blank party is sent as null, so an empty form does not write three empty records. */
function toParty(party: { name: string; address: string; contact: string } | undefined) {
  if (!party || (!party.name && !party.address && !party.contact)) return null;
  return { name: party.name || null, address: party.address || null, contact: party.contact || null };
}

function fromParty(party: { name: string | null; address: string | null; contact: string | null } | null) {
  return { name: party?.name ?? '', address: party?.address ?? '', contact: party?.contact ?? '' };
}
