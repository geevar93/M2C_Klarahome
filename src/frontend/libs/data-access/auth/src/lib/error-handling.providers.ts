import {
  ErrorHandler,
  EnvironmentProviders,
  Injectable,
  inject,
  makeEnvironmentProviders,
} from '@angular/core';
import { isApiError } from '@klarahome/data-access-api';
import { ToastService } from '@klarahome/util';

/**
 * The last catch: anything thrown that nobody handled.
 *
 * Angular's default `ErrorHandler` writes to the console and stops, which on a phone means the
 * customer sees a screen that did not change and has no idea whether their tap registered. This
 * one tells them, once, in the same voice as every other failure.
 *
 * **It does not re-report an `ApiError`.** The error-normalisation interceptor has already shown
 * that failure with its own message and its correlation id; toasting again would give the same
 * request two notifications, differently worded. Those are logged and swallowed here.
 */
@Injectable()
export class KlaraHomeErrorHandler implements ErrorHandler {
  private readonly toasts = inject(ToastService);

  handleError(error: unknown): void {
    // Kept: the browser console is where a developer looks first, and a stack trace is worth
    // more than any message we could write.
    console.error(error);

    if (isApiError(error)) return;
    if (isNavigationCancellation(error)) return;

    this.toasts.danger(
      'Something went wrong on this page. Please try again — if it keeps happening, reload the page.',
      'Unexpected error',
    );
  }
}

/**
 * A cancelled navigation is not a fault.
 *
 * A guard redirecting, or a customer tapping a second link before the first route resolved,
 * rejects the in-flight navigation. It is the router working, and telling somebody about it would
 * mean an error toast every time they changed their mind.
 */
function isNavigationCancellation(error: unknown): boolean {
  const message = error instanceof Error ? error.message : String(error ?? '');
  return message.includes('NG04002') || message.includes('Navigation cancel');
}

/** Installs the handler. Both apps use it; the storefront installs it in `appConfig`. */
export function provideKlaraHomeErrorHandling(): EnvironmentProviders {
  return makeEnvironmentProviders([{ provide: ErrorHandler, useClass: KlaraHomeErrorHandler }]);
}
