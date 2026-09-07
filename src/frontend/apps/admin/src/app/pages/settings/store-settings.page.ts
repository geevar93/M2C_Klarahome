import { ChangeDetectionStrategy, Component, inject, signal } from '@angular/core';
import { PlatformSettingsService, SettingsSectionResponse } from '@klarahome/data-access-admin';
import { HasPermission } from '@klarahome/data-access-auth';
import { PageHeader } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Field, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';

/** One editable leaf of a settings section, addressed by its dotted path within the section. */
interface SettingField {
  readonly path: string;
  readonly label: string;
  readonly kind: 'text' | 'number' | 'boolean' | 'json';
  value: string;
  checked: boolean;
}

/**
 * The store's settings.
 *
 * **The form is derived from the value the server sent, not from a schema this file holds.**
 * Settings are typed on the server and opaque over the wire (Step 6): each section is a strongly
 * typed options record the API validates, carried as a JSON object so that adding a field is not a
 * breaking change to the client. So this screen walks the object it was given and renders a control
 * per leaf — a string gets a text box, a number gets a number box, a boolean gets a tick, and
 * anything structured gets a JSON editor.
 *
 * That has an honest limit worth stating on the screen as well as here: **it knows the shape and
 * not the rules.** It can tell that `codThreshold` is a number because the value it was sent is a
 * number; it cannot tell that the number must be positive. The API is the validator, and a refusal
 * comes back as a field error against the section. There is deliberately no second copy of the
 * rules here to go stale — the alternative, a hand-written form per section, is a form that is
 * wrong the first time somebody adds a field on the server.
 *
 * **A section is saved whole**, because several of them hold rules that relate two fields to each
 * other and a per-field write would be a validation the server cannot perform.
 */
