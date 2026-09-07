import { Route } from '@angular/router';
import { anonymousOnlyGuard, authenticatedGuard } from '@klarahome/data-access-auth';

import { adminRoutes } from './core/navigation';

/**
 * The back office's route map.
 *
 * Two halves, and the boundary between them is the shell:
 *
 *  - **Outside it**: `/login` and `/forgot-password`. They render their own full-page layout,
 *    because a navigation sidebar around a sign-in form would be a menu of screens the visitor
 *    cannot open, and the top bar would have no identity to name.
 *  - **Inside it**: everything else, under one layout route guarded by `authenticatedGuard`. An
 *    unauthenticated visitor to any URL — including one that does not exist — is therefore sent to
 *    sign in *with their destination in `returnUrl`*, rather than shown a 404 that would have been
 *    a real page had they been signed in.
 *
 * The screens themselves come from `adminRoutes()`, which builds them from the same declaration
 * the sidebar is built from. That is deliberate and is the point of the step: a route and its menu
 * item cannot describe different permissions, because there is only one place either is written.
 *
 * `loadComponent` on every route, so nothing but the shell is in the initial bundle — the admin's
 * budget is 300 kB gzipped and there are forty screens behind this file.
 */
export const appRoutes: Route[] = [
  {
    path: 'login',
    canActivate: [anonymousOnlyGuard],
    loadComponent: () => import('./pages/auth/login.page').then((m) => m.LoginPage),
    title: 'Sign in',
  },
  {
    path: 'forgot-password',
    canActivate: [anonymousOnlyGuard],
    loadComponent: () => import('./pages/auth/forgot-password.page').then((m) => m.ForgotPasswordPage),
    title: 'Reset your password',
  },

  {
    path: '',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./core/shell.layout').then((m) => m.ShellLayout),
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'dashboard' },

      ...adminRoutes(),

      {
        path: 'profile',
        loadComponent: () => import('./pages/profile.page').then((m) => m.ProfilePage),
        data: { title: 'Your profile and security' },
      },
      {
        path: '403',
        loadComponent: () => import('./pages/errors/forbidden.page').then((m) => m.ForbiddenPage),
        data: { title: 'No access' },
      },

      // Inside the shell on purpose: somebody who has followed a stale link still has the
      // navigation in front of them, which is the fastest way out of a 404.
      {
        path: '**',
        loadComponent: () => import('./pages/errors/not-found.page').then((m) => m.NotFoundPage),
        data: { title: 'Page not found' },
      },
    ],
  },
];
