import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import {
  PlatformSettingsService,
  SettingsFieldSchema,
  SettingsSchemaResponse,
  SettingsSectionResponse,
} from '@klarahome/data-access-admin';
import { HasPermission, SessionStore } from '@klarahome/data-access-auth';
import { HasUnsavedChanges, PageHeader, ThemePicker } from '@klarahome/ui-admin';
import { Alert, Badge, Button, Checkbox, Control, Disclosure, Field, Skeleton } from '@klarahome/ui-primitives';
import {
  THEME_MARKER_TOKEN,
  THEME_PRESETS,
  ToastService,
  presetIdFromTokens,
} from '@klarahome/util';

import { describeError, fieldErrors } from '../../core/describe-error';

/** One editable leaf of a settings section, addressed by its dotted path within the section. */
interface SettingField {
  readonly path: string;
  readonly label: string;
  readonly kind: 'text' | 'number' | 'boolean' | 'json' | 'color' | 'theme';
  value: string;
  checked: boolean;
  /** The rules the server holds for this leaf, where it declared any. */
  schema?: SettingsFieldSchema;
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
 * **The rules come from the server too, and that is Step 28B's change** (deliverable 10). This
 * screen used to know that `codThreshold` was a number and not that the number had to be positive,
 * so an operator learnt the rule by being refused. `GET /admin/settings/schema` now serves what the
 * validators encode — bounds, lengths, patterns, required fields and the words a choice accepts —
 * and the controls carry them.
 *
 * It is still not a second validator, and the distinction matters: the schema is *advisory*, the
 * API validates every write, and a rule the reader cannot express (a conditional, a comparison
 * between two fields) is simply absent rather than approximated. A refusal still comes back as a
 * field error against the section, and it is still the authority.
 *
 * **A section is saved whole**, because several of them hold rules that relate two fields to each
 * other and a per-field write would be a validation the server cannot perform.
 */
@Component({
  selector: 'kh-store-settings-page',
  imports: [
    Alert,
    Badge,
    Button,
    Checkbox,
    Control,
    Disclosure,
    Field,
    HasPermission,
    PageHeader,
    Skeleton,
    ThemePicker,
  ],
  template: `
    <kh-page-header
      heading="Store settings"
      description="Settings that control how your store looks and behaves."
    />

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Settings could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="20rem" />
    } @else {
      <p class="note">
        Each section saves separately, so you can update one part without touching the rest. If a
        save is rejected, the problem is shown next to that section.
      </p>

      <div class="layout">
      <nav class="sections" aria-label="Settings sections">
        <ul>
          @for (entry of sections(); track entry.key) {
            <li>
              <button
                type="button"
                [attr.aria-current]="current() === entry.key ? 'true' : null"
                (click)="jump(entry.key)"
              >
                {{ humanise(entry.key) }}
              </button>
            </li>
          }
        </ul>
      </nav>

      <div class="stack">
      @for (section of sections(); track section.key) {
        <section class="panel" [id]="'settings-' + section.key">
          <header>
            <h2>{{ humanise(section.key) }}</h2>
            @if (section.isPublic) {
              <kh-badge tone="info">Shown on the storefront</kh-badge>
            }
          </header>

          @if (errorFor(section.key); as message) {
            <kh-alert tone="danger" heading="It could not be saved">{{ message }}</kh-alert>
          }

          @if (fieldsFor(section.key).length === 0) {
            <p class="note">This section is empty.</p>
          } @else {
            @for (field of fieldsFor(section.key); track field.path) {
              @if (field.kind === 'theme') {
                <kh-theme-picker
                  [presets]="presets"
                  [selected]="presetFor(field)"
                  [disabled]="!canManage()"
                  (chosen)="chooseTheme(section.key, field.path, $event)"
                />
                <kh-disclosure heading="Colour tokens" hint="Advanced">
                  <kh-field
                    label="Tokens the theme sets"
                    [for]="section.key + '-' + field.path"
                    hint="JSON — one CSS custom property per key. Choosing a theme above rewrites this."
                  >
                    <textarea
                      khControl
                      [id]="section.key + '-' + field.path"
                      rows="8"
                      [disabled]="!canManage()"
                      [value]="field.value"
                      (input)="setValue(section.key, field.path, $any($event.target).value)"
                    ></textarea>
                  </kh-field>
                </kh-disclosure>
              } @else if (field.kind === 'color') {
                <kh-field [label]="field.label" [for]="section.key + '-' + field.path" [hint]="hintFor(field)">
                  <span class="colour">
                    <input
                      class="colour-well"
                      type="color"
                      [attr.aria-label]="field.label + ' picker'"
                      [disabled]="!canManage()"
                      [value]="field.value"
                      (input)="setValue(section.key, field.path, $any($event.target).value)"
                    />
                    <input
                      khControl
                      [id]="section.key + '-' + field.path"
                      type="text"
                      maxlength="7"
                      [attr.pattern]="field.schema?.pattern ?? null"
                      [disabled]="!canManage()"
                      [value]="field.value"
                      (input)="setValue(section.key, field.path, $any($event.target).value)"
                    />
                  </span>
                </kh-field>
              } @else if (field.kind === 'boolean') {
                <kh-checkbox
                  [label]="field.label"
                  [inputId]="section.key + '-' + field.path"
                  [checked]="field.checked"
                  [disabled]="!canManage()"
                  (checkedChange)="setBoolean(section.key, field.path, $event)"
                />
              } @else {
                <kh-field
                  [label]="field.label"
                  [for]="section.key + '-' + field.path"
                  [hint]="hintFor(field)"
                  [optional]="!field.schema?.isRequired && field.kind !== 'json'"
                >
                  @if (field.kind === 'json') {
                    <textarea
                      khControl
                      [id]="section.key + '-' + field.path"
                      rows="4"
                      [disabled]="!canManage()"
                      [value]="field.value"
                      (input)="setValue(section.key, field.path, $any($event.target).value)"
                    ></textarea>
                  } @else if (choicesFor(field); as choices) {
                    <select
                      khControl
                      [id]="section.key + '-' + field.path"
                      [disabled]="!canManage()"
                      [value]="field.value"
                      (change)="setValue(section.key, field.path, $any($event.target).value)"
                    >
                      @for (choice of choices; track choice) {
                        <option [value]="choice">{{ choice }}</option>
                      }
                    </select>
                  } @else {
                    <input
                      khControl
                      [id]="section.key + '-' + field.path"
                      [type]="field.kind === 'number' ? 'number' : 'text'"
                      [attr.min]="field.schema?.minimum ?? null"
                      [attr.max]="field.schema?.maximum ?? null"
                      [attr.maxlength]="field.schema?.maxLength ?? null"
                      [attr.pattern]="field.schema?.pattern ?? null"
                      [attr.required]="field.schema?.isRequired ? '' : null"
                      [disabled]="!canManage()"
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
      </div>
      </div>
    }
  `,
  styles: `
    /* A single track that may shrink: an auto track is as wide as its widest nowrap child, which
       is the whole tab row, and that pushed the form card off the right edge of a phone. */
    .layout {
      display: grid;
      grid-template-columns: minmax(0, 1fr);
      gap: var(--space-4);
    }

    .sections {
      min-width: 0;
    }

    .stack {
      min-width: 0;
      max-width: 48rem;
    }

    /* A phone gets the sections as a row of tabs that scrolls sideways; a desktop gets the
       prototype's vertical list, held beside the form while it scrolls. */
    .sections ul {
      display: flex;
      gap: var(--space-1);
      margin: 0;
      padding: 0;
      overflow-x: auto;
      list-style: none;
      scrollbar-width: none;
    }

    .sections li {
      display: flex;
    }

    .sections button {
      min-block-size: 2.5rem;
      line-height: var(--leading-tight);
      padding: var(--space-2) var(--space-3);
      border: 0;
      border-radius: var(--radius-md);
      background: none;
      color: var(--color-text-muted);
      font-size: var(--text-sm);
      font-weight: var(--weight-medium);
      white-space: nowrap;
      cursor: pointer;
    }

    .sections button:hover {
      background: var(--color-surface);
      color: var(--color-text);
    }

    .sections button[aria-current='true'] {
      background: var(--color-primary-subtle);
      color: var(--color-primary);
    }

    @media (min-width: 1024px) {
      .layout {
        grid-template-columns: 13rem minmax(0, 1fr);
        align-items: start;
      }

      .sections {
        position: sticky;
        inset-block-start: calc(var(--header-height) + var(--space-4));
      }

      .sections ul {
        flex-direction: column;
        overflow: visible;
      }

      .sections button {
        inline-size: 100%;
        text-align: start;
      }

      .panel {
        scroll-margin-block-start: calc(var(--header-height) + var(--space-4));
      }
    }

    kh-alert,
    kh-theme-picker,
    kh-disclosure {
      display: block;
      margin-block-end: var(--space-4);
    }

    .colour {
      display: flex;
      align-items: stretch;
      gap: var(--space-2);
    }

    .colour-well {
      inline-size: var(--touch-target-min);
      block-size: auto;
      padding: 0;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-sm);
      background: var(--color-surface-raised);
      cursor: pointer;
    }

    .panel {
      margin-block-end: var(--space-4);
      padding: var(--space-5);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-sm);
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
      margin: 0 0 var(--space-4);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .panel > button {
      margin-block-start: var(--space-3);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StoreSettingsPage implements HasUnsavedChanges {
  private readonly settings = inject(PlatformSettingsService);
  private readonly toasts = inject(ToastService);
  private readonly session = inject(SessionStore);

  protected readonly presets = THEME_PRESETS;
  /** Whether this user may save. Read-only users still see the values; they do not get live controls. */
  protected readonly canManage = computed(() => this.session.hasPermission('platform.settings.manage'));

  /** The sections edited since they were last loaded or saved — what the route guard asks about. */
  private readonly dirty = signal<ReadonlySet<string>>(new Set());

  protected readonly sections = signal<readonly SettingsSectionResponse[]>([]);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly savingKey = signal<string | null>(null);

  /** Field lists, keyed by section. Kept apart from the responses so an edit is not a re-parse. */
  private readonly fields = signal<Readonly<Record<string, readonly SettingField[]>>>({});
  private readonly errors = signal<Readonly<Record<string, string>>>({});

  /** The served schema, indexed by `<section>.<field path>`. Empty until it arrives. */
  private readonly schema = signal<ReadonlyMap<string, SettingsFieldSchema>>(new Map());

  /**
   * What the control says beneath itself.
   *
   * The declared rules in words, because a `min` attribute is enforced by the browser and read by
   * nobody. A field with no declared rule keeps the old behaviour: a note for JSON, nothing
   * otherwise.
   */
  /** The words a choice field accepts, or null when it is not one. */
  protected choicesFor(field: SettingField): readonly string[] | null {
    const choices = field.schema?.choices;
    return choices && choices.length > 0 ? choices : null;
  }

  protected hintFor(field: SettingField): string {
    if (field.kind === 'json') return 'JSON — a list or a nested object';

    const rules = field.schema;
    if (!rules) return '';

    const parts: string[] = [];

    if (rules.minimum !== null && rules.maximum !== null) {
      parts.push(`Between ${rules.minimum} and ${rules.maximum}`);
    } else if (rules.minimum !== null) {
      parts.push(`At least ${rules.minimum}`);
    } else if (rules.maximum !== null) {
      parts.push(`At most ${rules.maximum}`);
    }

    if (rules.maxLength !== null) parts.push(`Up to ${rules.maxLength} characters`);
    if (rules.pattern) parts.push('It has to match a set format');

    return parts.join(' · ');
  }

  hasUnsavedChanges(): boolean {
    return this.dirty().size > 0;
  }

  /** Which preset the branding section's tokens came from, or null for a custom set. */
  protected presetFor(field: SettingField): string | null {
    try {
      return presetIdFromTokens(JSON.parse(field.value || '{}') as Record<string, string>);
    } catch {
      return null;
    }
  }

  /**
   * A theme was picked: the section records *which* theme; the storefront wears it once saved.
   *
   * Only the marker is stored. Writing the whole palette out froze it at the moment of the click —
   * a later improvement to that preset, or a contrast fix, then never reached this store, because
   * the settings document went on serving the copy taken today. `ThemeService` expands a
   * marker-only set from the presets the app actually ships with (see `resolvePreset` there), so a
   * palette change is a deploy rather than a visit to every tenant's settings page. A token set
   * edited by hand still carries its own colours and is still applied verbatim.
   */
  protected chooseTheme(sectionKey: string, path: string, presetId: string): void {
    const preset = THEME_PRESETS.find((candidate) => candidate.id === presetId);
    if (!preset) return;
    this.setValue(sectionKey, path, JSON.stringify({ [THEME_MARKER_TOKEN]: preset.id }, null, 2));
    // Not previewed on the back office itself: the admin keeps its own fixed skin (see
    // `apps/admin/src/styles/_admin-theme.scss`), and the picker's swatches are the preview. The
    // palette is what the *storefront* will wear once this is saved.
  }

  protected readonly current = signal<string | null>(null);

  protected jump(key: string): void {
    this.current.set(key);
    document.getElementById(`settings-${key}`)?.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

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
    this.markDirty(sectionKey);
    this.fields.update((current) => ({
      ...current,
      [sectionKey]: (current[sectionKey] ?? []).map((field) =>
        field.path === path ? { ...field, value } : field,
      ),
    }));
  }

  protected setBoolean(sectionKey: string, path: string, checked: boolean): void {
    this.markDirty(sectionKey);
    this.fields.update((current) => ({
      ...current,
      [sectionKey]: (current[sectionKey] ?? []).map((field) =>
        field.path === path ? { ...field, checked } : field,
      ),
    }));
  }

  private markDirty(sectionKey: string): void {
    this.dirty.update((current) => new Set([...current, sectionKey]));
  }

  private markClean(sectionKey: string): void {
    this.dirty.update((current) => {
      const next = new Set(current);
      next.delete(sectionKey);
      return next;
    });
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
        this.markClean(sectionKey);
        this.toasts.success(`${this.humanise(sectionKey)} saved.`);
        // Refilled from what came back, so a value the API normalised is what is on screen.
        this.fields.update((current) => ({
          ...current,
          [sectionKey]: this.describe(sectionKey, flatten(saved.value, '')),
        }));
        // The storefront reads the saved theme on its next load; the back office keeps its own skin.
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
      case 'theme':
        try {
          return JSON.parse(field.value || '{}') as unknown;
        } catch {
          return MALFORMED;
        }
      default:
        return field.value === '' ? null : field.value;
    }
  }

  private load(): void {
    this.loading.set(true);

    // The schema first, so the fields are built with their rules already in hand. A failure here is
    // not fatal — the form falls back to the shape it can see in the values, which is what it did
    // before Step 28B — so it does not stop the settings loading.
    this.settings.settingsSchema().subscribe({
      next: (response) => {
        this.schema.set(indexSchema(response));
        this.loadSections();
      },
      error: () => {
        this.schema.set(new Map());
        this.loadSections();
      },
    });
  }

  private loadSections(): void {
    this.settings.settings().subscribe({
      next: (response) => {
        this.loading.set(false);
        this.dirty.set(new Set());
        this.sections.set(response.sections);
        this.fields.set(
          Object.fromEntries(
            response.sections.map((section) => [
              section.key,
              this.describe(section.key, flatten(section.value, '')),
            ]),
          ),
        );
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'They could not be loaded.'));
      },
    });
  }

  /** Attaches each leaf's declared rules, where the schema has any for it. */
  private describe(sectionKey: string, fields: readonly SettingField[]): SettingField[] {
    const schema = this.schema();

    // A branding row saved before the theme existed carries no `themeTokens` key, and the API's
    // contract defaults it to empty rather than requiring it - so the picker has to be offered
    // whether or not the stored JSON mentions it, or a store can never choose a theme at all.
    const hasTheme = fields.some((field) => field.kind === 'theme');
    const withTheme =
      sectionKey === 'branding' && !hasTheme
        ? [...fields, { path: 'themeTokens', label: 'Theme', kind: 'theme' as const, value: '{}', checked: false }]
        : fields;

    return withTheme.map((field) => {
      const rules = schema.get(`${sectionKey}.${field.path}`);
      // A text field the server validates as a hex colour gets a colour well beside its text box.
      const isColour = field.kind === 'text' && !!rules?.pattern && /#/.test(rules.pattern);
      return { ...field, kind: isColour ? ('color' as const) : field.kind, schema: rules };
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

    // The theme is one value, not twenty-four text boxes with names like "-color-primary": it is
    // chosen from the presets, and the raw tokens sit behind an "Advanced" disclosure.
    if (key === 'themeTokens' && typeof entry === 'object' && entry !== null && !Array.isArray(entry)) {
      return [
        { path, label: 'Theme', kind: 'theme' as const, value: JSON.stringify(entry, null, 2), checked: false },
      ];
    }

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

/** The served schema as a lookup, keyed `<section>.<field path>`. */
function indexSchema(response: SettingsSchemaResponse): ReadonlyMap<string, SettingsFieldSchema> {
  const index = new Map<string, SettingsFieldSchema>();

  for (const section of response.sections) {
    for (const field of section.fields) {
      index.set(`${section.key}.${field.name}`, field);
    }
  }

  return index;
}