@Component({
  selector: 'kh-store-settings-page',
  imports: [Alert, Badge, Button, Checkbox, Control, Field, HasPermission, PageHeader, Skeleton],
  template: `
    <kh-page-header
      heading="Store settings"
      description="How this deployment behaves. Each section is validated by the API when it is saved."
    />

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Settings could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="20rem" />
    } @else {
      <kh-alert tone="info" heading="These forms know the shape, not the rules">
        The controls below are built from the values the API returned. What is allowed in each is decided by
        the API, and a refusal comes back against the section you saved.
      </kh-alert>

      @for (section of sections(); track section.key) {
        <section class="panel">
          <header>
            <h2>{{ humanise(section.key) }}</h2>
            <div class="header-meta">
              @if (section.isPublic) {
                <kh-badge tone="info">Readable by the storefront</kh-badge>
              }
              <code>{{ section.key }}</code>
            </div>
          </header>

          @if (errorFor(section.key); as message) {
            <kh-alert tone="danger" heading="It could not be saved">{{ message }}</kh-alert>
          }

          @if (fieldsFor(section.key).length === 0) {
            <p class="note">This section is empty.</p>
          } @else {
            @for (field of fieldsFor(section.key); track field.path) {
              @if (field.kind === 'boolean') {
                <kh-checkbox
                  [label]="field.label"
                  [inputId]="section.key + '-' + field.path"
                  [checked]="field.checked"
                  (checkedChange)="setBoolean(section.key, field.path, $event)"
                />
              } @else {
                <kh-field
                  [label]="field.label"
                  [for]="section.key + '-' + field.path"
                  [hint]="field.kind === 'json' ? 'JSON — a list or a nested object' : ''"
                >
                  @if (field.kind === 'json') {
                    <textarea
                      khControl
                      [id]="section.key + '-' + field.path"
                      rows="4"
                      [value]="field.value"
                      (input)="setValue(section.key, field.path, $any($event.target).value)"
                    ></textarea>
                  } @else {
                    <input
                      khControl
                      [id]="section.key + '-' + field.path"
                      [type]="field.kind === 'number' ? 'number' : 'text'"
                      [value]="field.value"
                      (input)="setValue(section.key, field.path, $any($event.target).value)"
                    />
                  }
                </kh-field>
              }
            }

            <button
              khButton
              type="button"
              variant="primary"
              *khHasPermission="'platform.settings.manage'"
              [disabled]="savingKey() === section.key"
              (click)="save(section.key)"
            >
              {{ savingKey() === section.key ? 'Saving…' : 'Save this section' }}
            </button>
          }
        </section>
      }
    }
  `,
  styles: `
    kh-alert {
      margin-block-end: var(--space-4);
    }

    .panel {
      margin-block-end: var(--space-4);
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface-raised);
    }

    header {
      display: flex;
      gap: var(--space-3);
      align-items: baseline;
      justify-content: space-between;
      flex-wrap: wrap;
      margin-block-end: var(--space-3);
    }

    h2 {
      margin: 0;
      font-size: var(--text-lg);
    }

    .header-meta {
      display: flex;
      gap: var(--space-2);
      align-items: center;
    }

    code {
      color: var(--color-text-muted);
      font-family: var(--font-mono);
      font-size: var(--text-xs);
    }

    .note {
      margin: 0;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    button {
      margin-block-start: var(--space-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StoreSettingsPage {
  private readonly settings = inject(PlatformSettingsService);
  private readonly toasts = inject(ToastService);

  protected readonly sections = signal<readonly SettingsSectionResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly savingKey = signal<string | null>(null);

  /** Field lists, keyed by section. Kept apart from the responses so an edit is not a re-parse. */
  private readonly fields = signal<Readonly<Record<string, readonly SettingField[]>>>({});
  private readonly errors = signal<Readonly<Record<string, string>>>({});

  protected fieldsFor(key: string): readonly SettingField[] {
    return this.fields()[key] ?? [];
  }

  protected errorFor(key: string): string | null {
    return this.errors()[key] || null;
  }

  constructor() {
    this.load();
  }

  protected humanise(key: string): string {
    const spaced = key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[._-]+/g, ' ');
    return spaced.charAt(0).toUpperCase() + spaced.slice(1);
  }

  protected setValue(sectionKey: string, path: string, value: string): void {
    this.fields.update((current) => ({
      ...current,
      [sectionKey]: (current[sectionKey] ?? []).map((field) =>
        field.path === path ? { ...field, value } : field,
      ),
    }));
  }

  protected setBoolean(sectionKey: string, path: string, checked: boolean): void {
    this.fields.update((current) => ({
      ...current,
      [sectionKey]: (current[sectionKey] ?? []).map((field) =>
        field.path === path ? { ...field, checked } : field,
      ),
    }));
  }

  protected save(sectionKey: string): void {
    if (this.savingKey()) return;

    const fields = this.fields()[sectionKey] ?? [];
    const value: Record<string, unknown> = {};
    let malformed: string | null = null;

    for (const field of fields) {
      const parsed = this.parse(field);
      if (parsed === MALFORMED) {
        malformed = `${field.label} is not valid JSON.`;
        break;
      }
      assign(value, field.path, parsed);
    }

    if (malformed) {
      this.errors.update((current) => ({ ...current, [sectionKey]: malformed }));
      return;
    }

    this.savingKey.set(sectionKey);
    this.errors.update((current) => ({ ...current, [sectionKey]: '' }));

    this.settings.updateSection(sectionKey, value).subscribe({
      next: (saved) => {
        this.savingKey.set(null);
        this.toasts.success(`${this.humanise(sectionKey)} saved.`);
        // Refilled from what came back, so a value the API normalised is what is on screen.
        this.fields.update((current) => ({ ...current, [sectionKey]: flatten(saved.value, '') }));
      },
      error: (error: unknown) => {
        this.savingKey.set(null);
        const problems = fieldErrors(error);
        const message = problems
          ? Object.values(problems).flatMap((messages) => [...messages])[0]
          : describeError(error, 'It could not be saved.');
        this.errors.update((current) => ({ ...current, [sectionKey]: message }));
      },
    });
  }

  /** A field's editor value as the JSON it stands for, or `MALFORMED` when it will not parse. */
  private parse(field: SettingField): unknown {
    switch (field.kind) {
      case 'boolean':
        return field.checked;
      case 'number': {
        const parsed = Number(field.value);
        return field.value.trim() === '' ? null : Number.isFinite(parsed) ? parsed : field.value;
      }
      case 'json':
        try {
          return JSON.parse(field.value) as unknown;
        } catch {
          return MALFORMED;
        }
      default:
        return field.value === '' ? null : field.value;
    }
  }

  private load(): void {
    this.loading.set(true);
    this.settings.settings().subscribe({
      next: (response) => {
        this.loading.set(false);
        this.sections.set(response.sections);
        this.fields.set(
          Object.fromEntries(response.sections.map((section) => [section.key, flatten(section.value, '')])),
        );
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'They could not be loaded.'));
      },
    });
  }
}

/** A sentinel for "this editor's text is not valid JSON", which is distinct from `null`. */
const MALFORMED = Symbol('malformed');

/**
 * Walks a settings value into a flat list of editable leaves.
 *
 * Nested objects are flattened into dotted paths so that a section with a nested options record
 * still produces ordinary fields; arrays and anything else structured stay whole and are edited as
 * JSON, because a repeater built without a schema would be guessing at what an element looks like.
 */
function flatten(value: unknown, prefix: string): SettingField[] {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) return [];

  return Object.entries(value as Record<string, unknown>).flatMap(([key, entry]) => {
    const path = prefix ? `${prefix}.${key}` : key;
    const label = humaniseLeaf(key);

    if (typeof entry === 'boolean') {
      return [{ path, label, kind: 'boolean' as const, value: '', checked: entry }];
    }
    if (typeof entry === 'number') {
      return [{ path, label, kind: 'number' as const, value: String(entry), checked: false }];
    }
    if (typeof entry === 'string' || entry === null) {
      return [{ path, label, kind: 'text' as const, value: entry ?? '', checked: false }];
    }
    if (Array.isArray(entry)) {
      return [{ path, label, kind: 'json' as const, value: JSON.stringify(entry, null, 2), checked: false }];
    }

    const nested = flatten(entry, path);
    return nested.length > 0
      ? nested
      : [{ path, label, kind: 'json' as const, value: JSON.stringify(entry, null, 2), checked: false }];
  });
}

function humaniseLeaf(key: string): string {
  const spaced = key.replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/[._-]+/g, ' ');
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

/** Writes a value back at its dotted path, creating the objects on the way down. */
function assign(target: Record<string, unknown>, path: string, value: unknown): void {
  const segments = path.split('.');
  let current = target;

  for (const segment of segments.slice(0, -1)) {
    const existing = current[segment];
    if (typeof existing !== 'object' || existing === null || Array.isArray(existing)) {
      current[segment] = {};
    }
    current = current[segment] as Record<string, unknown>;
  }

  current[segments[segments.length - 1]] = value;
}
