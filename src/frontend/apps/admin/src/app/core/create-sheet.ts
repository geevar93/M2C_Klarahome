import { NgTemplateOutlet } from '@angular/common';
import { ChangeDetectionStrategy, Component, input, output } from '@angular/core';
import { RouterLink } from '@angular/router';
import { Drawer, ICON_NAMES, Icon, IconName } from '@klarahome/ui-primitives';

import { QuickAction } from './quick-actions';

/**
 * The sheet behind "+ New".
 *
 * A bottom sheet on a phone and a small centred dialog from 768px up — `kh-drawer`'s `bottom`
 * side already does both — listing the things this session may make, one full-width row each.
 * It is a list of links, not a form: choosing one goes to the screen that makes the thing, which
 * owns the fields and the rules, and the sheet closes behind it.
 *
 * It renders what it is given. The app filters the actions through the same `canReach` the route
 * guards use, so nothing here can offer a screen the session cannot open.
 */
@Component({
  selector: 'kh-create-sheet',
  imports: [Drawer, Icon, NgTemplateOutlet, RouterLink],
  template: `
    <kh-drawer [open]="open()" side="bottom" label="Make something new" (closed)="closed.emit()">
      <div class="sheet">
        <h2>New</h2>
        <ul>
          @for (action of actions(); track action.targetPath) {
            <li>
              @if (action.opens) {
                <button type="button" (click)="opened.emit(action)">
                  <ng-container *ngTemplateOutlet="row; context: { $implicit: action }" />
                </button>
              } @else {
                <a [routerLink]="action.targetPath" (click)="closed.emit()">
                  <ng-container *ngTemplateOutlet="row; context: { $implicit: action }" />
                </a>
              }
            </li>
          }
        </ul>
      </div>
    </kh-drawer>

    <ng-template #row let-action>
      <kh-icon [name]="iconFor(action.icon)" />
      <span class="text">
        <span class="label">{{ action.label }}</span>
        <span class="hint">{{ action.hint }}</span>
      </span>
      <kh-icon name="chevron-right" size="sm" class="chevron" />
    </ng-template>
  `,
  styles: `
    .sheet {
      padding: var(--space-4);
      padding-block-end: max(var(--space-4), env(safe-area-inset-bottom, 0px));
    }

    h2 {
      margin: 0 0 var(--space-3);
      font-size: var(--text-lg);
    }

    ul {
      margin: 0;
      padding: 0;
      list-style: none;
    }

    li + li {
      border-block-start: 1px solid var(--color-border);
    }

    /* A link and a button drawn as one row: which it is depends on whether the thing is made on a
       screen or in a sheet, and that is not a difference the eye should have to notice. */
    a,
    button {
      display: flex;
      gap: var(--space-3);
      align-items: center;
      width: 100%;
      min-height: var(--touch-target-min);
      padding: var(--space-3) var(--space-2);
      border: 0;
      border-radius: var(--radius-md);
      background: none;
      color: var(--color-text);
      font: inherit;
      text-align: start;
      text-decoration: none;
      cursor: pointer;
    }

    a:hover,
    button:hover {
      background: var(--color-surface-raised);
    }

    .text {
      display: flex;
      flex: 1;
      flex-direction: column;
      min-width: 0;
      line-height: var(--leading-tight);
    }

    .label {
      font-weight: var(--weight-medium);
    }

    .hint {
      font-size: var(--text-xs);
      color: var(--color-text-muted);
    }

    .chevron {
      color: var(--color-text-muted);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class CreateSheet {
  readonly open = input(false);
  readonly actions = input.required<readonly QuickAction[]>();

  readonly closed = output<void>();
  /** An action with a sheet of its own was chosen. The app closes this one and opens that. */
  readonly opened = output<QuickAction>();

  protected iconFor(name: string): IconName {
    return (ICON_NAMES as readonly string[]).includes(name) ? (name as IconName) : 'plus';
  }
}
