export * from './lib/address-book.store';
export * from './lib/notification-preferences.store';
export * from './lib/profile.store';
export * from './lib/wallet.service';

/**
 * The shapes these services answer with. Types only — see `data-access-content` for the reasoning.
 */
export type {
  AddressInput,
  AddressResponse,
  AuthenticatedUserResponse,
  CustomerProfileResponse,
  MeResponse,
  PreferenceResponse,
  PreferencesResponse,
  UpdateMeBody,
  UpdatePreferenceRequest,
  WalletResponse,
  WalletTransactionResponse,
} from '@klarahome/data-access-api';
