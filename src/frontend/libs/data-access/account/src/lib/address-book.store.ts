import { Injectable, Signal, computed, inject, signal } from '@angular/core';
import { AddressInput, AddressResponse, IdentityApiClient } from '@klarahome/data-access-api';
import { Observable, finalize, tap, throwError } from 'rxjs';

/**
 * The customer's saved addresses.
 *
 * A store rather than a service because two screens need the same list at the same time — the
 * address book and the checkout's first step — and both may write to it. A shopper who adds an
 * address mid-checkout should find it in their account afterwards without a reload, which a service
 * returning observables to two independent components would not give them.
 *
 * **There is no anonymous address book.** Addresses belong to a signed-in customer, and a guest
 * checkout collects one into the checkout session instead (Step 13). So this store holds nothing
 * while signed out and says so, rather than inventing local storage that would leak somebody's home
 * address onto a shared device.
 *
 * Writes are **not** optimistic. An address is typed once, deliberately, on a form the customer is
 * already waiting on; a guessed row that disappears is more alarming than a button that spins.
 */
@Injectable({ providedIn: 'root' })
export class AddressBookStore {
  private readonly api = inject(IdentityApiClient);

  private readonly addresses = signal<readonly AddressResponse[]>([]);
  private readonly loading = signal(false);
  private readonly loaded = signal(false);
  private readonly saving = signal(false);

  readonly all: Signal<readonly AddressResponse[]> = this.addresses.asReadonly();
  readonly isLoading: Signal<boolean> = this.loading.asReadonly();
  readonly isSaving: Signal<boolean> = this.saving.asReadonly();
  readonly hasLoaded: Signal<boolean> = this.loaded.asReadonly();
  readonly isEmpty = computed(() => this.loaded() && this.addresses().length === 0);

  /** The one the checkout pre-selects: the default, or the only one, or the first. */
  readonly preferred = computed<AddressResponse | null>(() => {
    const all = this.addresses();
    return all.find((address) => address.isDefaultShipping) ?? all[0] ?? null;
  });

  load(): Observable<readonly AddressResponse[]> {
    this.loading.set(true);
    return this.api.storeMeAddressesGet({ silentErrors: true }).pipe(
      tap((addresses) => this.addresses.set(addresses)),
      finalize(() => {
        this.loading.set(false);
        this.loaded.set(true);
      }),
    );
  }

  loadOnce(): void {
    if (this.loaded() || this.loading()) return;
    this.load().subscribe({ error: () => this.loaded.set(true) });
  }

  create(address: AddressInput): Observable<AddressResponse> {
    this.saving.set(true);
    return this.api.storeMeAddressCreate(address, { silentErrors: true }).pipe(
      tap((created) => this.upsert(created)),
      finalize(() => this.saving.set(false)),
    );
  }

  update(id: string, address: AddressInput): Observable<AddressResponse> {
    this.saving.set(true);
    return this.api.storeMeAddressUpdate(id, address, { silentErrors: true }).pipe(
      tap((updated) => this.upsert(updated)),
      finalize(() => this.saving.set(false)),
    );
  }

  remove(id: string): Observable<void> {
    this.saving.set(true);
    return this.api.storeMeAddressDelete(id, { silentErrors: true }).pipe(
      tap(() => this.addresses.update((all) => all.filter((address) => address.id !== id))),
      finalize(() => this.saving.set(false)),
    );
  }

  /**
   * Makes an address the default.
   *
   * A full update rather than a dedicated endpoint, because the API models "default" as a flag on
   * the address and not as a separate resource. The local list is corrected afterwards: the server
   * clears the flag on whichever address held it, and a client that only flipped the one it wrote
   * would show two defaults until the next load.
   */
  makeDefault(id: string): Observable<AddressResponse> {
    const address = this.addresses().find((candidate) => candidate.id === id);
    // An id that is not in the list means the list is stale. Refusing here is better than sending a
    // half-built address: the update endpoint replaces the whole row, so a guess would erase it.
    if (!address) return throwError(() => new Error('That address is no longer in your address book.'));

    return this.update(id, { ...toInput(address), isDefaultShipping: true, isDefaultBilling: true }).pipe(
      tap(() =>
        this.addresses.update((all) =>
          all.map((candidate) =>
            candidate.id === id
              ? candidate
              : { ...candidate, isDefaultShipping: false, isDefaultBilling: false },
          ),
        ),
      ),
    );
  }

  /** Drops the list. Called on sign-out, where the next customer's addresses are not these. */
  clear(): void {
    this.addresses.set([]);
    this.loaded.set(false);
  }

  private upsert(address: AddressResponse): void {
    this.addresses.update((all) => {
      const without = all.filter((candidate) => candidate.id !== address.id);
      const next = [...without, address];
      // A new default clears the others locally, for the same reason as `makeDefault`.
      return address.isDefaultShipping
        ? next.map((candidate) =>
            candidate.id === address.id ? candidate : { ...candidate, isDefaultShipping: false },
          )
        : next;
    });
  }
}

/** The response's fields, as the update endpoint wants them back. The two shapes match field for field. */
function toInput(address: AddressResponse): AddressInput {
  return {
    label: address.label,
    recipientName: address.recipientName,
    mobile: address.mobile,
    line1: address.line1,
    line2: address.line2,
    landmark: address.landmark,
    city: address.city,
    stateId: address.stateId,
    pincode: address.pincode,
    gstin: address.gstin,
    type: address.type,
    isDefaultShipping: address.isDefaultShipping,
    isDefaultBilling: address.isDefaultBilling,
  };
}
