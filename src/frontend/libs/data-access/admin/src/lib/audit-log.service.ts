import { Injectable, inject } from '@angular/core';
import { AuditLogResponse, PlatformApiClient } from '@klarahome/data-access-api';
import { map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

/** The audit trail's filters, in the API's own vocabulary. */
export interface AuditLogFilters {
  readonly entityType?: string;
  readonly entityId?: string;
  readonly actorId?: string;
  readonly action?: string;
  /** ISO dates, inclusive. */
  readonly from?: string;
  readonly to?: string;
}

/**
 * The audit trail.
 *
 * Two callers, one endpoint: the audit-log screen, which filters by whatever is asked of it, and
 * the trail panel on an entity page, which filters by that entity. They are the same query, so
 * they are the same code — `forEntity` is a preset, not a second implementation.
 *
 * `platform.audit_logs` is partitioned by month (Step 6) and is append-only. Nothing here writes:
 * an audit entry is written by the API as a side effect of the action it records, never by a
 * client, which is the property that makes the trail worth reading at all.
 */
@Injectable({ providedIn: 'root' })
export class AuditLogService {
  private readonly platform = inject(PlatformApiClient);

  /** A paged, filterable list. The caller owns it and drives it from its own screen. */
  list(filters: AuditLogFilters = {}, pageSize = 25): CursorList<AuditLogResponse, AuditLogFilters> {
    return new CursorList<AuditLogResponse, AuditLogFilters>(
      (current, cursor, size) =>
        this.platform
          .adminAuditLogsGet({
            EntityType: current.entityType,
            EntityId: current.entityId,
            ActorId: current.actorId,
            Action: current.action,
            From: current.from,
            To: current.to,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<AuditLogResponse> => result)),
      filters,
      pageSize,
    );
  }

  /** The trail for one record, for the panel embedded on its detail page. */
  forEntity(
    entityType: string,
    entityId: string,
    pageSize = 20,
  ): CursorList<AuditLogResponse, AuditLogFilters> {
    return this.list({ entityType, entityId }, pageSize);
  }
}
