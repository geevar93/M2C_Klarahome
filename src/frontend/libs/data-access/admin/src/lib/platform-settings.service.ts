import { Injectable, inject } from '@angular/core';
import {
  FeatureFlagResponse,
  PlatformApiClient,
  RolloutModel,
  SettingsSectionResponse,
  StoreSettingsResponse,
} from '@klarahome/data-access-api';
import { Observable } from 'rxjs';

/**
 * The store's own configuration: its settings sections and its feature flags.
 *
 * **A settings section's value is `unknown`, and that is the contract rather than a gap.** Step 6
 * made settings *typed on the server and opaque over the wire*: each section is a strongly typed
 * options record that the API validates on the way in, and the transport carries it as a JSON
 * object so that adding a field to a section is not a breaking change to the client. The admin
 * screen therefore renders whatever shape came back rather than a form somebody wrote by hand —
 * which is the same choice the CMS composer makes about block schemas, minus the schema endpoint.
 *
 * That has a real consequence worth stating plainly: **the screen can show the shape but not the
 * rules.** It knows `codThreshold` is a number because the value it was sent is a number; it does
 * not know the number must be positive. The API is the validator, and a refusal comes back as a
 * field error the form shows. There is no second copy of the rules here to go stale.
 *
 * **A flag is a kill switch, not a preference.** `updateFlag` sends the whole rollout — the
 * percentage, the named users, the segments — because a partial update of a rollout is how a flag
 * ends up enabled for nobody while reading as on.
 */
@Injectable({ providedIn: 'root' })
export class PlatformSettingsService {
  private readonly api = inject(PlatformApiClient);

  /** Every settings section, public and private, with its current value. */
  settings(): Observable<StoreSettingsResponse> {
    return this.api.adminSettingsGet();
  }

  /**
   * Replaces one section.
   *
   * Whole-section rather than per-field: the API validates a section as a unit — several of them
   * hold rules that relate two fields to each other — and a per-field write would be a validation
   * the server cannot perform.
   */
  updateSection(key: string, value: unknown): Observable<SettingsSectionResponse> {
    return this.api.adminSettingsPut(key, value);
  }

  flags(): Observable<FeatureFlagResponse[]> {
    return this.api.adminFeatureFlagsGet();
  }

  updateFlag(
    key: string,
    enabled: boolean,
    rollout: RolloutModel | null,
    description: string | null = null,
  ): Observable<FeatureFlagResponse> {
    return this.api.adminFeatureFlagsPut(key, { enabled, rollout, description });
  }
}
