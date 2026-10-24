import { Injectable, inject } from '@angular/core';
import {
  ContentAdminService,
  PlatformSettingsService,
  StorePageResponse,
  StorefrontTemplateResponse,
} from '@klarahome/data-access-admin';
import { StoreBlockResponse } from '@klarahome/data-access-content';
import { CmsBlockView, FooterStyle, HeaderStyle } from '@klarahome/ui-patterns';
import { THEME_MARKER_TOKEN, THEME_PRESETS, presetIdFromTokens } from '@klarahome/util';
import { Observable, map } from 'rxjs';

import { StorefrontBlockMapper } from './block-view.mapper';

export const HEADER_STYLES: readonly HeaderStyle[] = ['inline', 'centred', 'search', 'wordmark'];
export const FOOTER_STYLES: readonly FooterStyle[] = ['columns', 'slim', 'columnsNewsletter'];

/** The `branding` section as the designer reads it: what is chosen, and the raw value to merge over. */
export interface Branding {
  readonly storeName: string;
  readonly templateId: string;
  /** The preset id in `--kh-theme`, or null for a custom or empty token set (which draws as sand). */
  readonly presetId: string | null;
  readonly headerStyle: HeaderStyle;
  readonly footerStyle: FooterStyle;
  /** The whole section value as stored: PUT back with only the designer's keys changed. */
  readonly section: Readonly<Record<string, unknown>>;
}

export function asHeaderStyle(value: unknown): HeaderStyle {
  return HEADER_STYLES.find((style) => style === value) ?? 'inline';
}

export function asFooterStyle(value: unknown): FooterStyle {
  return FOOTER_STYLES.find((style) => style === value) ?? 'columns';
}

/** Reads the branding section out of a settings value; a missing or odd one reads as the defaults. */
export function readBranding(section: unknown): Branding {
  const value = isRecord(section) ? section : {};
  const tokens = isRecord(value['themeTokens']) ? (value['themeTokens'] as Record<string, string>) : undefined;
  const name = typeof value['storeName'] === 'string' ? value['storeName'].trim() : '';

  return {
    storeName: name || 'Klara Home',
    templateId: typeof value['templateId'] === 'string' ? value['templateId'] : '',
    presetId: presetIdFromTokens(tokens),
    headerStyle: asHeaderStyle(value['headerStyle']),
    footerStyle: asFooterStyle(value['footerStyle']),
    section: value,
  };
}

/**
 * The section to PUT when a look is chosen: the current value with only the designer's four keys
 * replaced. Everything else in `branding` (the store name, tagline, logo, colours) is carried over
 * untouched, because the API validates and stores a section as a whole.
 */
export function brandingWithLook(
  current: Readonly<Record<string, unknown>>,
  look: {
    readonly templateId: string;
    readonly presetId: string;
    readonly headerStyle: string;
    readonly footerStyle: string;
  },
): Record<string, unknown> {
  return {
    ...current,
    themeTokens: { [THEME_MARKER_TOKEN]: look.presetId },
    templateId: look.templateId,
    headerStyle: look.headerStyle,
    footerStyle: look.footerStyle,
  };
}

/** The presets a template offers, in the template's order, skipping any this build does not know. */
export function presetsFor(template: Pick<StorefrontTemplateResponse, 'paletteIds'> | null) {
  if (!template) return [];
  return template.paletteIds
    .map((id) => THEME_PRESETS.find((preset) => preset.id === id))
    .filter((preset): preset is (typeof THEME_PRESETS)[number] => preset !== undefined);
}

/**
 * What the three designer screens share: the template catalogue, the branding section and the
 * mapping from a previewed page to the views the canvas draws.
 */
@Injectable({ providedIn: 'root' })
export class StorefrontStudio {
  private readonly content = inject(ContentAdminService);
  private readonly settings = inject(PlatformSettingsService);
  private readonly mapper = inject(StorefrontBlockMapper);

  templates(): Observable<StorefrontTemplateResponse[]> {
    return this.content.storefrontTemplates();
  }

  /** The current `branding` section. */
  branding(): Observable<Branding> {
    return this.settings
      .settings()
      .pipe(map((response) => readBranding(response.sections.find((section) => section.key === 'branding')?.value)));
  }

  /** Saves the branding section; resolves with what the API stored. */
  saveBranding(section: Record<string, unknown>) {
    return this.settings.updateSection('branding', section);
  }

  /** A previewed page's blocks, in order, as the shared renderer draws them. */
  views(page: StorePageResponse): CmsBlockView[] {
    return this.mapper.toViews(page.blocks);
  }

  /** One rendered block as the shared renderer draws it. */
  view(block: StoreBlockResponse): CmsBlockView {
    return this.mapper.toViews([block])[0];
  }
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}
