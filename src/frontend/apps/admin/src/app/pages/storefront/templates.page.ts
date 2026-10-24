import { ChangeDetectionStrategy, Component, computed, inject, signal } from '@angular/core';
import { Router } from '@angular/router';
import { ContentAdminService, StorefrontTemplateResponse } from '@klarahome/data-access-admin';
import { PageHeader } from '@klarahome/ui-admin';
import { Alert, Button, Skeleton } from '@klarahome/ui-primitives';
import { catchError, of } from 'rxjs';

import { describeError } from '../../core/describe-error';
import { CanvasFrame, StorefrontCanvas } from './storefront-canvas';
import { StorefrontStudio, asFooterStyle, asHeaderStyle } from './storefront-studio';

/** The outcome of drawing one template: its blocks, or why it could not be. */
type Render = { readonly frames: readonly CanvasFrame[] } | { readonly error: string };

/**
 * Step 1 of "Change the look": choose a template.
 *
 * One row per template - its name, who it suits and what it is on the left, a desktop render in
 * the middle and a phone render on the right. Each render is `GET storefront-templates/{id}/preview`
 * (the template filled with this shop's own categories and products, not persisted), drawn by the
 * shared block renderer under the template's own header and footer, in the first of its palettes.
 *
 * Choosing writes nothing. *Continue* carries the choice to the palette step in the URL, so a
 * reload of that step still knows which template it is for.
 */
