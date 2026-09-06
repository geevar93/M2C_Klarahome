import { HttpInterceptorFn } from '@angular/common/http';
import { newUuid } from '@klarahome/util';

import { ApiHeaders } from '../runtime/api-config';

/**
 * Tags every outbound request with a correlation id.
 *
 * The API echoes it on the response and stamps it on every log line it writes while handling the
 * request (docs/04-api-specification.md §1). That is what makes "it failed at 14:32" answerable:
 * the id shown to the user in the error toast is the id in the server's log.
 *
 * A caller that has already set one keeps it, so a multi-step flow — place order, then poll the
 * payment — can be followed end to end under a single id.
 */
export const correlationIdInterceptor: HttpInterceptorFn = (request, next) => {
  if (request.headers.has(ApiHeaders.correlationId)) return next(request);
  return next(request.clone({ setHeaders: { [ApiHeaders.correlationId]: newUuid() } }));
};
