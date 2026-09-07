import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { Badge, Button, Icon } from '@klarahome/ui-primitives';

import { AddressView } from './commerce.model';

/**
 * A saved address — read in the address book, chosen at checkout.
 *
 * Two modes, one component, because they are the same card and a second one would drift. In
 * `selectable` mode the whole card is a `<label>` around a real radio, so it is reachable by Tab,
 * moved between with the arrow keys and announced as one of N — which a `<div (click)>` with a
 * border colour is not.
 *
 * The lines arrive already assembled. Which order an Indian address is written in — recipient,
 * street, landmark, city, state, PIN — is not a decision a card should be making six times over.
 */
@Component({
  selector: 'kh-address-card',
  imports: [Badge, Button, Icon, NgTemplateOutlet],
  template: `
    @if (selectable()) {
      <label class="kh-choice">
        <input
          type="radio"
          [name]="group()"
          [value]="address().id"
          [checked]="selected()"
          (change)="chosen.emit(address().id)"
        />
        <span class="body">
          <ng-container [ngTemplateOutlet]="details" />
        </span>
      </label>
    } @else {
      <div class="body plain">
        <ng-container [ngTemplateOutlet]="details" />
      </div>
    }

    <ng-template #details>
      <span class="head">
        <span class="name">{{ address().recipientName }}</span>
        @if (address().label) {
          <kh-badge tone="neutral">{{ address().label }}</kh-badge>
        }
        @if (address().isDefaultShipping) {
          <kh-badge tone="info">Default</kh-badge>
        }
      </span>

      @for (line of address().lines; track $index) {
        <span class="line">{{ line }}</span>
      }

      <span class="line mobile">{{ address().mobile }}</span>

      @if (address().gstin) {
        <span class="line gstin">GSTIN {{ address().gstin }}</span>
      }

      @if (showActions()) {
        <span class="actions">
          <button khButton variant="tertiary" size="sm" type="button" (click)="edited.emit(address().id)">
            <kh-icon name="edit" size="sm" />
            Edit
          </button>
          <button khButton variant="tertiary" size="sm" type="button" (click)="removed.emit(address().id)">
            <kh-icon name="trash" size="sm" />
            Delete
          </button>
          @if (!address().isDefaultShipping) {
            <button
              khButton
              variant="tertiary"
              size="sm"
              type="button"
              (click)="madeDefault.emit(address().id)"
            >
              Make default
            </button>
          }
        </span>
      }
    </ng-template>
  `,
  styles: `
    :host {
      display: block;
    }

    .body {
      display: flex;
      flex-direction: column;
      gap: var(--space-1);
      min-width: 0;
      font-size: var(--text-sm);
    }

    .plain {
      padding: var(--space-3);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    .head {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
    }

    .name {
      font-weight: var(--weight-medium);
    }

    .line {
      color: var(--color-text-muted);
    }

    .actions {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-1);
      margin-block-start: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AddressCard {
  readonly address = input.required<AddressView>();
  /** Renders the card as a radio option. The checkout's address step. */
  readonly selectable = input(false);
  readonly selected = input(false);
  /** The radio group name — one per list, so two lists on a page do not fight. */
  readonly group = input('address');
  readonly showActions = input(false);

  readonly chosen = output<string>();
  readonly edited = output<string>();
  readonly removed = output<string>();
  readonly madeDefault = output<string>();
}
