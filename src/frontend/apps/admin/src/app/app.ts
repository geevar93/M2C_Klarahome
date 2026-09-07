import { ChangeDetectionStrategy, Component, inject } from '@angular/core';
import { NavigationEnd, Router, RouterOutlet } from '@angular/router';
import { LoadingIndicator, RouteFocusManager } from '@klarahome/util';
import { filter } from 'rxjs';

/**
 * The application component.
 *
 * Deliberately thin, and it stayed thin at Step 26. The back office's chrome — sidebar, top bar,
 * global search, toasts — belongs to `ShellLayout`, a *routed* layout, because `/login` must sit
 * outside all of it. What is left here is what is true of every route including the sign-in
 * screen: the progress indicator, and moving focus when the route changes.
 *
 * The skip link and the `main` landmark moved into `kh-admin-shell` for the same reason: the
 * sign-in page is a single form and has its own `main`, while every other screen has a navigation
 * landmark to skip past.
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
