import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { RUNTIME_CONFIG } from '@klarahome/util';
import { provideKlaraHomeHttp } from '@klarahome/data-access-auth';
import { provideKlaraHomeI18n } from '@klarahome/i18n';

import { App } from './app';

/**
 * A smoke test for the shell, not for its behaviour.
 *
 * It stands the app up against the real provider chain — runtime config, locale, the HTTP stack
 * and its five interceptors — so that a provider which cannot be constructed fails here rather
 * than as a blank page in a browser. The landmarks are asserted because they are the accessibility
 * contract every later step builds on: a `main` to skip to, and a skip link to reach it with.
 */
describe('App shell', () => {
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

  it('renders the main landmark and a skip link to it', () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();

    const element: HTMLElement = fixture.nativeElement;
    const main = element.querySelector('main');
    const skipLink = element.querySelector('a.kh-skip-link');

    expect(main).not.toBeNull();
    expect(main?.id).toBe('main-content');
    expect(skipLink?.getAttribute('href')).toBe('#main-content');
  });
});
