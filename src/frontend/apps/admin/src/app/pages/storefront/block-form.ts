import { ChangeDetectionStrategy, Component, inject, input, output } from '@angular/core';
import { BlockFieldResponse, BlockTypeResponse } from '@klarahome/data-access-admin';
import { ReferenceTags, SchemaField } from '@klarahome/ui-admin';
import { Alert, Button, Icon } from '@klarahome/ui-primitives';

import { FIELD_LABEL_OVERRIDES } from '../content/content-vocabulary';
import { BlockDraft } from './draft-model';
import { BrowseTarget, ReferenceDirectory, idsOf, isPickable, nounFor } from './reference-directory';

/** What the form asks the editor to do. The editor owns the draft; this holds nothing. */
export interface FieldChange {
  readonly field: string;
  readonly value: unknown;
}

export interface ItemChange extends FieldChange {
  readonly index: number;
}

const REFERENCE_HINTS: Readonly<Record<string, string>> = {
  Link: 'An absolute URL, or a path beginning with /',
};

/**
 * The fields of one block, drawn from the schema the server sent.
 *
 * The same controls and the same rules as the page composer (`PageComposerPage`): `kh-schema-field`
 * for plain values, `kh-reference-tags` for the ids a block points at, and a repeater for the
 * block's items. There is no `switch (block.type)` here either - a block type added on the server
 * appears in the editor with no change to this file.
 */
