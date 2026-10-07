import { humanise, toneFor } from '@klarahome/ui-admin';

describe('the status vocabulary', () => {
  it('humanises the way the API spells a status', () => {
    expect(humanise('PartiallyShipped')).toBe('Partially shipped');
    expect(humanise('under_review')).toBe('Under review');
    expect(humanise(null)).toBe('—');
  });

  it.each([
    ['Paid', 'success'],
    ['Delivered', 'success'],
    ['Pending', 'warning'],
    ['Unfulfilled', 'warning'],
    ['Draft', 'info'],
    ['Shipped', 'info'],
    ['InProgress', 'info'],
    ['ReturnInProgress', 'info'],
    ['OutForDelivery', 'info'],
    ['PickedUp', 'info'],
    ['Exception', 'danger'],
    ['PartiallyShipped', 'info'],
    ['Failed', 'danger'],
    ['Rejected', 'danger'],
    ['Cancelled', 'neutral'],
    ['Archived', 'neutral'],
    ['PartiallyRefunded', 'neutral'],
    ['Inactive', 'neutral'],
    ['Something new', 'neutral'],
  ])('draws %s as %s', (status, tone) => {
    expect(toneFor(status)).toBe(tone);
  });

  it('does not let "unverified" read as verified, or "unpaid" as paid', () => {
    expect(toneFor('Unverified')).toBe('warning');
    expect(toneFor('Unpaid')).toBe('warning');
  });
});
