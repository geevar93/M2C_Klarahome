import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { ActivatedRoute, Router } from '@angular/router';
import { ContentAdminService, StorefrontTemplateResponse } from '@klarahome/data-access-admin';
import { PageHeader, ThemePicker } from '@klarahome/ui-admin';
import { Alert, Button, Skeleton } from '@klarahome/ui-primitives';
import { ToastService } from '@klarahome/util';
import { catchError, of, switchMap } from 'rxjs';

import { describeError } from '../../core/describe-error';
import { CanvasFrame, CanvasViewport, StorefrontCanvas } from './storefront-canvas';
import {
  Branding,
  StorefrontStudio,
  asFooterStyle,
  asHeaderStyle,
  brandingWithLook,
  presetsFor,
} from './storefront-studio';
import { ViewportToggle } from './viewport-toggle';

/**
 * Step 2 of "Change the look": choose a palette for the template.
 *
 * The palettes offered are the template's own three or four, taken from the curated presets in
 * `libs/util/src/lib/theme-presets.ts` - no per-colour tweaking here (the raw token JSON stays
 * under Store settings for staff). Choosing one re-colours the preview at once.
 *
 * *Continue* is the one step that writes. It applies the template (which **replaces the home page
 * draft**; the version that is live stays in the history), then saves the `branding` section with
 * the palette, template and header/footer treatments merged over whatever it held, and opens the
 * editor. The two writes are separate requests, so a failure of the second says so and *Continue*
 * can simply be pressed again.
 */
