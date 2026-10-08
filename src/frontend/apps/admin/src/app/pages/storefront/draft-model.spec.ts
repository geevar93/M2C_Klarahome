import { BlockResponse, BlockTypeResponse } from '@klarahome/data-access-admin';
import { StoreBlockResponse } from '@klarahome/data-access-content';

import { HomeDraft, alignPreview, seedConfig, toBody } from './draft-model';

const block = (id: string, position: number, config: Record<string, unknown> = {}): BlockResponse => ({
  id,
  type: 'Hero',
  position,
  config,
  isVisible: true,
  startsAt: null,
  endsAt: null,
});

const rendered = (id: string, position: number): StoreBlockResponse => ({
  id,
  type: 'Hero',
  position,
  config: {},
  images: [],
  products: [],
  categories: [],
});

const heroType: BlockTypeResponse = {
  type: 'Hero',
  label: 'Hero',
  description: '',
  maxItems: 0,
  isPrivileged: false,
  itemFields: null,
  fields: [
    { name: 'headline', kind: 'Text', isRequired: true, isList: false, maxLength: 120, choices: null },
    { name: 'align', kind: 'Choice', isRequired: true, isList: false, maxLength: 0, choices: ['left', 'centre'] },
    { name: 'subheadline', kind: 'Text', isRequired: false, isList: false, maxLength: 200, choices: null },
  ],
};

describe('HomeDraft', () => {
  let draft: HomeDraft;

  beforeEach(() => {
    draft = new HomeDraft();
    draft.load([block('b2', 1), block('b1', 0, { headline: 'Hello' })]);
  });

  it('loads in position order and is clean', () => {
    expect(draft.blocks().map((entry) => entry.id)).toEqual(['b1', 'b2']);
    expect(draft.dirty()).toBe(false);
  });

  it('is dirty after an edit, and clean again when the edit is undone', () => {
    draft.setField('b1', 'headline', 'Hello!');
    expect(draft.dirty()).toBe(true);

    draft.setField('b1', 'headline', 'Hello');
    expect(draft.dirty()).toBe(false);
  });

  it('counts a move, a hide, an add and a remove as changes, and undoing them as none', () => {
    draft.move('b1', 1);
    expect(draft.dirty()).toBe(true);
    draft.move('b1', -1);
    expect(draft.dirty()).toBe(false);

    draft.setVisible('b2', false);
    expect(draft.dirty()).toBe(true);
    draft.setVisible('b2', true);
    expect(draft.dirty()).toBe(false);

    const id = draft.add(heroType, 0);
    expect(draft.blocks()[0].id).toBe(id);
    expect(draft.dirty()).toBe(true);
    draft.remove(id);
    expect(draft.dirty()).toBe(false);

    draft.remove('b2');
    expect(draft.dirty()).toBe(true);
  });

  it('keeps the edit and clears dirty when it is marked saved', () => {
    draft.setField('b1', 'headline', 'Changed');
    draft.markSaved();

    expect(draft.dirty()).toBe(false);
    expect(draft.blocks()[0].config['headline']).toBe('Changed');
  });

  it('does not move a block past either end', () => {
    expect(draft.move('b1', -1)).toBe(false);
    expect(draft.move('b2', 1)).toBe(false);
    expect(draft.move('b1', 1)).toBe(true);
    expect(draft.blocks().map((entry) => entry.id)).toEqual(['b2', 'b1']);
  });

  it('restores a removed block where it was', () => {
    const [first] = draft.blocks();
    draft.remove('b1');
    expect(draft.blocks().map((entry) => entry.id)).toEqual(['b2']);

    draft.restore(first, 0);
    expect(draft.blocks().map((entry) => entry.id)).toEqual(['b1', 'b2']);
    expect(draft.dirty()).toBe(false);
  });

  it('edits one item without touching its neighbours, and moves items within bounds', () => {
    draft.addItem('b1', { question: 'A' });
    draft.addItem('b1', { question: 'B' });
    draft.setItemField('b1', 1, 'answer', 'yes');
    draft.moveItem('b1', 1, -1);
    draft.moveItem('b1', 0, -1);

    expect(draft.itemsOf('b1')).toEqual([{ question: 'B', answer: 'yes' }, { question: 'A' }]);
  });

  it('sends a block the server has not seen with no id, and every other field as held', () => {
    draft.setVisible('b2', false);
    const added = draft.add(heroType);
    const bodies = draft.toBodies();

    expect(bodies.map((body) => body.id)).toEqual(['b1', 'b2', null]);
    expect(bodies[1].isVisible).toBe(false);
    expect(toBody(draft.blocks().find((entry) => entry.id === added)!).type).toBe('Hero');
  });

  it('seeds required text and choice fields so a new block can be previewed at once', () => {
    expect(seedConfig(heroType)).toEqual({ headline: 'New block', align: 'left' });
  });
});

describe('alignPreview', () => {
  const drafts = (...entries: [string, boolean][]) =>
    entries.map(([id, isVisible]) => ({
      id,
      type: 'Hero',
      config: {},
      isVisible,
      startsAt: '',
      endsAt: '',
    }));

  it('matches by id where the server echoes it, whatever order it answers in', () => {
    const result = alignPreview(drafts(['a', true], ['b', true]), [rendered('b', 1), rendered('a', 0)]);
    expect(result.map((entry) => entry?.id)).toEqual(['a', 'b']);
  });

  it('gives a hidden draft no render, and matches the rest in order', () => {
    // The server dropped the hidden block and gave the new one its own id.
    const result = alignPreview(drafts(['a', true], ['hidden', false], ['new-1', true]), [
      rendered('a', 0),
      rendered('server-id', 1),
    ]);
    expect(result.map((entry) => entry?.id ?? null)).toEqual(['a', null, 'server-id']);
  });

  it('leaves a draft without a render when the server drew fewer blocks than were sent', () => {
    const result = alignPreview(drafts(['a', true], ['b', true]), [rendered('a', 0)]);
    expect(result[1]).toBeNull();
  });
});
