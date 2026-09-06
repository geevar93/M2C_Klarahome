using FluentValidation;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Reporting.Domain;
using KlaraHome.Modules.Reporting.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Reporting.Application.Schedules;

/// <summary>Lists the standing instructions.</summary>
/// <param name="ActiveOnly">Only the ones that run.</param>
internal sealed record ListSchedulesQuery(bool? ActiveOnly) : IQuery<IReadOnlyList<ReportScheduleResponse>>;

/// <summary>Opens a standing instruction.</summary>
/// <param name="ReportKey">Which report.</param>
/// <param name="Name">What to call it.</param>
/// <param name="Frequency">Daily, Weekly or Monthly.</param>
/// <param name="HourUtc">The hour it runs at, in UTC.</param>
/// <param name="DayOfWeek">Which weekday, for a weekly schedule. Monday is 1.</param>
/// <param name="DayOfMonth">Which day, for a monthly one.</param>
/// <param name="Recipients">Where to send it. Empty to only file it.</param>
internal sealed record CreateScheduleCommand(
    string? ReportKey,
    string? Name,
    string? Frequency,
    int HourUtc,
    int? DayOfWeek,
    int? DayOfMonth,
    IReadOnlyList<string>? Recipients) : ICommand<ReportScheduleResponse>;

/// <summary>Changes a standing instruction. Which report it runs is not editable.</summary>
/// <param name="Id">The schedule.</param>
/// <param name="Name">What to call it.</param>
/// <param name="Frequency">Daily, Weekly or Monthly.</param>
/// <param name="HourUtc">The hour it runs at, in UTC.</param>
/// <param name="DayOfWeek">Which weekday.</param>
/// <param name="DayOfMonth">Which day of the month.</param>
/// <param name="Recipients">Where to send it.</param>
/// <param name="IsActive">Whether it runs.</param>
internal sealed record UpdateScheduleCommand(
    Guid Id,
    string? Name,
    string? Frequency,
    int HourUtc,
    int? DayOfWeek,
    int? DayOfMonth,
    IReadOnlyList<string>? Recipients,
    bool IsActive) : ICommand<ReportScheduleResponse>;

/// <summary>Removes a standing instruction.</summary>
/// <param name="Id">The schedule.</param>
internal sealed record DeleteScheduleCommand(Guid Id) : ICommand;

/// <summary>Rules a new schedule has to satisfy.</summary>
internal sealed class CreateScheduleCommandValidator : AbstractValidator<CreateScheduleCommand>
{
    public CreateScheduleCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(ReportSchedule.MaxNameLength);
        RuleFor(command => command.HourUtc).InclusiveBetween(0, 23);

        RuleFor(command => command.ReportKey)
            .Must(key => ReportCatalog.Find(key) is not null)
            .WithMessage("That is not a report this platform produces.");

        RuleFor(command => command.Frequency)
            .Must(frequency => Enum.TryParse<ReportFrequency>(frequency, ignoreCase: true, out _))
            .WithMessage("A schedule runs Daily, Weekly or Monthly.");

        RuleForEach(command => command.Recipients).EmailAddress();

        RuleFor(command => command.Recipients)
            .Must(recipients => recipients is null || recipients.Count <= ReportSchedule.MaxRecipients)
            .WithMessage($"A schedule sends to at most {ReportSchedule.MaxRecipients} addresses.");
    }
}

/// <summary>Rules an edit has to satisfy.</summary>
internal sealed class UpdateScheduleCommandValidator : AbstractValidator<UpdateScheduleCommand>
{
    public UpdateScheduleCommandValidator()
    {
        RuleFor(command => command.Id).NotEmpty();
        RuleFor(command => command.Name).NotEmpty().MaximumLength(ReportSchedule.MaxNameLength);
        RuleFor(command => command.HourUtc).InclusiveBetween(0, 23);

        RuleFor(command => command.Frequency)
            .Must(frequency => Enum.TryParse<ReportFrequency>(frequency, ignoreCase: true, out _))
            .WithMessage("A schedule runs Daily, Weekly or Monthly.");

        RuleForEach(command => command.Recipients).EmailAddress();
    }
}

