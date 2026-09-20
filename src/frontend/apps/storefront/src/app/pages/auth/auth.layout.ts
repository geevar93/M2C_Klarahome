import { ChangeDetectionStrategy, Component, ViewEncapsulation, input } from '@angular/core';

/**
 * The one way an auth page starts.
 *
 * Login, register, OTP and forgot-password each used to carry their own copy of the same panel —
 * a centred column, an `<h1>` at one size or another, a muted lead paragraph, and a form laid out
 * as a grid of `var(--space-4)` gaps. Four copies of one idea drift: this page's `<h1>` was
 * `var(--text-display-sm)`, that one was `var(--text-2xl)`, and nothing said which was right. This
 * component is the one copy, at `var(--text-2xl)` — the size every sign-in screen agreed on once
 * the drift was pointed out.
 *
 * `ViewEncapsulation.None` is deliberate and the one exception in this application: `form` is
 * written by whichever page projects it into `<ng-content>`, so a style scoped to this component's
 * own template — the ordinary, safer default — would never reach it. Selectors here are all
 * qualified with the `kh-auth-layout` tag, so nothing here reaches a form anywhere else in the app.
 */
@Component({
  selector: 'kh-auth-layout',
  template: `
    <div class="panel">
      <h1>{{ title() }}</h1>
      @if (lead()) {
        <p class="lead">{{ lead() }}</p>
      }
      <ng-content />
    </div>
  `,
  styles: `
    kh-auth-layout {
      display: block;
      padding-block: var(--space-8) var(--space-10);
    }

    kh-auth-layout .panel {
      max-inline-size: 26rem;
      margin-inline: auto;
    }

    kh-auth-layout h1 {
      margin: 0;
      font-size: var(--text-2xl);
    }

    kh-auth-layout .lead {
      margin: var(--space-2) 0 var(--space-6);
      color: var(--color-text-muted);
      font-size: var(--text-sm);
    }

    kh-auth-layout form {
      display: grid;
      gap: var(--space-4);
    }
  `,
  encapsulation: ViewEncapsulation.None,
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AuthLayout {
  readonly title = input.required<string>();
  readonly lead = input<string | null>(null);
}
