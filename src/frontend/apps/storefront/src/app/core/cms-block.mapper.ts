import { Injectable, inject } from '@angular/core';
import {
  CategoryTileResponse,
  ContentImageResponse,
  StoreBlockResponse,
} from '@klarahome/data-access-content';
import { CmsBlockView } from '@klarahome/ui-patterns';
import { ImageSource, ImageUrls } from '@klarahome/util';

import { CatalogMapper } from './catalog.mapper';

/**
 * A published block document, in the shape the renderer draws.
 *
 * The API answers `{ type, config: unknown, images, products, categories }`: the block's own
 * fields are an untyped JSON object an editor filled in (Step 20 keeps the schemas as *data*, so
 * that the admin form and the validator cannot drift), while the things a block *points at* have
 * been resolved server-side into the three typed arrays beside it.
 *
 * So this file does exactly two things: read the config's scalar fields defensively, and match the
 * resolved images back to the file ids the config names. Everything it cannot recognise becomes an
 * `unknown` block, which the renderer draws as nothing — a storefront that has not been redeployed
 * since a new block type shipped renders the rest of the page rather than an error.
 *
 * **Nothing here trusts a type.** `config` is `unknown` on the wire and is read through the
 * accessors below rather than cast, because a cast is a promise about somebody else's JSON.
 */
@Injectable({ providedIn: 'root' })
export class CmsBlockMapper {
  private readonly images = inject(ImageUrls);
  private readonly catalog = inject(CatalogMapper);

  toViews(blocks: readonly StoreBlockResponse[]): CmsBlockView[] {
    return [...blocks]
      .sort((left, right) => left.position - right.position)
      .map((block) => this.toView(block));
  }

  private toView(block: StoreBlockResponse): CmsBlockView {
    const config = asRecord(block.config);
    const id = block.id;

    switch (block.type) {
      case 'Hero':
        return {
          kind: 'hero',
          id,
          headline: text(config['headline']),
          subheadline: optional(config['subheadline']),
          image: this.imageFor(
            block.images,
            text(config['imageFileId']),
            text(config['imageAlt']) || text(config['headline']),
          ),
          ctaLabel: optional(config['ctaLabel']),
          ctaHref: optional(config['ctaHref']),
          align: align(config['align']),
        };

      case 'BannerGrid':
        return {
          kind: 'bannerGrid',
          id,
          heading: optional(config['heading']),
          columns: columns(config['columns'], 2),
          items: items(config).map((item) => ({
            image: this.imageFor(
              block.images,
              text(item['imageFileId']),
              text(item['imageAlt']) || text(item['caption']),
            ),
            caption: optional(item['caption']),
            href: optional(item['href']),
          })),
        };

      case 'ProductCarousel':
        return {
          kind: 'productCarousel',
          id,
          heading: optional(config['heading']),
          viewAllHref: optional(config['viewAllHref']),
          products: block.products.map((product) => this.catalog.productCardToCard(product)),
        };

      case 'CategoryTiles':
        return {
          kind: 'categoryTiles',
          id,
          heading: optional(config['heading']),
          columns: columns(config['columns'], 3),
          // The resolved tiles, not the config's items: the tile carries the category's slug, and
          // a link built from an id would 404. The config's optional label overrides the name.
          items: block.categories.map((tile: CategoryTileResponse, index) => ({
            label: optional(items(config)[index]?.['label']) ?? tile.name,
            href: `/c/${tile.slug}`,
            image: this.images.sourceForImage(tile.image, tile.name),
          })),
        };

      case 'RichText':
        return {
          kind: 'richText',
          id,
          heading: optional(config['heading']),
          html: text(config['body']),
          width: width(config['width']),
        };

      case 'Faq':
        return {
          kind: 'faq',
          id,
          heading: optional(config['heading']),
          items: items(config)
            .map((item) => ({ question: text(item['question']), answer: text(item['answer']) }))
            .filter((item) => item.question.length > 0),
        };

      case 'Testimonial':
        return {
          kind: 'testimonial',
          id,
          heading: optional(config['heading']),
          items: items(config)
            .map((item) => ({
              quote: text(item['quote']),
              author: text(item['author']),
              location: optional(item['location']),
              rating: numeric(item['rating']),
            }))
            .filter((item) => item.quote.length > 0),
        };

      // `CustomHtml` is privileged on the API and is deliberately **not** rendered here. It is raw
      // markup from an editor, and the one place this storefront will not inject unreviewed HTML
      // is the home page every visitor lands on (docs/07-security-compliance.md §4).
      default:
        return { kind: 'unknown', id, type: block.type };
    }
  }

  /**
   * The resolved image a config field names.
   *
   * By file id, not by position: the composer returns the block's media in the order of the ids it
   * collected across the whole block, so a hero's mobile image and its desktop image are two
   * entries whose order is not the order the config lists them in. Falls back to the first image
   * when the id is absent, which is what a block with one image always means.
   */
  private imageFor(images: readonly ContentImageResponse[], fileId: string, alt: string): ImageSource | null {
    const match = images.find((image) => image.fileId === fileId) ?? (fileId ? null : images[0]);
    return this.images.sourceForImage(match ?? null, alt);
  }
}

/** `config` as an object, or an empty one. A block whose config is not an object has none. */
function asRecord(value: unknown): Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
    ? (value as Record<string, unknown>)
    : {};
}

/** The repeated part of a block — `items` on the four types that have one. */
function items(config: Record<string, unknown>): Record<string, unknown>[] {
  const raw = config['items'];
  return Array.isArray(raw) ? raw.map(asRecord) : [];
}

function text(value: unknown): string {
  return typeof value === 'string' ? value : '';
}

/** A string field that is meaningfully absent — blank and missing are the same thing to a reader. */
function optional(value: unknown): string | null {
  const written = text(value).trim();
  return written.length > 0 ? written : null;
}

function numeric(value: unknown): number | null {
  if (typeof value === 'number' && Number.isFinite(value)) return value;
  const parsed = Number.parseFloat(text(value));
  return Number.isFinite(parsed) ? parsed : null;
}

/** The column count, clamped: a `columns` an editor typed by hand must not produce a 40-wide grid. */
function columns(value: unknown, fallback: number): number {
  const parsed = numeric(value);
  return parsed && parsed >= 1 && parsed <= 6 ? Math.floor(parsed) : fallback;
}

function align(value: unknown): 'left' | 'centre' | 'right' {
  const written = text(value);
  return written === 'centre' || written === 'right' ? written : 'left';
}

function width(value: unknown): 'narrow' | 'wide' | 'full' {
  const written = text(value);
  return written === 'wide' || written === 'full' ? written : 'narrow';
}
