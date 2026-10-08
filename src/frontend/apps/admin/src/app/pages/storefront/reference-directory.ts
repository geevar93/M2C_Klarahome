import { Injectable, inject, signal, untracked } from '@angular/core';
import {
  BlockFieldResponse,
  BlockTypeResponse,
  CatalogAdminService,
  CategoryNode,
  ContentAdminService,
  MediaFileResponse,
  MediaLibraryService,
} from '@klarahome/data-access-admin';
import { EntityOption, EntitySearch, ReferenceTag } from '@klarahome/ui-admin';
import { ImageUrls } from '@klarahome/util';
import { Observable, catchError, forkJoin, map, of, shareReplay } from 'rxjs';

import { BlockDraft } from './draft-model';

/** The field kinds that can be browsed for, and what one and several of them are called. */
export const REFERENCE_NOUNS: Readonly<Record<string, readonly [string, string]>> = {
  MediaRef: ['an image', 'images'],
  ProductRef: ['a product', 'products'],
  CategoryRef: ['a category', 'categories'],
  CollectionRef: ['a collection', 'collections'],
};

/** Thumbnails are requested at this width; the API redirects to the nearest rendition. */
const THUMB_WIDTH = 180;

/**
 * Where a browse dialog writes: one value (`single`, optionally inside item `itemIndex`), a list
 * field it appends to (`list`), or the block's repeater, one new item per choice (`items`).
 */
export interface BrowseTarget {
  readonly blockId: string;
  readonly field: BlockFieldResponse;
  readonly mode: 'single' | 'list' | 'items';
  readonly itemIndex?: number;
  readonly maxItems?: number;
}

export function isPickable(field: BlockFieldResponse): boolean {
  return field.kind in REFERENCE_NOUNS;
}

export function nounFor(kind: string, plural: boolean): string {
  const noun = REFERENCE_NOUNS[kind] ?? ['item', 'items'];
  return plural ? noun[1] : noun[0];
}

/** A reference field's value as its ids: a list field's entries, or a single id as a list of one. */
export function idsOf(value: unknown): string[] {
  if (Array.isArray(value)) return value.map((entry) => String(entry)).filter((entry) => entry.length > 0);
  return typeof value === 'string' && value.length > 0 ? [value] : [];
}

function referenceKey(kind: string, id: string): string {
  return `${kind}:${id}`;
}

/**
 * Names for the ids a block points at, and the searches that find new ones.
 *
 * The same job the page composer does inline (`PageComposerPage.resolveReferences`, kept as it is
 * so Advanced is untouched): a block stores a product, category, collection or image as an id, and
 * `kh-reference-tags` shows it by name. Provided by the editor page, so its cache lives as long as
 * the editor does.
 */
@Injectable()
export class ReferenceDirectory {
  private readonly catalog = inject(CatalogAdminService);
  private readonly content = inject(ContentAdminService);
  private readonly media = inject(MediaLibraryService);
  private readonly images = inject(ImageUrls);

  /** Names already learned, keyed by kind and id: from a picker, or looked up for a loaded page. */
  private readonly known = signal<ReadonlyMap<string, ReferenceTag>>(new Map());
  private readonly resolving = new Set<string>();

  tagsFor(kind: string, value: unknown): readonly ReferenceTag[] {
    const known = this.known();
    return idsOf(value).map((id) => known.get(referenceKey(kind, id)) ?? { id, label: id, pending: true });
  }

  remember(kind: string, tags: readonly ReferenceTag[]): void {
    this.known.update((current) => {
      const next = new Map(current);
      for (const tag of tags) next.set(referenceKey(kind, tag.id), tag);
      return next;
    });
  }

  searchFor(kind: string): EntitySearch {
    switch (kind) {
      case 'CategoryRef':
        return this.categorySearch;
      case 'CollectionRef':
        return this.collectionSearch;
      default:
        return this.productSearch;
    }
  }

  mediaOptions(files: readonly MediaFileResponse[]): readonly EntityOption[] {
    return files.map((file) => ({
      id: file.id,
      label: file.fileName,
      imageUrl: this.images.forFile(file.id, THUMB_WIDTH) ?? file.url,
    }));
  }

