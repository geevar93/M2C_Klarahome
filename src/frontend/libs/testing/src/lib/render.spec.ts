import { TestBed } from '@angular/core/testing';
import { ApiError, ApiHeaders, PlatformApiClient } from '@klarahome/data-access-api';
import { HttpResponse, http } from 'msw';
import { firstValueFrom } from 'rxjs';

import { apiUrl, problemDetails } from './api-handlers';
import { apiMockServer, useApiMocks } from './msw-server';
import { configureKlaraHomeTestingModule } from './render';

/**
 * A test of the harness, not of a feature.
 *
 * It is the one place the Step 22 wiring is proved end to end: a generated client method, the
 * transport that builds its request, the five interceptors, and MSW standing in for the API. If
 * this passes, every later test that uses `configureKlaraHomeTestingModule` is exercising the real
 * chain rather than a stub of it.
 */
describe('the API testing harness', () => {
  useApiMocks();

  beforeEach(() => configureKlaraHomeTestingModule());

  it('sends a generated request through the interceptor chain and stamps a correlation id', async () => {
    let seenCorrelationId: string | null = null;

    apiMockServer.use(
      http.get(apiUrl('/api/v1/store/pincodes/:pincode'), ({ request, params }) => {
        seenCorrelationId = request.headers.get(ApiHeaders.correlationId);
        return HttpResponse.json({ pincode: params['pincode'], city: 'Hyderabad', state: 'Telangana' });
      }),
    );

    const client = TestBed.inject(PlatformApiClient);
    const response = await firstValueFrom(client.storePincodeGet('500081'));

    expect(response.city).toBe('Hyderabad');
    // Not a fixed value: what matters is that something set one, because the correlation id is
    // what joins a failure the user reports to the line the server logged.
    expect(seenCorrelationId).toMatch(/^[0-9a-f-]{36}$/);
  });

  it('normalises a ProblemDetails failure into an ApiError carrying the server code', async () => {
    apiMockServer.use(
      http.get(apiUrl('/api/v1/store/pincodes/:pincode'), () =>
        problemDetails(422, 'PINCODE_NOT_SERVICEABLE'),
      ),
    );

    const client = TestBed.inject(PlatformApiClient);

    // `silentErrors` so the harness does not queue a toast nobody is going to read.
    const failure = await firstValueFrom(client.storePincodeGet('500081', { silentErrors: true })).catch(
      (error: unknown) => error,
    );

    expect(failure).toBeInstanceOf(ApiError);
    expect((failure as ApiError).code).toBe('PINCODE_NOT_SERVICEABLE');
    expect((failure as ApiError).status).toBe(422);
    expect((failure as ApiError).correlationId).toBeDefined();
  });
});
