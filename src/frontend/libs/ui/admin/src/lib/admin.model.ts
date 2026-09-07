/**
 * The view models the admin building blocks are driven by.
 *
 * Every one of these is a **view model, not a DTO**. The `ui` layer may not import `data-access`
 * (the lint boundary in `eslint.config.mjs` enforces it), and that rule earns its keep here more
 * than anywhere else in the workspace: a data table that took `PagedResultOfAdminUserResponse`
 * would be a data table for one endpoint. These types are what forty screens have in common, and
 * the app maps its own responses onto them on the way in.
 *
 * They are also the reason none of these components fetch anything. A table that owned its own
 * request could not be reused by a page that already had the rows, could not be rendered in a
 * test without a mock server, and would decide paging policy in the wrong layer.
 */

// -------------------------------------------------------------------------------------------------
// Navigation
// -------------------------------------------------------------------------------------------------

/** One destination in the sidebar. */
export interface AdminNavItem {
  readonly label: string;
  /** An internal router path. Every admin destination is internal; there are no outbound links. */
  readonly path: string;
  readonly icon?: string;
  /** A count worth showing next to the label — parcels awaiting dispatch, returns to grade. */
  readonly badge?: number | null;
}

/** A labelled group of destinations. A section with no visible items is not rendered at all. */
export interface AdminNavSection {
  readonly label: string;
  readonly items: readonly AdminNavItem[];
}

// -------------------------------------------------------------------------------------------------
// Data table
// -------------------------------------------------------------------------------------------------

/** How a cell's value is drawn. `text` is the default and covers most of them. */
export type ColumnKind = 'text' | 'number' | 'date' | 'badge' | 'custom';

/**
 * One column.
 *
 * `key` is both the property read off the row and the identifier the caller gets back when the
 * column is sorted or hidden, so a table cannot sort by a column that is not in it.
 */
export interface DataTableColumn<TRow> {
  readonly key: string;
  readonly label: string;
  readonly kind?: ColumnKind;
  /**
   * The sort field the API accepts for this column, when it accepts one. **A column with no
   * `sortKey` is not sortable** — this platform's list endpoints declare the orders they support,
   * and offering a header that produces a 400 is worse than offering none.
   */
  readonly sortKey?: string;
  /** Reads the display value. Omitted for a `custom` column, whose cell the caller projects. */
  readonly value?: (row: TRow) => string | number | null | undefined;
  /** The tone for a `badge` column. */
  readonly tone?: (row: TRow) => BadgeTone;
  /** Hidden by default in the column chooser. The column still exists and can be switched on. */
  readonly hiddenByDefault?: boolean;
  /** Right-aligned. Set automatically for `number`; here for the exceptions. */
  readonly numeric?: boolean;
  /** A fixed width, e.g. `12rem`. Omitted means the column takes what it needs. */
  readonly width?: string;
}

/** Re-declared rather than imported from `ui-primitives` so a column definition is data. */
export type BadgeTone = 'neutral' | 'primary' | 'success' | 'warning' | 'danger' | 'info';

export type SortDirection = 'asc' | 'desc';

/** What the table asks the caller to fetch. */
export interface TableSort {
  readonly key: string;
  readonly direction: SortDirection;
}

/**
 * An action offered on the current selection.
 *
 * `destructive` does two things: it draws the control in the danger variant, and it makes the
 * table ask for confirmation before it emits — see `ConfirmDialog` for why that is not optional.
 */
export interface BulkAction {
  readonly key: string;
  readonly label: string;
  readonly destructive?: boolean;
  /** Disabled with a reason, rather than absent — a control that vanishes teaches nothing. */
  readonly disabledReason?: string | null;
}

/**
 * Where the table currently is, in the API's own terms.
 *
 * **There are no page numbers, and that is not an omission.** Every list endpoint on this platform
 * pages by keyset (`PageInfo.nextCursor`, `docs/04-api-specification.md` §1.1) so that a page
 * fetched while rows are being inserted neither repeats nor skips one. A "page 7 of 42" control
 * cannot be built on that, and faking it with a row count the API says may be null would be a
 * control that is wrong exactly when the data is busiest. The table therefore offers Previous and
 * Next, and the caller keeps the cursors it has already seen.
 */
export interface TablePage {
  /** The cursor for the following page, or null when this is the last one. */
  readonly nextCursor: string | null;
  /** True when the caller holds a cursor for the page before this one. */
  readonly hasPrevious: boolean;
  /** How many rows were asked for. */
  readonly size: number;
  /** The server's row count when it offers one. Rendered as "about", never as a page count. */
  readonly total?: number | null;
}

// -------------------------------------------------------------------------------------------------
// Audit trail
// -------------------------------------------------------------------------------------------------

/** One change, as the audit trail records it. */
export interface AuditEntryView {
  readonly id: string;
  readonly occurredAt: string;
  /** `order.cancelled`, `vendor.approved` — the action as the API spells it. */
  readonly action: string;
  readonly entityType: string;
  readonly entityId: string | null;
  /** Who did it: a user's name where one is known, otherwise the actor type. */
  readonly actor: string;
  readonly ip: string | null;
  readonly correlationId: string | null;
  /** The changed fields, before and after. Empty for an action that changed no field. */
  readonly changes: readonly AuditChangeView[];
}

/** One field that moved. Both sides are already rendered as text by the mapper. */
export interface AuditChangeView {
  readonly field: string;
  readonly before: string | null;
  readonly after: string | null;
}

// -------------------------------------------------------------------------------------------------
// Uploads
// -------------------------------------------------------------------------------------------------

/** One file the uploader is holding, and what has become of it. */
export interface UploadItem {
  readonly id: string;
  readonly name: string;
  readonly sizeBytes: number;
  readonly status: 'pending' | 'uploading' | 'done' | 'failed';
  /** 0–100 while uploading. Null when the transport cannot report progress. */
  readonly progress?: number | null;
  /** Why it failed, in the API's words. */
  readonly error?: string | null;
}
