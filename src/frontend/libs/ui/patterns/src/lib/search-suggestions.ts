import { ChangeDetectionStrategy, Component, computed, input, output } from '@angular/core';
import { ProductImage } from '@klarahome/ui-primitives';
import { MoneyPipe } from '@klarahome/i18n';

import { SuggestionView } from './catalog.model';

/**
 * The panel under the search box: what the shopper searched before, and what the API suggests now.
 *
 * It is a **combobox popup**, and that is the whole design constraint. The listbox is
 * `role="listbox"`, each row `role="option"`, and the *active* option is marked with
 * `aria-activedescendant` on the input rather than by moving focus — because focus has to stay in
 * the text box while the arrow keys walk the list, or every keystroke after the first goes to the
 * wrong element. The keyboard model lives in `SearchEntry`, which owns the input; this component
 * renders what it is told and reports what was clicked.
 *
 * Recent searches are shown only when the box is empty. Once there is a query, suggestions from
 * the server are strictly more useful than the shopper's own history, and showing both makes a
 * list nobody can scan on a 360px screen.
 */
@Component({
  selector: 'kh-search-suggestions',
  imports: [MoneyPipe, ProductImage],
  template: `
    <div class="panel" [id]="listboxId()" role="listbox" [attr.aria-label]="heading()">
      @if (heading()) {
        <p class="heading" aria-hidden="true">{{ heading() }}</p>
      }

      @for (item of items(); track item.href; let index = $index) {
        <div
          class="option"
          role="option"
          [id]="optionId(index)"
          [attr.aria-selected]="index === activeIndex()"
          [class.is-active]="index === activeIndex()"
          (click)="chosen.emit(item)"
          (mousedown)="$event.preventDefault()"
        >
          @if (item.kind === 'product') {
            <kh-product-image [source]="item.image" [placeholder]="item.text" sizes="3rem" />
          }
          <span class="text">{{ item.text }}</span>
          @if (item.price) {
            <span class="price">{{ item.price | khMoney }}</span>
          } @else if (item.kind !== 'product' && item.kind !== 'query') {
            <span class="kind">in {{ item.kind }}</span>
          }
        </div>
      }
    </div>
  `,
  styles: `
    :host {
      position: absolute;
      inset-inline: 0;
      inset-block-start: 100%;
      z-index: var(--z-drawer);
      display: block;
    }

    .panel {
      max-block-size: 60vh;
      overflow-y: auto;
      padding: var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-lg);
    }

    .heading {
      margin: 0 0 var(--space-1);
      padding-inline: var(--space-2);
      font-size: var(--text-xs);
      text-transform: uppercase;
      letter-spacing: 0.04em;
      color: var(--color-text-muted);
    }

    .option {
      display: flex;
      align-items: center;
      gap: var(--space-3);
      min-block-size: var(--touch-target-min);
      padding: var(--space-1) var(--space-2);
      border-radius: var(--radius-sm);
      cursor: pointer;
    }

    /* Both, not one: the pointer state and the keyboard state are different users and both need
       to see where they are. */
    .option:hover,
    .option.is-active {
      background: var(--color-surface);
    }

    kh-product-image {
      inline-size: 3rem;
      flex: none;
    }

    .text {
      flex: 1;
      min-inline-size: 0;
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .price,
    .kind {
      font-size: var(--text-sm);
      color: var(--color-text-muted);
      white-space: nowrap;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class SearchSuggestions {
  readonly items = input.required<readonly SuggestionView[]>();
  /** "Recent searches", "Suggestions" — or empty for an unlabelled list. */
  readonly heading = input('');
  /** Which row the arrow keys are on. −1 means the input's own text is the selection. */
  readonly activeIndex = input(-1);
  /** Must match what `SearchEntry` puts in `aria-controls`. */
  readonly listboxId = input('kh-search-listbox');

  readonly chosen = output<SuggestionView>();

  protected readonly prefix = computed(() => `${this.listboxId()}-option-`);

  protected optionId(index: number): string {
    return `${this.prefix()}${index}`;
  }
}
