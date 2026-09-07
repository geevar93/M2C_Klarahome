import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { toSignal } from '@angular/core/rxjs-interop';
import { AddressBookStore } from '@klarahome/data-access-account';
import { ReferenceDataService } from '@klarahome/data-access-content';
import { Alert, Button, Drawer, EmptyState, Skeleton } from '@klarahome/ui-primitives';
import {
  AddressCard,
  AddressForm,
  AddressFormValue,
  AddressView,
  PincodePlaceView,
} from '@klarahome/ui-patterns';
import { ToastService } from '@klarahome/util';

import { CommerceMapper } from '../../core/commerce.mapper';
import { describeError } from '../../core/describe-error';

/**
 * Your addresses — `/account/addresses`.
 *
 * The address book, and the one form that both adds and edits. One form because they are one act
 * with a different starting point, and two would drift — an autocomplete token added to the "add"
 * form and forgotten on the "edit" one is exactly the kind of divergence that goes unnoticed for a
 * year.
 *
 * The form opens as a **bottom sheet** rather than a route. Adding an address is a detour from a
 * list the customer is reading, and taking them to another URL to do it means a back button that
 * loses whatever they had typed.
 *
 * Deleting asks first, and the confirmation names the recipient. An address book has three entries
 * that look alike on a phone screen, and "are you sure?" over the wrong one is how people lose the
 * address they use.
 */
@Component({
  selector: 'kh-account-addresses-page',
  imports: [AddressCard, AddressForm, Alert, Button, Drawer, EmptyState, Skeleton],
  template: `
    <div class="head">
      <h1>Your addresses</h1>
      <button khButton variant="primary" type="button" (click)="add()">Add an address</button>
    </div>

    @if (store.isLoading() && !store.hasLoaded()) {
      <kh-skeleton height="8rem" />
    } @else if (store.isEmpty()) {
      <kh-empty-state
        heading="No addresses saved yet"
        message="Add one now and checkout will be a couple of taps."
      >
        <button khButton variant="primary" type="button" (click)="add()">Add an address</button>
      </kh-empty-state>
    } @else {
      <div class="list">
        @for (address of addresses(); track address.id) {
          <kh-address-card
            [address]="address"
            [showActions]="true"
            (edited)="edit($event)"
            (removed)="askToRemove($event)"
            (madeDefault)="makeDefault($event)"
          />
        }
      </div>
    }

    <kh-drawer
      [open]="formOpen()"
      side="bottom"
      [label]="editing() ? 'Edit address' : 'Add an address'"
      (closed)="closeForm()"
    >
      <div class="sheet">
        <h2>{{ editing() ? 'Edit address' : 'Add an address' }}</h2>
        <kh-address-form
          [states]="states()"
          [address]="editing()"
          [place]="place()"
          [saving]="store.isSaving()"
          [submitLabel]="editing() ? 'Save changes' : 'Save address'"
          (pincodeEntered)="lookUpPincode($event)"
          (saved)="save($event)"
          (cancelled)="closeForm()"
        />
      </div>
    </kh-drawer>

    <kh-drawer
      [open]="removing() !== null"
      side="bottom"
      label="Delete this address"
      (closed)="removing.set(null)"
    >
      <div class="sheet">
        <h2>Delete this address?</h2>
        <kh-alert tone="warning">
          {{ removing()?.recipientName }}'s address will be removed from your address book. Orders already
          sent there are not affected.
        </kh-alert>
        <div class="actions">
          <button
            khButton
            variant="danger"
            type="button"
            [disabled]="store.isSaving()"
            (click)="confirmRemove()"
          >
            Delete it
          </button>
          <button khButton variant="tertiary" type="button" (click)="removing.set(null)">Keep it</button>
        </div>
      </div>
    </kh-drawer>
  `,
  styles: `
    :host {
      display: block;
    }

    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-3);
    }

    h1 {
      margin: 0;
      font-size: var(--text-2xl);
    }

    .list {
      display: flex;
      flex-direction: column;
      gap: var(--space-3);
      margin-block-start: var(--space-4);
    }

    .sheet {
      padding: var(--space-4);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
      margin-block-start: var(--space-4);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AccountAddressesPage {
  protected readonly store = inject(AddressBookStore);
  private readonly reference = inject(ReferenceDataService);
  private readonly mapper = inject(CommerceMapper);
  private readonly toasts = inject(ToastService);

  protected readonly formOpen = signal(false);
  protected readonly editing = signal<AddressView | null>(null);
  protected readonly removing = signal<AddressView | null>(null);
  protected readonly place = signal<PincodePlaceView | null>(null);

  private readonly stateRows = toSignal(this.reference.states(), { initialValue: [] });

  protected readonly states = computed(() =>
    this.stateRows().map((state) => ({ id: state.id, name: state.name })),
  );

  private readonly stateNames = computed(
    () => new Map(this.stateRows().map((state) => [state.id, state.name])),
  );

  protected readonly addresses = computed(() =>
    this.store.all().map((address) => this.mapper.address(address, this.stateNames().get(address.stateId))),
  );

  constructor() {
    this.store.loadOnce();
  }

  protected add(): void {
    this.editing.set(null);
    this.place.set(null);
    this.formOpen.set(true);
  }

  protected edit(id: string): void {
    const address = this.addresses().find((candidate) => candidate.id === id);
    if (!address) return;
    this.editing.set(address);
    this.place.set(null);
    this.formOpen.set(true);
  }

  protected closeForm(): void {
    this.formOpen.set(false);
    this.editing.set(null);
  }

  protected lookUpPincode(pincode: string): void {
    this.reference.pincode(pincode).subscribe((response) => {
      if (!response) return;
      this.place.set({
        pincode: response.pincode,
        city: response.city,
        // The lookup answers a state *code*; the address stores an id. Matched against the same
        // reference list the form is showing, so the two cannot name different states.
        stateId: this.stateRows().find((state) => state.code === response.stateCode)?.id ?? null,
        stateName: response.stateName,
      });
    });
  }

  protected save(value: AddressFormValue): void {
    const existing = this.editing();
    const request = existing ? this.store.update(existing.id, value) : this.store.create(value);

    request.subscribe({
      next: () => {
        this.closeForm();
        this.toasts.success(existing ? 'Address updated.' : 'Address saved.');
      },
      error: (error: unknown) =>
        this.toasts.danger(
          describeError(error, 'We could not save that address. Please check it and try again.'),
        ),
    });
  }

  protected askToRemove(id: string): void {
    this.removing.set(this.addresses().find((candidate) => candidate.id === id) ?? null);
  }

  protected confirmRemove(): void {
    const address = this.removing();
    if (!address) return;

    this.store.remove(address.id).subscribe({
      next: () => {
        this.removing.set(null);
        this.toasts.success('Address deleted.');
      },
      error: (error: unknown) => {
        this.removing.set(null);
        this.toasts.danger(describeError(error, 'We could not delete that address.'));
      },
    });
  }

  protected makeDefault(id: string): void {
    this.store.makeDefault(id).subscribe({
      next: () => this.toasts.success('That is now your default address.'),
      error: (error: unknown) =>
        this.toasts.danger(describeError(error, 'We could not change your default address.')),
    });
  }
}
