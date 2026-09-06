import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { LoadingIndicator, RouteFocusManager } from '@klarahome/util';
import { filter } from 'rxjs';

/**
 * The admin shell.
 *
 * Deliberately almost empty: the sidebar, top bar and permission-driven navigation are Step 26's.
 * What is here is the part that is not a layout decision — the landmarks a screen reader navigates
 * by, the skip link, the progress indicator, and moving focus on every navigation.
 */
@Component({
  selector: 'kh-root',
  imports: [RouterOutlet],
  templateUrl: './app.html',
  styleUrl: './app.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class App {
  private readonly router = inject(Router);
  private readonly focus = inject(RouteFocusManager);

  protected readonly loading = inject(LoadingIndicator).isLoading;

  constructor() {
    // A single-page app that navigates without moving focus leaves a keyboard user reading the
    // old page. `takeUntilDestroyed` is not needed: the shell lives as long as the application.
    this.router.events
      .pipe(filter((event) => event instanceof NavigationEnd))
      .subscribe(() => this.focus.focusMainContent());
  }
}
