using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Identity.Domain;
using KlaraHome.Modules.Identity.Infrastructure;
using KlaraHome.Modules.Identity.Infrastructure.Access;
using KlaraHome.Modules.Identity.Infrastructure.External;
using KlaraHome.Modules.Identity.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Identity.Application.Authentication;

/// <summary>Which identity providers this deployment offers.</summary>
internal sealed record GetExternalProvidersQuery : IQuery<IReadOnlyList<ExternalProviderResponse>>;

/// <summary>Begins a sign-in with an identity provider.</summary>
/// <param name="Provider">The route segment naming the provider.</param>
/// <param name="ReturnUrl">Where to send the storefront afterwards, checked against the allow-list.</param>
internal sealed record StartExternalLoginCommand(string Provider, string? ReturnUrl)
    : ICommand<ExternalLoginRedirect>;

/// <summary>Completes a sign-in the provider has redirected back from.</summary>
/// <param name="Provider">The route segment naming the provider.</param>
/// <param name="Code">The authorization code.</param>
/// <param name="State">The state the provider echoed back.</param>
/// <param name="StateCookie">The encrypted cookie this sign-in was started with.</param>
/// <param name="Device">What the caller looks like.</param>
internal sealed record CompleteExternalLoginCommand(
    string Provider,
    string? Code,
    string? State,
    string? StateCookie,
    DeviceInfo Device) : ICommand<ExternalLoginOutcome>;

/// <summary>The providers linked to the caller's own account.</summary>
internal sealed record GetExternalLoginsQuery : IQuery<IReadOnlyList<ExternalLoginResponse>>;

/// <summary>Removes one of the caller's linked providers.</summary>
/// <param name="Id">The link to remove.</param>
internal sealed record UnlinkExternalLoginCommand(Guid Id) : ICommand;

/// <summary>One provider, as the sign-in page renders its button.</summary>
/// <param name="Provider">The wire name, for the start URL.</param>
/// <param name="DisplayName">What the button says.</param>
internal sealed record ExternalProviderResponse(string Provider, string DisplayName);

/// <summary>Where to send the browser, and the cookie that has to travel with it.</summary>
/// <param name="AuthorizationUri">The provider's authorization endpoint, with the request on it.</param>
/// <param name="StateCookie">The encrypted state, which the endpoint writes as a cookie.</param>
internal sealed record ExternalLoginRedirect(Uri AuthorizationUri, string StateCookie);

/// <summary>A completed callback: a session, and where to send the browser next.</summary>
/// <param name="SignIn">The session, or the challenge that still stands in its way.</param>
/// <param name="ReturnUrl">Where the storefront resumes.</param>
internal sealed record ExternalLoginOutcome(SignInResult SignIn, string ReturnUrl);

/// <summary>One linked provider, as the account page lists it.</summary>
/// <param name="Id">The link id, for unlinking.</param>
/// <param name="Provider">The wire name.</param>
/// <param name="Email">The address the provider last asserted.</param>
/// <param name="LinkedAt">When it was linked.</param>
/// <param name="LastLoginAt">When it was last used.</param>
internal sealed record ExternalLoginResponse(
    Guid Id,
    string Provider,
    string? Email,
    DateTimeOffset LinkedAt,
    DateTimeOffset LastLoginAt);

/// <summary>The failures the external sign-in paths share.</summary>
internal static class ExternalAuthErrors
{
    /// <summary>The route named a provider this deployment does not offer.</summary>
    /// <param name="provider">What the route said.</param>
    public static Error UnknownProvider(string provider)
        => Error.NotFound(
            "IDENTITY_PROVIDER_UNKNOWN",
            $"'{provider}' is not a sign-in method this store offers.");

    /// <summary>
    /// The callback did not belong to a sign-in this server started, for any reason.
    /// </summary>
    /// <remarks>
    /// One error for a missing cookie, a forged state, an expired attempt and a provider mismatch.
    /// Telling them apart would help whoever is probing the callback and nobody else — the
    /// legitimate answer to all four is the same: start again.
    /// </remarks>
    public static Error InvalidCallback()
        => Error.Unauthorized(
            "IDENTITY_EXTERNAL_CALLBACK_INVALID",
            "That sign-in could not be completed. Please try again.");

    /// <summary>The provider could not be reached.</summary>
    /// <param name="provider">Which one.</param>
    public static Error ProviderUnavailable(ExternalProvider provider)
        => Error.Unavailable(
            "IDENTITY_PROVIDER_UNAVAILABLE",
            $"{provider} could not be reached. Please try again, or sign in another way.");
}

/// <summary>Lists the providers that are both switched on and actually configured.</summary>
/// <param name="providers">Every registered adapter.</param>
internal sealed class GetExternalProvidersQueryHandler(IEnumerable<IExternalIdentityProvider> providers)
    : IQueryHandler<GetExternalProvidersQuery, IReadOnlyList<ExternalProviderResponse>>
{
    public Task<Result<IReadOnlyList<ExternalProviderResponse>>> HandleAsync(
        GetExternalProvidersQuery query,
        CancellationToken cancellationToken)
    {
        // Usable, not merely enabled: a button that fails when somebody presses it is worse than
        // no button, and a half-configured provider is the ordinary state of a fresh deployment.
        IReadOnlyList<ExternalProviderResponse> usable = providers
            .Where(provider => provider.IsUsable)
            .Select(provider => new ExternalProviderResponse(
                ProviderNames.Of(provider.Provider),
                provider.Provider.ToString()))
            .ToList();

        return Task.FromResult(Result.Success(usable));
    }
}