/// <summary>Lists the standing instructions, soonest first.</summary>
/// <remarks>
/// Unpaged. A store has a handful of scheduled reports, not hundreds, and a cursor here would be a
/// cursor nobody ever uses on a screen that fits on one.
/// </remarks>
/// <param name="context">The Reporting data context.</param>
internal sealed class ListSchedulesQueryHandler(ReportingDbContext context)
    : IQueryHandler<ListSchedulesQuery, IReadOnlyList<ReportScheduleResponse>>
{
    /// <summary>The most schedules one screen will show.</summary>
    private const int Ceiling = 200;

    public async Task<Result<IReadOnlyList<ReportScheduleResponse>>> HandleAsync(
        ListSchedulesQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = context.Schedules.AsNoTracking().AsQueryable();

        if (query.ActiveOnly == true)
        {
            rows = rows.Where(schedule => schedule.IsActive);
        }

        var schedules = await rows
            .OrderBy(schedule => schedule.NextRunAt)
            .Take(Ceiling)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        IReadOnlyList<ReportScheduleResponse> responses =
            [.. schedules.Select(ReportingProjection.ToResponse)];

        return Result.Success(responses);
    }
}

/// <summary>Opens a standing instruction.</summary>
/// <param name="context">The Reporting data context.</param>
/// <param name="clock">The sanctioned clock; the first run is computed from it.</param>
internal sealed class CreateScheduleCommandHandler(ReportingDbContext context, IClock clock)
    : ICommandHandler<CreateScheduleCommand, ReportScheduleResponse>
{
    public async Task<Result<ReportScheduleResponse>> HandleAsync(
        CreateScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var definition = ReportCatalog.Find(command.ReportKey);

        if (definition is null)
        {
            return Result.Failure<ReportScheduleResponse>(ReportErrors.UnknownReport(command.ReportKey));
        }

        var schedule = ReportSchedule.Open(
            definition.Key,
            command.Name!,
            Enum.Parse<ReportFrequency>(command.Frequency!, ignoreCase: true),
            command.HourUtc,
            command.DayOfWeek,
            command.DayOfMonth,
            command.Recipients,
            clock.UtcNow);

        context.Schedules.Add(schedule);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReportingProjection.ToResponse(schedule));
    }
}

/// <summary>
/// Changes a standing instruction.
/// </summary>
/// <remarks>
/// Which report it runs is not editable, and that is deliberate. A schedule's run history points at
/// it, and changing the report underneath would leave a log in which the same schedule produced two
/// different things — with no way to tell from a row which. Delete it and make the other one.
/// </remarks>
/// <param name="context">The Reporting data context.</param>
/// <param name="clock">The sanctioned clock; the next run is recomputed from it.</param>
internal sealed class UpdateScheduleCommandHandler(ReportingDbContext context, IClock clock)
    : ICommandHandler<UpdateScheduleCommand, ReportScheduleResponse>
{
    public async Task<Result<ReportScheduleResponse>> HandleAsync(
        UpdateScheduleCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var schedule = await context.Schedules
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (schedule is null)
        {
            return Result.Failure<ReportScheduleResponse>(ReportErrors.NotFound("schedule"));
        }

        schedule.Update(
            command.Name!,
            Enum.Parse<ReportFrequency>(command.Frequency!, ignoreCase: true),
            command.HourUtc,
            command.DayOfWeek,
            command.DayOfMonth,
            command.Recipients,
            command.IsActive,
            clock.UtcNow);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReportingProjection.ToResponse(schedule));
    }
}

/// <summary>Removes a standing instruction.</summary>
/// <remarks>
/// A hard delete, and the runs it produced stay. They carry the schedule's id and nothing joins to
/// it, so the history survives a schedule being retired — which is the right way round: the reports
/// that were sent are a record, and the instruction that sent them is a setting.
/// </remarks>
/// <param name="context">The Reporting data context.</param>
internal sealed class DeleteScheduleCommandHandler(ReportingDbContext context)
    : ICommandHandler<DeleteScheduleCommand>
{
    public async Task<Result> HandleAsync(DeleteScheduleCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var schedule = await context.Schedules
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (schedule is null)
        {
            return Result.Failure(ReportErrors.NotFound("schedule"));
        }

        context.Schedules.Remove(schedule);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
