import { inject } from '@angular/core';
import { Route } from '@angular/router';
import { anonymousOnlyGuard, authenticatedGuard } from '@klarahome/data-access-auth';
import { RUNTIME_CONFIG, featureFlagGuard } from '@klarahome/util';

import {
  categoryResolver,
  cmsPageResolver,
  collectionResolver,
  homePageResolver,
  productResolver,
  vendorResolver,
} from './core/resolvers';
import { PlaceholderDetail } from './pages/placeholder.page';

/**
 * The storefront's route map — every URL in `docs/05-frontend-architecture.md` §3.2.
 *
 * Three conventions run through it, and they are what make the shell work:
 *
 *  - **`loadComponent` on every feature route.** Nothing but the shell is in the initial bundle,
 *    which is the whole reason the budget in §3.4 is reachable on a mid-tier Android.
 *  - **`data.seo`** carries the page's title and description. The shell applies it on `ResolveEnd`,
 *    *before* the page is constructed, so a page that knows better — a product, a CMS page —
 *    overrides it with real data and wins.
 *  - **`data.breadcrumb`** is the trail. `BreadcrumbTrail` derives the whole path from it, so no
 *    page assembles its own and none of them can disagree with the router.
 *
 * The discovery pages arrived at Step 24 and the buying and account pages at Step 25. What is left
 * on the honest placeholder is the blog, which belongs to a later step and ships behind a flag that
 * is off.
 *
 * **`/checkout` and `/account` are guarded, and the guard is not a security boundary.** The API
 * refuses every one of their endpoints to an anonymous caller — `/store/checkout` requires
 * authorization for the whole group — so the guard exists to send a shopper to sign in and back
 * again with their `returnUrl` and their basket, rather than to a screen full of 401s.
 */

const placeholder = (heading: string, step: string): { placeholder: PlaceholderDetail } => ({
  placeholder: { heading, step },
});

const loadPlaceholder = () => import('./pages/placeholder.page').then((m) => m.PlaceholderPage);

