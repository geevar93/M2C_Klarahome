using FluentValidation;
using KlaraHome.Contracts.Notifications;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Infrastructure.Tenancy;
using KlaraHome.Modules.Notifications.Domain;
using KlaraHome.Modules.Notifications.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Notifications.Application;

/// <summary>What one person has chosen for one category.</summary>
/// <param name="Category">The bucket.</param>
/// <param name="Description">What falls into it, so the screen is not a list of bare words.</param>
/// <param name="Email">Whether email is wanted.</param>
/// <param name="Sms">Whether SMS is wanted.</param>
/// <param name="WhatsApp">Whether WhatsApp is wanted.</param>
/// <param name="InApp">Whether in-application messages are wanted.</param>
internal sealed record PreferenceResponse(
    string Category,
    string Description,
    bool Email,
    bool Sms,
    bool WhatsApp,
    bool InApp);

/// <summary>The whole preference screen.</summary>
/// <param name="Categories">One entry per category a person can choose about.</param>
/// <param name="AvailableChannels">
/// The channels this deployment can actually deliver on. A toggle for a channel with no provider
/// would be a switch that changes nothing.
/// </param>
internal sealed record PreferencesResponse(
    IReadOnlyList<PreferenceResponse> Categories,
    IReadOnlyList<string> AvailableChannels);

/// <summary>Reads the current person's preferences.</summary>
internal sealed record GetPreferencesQuery : IQuery<PreferencesResponse>;

/// <summary>Replaces one category's choices.</summary>
/// <param name="Category">The bucket.</param>
/// <param name="Email">Whether email is wanted.</param>
/// <param name="Sms">Whether SMS is wanted.</param>
/// <param name="WhatsApp">Whether WhatsApp is wanted.</param>
/// <param name="InApp">Whether in-application messages are wanted.</param>
internal sealed record UpdatePreferenceCommand(string Category, bool Email, bool Sms, bool WhatsApp, bool InApp)
    : ICommand<PreferencesResponse>;

/// <summary>Rules for a preference change.</summary>
internal sealed class UpdatePreferenceCommandValidator : AbstractValidator<UpdatePreferenceCommand>
{
    public UpdatePreferenceCommandValidator()
        => RuleFor(command => command.Category)
            .NotEmpty()
            .Must(category => Enum.TryParse<NotificationCategory>(category, ignoreCase: true, out var parsed)
                              && parsed != NotificationCategory.Security)
            .WithMessage(
                "Unknown category. Security messages cannot be switched off: they are the only "
                + "warning you would get that somebody else has changed your account.");
}

/// <summary>The categories a person may choose about, and what falls into each.</summary>
internal static class PreferenceCatalogue
{
    /// <summary>Every category except Security, which nobody may opt out of.</summary>
    public static IReadOnlyList<(NotificationCategory Category, string Description)> All { get; } =
    [
        (NotificationCategory.Orders, "Order confirmations, invoices and cancellations."),
        (NotificationCategory.Shipping, "Dispatch, tracking and delivery updates."),
        (NotificationCategory.Payments, "Refunds, returns and credit notes."),
        (NotificationCategory.Vendor, "Seller operations: new orders and settlement statements."),
        (NotificationCategory.Marketing, "Offers and campaigns."),
    ];
}

/// <param name="context">The Notifications data context.</param>
/// <param name="userContext">Whose preferences to read.</param>
/// <param name="router">Reports which channels can actually deliver.</param>
internal sealed class GetPreferencesQueryHandler(
    NotificationsDbContext context,
    IUserContext userContext,
    Infrastructure.Channels.ChannelRouter router) : IQueryHandler<GetPreferencesQuery, PreferencesResponse>
{
    public async Task<Result<PreferencesResponse>> HandleAsync(
        GetPreferencesQuery query,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Result.Failure<PreferencesResponse>(Error.Unauthorized());
        }

        var stored = await context.Preferences
            .AsNoTracking()
            .Where(preference => preference.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(Build(stored, router));
    }

    /// <summary>
    /// Projects stored rows onto the full catalogue, filling in defaults where nobody has chosen.
    /// </summary>
    /// <remarks>
    /// The screen always shows every category, whether or not a row exists. A person who has never
    /// opened this page has no rows, and a response listing nothing would read as "you receive
    /// nothing" — the opposite of the truth.
    /// </remarks>
    internal static PreferencesResponse Build(
        IReadOnlyList<NotificationPreference> stored,
        Infrastructure.Channels.ChannelRouter router)
    {
        ArgumentNullException.ThrowIfNull(stored);
        ArgumentNullException.ThrowIfNull(router);

        IReadOnlyList<PreferenceResponse> categories =
        [
            .. PreferenceCatalogue.All.Select(entry =>
            {
                var preference = stored.FirstOrDefault(candidate => candidate.Category == entry.Category);
                var fallback = NotificationPreference.DefaultFor(entry.Category);

                return new PreferenceResponse(
                    entry.Category.ToString(),
                    entry.Description,
                    preference?.Email ?? fallback,
                    preference?.Sms ?? fallback,
                    preference?.WhatsApp ?? fallback,
                    preference?.InApp ?? fallback);
            }),
        ];

        IReadOnlyList<string> available =
        [
            .. Enum.GetValues<NotificationChannel>()
                .Where(router.HasProvider)
                .Select(channel => channel.ToString()),
        ];

        return new PreferencesResponse(categories, available);
    }
}

/// <param name="context">The Notifications data context.</param>
/// <param name="userContext">Whose preferences to change.</param>
/// <param name="router">Reports which channels can actually deliver.</param>
internal sealed class UpdatePreferenceCommandHandler(
    NotificationsDbContext context,
    IUserContext userContext,
    Infrastructure.Channels.ChannelRouter router)
    : ICommandHandler<UpdatePreferenceCommand, PreferencesResponse>
{
    public async Task<Result<PreferencesResponse>> HandleAsync(
        UpdatePreferenceCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (userContext.UserId is not { } userId)
        {
            return Result.Failure<PreferencesResponse>(Error.Unauthorized());
        }

        var category = Enum.Parse<NotificationCategory>(command.Category, ignoreCase: true);

        var preference = await context.Preferences
            .FirstOrDefaultAsync(
                candidate => candidate.UserId == userId && candidate.Category == category,
                cancellationToken)
            .ConfigureAwait(false);

        if (preference is null)
        {
            preference = NotificationPreference.For(userId, category);
            context.Preferences.Add(preference);
        }

        preference.Set(command.Email, command.Sms, command.WhatsApp, command.InApp);
        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        var stored = await context.Preferences
            .AsNoTracking()
            .Where(candidate => candidate.UserId == userId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success(GetPreferencesQueryHandler.Build(stored, router));
    }
}
