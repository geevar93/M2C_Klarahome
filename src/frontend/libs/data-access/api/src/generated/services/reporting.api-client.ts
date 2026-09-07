/**
 * DO NOT EDIT. Generated from the API's OpenAPI document by tools/generate-api-client.mjs.
 *
 * Regenerate with:  pwsh tools/generate-api-client.ps1
 * CI fails if this file differs from what the current API produces.
 */
/* eslint-disable */

import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

import { ApiRequestOptions, ApiTransport } from '../../runtime';
import type * as Models from '../models';

/** Query string for `adminListReportRuns`. */
export interface AdminListReportRunsQuery {
  reportKey?: string;
  status?: string;
  cursor?: string;
  size?: number;
}

/** Query string for `adminListReportSchedules`. */
export interface AdminListReportSchedulesQuery {
  activeOnly?: boolean;
}

/** Query string for `adminRunReport`. */
export interface AdminRunReportQuery {
  from?: string;
  to?: string;
  groupBy?: string;
  vendorId?: string;
}

/** `Reporting` endpoints, generated from the API's OpenAPI document. */
@Injectable({ providedIn: 'root' })
export class ReportingApiClient {
  private readonly http = inject(ApiTransport);
  private readonly baseUrl = this.http.baseUrl;

  /**
   * Opens a standing instruction to produce a report on a timetable.
   * `POST /api/v1/admin/report-schedules`
   */
  adminCreateReportSchedule(body: Models.ScheduleBody, options?: ApiRequestOptions): Observable<Models.ReportScheduleResponse> {
    return this.http.request<Models.ReportScheduleResponse>('POST', `${this.baseUrl}/api/v1/admin/report-schedules`, body, undefined, options);
  }

  /**
   * Removes a standing instruction. The reports it produced stay.
   * `DELETE /api/v1/admin/report-schedules/{id}`
   */
  adminDeleteReportSchedule(id: string, options?: ApiRequestOptions): Observable<void> {
    return this.http.request<void>('DELETE', `${this.baseUrl}/api/v1/admin/report-schedules/${encodeURIComponent(String(id))}`, undefined, undefined, options);
  }

  /**
   * A short-lived signed link to a produced report.
   * `GET /api/v1/admin/report-runs/{id}/download`
   */
  adminDownloadReportRun(id: string, options?: ApiRequestOptions): Observable<Models.ReportDownloadResponse> {
    return this.http.request<Models.ReportDownloadResponse>('GET', `${this.baseUrl}/api/v1/admin/report-runs/${encodeURIComponent(String(id))}/download`, undefined, undefined, options);
  }

  /**
   * Produces a CSV of the same report and answers with the run, whose download link is fetched from /admin/report-runs/{id}/download.
   * `POST /api/v1/admin/reports/{reportKey}/export`
   */
  adminExportReport(reportKey: string, body?: null | Models.ExportReportBody, options?: ApiRequestOptions): Observable<Models.ReportRunResponse> {
    return this.http.request<Models.ReportRunResponse>('POST', `${this.baseUrl}/api/v1/admin/reports/${encodeURIComponent(String(reportKey))}/export`, body, undefined, options);
  }

  /**
   * Every report produced, newest first, failures included.
   * `GET /api/v1/admin/report-runs`
   */
  adminListReportRuns(query?: AdminListReportRunsQuery, options?: ApiRequestOptions): Observable<Models.PagedResultOfReportRunResponse> {
    return this.http.request<Models.PagedResultOfReportRunResponse>('GET', `${this.baseUrl}/api/v1/admin/report-runs`, undefined, query, options);
  }

  /**
   * Every report this platform produces, with its columns and its groupings.
   * `GET /api/v1/admin/reports`
   */
  adminListReports(options?: ApiRequestOptions): Observable<Models.ReportDefinition[]> {
    return this.http.request<Models.ReportDefinition[]>('GET', `${this.baseUrl}/api/v1/admin/reports`, undefined, undefined, options);
  }

  /**
   * The standing instructions, soonest due first.
   * `GET /api/v1/admin/report-schedules`
   */
  adminListReportSchedules(query?: AdminListReportSchedulesQuery, options?: ApiRequestOptions): Observable<Models.ReportScheduleResponse[]> {
    return this.http.request<Models.ReportScheduleResponse[]>('GET', `${this.baseUrl}/api/v1/admin/report-schedules`, undefined, query, options);
  }

  /**
   * Runs a report and answers with the table.
   * `GET /api/v1/admin/reports/{reportKey}`
   */
  adminRunReport(reportKey: string, query?: AdminRunReportQuery, options?: ApiRequestOptions): Observable<Models.ReportResult> {
    return this.http.request<Models.ReportResult>('GET', `${this.baseUrl}/api/v1/admin/reports/${encodeURIComponent(String(reportKey))}`, undefined, query, options);
  }

  /**
   * Changes the timetable and the recipients. The report it runs is not editable.
   * `PUT /api/v1/admin/report-schedules/{id}`
   */
  adminUpdateReportSchedule(id: string, body: Models.ScheduleBody, options?: ApiRequestOptions): Observable<Models.ReportScheduleResponse> {
    return this.http.request<Models.ReportScheduleResponse>('PUT', `${this.baseUrl}/api/v1/admin/report-schedules/${encodeURIComponent(String(id))}`, body, undefined, options);
  }
}
