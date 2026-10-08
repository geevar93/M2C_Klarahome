import { computed, signal } from '@angular/core';
import { BlockBody, BlockResponse, BlockTypeResponse } from '@klarahome/data-access-admin';
import { StoreBlockResponse } from '@klarahome/data-access-content';

/** One block as the editor holds it: the API's record, plus the config being edited. */
export interface BlockDraft {
  readonly id: string;
  readonly type: string;
  readonly config: Readonly<Record<string, unknown>>;
  readonly isVisible: boolean;
  /** `datetime-local` text, kept so a scheduled block survives an edit it has nothing to do with. */
  readonly startsAt: string;
  readonly endsAt: string;
}

/** The prefix of an id the server has not seen. `BlockBody.id` is null for these. */
export const NEW_BLOCK_PREFIX = 'new-';

/**
 * The home page's blocks while they are being edited, and what has been saved of them.
 *
 * **The local draft is the source of truth.** The canvas renders what the server returns from
 * `preview-draft`, but that is a picture of this list, never the other way round: an edit changes
 * this, and the preview follows (debounced) afterwards. A slow or refused preview therefore cannot
 * lose or reorder anything.
 *
 * **Dirty is a comparison, not a flag.** The list is serialised and compared with the snapshot of
 * the last load or save, so typing a letter and deleting it is clean again and the unsaved-changes
 * guard does not nag about a change that is not one. A plain class over signals rather than a
 * component so the rules (what a move, a remove or an item edit does to the list) are tested
 * without a TestBed.
 */
export class HomeDraft {
  readonly blocks = signal<readonly BlockDraft[]>([]);
  private readonly snapshot = signal(serialise([]));

  /** Whether the list differs from the last load or save. */
  readonly dirty = computed(() => serialise(this.blocks()) !== this.snapshot());

  /** Replaces the list with what the server holds and calls it saved. */
  load(blocks: readonly BlockResponse[]): void {
    const drafts = [...blocks].sort((a, b) => a.position - b.position).map(toDraft);
    this.blocks.set(drafts);
    this.snapshot.set(serialise(drafts));
  }

  /**
   * Keeps what is on screen and calls it saved.
   *
   * Used after a save: the server's answer carries real ids for blocks that were new, but the
   * editor's selection and the preview are keyed by the ids it already has, so they are left alone
   * and only the dirty comparison is moved on. The next load replaces the ids.
   */
  markSaved(): void {
    this.snapshot.set(serialise(this.blocks()));
  }

  /** Adds a block of this type at `index` (the end when omitted) and returns its id. */
  add(type: BlockTypeResponse, index?: number): string {
    const id = `${NEW_BLOCK_PREFIX}${crypto.randomUUID()}`;
    const draft: BlockDraft = {
      id,
      type: type.type,
      config: seedConfig(type),
      isVisible: true,
      startsAt: '',
      endsAt: '',
    };
    this.blocks.update((current) => {
      const at = clamp(index ?? current.length, 0, current.length);
      return [...current.slice(0, at), draft, ...current.slice(at)];
    });
    return id;
  }

  remove(id: string): void {
    this.blocks.update((current) => current.filter((draft) => draft.id !== id));
  }

  /** Puts a removed block back where it was (or at the end if the list has since got shorter). */
  restore(draft: BlockDraft, index: number): void {
    this.blocks.update((current) => {
      const at = clamp(index, 0, current.length);
      return [...current.slice(0, at), draft, ...current.slice(at)];
    });
  }

  /** Moves a block one place (`-1` up, `1` down). Does nothing at either end. Returns whether it moved. */
  move(id: string, delta: -1 | 1): boolean {
    const current = this.blocks();
    const from = current.findIndex((draft) => draft.id === id);
    const to = from + delta;
    if (from < 0 || to < 0 || to >= current.length) return false;

    const next = [...current];
    [next[from], next[to]] = [next[to], next[from]];
    this.blocks.set(next);
    return true;
  }

  setVisible(id: string, isVisible: boolean): void {
    this.patch(id, (draft) => ({ ...draft, isVisible }));
  }

  /** Writes one of a block's own fields. */
  setField(id: string, field: string, value: unknown): void {
    this.patch(id, (draft) => ({ ...draft, config: { ...draft.config, [field]: value } }));
  }

  itemsOf(id: string): Record<string, unknown>[] {
    const items = this.blocks().find((draft) => draft.id === id)?.config['items'];
    return Array.isArray(items) ? (items as Record<string, unknown>[]) : [];
  }

