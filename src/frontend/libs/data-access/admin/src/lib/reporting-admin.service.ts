import { Injectable, inject } from '@angular/core';
import {
  ReportDefinition,
  ReportDownloadResponse,
  ReportResult,
  ReportRunResponse,
  ReportScheduleResponse,
  ReportingApiClient,
  ScheduleBody,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

/**
 * A produced report: what it is, what it covers, and its rows.
 *
 * The generated shape, not a hand-written one. It used to be written out here because the endpoint
 * declared two 200 bodies — this one for `format=json` and `ReportRunResponse` for `format=csv` —
 * and OpenAPI carries one body per status, so the generator typed the operation as the CSV answer
 * and never emitted this shape at all; `table()` cast through `unknown` to use it. Step 28B split
 * the two formats into two operations, which is what makes this an alias rather than a copy
 * (deliverable 4).
 */
export type ReportTable = ReportResult;

/** One row of a produced report, keyed on the column keys the definition declares. */
export type ReportRow = Readonly<Record<string, unknown>>;

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
 * **CSV is not a download; it is a run.** `export()` produces a file in the private bucket and
 * answers with the run, and `download()` then exchanges that for a short-lived signed link. The
 * indirection is what makes a report somebody asked for and one that arrives by email every Monday
 * the same artefact, produced by the same code and recorded in the same log. It is a POST rather
 * than a format on the GET, because producing a file, recording a run and handing back a link is
 * not a read (Step 28B, deliverable 4).
 */
@Injectable({ providedIn: 'root' })
export class ReportingAdminService {
  private readonly api = inject(ReportingApiClient);

  /** Every report the caller may run, with its columns and its groupings. */
  definitions(): Observable<ReportDefinition[]> {
    return this.api.adminListReports();
  }

  /** Runs a report and returns its table. */
  table(reportKey: string, request: ReportRequest = {}): Observable<ReportTable> {
    return this.api.adminRunReport(reportKey, {
      from: request.from,
      to: request.to,
      groupBy: request.groupBy,
      vendorId: request.vendorId,
    });
  }

  /** Produces the same report as a file. Answers the run; `download` turns that into a link. */
  export(reportKey: string, request: ReportRequest = {}): Observable<ReportRunResponse> {
    return this.api.adminExportReport(reportKey, {
      from: request.from ?? null,
      to: request.to ?? null,
      groupBy: request.groupBy ?? null,
      vendorId: request.vendorId ?? null,
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