@Component({
  selector: 'kh-storefront-templates-page',
  imports: [Alert, Button, PageHeader, Skeleton, StorefrontCanvas],
  template: `
    <kh-page-header
      heading="Choose a template"
      description="Step 1 of 3. A template sets the home page layout, the header and the footer. Your products and categories fill it in."
      [crumbs]="[{ label: 'Your storefront', path: '/storefront' }]"
    >
      <button khButton type="button" variant="primary" [disabled]="!selected()" (click)="next()">
        Continue to palette
      </button>
    </kh-page-header>

    @if (loadError(); as message) {
      <kh-alert tone="danger" heading="The templates could not be loaded">{{ message }}</kh-alert>
    } @else if (loading()) {
      <kh-skeleton height="18rem" />
    } @else {
      <ul class="rows" aria-label="Templates">
        @for (template of templates(); track template.id) {
          <li class="kh-panel row" [class.chosen]="template.id === selected()">
            <div class="text">
              <h2>{{ template.name }}</h2>
              <p class="suits">{{ template.suits }}</p>
              <p>{{ template.description }}</p>
              <p class="meta">
                {{ headerName(template.headerStyle) }} header · {{ footerName(template.footerStyle) }} footer
              </p>
              <button
                khButton
                type="button"
                [variant]="template.id === selected() ? 'primary' : 'secondary'"
                [attr.aria-pressed]="template.id === selected()"
                (click)="selected.set(template.id)"
              >
                {{ template.id === selected() ? 'Selected' : 'Select ' + template.name }}
              </button>
            </div>

            @if (renders()[template.id]; as render) {
              @if ('error' in render) {
                <kh-alert tone="warning" heading="No preview">{{ render.error }}</kh-alert>
              } @else {
                <figure class="desktop">
                  <kh-storefront-canvas
                    [frames]="render.frames"
                    [preset]="firstPalette(template)"
                    [headerStyle]="header(template)"
                    [footerStyle]="footer(template)"
                    [interactive]="false"
                    [maxHeight]="360"
                    [title]="template.name + ' on a desktop'"
                  />
                  <figcaption>Desktop</figcaption>
                </figure>
                <figure class="phone">
                  <kh-storefront-canvas
                    viewport="phone"
                    [frames]="render.frames"
                    [preset]="firstPalette(template)"
                    [headerStyle]="header(template)"
                    [footerStyle]="footer(template)"
                    [interactive]="false"
                    [maxHeight]="360"
                    [title]="template.name + ' on a phone'"
                  />
                  <figcaption>Phone</figcaption>
                </figure>
              }
            } @else {
              <kh-skeleton height="12rem" />
              <kh-skeleton height="12rem" />
            }
          </li>
        }
      </ul>
    }
  `,
  styles: `
    :host {
      display: block;
    }

    kh-alert {
      display: block;
    }

    .rows {
      display: grid;
      gap: var(--space-4);
      margin: 0;
      padding: 0;
      list-style: none;
    }

    .row {
      display: grid;
      gap: var(--space-4);
      grid-template-columns: minmax(0, 1fr);
    }

    @media (min-width: 1280px) {
      .row {
        grid-template-columns: minmax(0, 2fr) minmax(0, 5fr) minmax(0, 2fr);
        align-items: start;
      }
    }

    .row.chosen {
      border-color: var(--color-primary);
      box-shadow:
        var(--shadow-card),
        0 0 0 2px var(--color-primary);
    }

    .text {
      display: grid;
      gap: var(--space-2);
      align-content: start;
    }

    .text h2,
    .text p {
      margin: 0;
    }

    .suits {
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    .meta {
      color: var(--color-text-muted);
      font-size: var(--text-xs);
    }

    .text button {
      justify-self: start;
      margin-block-start: var(--space-2);
    }

    figure {
      margin: 0;
      min-width: 0;
    }

    figure kh-storefront-canvas {
      overflow: hidden;
      border: 1px solid var(--color-border);
      border-radius: var(--radius-md);
    }

    figcaption {
      margin-block-start: var(--space-1);
      color: var(--color-text-muted);
      font-size: var(--text-xs);
      text-align: center;
    }
  `,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class StorefrontTemplatesPage {
  private readonly studio = inject(StorefrontStudio);
  private readonly content = inject(ContentAdminService);
  private readonly router = inject(Router);

  protected readonly loading = signal(true);
  protected readonly loadError = signal<string | null>(null);
  protected readonly templates = signal<readonly StorefrontTemplateResponse[]>([]);
  protected readonly renders = signal<Readonly<Record<string, Render>>>({});
  protected readonly selected = signal<string | null>(null);

  private readonly headerNames: Readonly<Record<string, string>> = {
    inline: 'Classic',
    centred: 'Centred',
    search: 'Search-led',
    wordmark: 'Wordmark',
  };
  private readonly footerNames: Readonly<Record<string, string>> = {
    columns: 'Columns',
    slim: 'Slim',
    columnsNewsletter: 'Columns with newsletter',
  };

  protected readonly canContinue = computed(() => this.selected() !== null);

  constructor() {
    // The template in use starts selected, so a merchant adjusting the palette finds their place.
    this.studio.branding().pipe(catchError(() => of(null))).subscribe((branding) => {
      if (branding?.templateId && this.selected() === null) this.selected.set(branding.templateId);
    });

    this.studio.templates().subscribe({
      next: (templates) => {
        this.templates.set(templates);
        this.loading.set(false);
        for (const template of templates) this.render(template.id);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.loadError.set(describeError(error, 'Please try again.'));
      },
    });
  }

  protected headerName(style: string): string {
    return this.headerNames[style] ?? style;
  }

  protected footerName(style: string): string {
    return this.footerNames[style] ?? style;
  }

  protected header(template: StorefrontTemplateResponse) {
    return asHeaderStyle(template.headerStyle);
  }

  protected footer(template: StorefrontTemplateResponse) {
    return asFooterStyle(template.footerStyle);
  }

  protected firstPalette(template: StorefrontTemplateResponse): string | null {
    return template.paletteIds[0] ?? null;
  }

  protected next(): void {
    const id = this.selected();
    if (id) void this.router.navigate(['/storefront/palette'], { queryParams: { template: id } });
  }

  private render(id: string): void {
    this.content.previewStorefrontTemplate(id).subscribe({
      next: (page) => this.renders.update((current) => ({ ...current, [id]: { frames: this.studio.views(page).map((view) => ({ id: view.id, label: view.kind, view })) } })),
      error: (error: unknown) =>
        this.renders.update((current) => ({
          ...current,
          [id]: { error: describeError(error, 'This template could not be previewed.') },
        })),
    });
  }
}
