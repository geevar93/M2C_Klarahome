import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RUNTIME_CONFIG } from '@klarahome/util';
import { provideKlaraHomeHttp } from '@klarahome/data-access-auth';
import { provideKlaraHomeI18n } from '@klarahome/i18n';

import { App } from './app';

/**
 * A smoke test for the application component, not for its behaviour.
 *
 * It stands the app up against the real provider chain — runtime config, locale, the HTTP stack
 * and its five interceptors — so that a provider which cannot be constructed fails here rather
 * than as a blank page in a browser.
 *
 * What it asserts changed at Step 26 and the reason is worth recording: the landmarks moved into
 * `kh-admin-shell`, which is rendered by the routed `ShellLayout`, because `/login` must not have
 * a navigation sidebar around it. The application component is now the outlet and the progress
 * indicator, and that is what is checked here.
 */
describe('Admin app component', () => {
  const config = {
    apiBaseUrl: 'http://api.klarahome.test',
    tenantCode: 'test',
    locale: 'en-IN',
    timeZone: 'Asia/Kolkata',
    environment: 'local' as const,
    features: {},
  };

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        { provide: RUNTIME_CONFIG, useValue: config },
        provideKlaraHomeI18n(config.locale),
        provideKlaraHomeHttp(config),
        provideRouter([]),
      ],
    }).compileComponents();
  });

  it('constructs against the real provider chain and renders the routed outlet', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    expect(element.querySelector('router-outlet')).not.toBeNull();
  });
});
