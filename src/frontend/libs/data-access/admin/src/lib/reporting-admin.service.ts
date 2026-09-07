import { Injectable, inject } from '@angular/core';
import {
  ReportColumn,
  ReportDefinition,
  ReportDownloadResponse,
  ReportRunResponse,
  ReportScheduleResponse,
  ReportingApiClient,
  ScheduleBody,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

/** One row of a produced report, keyed on the column keys the definition declares. */
export type ReportRow = Readonly<Record<string, unknown>>;

/**
 * A produced report: what it is, what it covers, and its rows.
 *
 * **Hand-written, and it is the only response shape in this library that is.** The endpoint
 * declares two 200 bodies — this one for `format=json` and `ReportRunResponse` for `format=csv` —
 * and the OpenAPI document can only carry one, so the generator typed the operation as the CSV
 * answer and never emitted this shape at all. The request is correct; only the declared response
 * is wrong, so `table()` casts through `unknown` at that one call and this interface is what the
 * rest of the application sees. Recorded in `PARKING_LOT.md`: the durable fix is for the two
 * formats to be two operations, or for the JSON body to be the declared one.
 *
 * The rows are dictionaries rather than a type per report, deliberately and on the server's side
 * too: thirteen report shapes would be thirteen response types the admin app has to switch on,
 * whereas a declared column list plus untyped rows lets one table render all of them.
 */
export interface ReportTable {
  readonly key: string;
  readonly name: string;
  readonly columns: readonly ReportColumn[];
  readonly from: string;
  readonly to: string;
  readonly groupBy: string | null;
  readonly currencyCode: string;
  readonly rows: readonly ReportRow[];
  /** Column totals, where totalling means anything. Null for a report of rates. */
  readonly totals: ReportRow | null;
  /** True when the row ceiling was hit. The screen says so rather than letting a partial read. */
  readonly truncated: boolean;
}

/** What a report is asked for. Every field is optional; the server has a default period. */
export interface ReportRequest {
  readonly from?: string;
  readonly to?: string;
  readonly groupBy?: string;
  readonly vendorId?: string;
}

export interface RunFilters {
  readonly reportKey?: string;
  readonly status?: string;
}

/**
 * The reporting surface.
 *
 * **The report catalogue is data, and the screens are built from it.** `definitions()` answers
 * every report this platform produces with its columns, its groupings and whether a seller may run
 * it — so the picker, the parameter form and the table headings all come off one declaration that
 * the CSV writer also uses. Nothing in the admin app names a report except by the key the server
 * gave it, which is why a report added on the server appears here with no change to this file.
 *
 * **A seller and a manager run the same query.** Scope comes off the caller's token, not off a
 * parameter, and the catalogue a seller is offered already excludes the reports that are about the
 * store rather than about them. `vendorId` exists for the manager who wants one seller's figures.
 *
 * **CSV is not a download; it is a run.** Asking for `format=csv` produces a file in the private
 * bucket and answers with the run, and `download()` then exchanges that for a short-lived signed
 * link. The indirection is what makes a report somebody asked for and one that arrives by email
 * every Monday the same artefact, produced by the same code and recorded in the same log.
 */
@Injectable({ providedIn: 'root' })
export class ReportingAdminService {
  private readonly api = inject(ReportingApiClient);

  /** Every report the caller may run, with its columns and its groupings. */
  definitions(): Observable<ReportDefinition[]> {
    return this.api.adminListReports();
  }

  /**
   * Runs a report and returns its table.
   *
   * The cast is the one described on `ReportTable`: the operation's declared response is the CSV
   * answer, and this call asks for JSON. It is a lie about the type, told in one place, rather
   * than a lie about the request.
   */
  table(reportKey: string, request: ReportRequest = {}): Observable<ReportTable> {
    return this.api
      .adminRunReport(reportKey, {
        from: request.from,
        to: request.to,
        groupBy: request.groupBy,
        vendorId: request.vendorId,
        format: 'json',
      })
      .pipe(map((result) => result as unknown as ReportTable));
  }

  /** Produces the same report as a file. Answers the run; `download` turns that into a link. */
  export(reportKey: string, request: ReportRequest = {}): Observable<ReportRunResponse> {
    return this.api.adminRunReport(reportKey, {
      from: request.from,
      to: request.to,
      groupBy: request.groupBy,
      vendorId: request.vendorId,
      format: 'csv',
    });
  }

  runs(filters: RunFilters = {}, pageSize = 25): CursorList<ReportRunResponse, RunFilters> {
    return new CursorList<ReportRunResponse, RunFilters>(
      (current, cursor, size) =>
        this.api
          .adminListReportRuns({
            reportKey: current.reportKey,
            status: current.status,
            cursor: cursor ?? undefined,
            size,
          })
          .pipe(map((result): CursorPage<ReportRunResponse> => result)),
      filters,
      pageSize,
    );
  }

  /** A short-lived signed link. Expires; the screen fetches a fresh one rather than caching it. */
  download(runId: string): Observable<ReportDownloadResponse> {
    return this.api.adminDownloadReportRun(runId);
  }

  // ---- Standing instructions --------------------------------------------------------------------

  schedules(activeOnly = false): Observable<ReportScheduleResponse[]> {
    return this.api.adminListReportSchedules({ activeOnly });
  }

  createSchedule(body: ScheduleBody): Observable<ReportScheduleResponse> {
    return this.api.adminCreateReportSchedule(body);
  }

  /** The timetable and the recipients. Which report it runs is not editable, by design. */
  updateSchedule(id: string, body: ScheduleBody): Observable<ReportScheduleResponse> {
    return this.api.adminUpdateReportSchedule(id, body);
  }

  deleteSchedule(id: string): Observable<void> {
    return this.api.adminDeleteReportSchedule(id);
  }
}
