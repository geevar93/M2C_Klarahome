import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { RouterLink } from '@angular/router';
import {
  ContentAdminService,
  PageSummaryResponse,
  StorefrontTemplateResponse,
} from '@klarahome/data-access-admin';
import { CmsBlockView } from '@klarahome/ui-patterns';
import { PageHeader, StatusBadge } from '@klarahome/ui-admin';
import { Alert, Icon, Skeleton } from '@klarahome/ui-primitives';
import { THEME_PRESETS } from '@klarahome/util';
import { catchError, forkJoin, of, switchMap } from 'rxjs';

import { describeError } from '../../core/describe-error';
import { StorefrontCanvas } from './storefront-canvas';
import { Branding, StorefrontStudio } from './storefront-studio';

/**
 * "Your storefront": where the designer starts.
 *
 * A thumbnail of the home page as it would be served, the template and palette in use, and three
 * doors - the look (template, then palette), the home page itself, and the existing CMS screens
 * under Advanced. Nothing here writes anything.
 *
 * The thumbnail is the draft (`GET /pages/{id}/preview`), not the published version: it is the page
 * the editor opens, and the status badge beside it says whether shoppers are seeing that or an
 * older one.
 */
@Component({
  selector: 'kh-storefront-overview-page',
  imports: [Alert, Icon, PageHeader, RouterLink, Skeleton, StatusBadge, StorefrontCanvas],
  template: `
    <kh-page-header
      heading="Your storefront"
      description="How the shop looks to shoppers: its template, its colours and its home page."
    />

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="Your storefront could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="22rem" />
    } @else {
      <section class="kh-panel summary" aria-labelledby="summary-heading">
        <div class="thumb" [class.empty]="!home()">
          @if (home()) {
            @if (previewError(); as message) {
              <kh-alert tone="warning" heading="The home page could not be drawn">{{ message }}</kh-alert>
            } @else {
              <kh-storefront-canvas
                [frames]="frames()"
                [preset]="branding()?.presetId ?? null"
                [headerStyle]="branding()?.headerStyle ?? 'inline'"
                [footerStyle]="branding()?.footerStyle ?? 'columns'"
                [storeName]="branding()?.storeName ?? 'Klara Home'"
                [interactive]="false"
                [maxHeight]="320"
                title="The home page, as shoppers will see it"
              />
            }
          } @else {
            <p class="none">No home page yet.</p>
          }
        </div>

        <div class="facts">
          <h2 id="summary-heading">In use right now</h2>
          <dl>
            <div>
              <dt>Template</dt>
              <dd>{{ templateName() }}</dd>
            </div>
            <div>
              <dt>Palette</dt>
              <dd class="palette">
                @if (preset(); as current) {
                  <span class="strip" aria-hidden="true">
                    @for (colour of current.swatches; track $index) {
                      <span class="swatch" [style.background]="colour"></span>
                    }
                  </span>
                }
                {{ paletteName() }}
              </dd>
            </div>
            <div>
              <dt>Home page</dt>
              <dd>
                @if (home(); as page) {
                  <kh-status-badge [status]="page.status" />
                  <span class="muted">version {{ page.version }} · {{ page.blockCount }} blocks</span>
                } @else {
                  <span>None yet</span>
                }
              </dd>
            </div>
          </dl>

          @if (!home()) {
            <kh-alert tone="info" heading="There is no home page yet">
              Choosing a template creates one. Use <strong>Change the look</strong> to start.
            </kh-alert>
          }
        </div>
      </section>

      <nav class="doors" aria-label="What would you like to do?">
        <a class="door primary" routerLink="/storefront/templates">
          <span class="icon"><kh-icon name="grid" /></span>
          <span class="title">Change the look</span>
          <span class="hint">Pick a template, then a palette. The home page draft is replaced.</span>
        </a>

        @if (home()) {
          <a class="door" routerLink="/storefront/editor">
            <span class="icon"><kh-icon name="edit" /></span>
            <span class="title">Edit the home page</span>
            <span class="hint">Change the words, pictures and products, block by block.</span>
          </a>
        } @else {
          <span class="door disabled" aria-disabled="true">
            <span class="icon"><kh-icon name="edit" /></span>
            <span class="title">Edit the home page</span>
            <span class="hint">Available once the home page exists. Change the look first.</span>
          </span>
        }

        <a class="door" routerLink="/content/pages">
          <span class="icon"><kh-icon name="settings" /></span>
          <span class="title">Advanced</span>
          <span class="hint">Pages, menus, banners and collections, one screen each.</span>
        </a>
      </nav>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    kh-alert {
      display: block;
      margin-block-end: var(--space-4);
    }

    .summary {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 1024px) {
      .summary {
        grid-template-columns: minmax(0, 3fr) minmax(0, 2fr);
        align-items: start;
      }
    }

    .thumb {
      min-width: 0;
      overflow: hidden;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
      background: var(--color-surface);
    }

    .thumb.empty {
      display: grid;
      place-items: center;
      min-height: 12rem;
    }

    .none {
      margin: 0;
      color: var(--color-text-muted);
    }

    .facts h2 {
      margin: 0 0 var(--space-3);
      font-size: var(--text-lg);
    }

    dl {
      display: grid;
      gap: var(--space-3);
      margin: 0 0 var(--space-4);
    }

    dl > div {
      display: grid;
      gap: var(--space-1);
    }

    dt {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    dd {
      display: flex;
      flex-wrap: wrap;
      align-items: center;
      gap: var(--space-2);
      margin: 0;
      font-weight: var(--weight-medium);
    }

    .muted {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
      font-weight: var(--weight-regular);
    }

    .strip {
      display: inline-flex;
      inline-size: var(--space-12);
      block-size: var(--space-4);
      overflow: hidden;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-sm);
    }

    .swatch {
      flex: 1;
    }

    .doors {
      display: grid;
      gap: var(--space-3);
      margin-block-start: var(--space-4);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 768px) {
      .doors {
        grid-template-columns: repeat(3, minmax(0, 1fr));
      }
    }

    .door {
      display: grid;
      gap: var(--space-1);
      align-content: start;
      min-height: 44px;
      padding: var(--space-4);
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface-raised);
      box-shadow: var(--shadow-card);
      color: var(--color-text);
      text-decoration: none;
    }

    a.door:hover {
      background: var(--color-primary-subtle);
    }

    a.door:focus-visible {
      outline: 2px solid var(--color-focus-ring);
      outline-offset: 2px;
    }

    .door.primary .icon {
      background: var(--color-primary);
      color: var(--color-on-primary);
    }

    .door.disabled {
      opacity: 0.65;
    }

    .icon {
      display: grid;
      place-items: center;
      inline-size: var(--space-10);
      block-size: var(--space-10);
      margin-block-end: var(--space-2);
      border-radius: var(--radius-md);
      background: var(--color-primary-subtle);
      color: var(--color-primary);
    }

    .title {
      font-size: var(--text-lg);
      font-weight: var(--weight-bold);
    }

    .hint {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StorefrontOverviewPage {
  private readonly studio = inject(StorefrontStudio);
  private readonly content = inject(ContentAdminService);

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly previewError = signal<string | null>(null);
  protected readonly branding = signal<Branding | null>(null);
  protected readonly home = signal<PageSummaryResponse | null>(null);
  protected readonly templates = signal<readonly StorefrontTemplateResponse[]>([]);
  private readonly views = signal<readonly CmsBlockView[]>([]);

  protected readonly frames = computed(() =>
    this.views().map((view) => ({ id: view.id, label: view.kind, view })),
  );

  protected readonly preset = computed(() => THEME_PRESETS.find((p) => p.id === this.branding()?.presetId) ?? null);

  protected readonly templateName = computed(() => {
    const id = this.branding()?.templateId ?? '';
    if (!id) return 'None chosen yet';
    return this.templates().find((template) => template.id === id)?.name ?? id;
  });

  protected readonly paletteName = computed(() => {
    const current = this.preset();
    if (current) return current.name;
    const branding = this.branding();
    const custom = branding && Object.keys((branding.section['themeTokens'] as object | undefined) ?? {}).length > 0;
    return custom ? 'Custom colours' : (THEME_PRESETS[0]?.name ?? 'Default');
  });

  constructor() {
    forkJoin({
      branding: this.studio.branding(),
      home: this.content.homePage(),
      // The catalogue only names the template in use; the page is fine without it.
      templates: this.studio.templates().pipe(catchError(() => of([] as StorefrontTemplateResponse[]))),
    })
      .pipe(
        switchMap((loaded) => {
          this.branding.set(loaded.branding);
          this.home.set(loaded.home);
          this.templates.set(loaded.templates);
          this.loading.set(false);
          return loaded.home
            ? this.content.previewPage(loaded.home.id).pipe(
                catchError((error: unknown) => {
                  this.previewError.set(describeError(error, 'It could not be previewed.'));
                  return of(null);
                }),
              )
            : of(null);
        }),
      )
      .subscribe({
        next: (page) => this.views.set(page ? this.studio.views(page) : []),
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadError.set(describeError(error, 'Please try again.'));
        },
      });
  }
}