@Component({
  selector: 'kh-storefront-palette-page',
  imports: [Alert, Button, PageHeader, Skeleton, StorefrontCanvas, ThemePicker, ViewportToggle],
  template: `
    <kh-page-header
      [heading]="template() ? 'Choose a palette for ' + template()!.name : 'Choose a palette'"
      description="Step 2 of 3. The preview changes as you choose. Typography stays the same in every palette."
      [crumbs]="[{ label: 'Choose a template', path: '/storefront/templates' }]"
    >
      <button khButton type="button" variant="primary" [disabled]="!ready() || busy()" (click)="next()">
        {{ busy() ? 'Applying…' : 'Continue to the editor' }}
      </button>
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="This step could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="22rem" />
    } @else if (template(); as current) {
      @if (saveError(); as message) {
        <kh-alert tone="danger" heading="It could not be applied">{{ message }}</kh-alert>
      }

      @if (homeIsLive()) {
        <kh-alert tone="warning" heading="Continuing changes the live home page straight away">
          The home page is published, and a published page has no separate draft: shoppers see the
          new template as soon as you continue. The version they see now is kept in the page's
          version history and can be restored from Advanced.
        </kh-alert>
      } @else {
        <kh-alert tone="warning" heading="Continuing replaces the home page draft">
          The template's blocks take the place of the current draft. Nothing reaches shoppers until
          you publish.
        </kh-alert>
      }

      <div class="layout">
        <section class="kh-panel choices">
          <kh-theme-picker
            [presets]="presets()"
            [selected]="chosen()"
            [disabled]="busy()"
            legend="Palette"
            (chosen)="chosen.set($event)"
          />
        </section>

        <section class="preview" aria-label="Preview">
          <div class="toolbar">
            <kh-viewport-toggle [(value)]="viewport" />
          </div>
          <div class="canvas">
            <kh-storefront-canvas
              [frames]="frames()"
              [preset]="chosen()"
              [headerStyle]="header()"
              [footerStyle]="footer()"
              [storeName]="branding()?.storeName ?? 'Klara Home'"
              [viewport]="viewport()"
              [interactive]="false"
              [maxHeight]="640"
              [title]="current.name + ' in the chosen palette'"
            />
          </div>
        </section>
      </div>
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

    .layout {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 1280px) {
      .layout {
        grid-template-columns: minmax(18rem, 1fr) minmax(0, 2fr);
        align-items: start;
      }
    }

    .toolbar {
      display: flex;
      justify-content: flex-end;
      margin-block-end: var(--space-2);
    }

    .canvas {
      overflow: hidden;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-lg);
      background: var(--color-surface);
      box-shadow: var(--shadow-card);
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StorefrontPalettePage {
  private readonly studio = inject(StorefrontStudio);
  private readonly content = inject(ContentAdminService);
  private readonly route = inject(ActivatedRoute);
  private readonly router = inject(Router);
  private readonly toasts = inject(ToastService);

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly saveError = signal<string | null>(null);
  protected readonly busy = signal(false);

  protected readonly template = signal<StorefrontTemplateResponse | null>(null);
  protected readonly branding = signal<Branding | null>(null);
  protected readonly frames = signal<readonly CanvasFrame[]>([]);
  protected readonly chosen = signal<string | null>(null);
  /**
   * Whether the home page is published. A published page serves its blocks directly (the composer's
   * "Save & update live site"), so applying a template to it is a live change, and the warning
   * above has to say so rather than promise a draft that does not exist.
   */
  protected readonly homeIsLive = signal(false);
  protected readonly viewport = signal<CanvasViewport>('desktop');

  protected readonly presets = computed(() => presetsFor(this.template()));
  protected readonly header = computed(() => asHeaderStyle(this.template()?.headerStyle));
  protected readonly footer = computed(() => asFooterStyle(this.template()?.footerStyle));
  protected readonly ready = computed(() => this.template() !== null && this.chosen() !== null);

  constructor() {
    const wanted = this.route.snapshot.queryParamMap.get('template');
    if (!wanted) {
      void this.router.navigate(['/storefront/templates']);
      return;
    }

    this.content
      .homePage()
      .pipe(catchError(() => of(null)))
      .subscribe((home) => this.homeIsLive.set(home?.status === 'Published'));

    this.studio
      .templates()
      .pipe(
        switchMap((templates) => {
          const found = templates.find((candidate) => candidate.id === wanted);
          if (!found) throw new Error(`There is no template called "${wanted}".`);
          this.template.set(found);
          return this.studio.branding().pipe(
            catchError(() => of(null)),
            switchMap((branding) => {
              this.branding.set(branding);
              // Keep the palette already in use when this template offers it; else its first.
              const offered = found.paletteIds;
              const current = branding?.presetId ?? null;
              this.chosen.set(current && offered.includes(current) ? current : (offered[0] ?? null));
              return this.content.previewStorefrontTemplate(found.id);
            }),
          );
        }),
      )
      .subscribe({
        next: (page) => {
          this.frames.set(this.studio.views(page).map((view) => ({ id: view.id, label: view.kind, view })));
          this.loading.set(false);
        },
        error: (error: unknown) => {
          this.loading.set(false);
          this.loadError.set(
            error instanceof Error && !('status' in error)
              ? error.message
              : describeError(error, 'Please try again.'),
          );
        },
      });
  }

  protected next(): void {
    const template = this.template();
    const preset = this.chosen();
    if (!template || !preset || this.busy()) return;

    this.busy.set(true);
    this.saveError.set(null);

    this.content
      .applyStorefrontTemplate(template.id)
      .pipe(
        // Read the section after the apply, so a change made in Store settings meanwhile is kept.
        switchMap(() => this.studio.branding()),
        switchMap((current) =>
          this.studio.saveBranding(
            brandingWithLook(current.section, {
              templateId: template.id,
              presetId: preset,
              headerStyle: template.headerStyle,
              footerStyle: template.footerStyle,
            }),
          ),
        ),
      )
      .subscribe({
        next: () => {
          this.busy.set(false);
          this.toasts.success(`${template.name} applied. Now make it yours.`);
          void this.router.navigate(['/storefront/editor']);
        },
        error: (error: unknown) => {
          this.busy.set(false);
          this.saveError.set(
            describeError(error, 'Nothing more was saved. Press Continue to try again.'),
          );
        },
      });
  }
}