@Component({
  selector: 'kh-block-form',
  imports: [Alert, Button, Icon, ReferenceTags, SchemaField],
  template: `
    @if (schema(); as definition) {
      @for (field of definition.fields; track field.name) {
        @if (pickable(field)) {
          <kh-reference-tags
            [controlId]="draft().id + '-' + field.name"
            [label]="humanise(field.name)"
            [hint]="hint(field)"
            [required]="field.isRequired"
            [tags]="references.tagsFor(field.kind, draft().config[field.name])"
            [browseLabel]="browseLabel(field, draft().config[field.name])"
            [full]="isFull(field, draft().config[field.name])"
            (removed)="referenceRemoved.emit({ field, id: $event })"
            (browse)="browseRequested.emit({ field, mode: field.isList ? 'list' : 'single' })"
          />
        } @else {
          <kh-schema-field
            [field]="field"
            [controlId]="draft().id + '-' + field.name"
            [label]="humanise(field.name)"
            [hint]="hint(field)"
            [value]="draft().config[field.name]"
            (changed)="fieldChanged.emit({ field: field.name, value: $event })"
          />
        }
      }

      @if (definition.itemFields && definition.itemFields.length > 0) {
        <section class="items">
          <h4>Items</h4>

          @for (item of items(); track $index) {
            <article class="item">
              <header>
                <span>Item {{ $index + 1 }}</span>
                <div class="item-actions">
                  <button
                    khButton
                    type="button"
                    size="sm"
                    variant="tertiary"
                    [disabled]="$index === 0"
                    (click)="itemMoved.emit({ index: $index, delta: -1 })"
                  >
                    Up
                  </button>
                  <button
                    khButton
                    type="button"
                    size="sm"
                    variant="tertiary"
                    [disabled]="$index === items().length - 1"
                    (click)="itemMoved.emit({ index: $index, delta: 1 })"
                  >
                    Down
                  </button>
                  <button khButton type="button" size="sm" variant="tertiary" (click)="itemRemoved.emit($index)">
                    Remove
                  </button>
                </div>
              </header>

              @for (field of definition.itemFields; track field.name) {
                @if (pickable(field)) {
                  <kh-reference-tags
                    [controlId]="draft().id + '-item-' + $index + '-' + field.name"
                    [label]="humanise(field.name)"
                    [hint]="hint(field)"
                    [required]="field.isRequired"
                    [tags]="references.tagsFor(field.kind, item[field.name])"
                    [browseLabel]="browseLabel(field, item[field.name])"
                    (removed)="itemFieldChanged.emit({ index: $index, field: field.name, value: null })"
                    (browse)="browseRequested.emit({ field, mode: 'single', itemIndex: $index })"
                  />
                } @else {
                  <kh-schema-field
                    [field]="field"
                    [controlId]="draft().id + '-item-' + $index + '-' + field.name"
                    [label]="humanise(field.name)"
                    [hint]="hint(field)"
                    [value]="item[field.name]"
                    (changed)="itemFieldChanged.emit({ index: $index, field: field.name, value: $event })"
                  />
                }
              }
            </article>
          }

          <div class="item-add">
            <button
              khButton
              type="button"
              size="sm"
              [disabled]="items().length >= definition.maxItems"
              (click)="itemAdded.emit()"
            >
              <kh-icon name="plus" size="sm" />
              Add an item
            </button>
            @if (pickableItemField(definition); as field) {
              <button
                khButton
                type="button"
                size="sm"
                variant="primary"
                [disabled]="items().length >= definition.maxItems"
                (click)="browseRequested.emit({ field, mode: 'items', maxItems: definition.maxItems })"
              >
                <kh-icon name="search" size="sm" />
                Add {{ noun(field.kind) }} in bulk
              </button>
            }
          </div>
        </section>
      }
    } @else {
      <kh-alert tone="warning" heading="This block type isn't recognised any more">
        <code>{{ draft().type }}</code> isn't available to edit any more. It's kept as it is and saved
        unchanged; remove it if it's no longer wanted.
      </kh-alert>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    .items {
      display: flex;
      flex-direction: column;
      gap: var(--space-2);
      padding: var(--space-2);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    .items h4 {
      margin: 0;
      font-size: var(--text-sm);
    }

    .item {
      padding: var(--space-2);
      border: 1px solid var(--color-border-subtle);
      border-radius: var(--radius-sm);
    }

    .item > header {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      justify-content: space-between;
      gap: var(--space-2);
      margin-block-end: var(--space-2);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
    }

    .item-actions,
    .item-add {
      display: flex;
      flex-wrap: wrap;
      gap: var(--space-2);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class BlockForm {
  protected readonly references = inject(ReferenceDirectory);

  readonly draft = input.required<BlockDraft>();
  readonly schema = input<BlockTypeResponse | undefined>(undefined);

  readonly fieldChanged = output<FieldChange>();
  readonly itemFieldChanged = output<ItemChange>();
  readonly itemAdded = output<void>();
  readonly itemRemoved = output<number>();
  readonly itemMoved = output<{ readonly index: number; readonly delta: -1 | 1 }>();
  readonly referenceRemoved = output<{ readonly field: BlockFieldResponse; readonly id: string }>();
  readonly browseRequested = output<Omit<BrowseTarget, 'blockId'>>();

  protected pickable(field: BlockFieldResponse): boolean {
    return isPickable(field);
  }

  protected noun(kind: string): string {
    return nounFor(kind, true);
  }

  protected items(): Record<string, unknown>[] {
    const items = this.draft().config['items'];
    return Array.isArray(items) ? (items as Record<string, unknown>[]) : [];
  }

  /** A field's label: a friendly override where one exists, otherwise the identifier spaced out. */
  protected humanise(name: string): string {
    const override = FIELD_LABEL_OVERRIDES[name];
    if (override) return override;
    const spaced = name.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[_-]+/g, ' ');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
  }

  protected hint(field: BlockFieldResponse): string {
    const parts: string[] = [];
    if (field.isList) parts.push(`One per line, up to ${field.maxLength}`);
    else if (field.maxLength > 0) parts.push(`Up to ${field.maxLength} characters`);
    const reference = REFERENCE_HINTS[field.kind];
    if (reference) parts.push(reference);
    if (field.kind === 'Html') parts.push('For advanced formatting only');
    return parts.join(' · ');
  }

  protected browseLabel(field: BlockFieldResponse, value: unknown): string {
    if (field.isList) return `Add ${nounFor(field.kind, true)}`;
    return idsOf(value).length > 0 ? 'Replace' : `Choose ${nounFor(field.kind, false)}`;
  }

  protected isFull(field: BlockFieldResponse, value: unknown): boolean {
    return field.isList && field.maxLength > 0 && idsOf(value).length >= field.maxLength;
  }

  /** The item field a bulk add fills: the first reference among an item's fields. */
  protected pickableItemField(schema: BlockTypeResponse): BlockFieldResponse | null {
    return (schema.itemFields ?? []).find((field) => isPickable(field) && !field.isList) ?? null;
  }
}
