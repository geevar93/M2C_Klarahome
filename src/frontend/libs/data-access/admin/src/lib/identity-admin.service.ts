import { Injectable, inject } from '@angular/core';
import {
  AdminUserResponse,
  CreateRoleBody,
  CreateUserBody,
  IdentityApiClient,
  PermissionGroupResponse,
  RoleResponse,
  UpdateRoleBody,
  UserStatus,
  UserType,
} from '@klarahome/data-access-api';
import { Observable, map } from 'rxjs';

import { CursorList, CursorPage } from './cursor-list';

export interface UserFilters {
  readonly search?: string;
  readonly userType?: UserType;
}

/**
 * Who may sign in, and what they may do once they have.
 *
 * **Permissions are not granted to people.** A user holds roles; a role holds permissions; the
 * permission catalogue itself is the platform's, and neither this service nor any screen can
 * invent a code. `permissions()` returns that catalogue grouped for display, which is what makes a
 * role editor a set of checkboxes over a known list rather than a free-text field somebody can
 * misspell into a role that grants nothing.
 *
 * **A role carries a scope, and the scope is not a permission.** A `Vendor`-scoped role means the
 * same permission codes evaluated against the holder's own seller; a `Platform` one means the
 * whole store. That distinction is the reason the admin's navigation separates `platformOnly`
 * from a permission check, and it is decided here, when the role is created.
 *
 * **There is no "reset this person's password".** `setTemporaryPassword` is the nearest thing and
 * it is deliberately different: it sets a one-time password the user must change at next sign-in
 * (Step 7A, for deployments with no mail delivery), and the response says `mustChangePassword` so
 * a screen can show that it took. Nothing here can read a password, and nothing here can set a
 * permanent one on somebody else's behalf.
 */
@Injectable({ providedIn: 'root' })
export class IdentityAdminService {
  private readonly api = inject(IdentityApiClient);

  // ---- Users ------------------------------------------------------------------------------------

  users(filters: UserFilters = {}, pageSize = 25): CursorList<AdminUserResponse, UserFilters> {
    return new CursorList<AdminUserResponse, UserFilters>(
      (current, cursor, size) =>
        this.api
          .adminUsersGet({
            Search: current.search,
            UserType: current.userType,
            Cursor: cursor ?? undefined,
            Size: size,
          })
          .pipe(map((result): CursorPage<AdminUserResponse> => result)),
      filters,
      pageSize,
    );
  }

  user(id: string): Observable<AdminUserResponse> {
    return this.api.adminUserGet(id);
  }

  /** A staff or seller account. `vendorId` is what makes the new user a seller's user. */
  createUser(body: CreateUserBody): Observable<AdminUserResponse> {
    return this.api.adminUserCreate(body);
  }

  setUserRoles(id: string, roleCodes: readonly string[]): Observable<AdminUserResponse> {
    return this.api.adminUserRolesPut(id, { roleCodes: [...roleCodes] });
  }

  /** Suspended keeps the account and refuses the sign-in; disabled is the end of it. */
  setUserStatus(id: string, status: UserStatus): Observable<AdminUserResponse> {
    return this.api.adminUserStatusPut(id, { status });
  }

  /** One-time, and the holder must change it at next sign-in. */
  setTemporaryPassword(id: string, temporaryPassword: string): Observable<AdminUserResponse> {
    return this.api.adminUserPasswordPut(id, { temporaryPassword });
  }

  // ---- Roles ------------------------------------------------------------------------------------

  roles(): Observable<RoleResponse[]> {
    return this.api.adminRolesGet();
  }

  createRole(body: CreateRoleBody): Observable<RoleResponse> {
    return this.api.adminRoleCreate(body);
  }

  /** A system role's permissions are fixed; the API refuses this for one, and the screen says so. */
  updateRole(id: string, body: UpdateRoleBody): Observable<RoleResponse> {
    return this.api.adminRolePut(id, body);
  }

  /** The whole permission catalogue, grouped as the role editor renders it. */
  permissions(): Observable<PermissionGroupResponse[]> {
    return this.api.adminPermissionsGet();
  }
}