  /** Edits one field of one item; the whole list is rewritten because the draft is immutable. */
  setItemField(id: string, index: number, field: string, value: unknown): void {
    this.setField(
      id,
      'items',
      this.itemsOf(id).map((item, at) => (at === index ? { ...item, [field]: value } : item)),
    );
  }

  addItem(id: string, item: Record<string, unknown> = {}): void {
    this.setField(id, 'items', [...this.itemsOf(id), item]);
  }

  removeItem(id: string, index: number): void {
    this.setField(
      id,
      'items',
      this.itemsOf(id).filter((_, at) => at !== index),
    );
  }

  moveItem(id: string, index: number, delta: -1 | 1): void {
    const items = this.itemsOf(id);
    const target = index + delta;
    if (target < 0 || target >= items.length) return;

    const next = [...items];
    [next[index], next[target]] = [next[target], next[index]];
    this.setField(id, 'items', next);
  }

  /** The block list as the save and preview calls send it. */
  toBodies(): BlockBody[] {
    return this.blocks().map(toBody);
  }

  private patch(id: string, change: (draft: BlockDraft) => BlockDraft): void {
    this.blocks.update((current) => current.map((draft) => (draft.id === id ? change(draft) : draft)));
  }
}

/** A draft as the API takes it: a block the server has not seen goes with no id. */
export function toBody(draft: BlockDraft): BlockBody {
  return {
    id: draft.id.startsWith(NEW_BLOCK_PREFIX) ? null : draft.id,
    type: draft.type,
    config: draft.config,
    isVisible: draft.isVisible,
    startsAt: draft.startsAt ? new Date(draft.startsAt).toISOString() : null,
    endsAt: draft.endsAt ? new Date(draft.endsAt).toISOString() : null,
  };
}

export function toDraft(block: BlockResponse): BlockDraft {
  return {
    id: block.id,
    type: block.type,
    config: isRecord(block.config) ? { ...block.config } : {},
    isVisible: block.isVisible,
    startsAt: localInput(block.startsAt),
    endsAt: localInput(block.endsAt),
  };
}

/**
 * Which rendered block belongs to which draft.
 *
 * `preview-draft` answers with the blocks it would serve, in order: a block that is switched off
 * or outside its window is not among them, and a block added a moment ago has whatever id the
 * server gave it. So drafts are matched by id where the server echoes it, and the rest are matched
 * in order against the drafts that are switched on and not yet matched. A draft with no render
 * (hidden, or added since the last preview) gets `null` and the canvas draws its frame empty.
 */
export function alignPreview(
  drafts: readonly BlockDraft[],
  rendered: readonly StoreBlockResponse[],
): (StoreBlockResponse | null)[] {
  const ordered = [...rendered].sort((a, b) => a.position - b.position);
  const result: (StoreBlockResponse | null)[] = drafts.map(() => null);
  const used = new Set<StoreBlockResponse>();

  drafts.forEach((draft, index) => {
    const match = ordered.find((block) => block.id === draft.id && !used.has(block));
    if (match) {
      result[index] = match;
      used.add(match);
    }
  });

  const rest = ordered.filter((block) => !used.has(block));
  let next = 0;
  drafts.forEach((draft, index) => {
    if (result[index] || !draft.isVisible || next >= rest.length) return;
    result[index] = rest[next++];
  });

  return result;
}

/**
 * A new block's starting config: a placeholder for every required text field.
 *
 * Without it the first preview of a block would be refused for the empty heading it was just
 * added with, and the editor would show an error for something the merchant has not done yet.
 */
export function seedConfig(type: BlockTypeResponse): Record<string, unknown> {
  const config: Record<string, unknown> = {};
  for (const field of type.fields) {
    if (field.isRequired && !field.isList && (field.kind === 'Text' || field.kind === 'RichText')) {
      config[field.name] = field.kind === 'RichText' ? '<p>Write something here.</p>' : 'New block';
    }
    if (field.isRequired && field.choices && field.choices.length > 0) config[field.name] = field.choices[0];
  }
  return config;
}

function serialise(blocks: readonly BlockDraft[]): string {
  return JSON.stringify(
    blocks.map((draft) => [
      draft.id.startsWith(NEW_BLOCK_PREFIX) ? NEW_BLOCK_PREFIX : draft.id,
      draft.type,
      draft.config,
      draft.isVisible,
      draft.startsAt,
      draft.endsAt,
    ]),
  );
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function clamp(value: number, min: number, max: number): number {
  return Math.min(Math.max(value, min), max);
}

function localInput(value: string | null): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  return new Date(date.getTime() - date.getTimezoneOffset() * 60_000).toISOString().slice(0, 16);
}
