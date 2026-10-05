import { describe, expect, it } from 'vitest';

import { pickOpenParcel, pickResumableParcel } from './fulfilment.page';

const parcel = (
  id: string,
  status: string,
  createdAt: string,
  awb: string | null = null,
  weightGrams = 0,
) => ({
  id,
  status,
  createdAt,
  awb,
  weightGrams,
});

describe('pickOpenParcel', () => {
  it('takes the newest parcel that has not left and is not cancelled', () => {
    const picked = pickOpenParcel([
      parcel('old', 'Draft', '2026-10-01T09:00:00Z'),
      parcel('new', 'LabelGenerated', '2026-10-02T09:00:00Z'),
      parcel('gone', 'Cancelled', '2026-10-03T09:00:00Z'),
      parcel('left', 'InTransit', '2026-10-04T09:00:00Z'),
    ]);
    expect(picked?.id).toBe('new');
  });

  it('is null when every parcel has left or been cancelled', () => {
    expect(pickOpenParcel([parcel('a', 'Cancelled', '2026-10-01T09:00:00Z')])).toBeNull();
    expect(pickOpenParcel([])).toBeNull();
  });
});

describe('pickResumableParcel', () => {
  const draft = [parcel('d', 'Draft', '2026-10-01T09:00:00Z')];

  it('starts at the pack step for a Confirmed part with only its empty auto-drafted parcel', () => {
    expect(pickResumableParcel(draft, 'Confirmed')).toBeNull();
  });

  it('resumes a Confirmed part whose parcel has a weight or a waybill (stale row)', () => {
    expect(
      pickResumableParcel([parcel('w', 'Draft', '2026-10-01T09:00:00Z', null, 500)], 'Confirmed')?.id,
    ).toBe('w');
    expect(
      pickResumableParcel([parcel('b', 'Created', '2026-10-01T09:00:00Z', 'AWB1', 500)], 'Confirmed')?.id,
    ).toBe('b');
  });

  it('resumes any part past Confirmed, even on a bare Draft', () => {
    expect(pickResumableParcel(draft, 'Processing')?.id).toBe('d');
  });

  it('is null with no open parcel', () => {
    expect(pickResumableParcel([], 'Packed')).toBeNull();
  });
});
