using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Modules;
using KlaraHome.Infrastructure.Options;
using KlaraHome.Infrastructure.Persistence;
using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Identity.Endpoints;
using KlaraHome.Modules.Identity.Infrastructure;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.Modules.Identity.Infrastructure.Security;
using KlaraHome.Modules.Identity.Infrastructure.Seeding;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace KlaraHome.Modules.Identity;

/// <summary>
/// Authentication and authorisation for the three actor classes: customers, vendor staff and
/// platform staff.
/// </summary>
/// <remarks>
/// <para>
/// This module registers the authentication scheme, which is why <c>UnsecuredEndpointGuard</c>
/// goes quiet once it is present: every endpoint that declared a required permission at Step 6 is
/// now behind a policy that checks it, and the fallback policy closes anything that declares
/// nothing at all.
/// </para>
/// <para>
/// It does not use ASP.NET Core Identity. The framework's implementation is built around a
/// cookie-first, <c>UserManager</c>-shaped model that does not fit mobile-OTP as the primary
/// credential, vendor-scoped data filtering, or permission-based rather than role-based
/// enforcement — and adapting it would have been more code than the parts we actually need.
/// </para>
/// </remarks>
public sealed class IdentityModule : IModule
{
    /// <summary>The Postgres schema this module owns.</summary>
    public const string SchemaName = "identity";

    /// <inheritdoc />
    public string Name => "Identity";

    /// <inheritdoc />
    public string Schema => SchemaName;

    /// <summary>
    /// Registered after Platform, whose settings and audit trail this module reads, and before
    /// everything else, which reads the caller this module authenticates.
    /// </summary>
    public int Order => 20;

    /// <inheritdoc />
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddModuleDbContext<IdentityDbContext>(configuration, this);
        services.AddValidatedOptions<AuthOptions>(configuration, AuthOptions.SectionName);

        services.AddSingleton<SigningKeyRing>();
        services.AddSingleton<BreachedPasswords>();
        services.AddScoped<PasswordHasher>();
        services.AddScoped<SecretProtector>();
        services.AddScoped<TokenIssuer>();

        services.AddScoped<AccessResolver>();
        services.AddScoped<SessionService>();
        services.AddScoped<SignInCoordinator>();
        services.AddScoped<OtpService>();

        services.AddScoped<Application.Authentication.PasswordRules>();
        services.AddScoped<Application.Account.AddressWriter>();
        services.AddScoped<Application.Administration.AdminUserScope>();

        // No SMS or email transport exists until the Notifications module at Step 8, so the code is
        // written to the log where a developer can read it. The registration is unconditional
        // because the alternative — a host with no dispatcher at all — fails at the moment somebody
        // tries to sign in rather than at startup; LoggingOtpDispatcher itself is what says, loudly,
        // that this is not a production arrangement.
        services.AddScoped<IOtpDispatcher, LoggingOtpDispatcher>();

        services.AddDataSeeder<PermissionSeeder>();
        services.AddDataSeeder<SystemRoleSeeder>();
        services.AddDataSeeder<BootstrapAdminSeeder>();

        AddAuthentication(services, configuration);
    }

    /// <inheritdoc />
    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var store = endpoints.MapGroup("/store");
        store.MapAuthEndpoints("store", includeOtp: true);
        store.MapAccountEndpoints("store", includeAddresses: true);

        var admin = endpoints.MapGroup("/admin");
        admin.MapAuthEndpoints("admin", includeOtp: false);
        admin.MapAccountEndpoints("admin", includeAddresses: false);
        admin.MapAdminIdentityEndpoints();
    }

    /// <summary>
    /// Registers the JWT bearer scheme.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The keys come from <see cref="SigningKeyRing"/> rather than from a discovery document: this
    /// service issues its own tokens, so there is no authority to fetch metadata from, and a
    /// deployment behind a firewall must not depend on being able to reach one.
    /// </para>
    /// <para>
    /// <c>MapInboundClaims</c> is off. With it on, the handler rewrites <c>sub</c> into a
    /// WS-Federation URI, and every reader of <c>KlaraHomeClaims.UserId</c> would silently find
    /// nothing.
    /// </para>
    /// </remarks>
    private static void AddAuthentication(IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(bearer =>
            {
                bearer.MapInboundClaims = false;
                bearer.SaveToken = false;

                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = options.Tokens.Issuer,
                    ValidAudience = options.Tokens.Audience,
                    NameClaimType = KlaraHomeClaims.UserId,

                    // Fifteen-minute tokens with five minutes of default skew are twenty-minute
                    // tokens. Thirty seconds is enough for clock drift between containers.
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        // The keys are attached from the container rather than inside the lambda above, because the
        // ring reads configuration and generates a Development key: constructing it during
        // registration would build it before configuration was complete, and would build a second
        // one for the issuer to sign with.
        services
            .AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<SigningKeyRing>((bearer, keys) =>
                bearer.TokenValidationParameters.IssuerSigningKeys = keys.ValidationKeys);
    }
}
