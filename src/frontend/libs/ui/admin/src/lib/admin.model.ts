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
  /**
   * A count worth showing next to the label — parcels awaiting dispatch, returns to grade. A
   * string is a count the app has already capped (`50+`); zero and null show nothing.
   */
  readonly badge?: number | string | null;
}

/** A labelled group of destinations. A section with no visible items is not rendered at all. */
export interface AdminNavSection {
  readonly label: string;
  readonly items: readonly AdminNavItem[];
  /**
   * In the sidebar: drawn as a disclosure that opens onto its items, rather than as a flat list.
   * The first section of each group is flat (it is what people come for); the rest fold away so
   * forty-odd destinations do not become forty-odd rows.
   */
  readonly collapsible?: boolean;
  /** The sidebar disclosure's own count: its items' counts added up. Zero and null show nothing. */
  readonly badge?: number | string | null;
}

/** A heading in the sidebar, with the sections filed under it. A null label draws no heading. */
export interface AdminNavGroup {
  readonly label: string | null;
  readonly sections: readonly AdminNavSection[];
}

/**
 * One of the five doors: a top-level destination in the tab bar and the rail.
 *
 * `path` is where the door opens — the first screen behind it that the session can reach, or a
 * landing page for a hub too broad for a row of tabs. `sections` are the groups behind it, and the
 * sub-nav draws them as a row of secondary tabs. A hub with no sections is not rendered.
 */
export interface AdminNavHub {
  readonly key: string;
  readonly label: string;
  readonly icon?: string;
  readonly path: string;
  readonly sections: readonly AdminNavSection[];
  /** Everything waiting behind this door, added up. Zero and null show nothing. */
  readonly badge?: number | string | null;
}

/** One thing in the "+ Create" menu. The app decides what it does when it is chosen. */
export interface AdminCreateAction {
  readonly key: string;
  readonly label: string;
  readonly hint?: string;
  readonly icon?: string;
}

/**
 * One line in the notifications panel: a queue with something waiting in it.
 *
 * These are the work queues the dashboard counts (parcels to pack, returns to decide), not a
 * feed of events, because the API has no event feed. The count is the point; the panel is the
 * answer to "is anything waiting for me", and every row is a link into the pre-filtered screen.
 */
export interface AdminAttentionItem {
  readonly key: string;
  readonly label: string;
  readonly hint: string;
  readonly count: number | string;
  readonly path: string;
  readonly icon?: string;
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
  /**
   * Pins the column to the right edge of a table that scrolls sideways (from 768px; below that rows
   * are cards). A column keyed `actions` is pinned without asking: the row's main action must never
   * be the thing that scrolls out of reach.
   */
  readonly sticky?: 'end';
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