export const appRoutes: Route[] = [
  // ---- Browse and discover (indexable, server-rendered) ----------------------------------------
  {
    path: '',
    pathMatch: 'full',
    loadComponent: () => import('./pages/home.page').then((m) => m.HomePage),
    resolve: { page: homePageResolver },
    data: { seo: { title: 'Home', canonicalPath: '/' } },
  },
  {
    path: 'c/:categorySlug',
    loadComponent: () => import('./pages/category.page').then((m) => m.CategoryPage),
    resolve: { category: categoryResolver },
    // The label is replaced with the category's real name once it resolves, and the taxonomy above
    // it is inserted by the page — see `BreadcrumbTrail.setAncestors`.
    data: { breadcrumb: 'Category', seo: { title: 'Category' } },
  },
  {
    path: 'collections/:slug',
    loadComponent: () => import('./pages/collection.page').then((m) => m.CollectionPage),
    resolve: { collection: collectionResolver },
    data: { breadcrumb: 'Collection', seo: { title: 'Collection' } },
  },
  {
    path: 'search',
    loadComponent: () => import('./pages/search.page').then((m) => m.SearchPage),
    data: {
      breadcrumb: 'Search',
      // A search result page is crawlable but must never be indexed: every query a bot invents
      // becomes a URL, and a store's index fills with pages nobody wrote (§3.5, index bloat).
      seo: { title: 'Search', noIndex: true },
    },
  },
  {
    path: 'p/:productSlug',
    loadComponent: () => import('./pages/product.page').then((m) => m.ProductPage),
    resolve: { product: productResolver },
    data: { breadcrumb: 'Product', seo: { title: 'Product', ogType: 'product' } },
  },
  {
    path: 'vendor/:slug',
    loadComponent: () => import('./pages/vendor.page').then((m) => m.VendorPage),
    resolve: { vendor: vendorResolver },
    data: { breadcrumb: 'Seller', seo: { title: 'Seller' } },
  },

  // ---- CMS ------------------------------------------------------------------------------------
  {
    path: 'pages/:slug',
    loadComponent: () => import('./pages/cms-page.page').then((m) => m.CmsPage),
    resolve: { page: cmsPageResolver },
    // The label is replaced with the page's real title once it resolves; this is what the trail
    // says while it is in flight.
    data: { breadcrumb: 'Page' },
  },

  // The blog ships behind `content.blog`, which is off by default. `canMatch` rather than
  // `canActivate` so the chunk is never even requested while the flag is off.
  {
    path: 'blog',
    canMatch: [featureFlagGuard('content.blog')],
    loadComponent: loadPlaceholder,
    data: { ...placeholder('Blog', 'a later step'), breadcrumb: 'Blog', seo: { title: 'Blog' } },
  },
  {
    path: 'blog/:slug',
    canMatch: [featureFlagGuard('content.blog')],
    loadComponent: loadPlaceholder,
    data: { ...placeholder('Article', 'a later step'), breadcrumb: 'Article', seo: { ogType: 'article' } },
  },

  // ---- Buying (personal; client-rendered) -----------------------------------------------------
  {
    path: 'cart',
    loadComponent: () => import('./pages/cart.page').then((m) => m.CartPage),
    data: {
      breadcrumb: 'Cart',
      seo: { title: 'Your cart', noIndex: true },
    },
  },
  {
    path: 'checkout',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./pages/checkout/checkout.page').then((m) => m.CheckoutPage),
    data: {
      breadcrumb: 'Checkout',
      seo: { title: 'Checkout', noIndex: true },
    },
  },
  {
    path: 'checkout/confirmation/:orderNumber',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./pages/checkout/confirmation.page').then((m) => m.OrderConfirmationPage),
    data: {
      seo: { title: 'Order confirmed', noIndex: true },
    },
  },

  // ---- The customer's own pages ---------------------------------------------------------------
  {
    path: 'account',
    canActivate: [authenticatedGuard],
    loadComponent: () => import('./pages/account/account.layout').then((m) => m.AccountLayout),
    data: { breadcrumb: 'Your account', seo: { title: 'Your account', noIndex: true } },
    children: [
      {
        path: '',
        pathMatch: 'full',
        loadComponent: () => import('./pages/account/dashboard.page').then((m) => m.AccountDashboardPage),
      },
      {
        path: 'orders',
        loadComponent: () => import('./pages/account/orders.page').then((m) => m.AccountOrdersPage),
        data: { breadcrumb: 'Orders', seo: { title: 'Your orders' } },
      },
      {
        path: 'orders/:orderNumber',
        loadComponent: () => import('./pages/account/order-detail.page').then((m) => m.OrderDetailPage),
        // Replaced with the order number once it loads — see `BreadcrumbTrail.setLeafLabel`.
        data: { breadcrumb: 'Order', seo: { title: 'Your order' } },
      },
      {
        path: 'returns',
        loadComponent: () => import('./pages/account/returns.page').then((m) => m.AccountReturnsPage),
        data: { breadcrumb: 'Returns', seo: { title: 'Your returns' } },
      },
      {
        path: 'returns/:rmaNumber',
        loadComponent: () => import('./pages/account/return-detail.page').then((m) => m.ReturnDetailPage),
        data: { breadcrumb: 'Return', seo: { title: 'Your return' } },
      },
      {
        path: 'addresses',
        loadComponent: () => import('./pages/account/addresses.page').then((m) => m.AccountAddressesPage),
        data: { breadcrumb: 'Addresses', seo: { title: 'Your addresses' } },
      },
      {
        path: 'profile',
        loadComponent: () => import('./pages/account/profile.page').then((m) => m.AccountProfilePage),
        data: { breadcrumb: 'Profile', seo: { title: 'Your profile' } },
      },
      {
        path: 'wishlist',
        loadComponent: () => import('./pages/account/wishlist.page').then((m) => m.AccountWishlistPage),
        data: { breadcrumb: 'Wishlist', seo: { title: 'Your wishlist' } },
      },
      {
        // Store credit is a flagged capability (Step 12). `canMatch`, so a deployment without it has
        // no such route and never downloads the chunk — the account menu hides the link to match.
        path: 'wallet',
        canMatch: [featureFlagGuard('pricing.wallet')],
        loadComponent: () => import('./pages/account/wallet.page').then((m) => m.AccountWalletPage),
        data: { breadcrumb: 'Store credit', seo: { title: 'Store credit' } },
      },
      {
        path: 'notifications',
        loadComponent: () =>
          import('./pages/account/notifications.page').then((m) => m.AccountNotificationsPage),
        data: { breadcrumb: 'Notifications', seo: { title: 'Notification preferences' } },
      },
    ],
  },

  // ---- Getting in -----------------------------------------------------------------------------
  {
    path: 'auth',
    canActivate: [anonymousOnlyGuard],
    data: { seo: { noIndex: true } },
    children: [
      { path: '', pathMatch: 'full', redirectTo: 'login' },
      {
        path: 'login',
        loadComponent: () => import('./pages/auth/login.page').then((m) => m.LoginPage),
        data: { seo: { title: 'Sign in' } },
      },
      {
        path: 'register',
        loadComponent: () => import('./pages/auth/register.page').then((m) => m.RegisterPage),
        data: { seo: { title: 'Create an account' } },
      },
      {
        path: 'otp',
        loadComponent: () => import('./pages/auth/otp.page').then((m) => m.OtpPage),
        data: { seo: { title: 'Enter your code' } },
      },
      {
        // One route for both halves of a reset: the request, and the emailed link coming back with
        // its `token`. See the page for why they are not two.
        path: 'forgot-password',
        loadComponent: () => import('./pages/auth/forgot-password.page').then((m) => m.ForgotPasswordPage),
        data: { seo: { title: 'Reset your password' } },
      },
    ],
  },

  // ---- Test fixtures, not shopper-facing --------------------------------------------------------
  // `noIndex: true` (in the component itself), same reasoning as the states below: not linked from
  // anywhere, not meant to be found, but a real route so `CmsBlockRenderer` can be stress-tested
  // by a real browser at every breakpoint — see the component's own doc comment for why this
  // exists instead of an intercepted API response or a page authored through the admin CMS.
  //
  // `canMatch` on the environment as well as `noIndex`: a page of deliberately hostile content has
  // no business being reachable on a deployed storefront at all. Outside `local`/`development` the
  // route never matches, its chunk is never requested, and the URL falls through to `**` like any
  // other unknown path. It is left out of `app.routes.server.ts` on purpose, so even where it does
  // render the server answers it under the catch-all's 404 status rather than a 200.
  {
    path: '__test/cms-blocks',
    canMatch: [() => ['local', 'development'].includes(inject(RUNTIME_CONFIG).environment)],
    loadComponent: () => import('./pages/dev/cms-stress-test.page').then((m) => m.CmsStressTestPage),
  },

  // ---- The states every application needs -----------------------------------------------------
  {
    path: '403',
    loadComponent: () => import('./pages/errors/forbidden.page').then((m) => m.ForbiddenPage),
  },
  {
    path: '404',
    loadComponent: () => import('./pages/errors/not-found.page').then((m) => m.NotFoundPage),
  },
  {
    path: '500',
    loadComponent: () => import('./pages/errors/server-error.page').then((m) => m.ServerErrorPage),
  },
  {
    path: 'offline',
    loadComponent: () => import('./pages/errors/offline.page').then((m) => m.OfflinePage),
  },

  // Anything else is a 404 — and it is answered with a real 404 status, because `**` is declared
  // that way in `app.routes.server.ts`.
  {
    path: '**',
    loadComponent: () => import('./pages/errors/not-found.page').then((m) => m.NotFoundPage),
  },
];
