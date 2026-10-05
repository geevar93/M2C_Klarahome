import { describe, expect, it } from 'vitest';

import { pickOpenParcel } from './fulfilment.page';

const parcel = (id: string, status: string, createdAt: string) => ({ id, status, createdAt });

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
