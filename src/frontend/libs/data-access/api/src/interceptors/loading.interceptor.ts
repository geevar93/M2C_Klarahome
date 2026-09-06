import { HttpInterceptorFn } from '@angular/common/http';
import { inject } from '@angular/core';
import { LoadingIndicator } from '@klarahome/util';
import { finalize } from 'rxjs';

import { SHOW_LOADING } from '../runtime/http-context';

/**
 * Counts requests in flight for the global progress bar.
 *
 * It is the outermost interceptor on purpose: the counter has to span the retries, or the bar
 * would blink off between attempt one and attempt two and read as a finished request that then
 * started again.
 *
 * `finalize` rather than a `tap`, so a cancelled request — a typeahead the user typed past, a
 * route the user navigated away from — decrements too. A leaked count is a bar that never stops.
 */
export const loadingInterceptor: HttpInterceptorFn = (request, next) => {
  if (!request.context.get(SHOW_LOADING)) return next(request);

  const indicator = inject(LoadingIndicator);
  indicator.begin();
  return next(request).pipe(finalize(() => indicator.end()));
};
