import { Component } from '@angular/core';
import { TestBed } from '@angular/core/testing';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { provideKlaraHomeHttp } from '@klarahome/data-access-auth';
import { provideKlaraHomeI18n } from '@klarahome/i18n';
import { RUNTIME_CONFIG } from '@klarahome/util';

import { MediaPicker } from './media-picker';

/**
 * `visibility="Private"` on `<kh-media-picker>` is a plain attribute, not a `[visibility]`
 * binding — exactly how `kyc.panel.ts` sets it. This host stands the picker up the same way, so
 * a regression that only shows up for a static attribute (and not a bound one) is caught here.
 */
@Component({
  selector: 'kh-test-host',
  imports: [MediaPicker],
  template: `<kh-media-picker [open]="true" visibility="Private" />`,
})
class TestHost {}

describe('MediaPicker', () => {
  const config = {
    apiBaseUrl: 'http://api.klarahome.test',
    tenantCode: 'test',
    locale: 'en-IN',
    timeZone: 'Asia/Kolkata',
    environment: 'local' as const,
    features: {},
  };

  let httpMock: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [TestHost],
      providers: [
        { provide: RUNTIME_CONFIG, useValue: config },
        provideKlaraHomeI18n(config.locale),
        provideKlaraHomeHttp(config),
        provideHttpClientTesting(),
      ],
    }).compileComponents();

    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => httpMock.verify());

  it('browses the bucket its static `visibility` attribute names, not the default', () => {
    const fixture = TestBed.createComponent(TestHost);
    fixture.detectChanges();

    const req = httpMock.expectOne(
      (request) => request.url === 'http://api.klarahome.test/api/v1/admin/media',
    );
    expect(req.request.params.get('visibility')).toBe('Private');
    req.flush({ items: [], page: { size: 24, nextCursor: null, prevCursor: null, total: null } });
  });
});
