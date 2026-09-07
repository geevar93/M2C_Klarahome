import { toAttributeParams, toSearchQuery } from './product-search.service';

/**
 * The listing query string.
 *
 * Worth a test during the build sprint under §3.2 rule 1: it is the mapping three things depend on
 * agreeing about — the URL the page writes, the request this builds, and the facet panel that reads
 * back what is selected — and getting it wrong is silent. A filter that is dropped does not fail;
 * it returns more products than the shopper asked for.
 *
 * The names are the generated interface's since Step 28B: the closed half of this query is declared
 * in the contract, so its spelling is the contract's rather than this file's (deliverable 6).
 */
describe('toSearchQuery', () => {
  it('sends nothing for an empty search', () => {
    expect(toSearchQuery({})).toEqual({});
  });

  it('trims the query and drops it when it is only whitespace', () => {
    expect(toSearchQuery({ q: '  teak table  ' })).toEqual({ q: 'teak table' });
    expect(toSearchQuery({ q: '   ' })).toEqual({});
  });

  it('keeps a zero minimum price, which is a filter, and drops a null one, which is not', () => {
    expect(toSearchQuery({ minPrice: 0 })).toEqual({ minPrice: 0 });
    expect(toSearchQuery({ minPrice: null, maxPrice: 4999 })).toEqual({ maxPrice: 4999 });
  });

  it('sends inStock only when it is on — the API reads its absence as "either"', () => {
    expect(toSearchQuery({ inStock: true })).toEqual({ inStock: true });
    expect(toSearchQuery({ inStock: false })).toEqual({});
  });

  it('carries the cursor, the sort and the page size', () => {
    expect(toSearchQuery({ sort: 'price_asc', cursor: 'abc', size: 24 })).toEqual({
      sort: 'price_asc',
      cursor: 'abc',
      size: 24,
    });
  });

  it('repeats a multi-valued filter rather than joining it', () => {
    expect(toSearchQuery({ brand: ['a', 'b'] })).toEqual({ brand: ['a', 'b'] });
  });

  it('leaves the attribute facets out — they are the undeclared half', () => {
    expect(toSearchQuery({ attributes: { colour: ['beige'] } })).toEqual({});
  });
});

/**
 * The open half, which no contract can declare.
 *
 * Split out at Step 28B when the closed half became a generated interface. It still travels through
 * the transport's escape hatch, and it is still the only thing that does.
 */
describe('toAttributeParams', () => {
  it('sends attribute filters under the prefix the endpoint reads them by', () => {
    expect(toAttributeParams({ attributes: { colour: ['beige'], size: ['l', 'xl'] } })).toEqual({
      'attr.colour': ['beige'],
      'attr.size': ['l', 'xl'],
    });
  });

  it('omits an attribute whose values have all been cleared', () => {
    expect(toAttributeParams({ attributes: { colour: [] } })).toEqual({});
  });

  it('sends nothing when no attribute is selected', () => {
    expect(toAttributeParams({ q: 'teak' })).toEqual({});
  });
});
