import { Injectable, inject } from '@angular/core';
import { DOCUMENT } from '@angular/common';
import { RouterStateSnapshot, TitleStrategy } from '@angular/router';

/** What every back-office tab ends with. */
const SUFFIX = 'Klara Home';

/**
 * The browser tab's title.
 *
 * The admin has no `SeoService` and needs none — nothing here is indexed — but it does need
 * titles, for a reason the storefront does not have: **people who use a back office keep eight
 * tabs of it open**, and eight tabs all reading "admin" is eight tabs they have to click through
 * to find the order they were on.
 *
 * The title comes from the route's own `data.title`, which is the label already declared in
 * `navigation.ts`. One declaration, so a screen renamed in the sidebar is renamed in the tab.
 */
@Injectable()
export class AdminTitleStrategy extends TitleStrategy {
  private readonly document = inject(DOCUMENT);

  override updateTitle(snapshot: RouterStateSnapshot): void {
    // `buildTitle` walks to the deepest route carrying a `title`; the declared `data.title` is
    // the fallback, which is what every generated screen uses.
    const declared = this.buildTitle(snapshot) ?? this.dataTitle(snapshot);
    this.document.title = declared ? `${declared} · ${SUFFIX}` : SUFFIX;
  }

  /** The deepest activated route that declares `data.title`. */
  private dataTitle(snapshot: RouterStateSnapshot): string | null {
    let route = snapshot.root;
    let title: string | null = null;

    while (route.firstChild) {
      route = route.firstChild;
      const candidate = route.data['title'];
      if (typeof candidate === 'string' && candidate.length > 0) title = candidate;
    }
    return title;
  }
}
