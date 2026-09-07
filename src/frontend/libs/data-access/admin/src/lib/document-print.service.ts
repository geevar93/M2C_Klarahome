import { HttpClient, HttpParams } from '@angular/common/http';
import { DOCUMENT } from '@angular/common';
import { Injectable, inject } from '@angular/core';
import { API_BASE_URL } from '@klarahome/data-access-api';
import { Observable } from 'rxjs';

/**
 * The documents the back office prints.
 *
 * A shipping label is the one thing on this platform that is neither JSON nor a signed URL: the
 * API streams the PDF from the endpoint itself, behind the same bearer token as everything else.
 * That combination is what makes it awkward, and worth ten lines of its own rather than a
 * workaround in each screen that prints:
 *
 *  - **A plain link or `window.open` cannot fetch it.** The access token lives in memory only
 *    (docs/07-security-compliance.md §3) — deliberately, so an XSS cannot read it out of storage —
 *    and a browser-initiated navigation carries no `Authorization` header. It would 401.
 *  - **`ApiTransport` cannot fetch it either.** It fixes `responseType: 'json'` for every generated
 *    call, which is right: 489 of the 490 operations answer JSON, and a transport that guessed
 *    from the response would guess wrong on an empty 204.
 *
 * So this goes to `HttpClient` directly, for a blob. It is still the whole interceptor chain —
 * the same auth, the same correlation id, the same retry — because that is where interceptors
 * live; what is bypassed is only the generated client's response-type policy.
 *
 * A document that arrives is opened in a tab rather than downloaded: a label is printed, and a
 * PDF in a tab is one Ctrl-P away, whereas a file in the downloads folder is four clicks and a
 * file manager. The object URL is revoked on a timer because there is no event for "the tab has
 * finished with it" — revoking immediately gives an about:blank in some browsers, and never
 * revoking leaks the blob for the lifetime of the session.
 */
@Injectable({ providedIn: 'root' })
export class DocumentPrintService {
  private readonly http = inject(HttpClient);
  private readonly document = inject(DOCUMENT);
  private readonly baseUrl = inject(API_BASE_URL).replace(/\/+$/, '');

  /** The label for one shipment: the courier's own where there is one, ours where there is not. */
  shipmentLabel(shipmentId: string): Observable<Blob> {
    return this.fetch(`/api/v1/admin/shipments/${encodeURIComponent(shipmentId)}/label`);
  }

  /** The CSV a product import must be shaped like. Behind the same permission as the import. */
  productImportTemplate(): Observable<Blob> {
    return this.fetch('/api/v1/admin/products/import/template');
  }

  /**
   * One seller's ledger statement as a spreadsheet.
   *
   * The same shape of problem as the label, for the same reason: `GET
   * /admin/vendors/{id}/ledger/export` streams CSV bytes behind the bearer token rather than
   * answering a signed URL, so a plain link would download a 401 page. Unlike the label this one
   * is *saved* rather than opened — a statement is a file an accountant keeps, not a page somebody
   * prints — so callers pair it with `download`.
   */
  vendorStatementCsv(vendorId: string, from?: string, to?: string): Observable<Blob> {
    return this.fetch(`/api/v1/admin/vendors/${encodeURIComponent(vendorId)}/ledger/export`, { from, to });
  }

  /** TCS and TDS for a period, per seller. Streamed the same way, and saved the same way. */
  statutoryExtractCsv(from?: string, to?: string, vendorId?: string): Observable<Blob> {
    return this.fetch('/api/v1/admin/reports/tcs-tds/export', { from, to, vendorId });
  }

  /**
   * Saves a fetched document under a filename.
   *
   * For the template, which is a file somebody edits rather than a page somebody prints. The
   * anchor is created, clicked and discarded — there is no API for "save this blob" other than a
   * link, and leaving it in the document would be a stray element per download.
   */
  download(blob: Blob, fileName: string): void {
    const view = this.document.defaultView;
    if (!view) return;

    const url = view.URL.createObjectURL(blob);
    const anchor = this.document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    view.setTimeout(() => view.URL.revokeObjectURL(url), 10_000);
  }

  /**
   * Opens a fetched document in a new tab.
   *
   * Answers false when the browser refused — a pop-up blocker, or a call that did not come from a
   * user gesture. The caller shows that as a failure rather than letting the click do nothing,
   * which is the most confusing outcome a print button has.
   */
  openInNewTab(blob: Blob): boolean {
    const view = this.document.defaultView;
    if (!view) return false;

    const url = view.URL.createObjectURL(blob);
    const opened = view.open(url, '_blank');
    view.setTimeout(() => view.URL.revokeObjectURL(url), 60_000);
    return opened !== null;
  }

  private fetch(path: string, query?: Readonly<Record<string, string | undefined>>): Observable<Blob> {
    let params = new HttpParams();
    for (const [key, value] of Object.entries(query ?? {})) {
      // Dropped rather than sent empty: the API reads `?from=` as a value, not as its absence.
      if (value !== undefined && value !== '') params = params.set(key, value);
    }

    return this.http.get(`${this.baseUrl}${path}`, {
      params,
      responseType: 'blob',
      withCredentials: true,
    });
  }
}
