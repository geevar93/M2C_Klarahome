import { Component, signal } from '@angular/core';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { EntityMultiPicker, EntityOption, EntitySearch } from '@klarahome/ui-admin';
import { of } from 'rxjs';

const CATALOGUE: readonly EntityOption[] = [
  { id: 'lamp', label: 'Brass table lamp', hint: 'Active' },
  { id: 'rug', label: 'Jute rug', hint: 'Active' },
  { id: 'throw', label: 'Cotton throw', hint: 'Active' },
];

@Component({
  selector: 'kh-test-host',
  imports: [EntityMultiPicker],
  template: `
    <kh-entity-multi-picker
      [open]="true"
      heading="Add products"
      [search]="search"
      [existingIds]="existing()"
      [limit]="limit()"
      (confirmed)="added = $event"
    />
  `,
})
class TestHost {
  readonly existing = signal<readonly string[]>(['rug']);
  readonly limit = signal<number | null>(null);
  readonly terms: string[] = [];
  added: readonly EntityOption[] = [];

  readonly search: EntitySearch = (term) => {
    this.terms.push(term);
    return of(CATALOGUE.filter((option) => option.label.toLowerCase().includes(term.toLowerCase())));
  };
}

describe('EntityMultiPicker', () => {
  let fixture: ComponentFixture<TestHost>;

  const rows = () => Array.from(document.querySelectorAll<HTMLLabelElement>('kh-entity-multi-picker .row'));
  const checkbox = (label: string) =>
    rows()
      .find((row) => row.textContent?.includes(label))
      ?.querySelector<HTMLInputElement>('input[type=checkbox]');
  const addButton = () =>
    Array.from(document.querySelectorAll<HTMLButtonElement>('kh-entity-multi-picker button')).find((button) =>
      button.textContent?.trim().startsWith('Add'),
    );

  beforeEach(async () => {
    await TestBed.configureTestingModule({ imports: [TestHost] }).compileComponents();
    fixture = TestBed.createComponent(TestHost);
    fixture.detectChanges();
    await fixture.whenStable();
    fixture.detectChanges();
  });

  it('searches with an empty term on open, so there is something to pick before typing', () => {
    expect(fixture.componentInstance.terms).toEqual(['']);
    expect(rows()).toHaveLength(3);
  });

  it('shows what the caller already holds as ticked and disabled', () => {
    const rug = checkbox('Jute rug');
    expect(rug?.checked).toBe(true);
    expect(rug?.disabled).toBe(true);
  });

  it('keeps ticked rows across searches and hands all of them back together', async () => {
    checkbox('Brass table lamp')?.click();
    fixture.detectChanges();

    const input = document.querySelector<HTMLInputElement>('kh-entity-multi-picker input[type=search]');
    if (!input) throw new Error('no search box');
    input.value = 'cotton';
    input.dispatchEvent(new Event('input'));
    await new Promise((resolve) => setTimeout(resolve, 300));
    fixture.detectChanges();

    expect(rows()).toHaveLength(1);
    checkbox('Cotton throw')?.click();
    fixture.detectChanges();

    expect(addButton()?.textContent?.trim()).toBe('Add 2');
    addButton()?.click();

    expect(fixture.componentInstance.added.map((option) => option.id)).toEqual(['lamp', 'throw']);
  });

  it('stops offering rows once the limit is reached', () => {
    fixture.componentInstance.limit.set(1);
    fixture.detectChanges();

    checkbox('Brass table lamp')?.click();
    fixture.detectChanges();
    fixture.detectChanges();

    expect(checkbox('Cotton throw')?.disabled).toBe(true);
  });
});
