import { AuditLogResponse } from '@klarahome/data-access-admin';
import { AuditChangeView, AuditEntryView, humanise } from '@klarahome/ui-admin';

/**
 * Turns an audit row into something a person can read.
 *
 * The API stores `before` and `after` as whole documents — that is the right thing for an audit
 * store, because a diff computed at write time can never be recomputed differently later. It is
 * the wrong thing to *show*: eighty unchanged fields around the one that moved is where a reader
 * stops looking.
 *
 * So the diff is computed here, at the last possible moment, and only the fields that actually
 * differ survive. Both documents are flattened first, so a change buried at
 * `pricing.mrp.amount` is one row rather than a nested object the reader has to compare by eye.
 */
export function toAuditEntry(row: AuditLogResponse): AuditEntryView {
  return {
    id: row.id,
    occurredAt: row.occurredAt,
    action: humanise(row.action),
    entityType: row.entityType,
    entityId: row.entityId,
    actor: describeActor(row),
    ip: row.ip,
    correlationId: row.correlationId,
    changes: diff(row.before, row.after),
  };
}

/**
 * Who did it.
 *
 * The row carries an actor *type* and an id, never a name — the audit store may not join to
 * identity, and storing a name would make the trail wrong the day somebody is renamed. So the type
 * is what is shown, with the id where there is one: "User a3f19c8e". A screen that has loaded the
 * user can do better; the log cannot.
 */
function describeActor(row: AuditLogResponse): string {
  const type = humanise(row.actorType);
  return row.actorId ? `${type} ${row.actorId.slice(0, 8)}` : type;
}

/** The fields that differ between two documents, each rendered as text. */
function diff(before: unknown, after: unknown): readonly AuditChangeView[] {
  const left = flatten(before);
  const right = flatten(after);
  const keys = [...new Set([...Object.keys(left), ...Object.keys(right)])].sort();

  const changes: AuditChangeView[] = [];
  for (const key of keys) {
    const from = left[key] ?? null;
    const to = right[key] ?? null;
    if (from === to) continue;
    changes.push({ field: key, before: from, after: to });
  }
  return changes;
}

/**
 * `{ price: { mrp: 2499 } }` → `{ 'price.mrp': '2499' }`.
 *
 * Arrays are rendered whole rather than indexed: `tags[0]`, `tags[1]`, `tags[2]` all changing
 * because one tag was inserted at the front is three rows saying nothing, where one row showing
 * both lists says what happened. Depth is capped so a document with a cycle — which a serialised
 * one cannot have, but a hand-written test double can — cannot loop.
 */
function flatten(value: unknown, prefix = '', depth = 0): Record<string, string> {
  const flat: Record<string, string> = {};
  if (value === null || value === undefined || depth > 6) return flat;

  if (typeof value !== 'object' || Array.isArray(value)) {
    if (prefix) flat[prefix] = render(value);
    return flat;
  }

  for (const [key, child] of Object.entries(value as Record<string, unknown>)) {
    const path = prefix ? `${prefix}.${key}` : key;
    if (child !== null && typeof child === 'object' && !Array.isArray(child)) {
      Object.assign(flat, flatten(child, path, depth + 1));
    } else {
      flat[path] = render(child);
    }
  }
  return flat;
}

function render(value: unknown): string {
  if (value === null || value === undefined) return '—';
  if (Array.isArray(value)) return value.map((entry) => render(entry)).join(', ');
  if (typeof value === 'object') return JSON.stringify(value);
  return `${value}`;
}
