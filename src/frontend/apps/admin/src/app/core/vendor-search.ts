import { VendorsAdminService } from '@klarahome/data-access-admin';
import { EntitySearch } from '@klarahome/ui-admin';
import { map } from 'rxjs';

/**
 * The seller typeahead's search: by name, answering the id the caller wanted with the seller's code
 * as the second line. One definition for the screens that take "a seller" (the ledger, the
 * commission simulator, the report runner) so none of them asks for a pasted id.
 */
export function vendorSearchFor(vendors: VendorsAdminService): EntitySearch {
  return (term) =>
    vendors
      .searchVendors(term)
      .pipe(
        map((sellers) =>
          sellers.map((seller) => ({ id: seller.id, label: seller.displayName, hint: seller.code })),
        ),
      );
}