/// <summary>Begins a sign-in: mints the state, and builds the URL the browser is sent to.</summary>
/// <param name="providers">Every registered adapter.</param>
/// <param name="state">Issues the encrypted state cookie.</param>
/// <param name="options">Supplies the callback origin and the return-URL allow-list.</param>
internal sealed class StartExternalLoginCommandHandler(
    IEnumerable<IExternalIdentityProvider> providers,
    ExternalLoginStateCookie state,
    IOptions<AuthOptions> options) : ICommandHandler<StartExternalLoginCommand, ExternalLoginRedirect>
{
    public async Task<Result<ExternalLoginRedirect>> HandleAsync(
        StartExternalLoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ProviderNames.TryParse(command.Provider, out var provider))
        {
            return ExternalAuthErrors.UnknownProvider(command.Provider);
        }

        var adapter = providers.FirstOrDefault(candidate => candidate.Provider == provider);

        if (adapter is null || !adapter.IsUsable)
        {
            return ExternalAuthErrors.UnknownProvider(command.Provider);
        }

        var external = options.Value.External;

        if (!ExternalLoginService.TryResolveReturnUrl(command.ReturnUrl, external, out var returnUrl))
        {
            // An unchecked returnUrl on the endpoint that is about to issue a session is a
            // phishing primitive, so a rejected one fails the sign-in rather than falling back.
            return Error.Validation(
                "IDENTITY_RETURN_URL_NOT_ALLOWED",
                "That return address is not one this store redirects to.");
        }

        var issued = state.Issue(provider, returnUrl);
        var redirectUri = ExternalLoginService.CallbackUriFor(provider, external);

        var authorization = await adapter
            .BuildAuthorizationUriAsync(
                issued.State.State,
                Pkce.ChallengeFor(issued.State.CodeVerifier),
                redirectUri,
                cancellationToken)
            .ConfigureAwait(false);

        return new ExternalLoginRedirect(authorization, issued.Cookie);
    }
}

/// <summary>Completes a callback: verifies the state, exchanges the code, signs the person in.</summary>
/// <param name="providers">Every registered adapter.</param>
/// <param name="state">Reads the encrypted state cookie back.</param>
/// <param name="logins">Applies the linking rules and starts the session.</param>
/// <param name="options">Supplies the callback origin.</param>
internal sealed class CompleteExternalLoginCommandHandler(
    IEnumerable<IExternalIdentityProvider> providers,
    ExternalLoginStateCookie state,
    ExternalLoginService logins,
    IOptions<AuthOptions> options) : ICommandHandler<CompleteExternalLoginCommand, ExternalLoginOutcome>
{
    public async Task<Result<ExternalLoginOutcome>> HandleAsync(
        CompleteExternalLoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!ProviderNames.TryParse(command.Provider, out var provider))
        {
            return ExternalAuthErrors.UnknownProvider(command.Provider);
        }

        var stored = state.Read(command.StateCookie, provider, command.State);

        if (stored is null || string.IsNullOrWhiteSpace(command.Code))
        {
            return ExternalAuthErrors.InvalidCallback();
        }

        var adapter = providers.FirstOrDefault(candidate => candidate.Provider == provider);

        if (adapter is null || !adapter.IsUsable)
        {
            return ExternalAuthErrors.UnknownProvider(command.Provider);
        }

        var exchanged = await adapter
            .ExchangeAsync(
                command.Code,
                stored.CodeVerifier,
                ExternalLoginService.CallbackUriFor(provider, options.Value.External),
                cancellationToken)
            .ConfigureAwait(false);

        if (exchanged.Identity is null)
        {
            return exchanged.Failure == ExternalExchangeFailure.Unavailable
                ? ExternalAuthErrors.ProviderUnavailable(provider)
                : ExternalAuthErrors.InvalidCallback();
        }

        var signedIn = await logins
            .SignInAsync(provider, exchanged.Identity, command.Device, cancellationToken)
            .ConfigureAwait(false);

        return signedIn.IsFailure
            ? signedIn.Error
            : new ExternalLoginOutcome(signedIn.Value, stored.ReturnUrl);
    }
}

/// <summary>Lists the providers linked to the caller's account.</summary>
/// <param name="context">The Identity data context.</param>
/// <param name="caller">The signed-in caller.</param>
internal sealed class GetExternalLoginsQueryHandler(IdentityDbContext context, ICallerContext caller)
    : IQueryHandler<GetExternalLoginsQuery, IReadOnlyList<ExternalLoginResponse>>
{
    public async Task<Result<IReadOnlyList<ExternalLoginResponse>>> HandleAsync(
        GetExternalLoginsQuery query,
        CancellationToken cancellationToken)
    {
        if (caller.UserId is null)
        {
            return Error.Unauthorized();
        }

        var links = await context.ExternalLogins
            .AsNoTracking()
            .Where(link => link.UserId == caller.UserId)
            .OrderBy(link => link.LinkedAt)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ExternalLoginResponse> response = links.ConvertAll(link => new ExternalLoginResponse(
            link.Id,
            ProviderNames.Of(link.Provider),
            link.Email,
            link.LinkedAt,
            link.LastLoginAt));

        return Result.Success(response);
    }
}

/// <summary>Removes one of the caller's linked providers.</summary>
/// <param name="logins">Owns the last-credential rule.</param>
/// <param name="caller">The signed-in caller.</param>
internal sealed class UnlinkExternalLoginCommandHandler(ExternalLoginService logins, ICallerContext caller)
    : ICommandHandler<UnlinkExternalLoginCommand>
{
    public async Task<Result> HandleAsync(
        UnlinkExternalLoginCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        return caller.UserId is null
            ? Result.Failure(Error.Unauthorized())
            : await logins.UnlinkAsync(caller.UserId.Value, command.Id, cancellationToken).ConfigureAwait(false);
    }
}