  /**
   * Looks up every referenced id nobody has a name for yet. Called from an effect, so it reads the
   * known names untracked: learning a name must not re-run the lookup that learned it.
   */
  resolve(drafts: readonly BlockDraft[], schemaFor: (type: string) => BlockTypeResponse | undefined): void {
    const wanted = new Map<string, Set<string>>();
    const known = untracked(() => this.known());

    for (const draft of drafts) {
      const schema = schemaFor(draft.type);
      if (!schema) continue;

      const collect = (
        fields: readonly BlockFieldResponse[] | null | undefined,
        owner: Record<string, unknown>,
      ) => {
        for (const field of fields ?? []) {
          if (!isPickable(field)) continue;
          for (const id of idsOf(owner[field.name])) {
            const key = referenceKey(field.kind, id);
            if (known.has(key) || this.resolving.has(key)) continue;
            this.resolving.add(key);
            const ids = wanted.get(field.kind) ?? new Set<string>();
            ids.add(id);
            wanted.set(field.kind, ids);
          }
        }
      };

      collect(schema.fields, draft.config as Record<string, unknown>);
      const items = draft.config['items'];
      if (Array.isArray(items)) for (const item of items) collect(schema.itemFields, item as Record<string, unknown>);
    }

    for (const [kind, set] of wanted) {
      const ids = [...set];
      const found: Observable<readonly EntityOption[]> =
        kind === 'ProductRef'
          ? this.productSearch(ids.join(' '))
          : kind === 'CategoryRef'
            ? this.categories$.pipe(map((all) => all.filter((entry) => set.has(entry.id))))
            : kind === 'CollectionRef'
              ? forkJoin(
                  ids.map((slug) =>
                    this.collectionSearch(slug).pipe(
                      map((options) => options.filter((option) => option.id === slug)),
                      catchError(() => of([] as EntityOption[])),
                    ),
                  ),
                ).pipe(map((groups) => groups.flat()))
              : forkJoin(
                  ids.map((id) =>
                    this.media.file(id).pipe(
                      map((file) => this.mediaOptions([file])),
                      catchError(() => of([] as EntityOption[])),
                    ),
                  ),
                ).pipe(map((groups) => groups.flat()));

      found.pipe(catchError(() => of(null))).subscribe((options) => {
        for (const id of ids) this.resolving.delete(referenceKey(kind, id));
        // A failed lookup is not "missing": the ids stay pending and are retried on the next change.
        if (options === null) return;

        const byId = new Map(options.map((option) => [option.id, option]));
        this.remember(
          kind,
          ids.map((id) => byId.get(id) ?? { id, label: `Not found (${id.slice(0, 8)}…)`, missing: true }),
        );
      });
    }
  }

  private readonly productSearch: EntitySearch = (term) =>
    this.catalog.searchProducts(term, 25).pipe(
      map((products) =>
        products.map((product) => ({
          id: product.id,
          label: product.name,
          hint: `${product.status} · /${product.slug}`,
          imageUrl: this.images.forFile(product.primaryImageFileId, THUMB_WIDTH),
        })),
      ),
    );

  /** The tree is small and changes rarely, so it is read once and filtered as the editor types. */
  private readonly categories$ = this.catalog.categoryTree().pipe(
    map((roots) => flattenCategories(roots, (fileId) => this.images.forFile(fileId, THUMB_WIDTH))),
    shareReplay({ bufferSize: 1, refCount: false }),
  );

  private readonly categorySearch: EntitySearch = (term) => {
    const needle = term.trim().toLowerCase();
    return this.categories$.pipe(
      map((entries) =>
        entries
          .filter(
            (entry) =>
              !needle ||
              entry.label.toLowerCase().includes(needle) ||
              (entry.hint ?? '').toLowerCase().includes(needle),
          )
          .slice(0, 50),
      ),
    );
  };

  /** A collection block stores the slug, not the id, so the slug is what the picker yields. */
  private readonly collectionSearch: EntitySearch = (term) =>
    this.content.searchCollections(term.trim()).pipe(
      map((collections) =>
        collections.map((collection) => ({
          id: collection.slug,
          label: collection.name,
          hint: `${collection.kind} · ${collection.itemCount} items${collection.isActive ? '' : ' · inactive'}`,
        })),
      ),
    );
}

/** Every category, depth-first, with its ancestry as the hint: "Home › Lighting". */
function flattenCategories(
  nodes: readonly CategoryNode[],
  image: (fileId: string | null) => string | null,
  trail: readonly string[] = [],
): EntityOption[] {
  return nodes.flatMap((node) => [
    {
      id: node.id,
      label: node.name,
      hint: [...trail, node.name].join(' › ') + (node.isActive ? '' : ' · inactive'),
      imageUrl: image(node.imageFileId),
    },
    ...flattenCategories(node.children, image, [...trail, node.name]),
  ]);
}
