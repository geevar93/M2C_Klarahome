using FluentValidation;
using KlaraHome.Contracts.Catalog;
using KlaraHome.Contracts.Identity;
using KlaraHome.Contracts.Notifications;
using KlaraHome.Infrastructure.Http;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Reviews.Domain;
using KlaraHome.Modules.Reviews.Infrastructure;
using KlaraHome.Modules.Reviews.Infrastructure.Persistence;
using KlaraHome.Modules.Reviews.Infrastructure.Projection;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Reviews.Application.Questions;

/// <summary>Asks a question about a product.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="Body">What they want to know.</param>
/// <param name="CustomerId">Who is asking, from the caller's token.</param>
internal sealed record AskQuestionCommand(Guid ProductId, string? Body, Guid CustomerId)
    : ICommand<QuestionResponse>;

/// <summary>Answers one.</summary>
/// <param name="QuestionId">The question.</param>
/// <param name="Body">The answer.</param>
internal sealed record AnswerQuestionCommand(Guid QuestionId, string? Body)
    : ICommand<QuestionResponse>;

/// <summary>Lists a product's published questions.</summary>
/// <param name="ProductId">The product.</param>
/// <param name="UnansweredOnly">Only the ones nobody has answered.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListProductQuestionsQuery(
    Guid ProductId,
    bool? UnansweredOnly,
    string? Cursor,
    int? Size) : IQuery<PagedResult<QuestionResponse>>;

/// <summary>Lists questions for the moderation queue.</summary>
/// <param name="Status">Pending, Approved or Rejected. Null for all of them.</param>
/// <param name="ProductId">Only this product's.</param>
/// <param name="UnansweredOnly">Only the ones nobody has answered.</param>
/// <param name="Cursor">Keyset cursor from a previous page.</param>
/// <param name="Size">How many to return.</param>
internal sealed record ListQuestionsForModerationQuery(
    string? Status,
    Guid? ProductId,
    bool? UnansweredOnly,
    string? Cursor,
    int? Size) : IQuery<PagedResult<ModeratedQuestionResponse>>;

/// <summary>Decides a question.</summary>
/// <param name="Id">The question.</param>
/// <param name="Approve">Whether to publish it.</param>
/// <param name="Note">Why, for a refusal.</param>
internal sealed record ModerateQuestionCommand(Guid Id, bool Approve, string? Note)
    : ICommand<ModeratedQuestionResponse>;

/// <summary>Decides an answer.</summary>
/// <param name="QuestionId">The question it is under.</param>
/// <param name="AnswerId">The answer.</param>
/// <param name="Approve">Whether to publish it.</param>
internal sealed record ModerateAnswerCommand(Guid QuestionId, Guid AnswerId, bool Approve)
    : ICommand<ModeratedQuestionResponse>;

/// <summary>Rules a question has to satisfy.</summary>
internal sealed class AskQuestionCommandValidator : AbstractValidator<AskQuestionCommand>
{
    public AskQuestionCommandValidator()
    {
        RuleFor(command => command.ProductId).NotEmpty();
        RuleFor(command => command.CustomerId).NotEmpty();
        RuleFor(command => command.Body).NotEmpty().MaximumLength(Question.MaxBodyLength);
    }
}

/// <summary>Rules an answer has to satisfy.</summary>
internal sealed class AnswerQuestionCommandValidator : AbstractValidator<AnswerQuestionCommand>
{
    public AnswerQuestionCommandValidator()
    {
        RuleFor(command => command.QuestionId).NotEmpty();
        RuleFor(command => command.Body).NotEmpty().MaximumLength(Answer.MaxBodyLength);
    }
}

/// <summary>
/// Asks a question about a product.
/// </summary>
/// <remarks>
/// The product is checked against the catalogue before the row is written, which is the one place
/// this module can establish that a product id names anything at all. Without it the questions table
/// would accumulate rows against ids nobody can resolve, and a moderation queue full of questions
/// about nothing is a queue that stops being worked.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="catalogue">Confirms the product exists.</param>
/// <param name="customers">Resolves the name the question is signed with.</param>
/// <param name="options">Whether a question is visible the moment it is asked.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class AskQuestionCommandHandler(
    ReviewsDbContext context,
    IProductProjectionSource catalogue,
    ICustomerDirectory customers,
    IOptionsMonitor<ReviewsOptions> options,
    IClock clock) : ICommandHandler<AskQuestionCommand, QuestionResponse>
{
    public async Task<Result<QuestionResponse>> HandleAsync(
        AskQuestionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var projections = await catalogue
            .FindByProductsAsync([command.ProductId], cancellationToken)
            .ConfigureAwait(false);

        if (projections.Count == 0)
        {
            return Result.Failure<QuestionResponse>(ReviewErrors.UnknownProduct);
        }

        var customer = await customers.FindAsync(command.CustomerId, cancellationToken).ConfigureAwait(false);

        var question = Question.Ask(
            command.ProductId,
            command.CustomerId,
            command.Body!,
            DisplayNames.Shorten(customer?.DisplayName));

        if (options.CurrentValue.AutoApproveQuestions)
        {
            question.Approve(moderatorId: null, clock.UtcNow);
        }

        context.Questions.Add(question);

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToPublic(question));
    }
}

/// <summary>
/// Answers a question, and tells whoever asked.
/// </summary>
/// <remarks>
/// <para>
/// How the answer is labelled comes from the caller's own claims and never from the request. "The
/// seller says" is the most trusted line on a product page, and a body that could assert it would
/// let any shopper put words in a seller's mouth.
/// </para>
/// <para>
/// A seller's own answer skips the queue where the store allows it, and that exception is
/// defensible in a way the general case is not: a seller is an identified party under a contract
/// with the store, and holding their answers behind moderation makes Q&amp;A useless — its whole
/// value is that somebody who knows replies while the shopper is still on the page.
/// </para>
/// <para>
/// The notification is queued rather than published as an event, because losing one is harmless: a
/// shopper who is not told still sees the answer next time they open the page, and an answer that
/// rolled back after the message was queued produces a message about an answer that is simply not
/// there yet. Anything where that would not be harmless goes through the outbox instead.
/// </para>
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="scope">Who is answering, and how their answer is labelled.</param>
/// <param name="customers">Resolves the name a customer's answer is signed with.</param>
/// <param name="notifier">Tells whoever asked.</param>
/// <param name="options">Whether an answer is visible immediately.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class AnswerQuestionCommandHandler(
    ReviewsDbContext context,
    ReviewScope scope,
    ICustomerDirectory customers,
    INotifier notifier,
    IOptionsMonitor<ReviewsOptions> options,
    IClock clock) : ICommandHandler<AnswerQuestionCommand, QuestionResponse>
{
    public async Task<Result<QuestionResponse>> HandleAsync(
        AnswerQuestionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var question = await context.Questions
            .Include(row => row.Answers)
            .FirstOrDefaultAsync(row => row.Id == command.QuestionId, cancellationToken)
            .ConfigureAwait(false);

        if (question is null || question.Status != PostStatus.Approved)
        {
            return Result.Failure<QuestionResponse>(ReviewErrors.NotFound("question"));
        }

        var settings = options.CurrentValue;
        var authorType = scope.AuthorType;

        // A seller's and the store's answers are labelled by their role rather than by a person's
        // name, because that is the authority a shopper is reading. Only another customer's answer is
        // signed with a name, and it is shortened like every other public name here.
        var authorName = authorType == AnswerAuthor.Customer && scope.ActorId is { } actorId
            ? DisplayNames.Shorten(
                (await customers.FindAsync(actorId, cancellationToken).ConfigureAwait(false))?.DisplayName)
            : null;

        var answer = Answer.Write(
            question.Id,
            command.Body!,
            authorType,
            scope.ActorId,
            authorType == AnswerAuthor.Vendor ? scope.VendorId : null,
            authorName);

        var autoApprove = authorType == AnswerAuthor.Vendor
            ? settings.AutoApproveVendorAnswers
            : authorType == AnswerAuthor.Store || settings.AutoApproveAnswers;

        if (autoApprove)
        {
            answer.Approve(moderatorId: null, clock.UtcNow);
        }

        question.AddAnswer(answer);
        question.RecordAnswerCount(question.Answers.Count(row => row.Status == PostStatus.Approved));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (answer.Status == PostStatus.Approved)
        {
            await NotifyAskerAsync(question, answer, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(ReviewProjection.ToPublic(question));
    }

    /// <summary>Tells the person who asked that they have an answer.</summary>
    /// <remarks>
    /// The recipient is named by user id alone. This module holds no email address for a customer and
    /// has no business holding one — Notifications resolves the address from the account, which is
    /// also what applies their preferences.
    /// </remarks>
    private async Task NotifyAskerAsync(Question question, Answer answer, CancellationToken cancellationToken)
        => await notifier
            .EnqueueAsync(
                new NotificationRequest(
                    NotificationEvents.QuestionAnswered,
                    new NotificationRecipient(question.CustomerId, Name: question.AuthorName),
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["name"] = question.AuthorName ?? "there",
                        ["question"] = question.Body,
                        ["answer"] = answer.Body,
                        ["answeredBy"] = answer.AuthorName ?? answer.AuthorType.ToString(),

                        // The product's name is not this module's to know; the storefront resolves it
                        // from the id in the link. Naming the id keeps the template honest about
                        // what it has.
                        ["productName"] = question.ProductId.ToString(),
                    }),
                cancellationToken)
            .ConfigureAwait(false);
}

/// <summary>Lists a product's published questions.</summary>
/// <remarks>
/// Published only, with published answers under them, and there is no parameter that changes either.
/// A question awaiting moderation is not a question the product page has, and an unapproved answer
/// under an approved question is the exact case that would let unmoderated text onto the page
/// through the back door.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="options">The page ceiling.</param>
internal sealed class ListProductQuestionsQueryHandler(
    ReviewsDbContext context,
    IOptionsMonitor<ReviewsOptions> options)
    : IQueryHandler<ListProductQuestionsQuery, PagedResult<QuestionResponse>>
{
    public async Task<Result<PagedResult<QuestionResponse>>> HandleAsync(
        ListProductQuestionsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.CurrentValue.MaxPageSize);

        var rows = context.Questions
            .AsNoTracking()
            .Include(question => question.Answers)
            .Where(question => question.ProductId == query.ProductId)
            .Where(question => question.Status == PostStatus.Approved);

        if (query.UnansweredOnly == true)
        {
            rows = rows.Where(question => question.AnswerCount == 0);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = rows.Where(question => question.Id.CompareTo(after) < 0);
        }

        var page = await rows
            // Answered first, then newest. A page of unanswered questions at the top reads as a
            // product nobody supports, which is the opposite of what the section is for.
            .OrderByDescending(question => question.AnswerCount)
            .ThenByDescending(question => question.Id)
            .Take(size + 1)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).ToList();

        var responses = items.Select(ReviewProjection.ToPublic).ToArray();
        var next = hasMore && items.Count > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<QuestionResponse>(responses, new PageInfo(size, next)));
    }
}

/// <summary>Lists questions for whoever is working the queue.</summary>
/// <param name="context">The Reviews data context.</param>
/// <param name="options">The page ceiling.</param>
internal sealed class ListQuestionsForModerationQueryHandler(
    ReviewsDbContext context,
    IOptionsMonitor<ReviewsOptions> options)
    : IQueryHandler<ListQuestionsForModerationQuery, PagedResult<ModeratedQuestionResponse>>
{
    public async Task<Result<PagedResult<ModeratedQuestionResponse>>> HandleAsync(
        ListQuestionsForModerationQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var size = Math.Min(Cursor.NormalizeSize(query.Size), options.CurrentValue.MaxPageSize);

        var rows = context.Questions.AsNoTracking().Include(question => question.Answers).AsQueryable();

        var pending = false;

        if (Enum.TryParse<PostStatus>(query.Status, ignoreCase: true, out var status))
        {
            rows = rows.Where(question => question.Status == status);
            pending = status == PostStatus.Pending;
        }

        if (query.ProductId is { } productId)
        {
            rows = rows.Where(question => question.ProductId == productId);
        }

        if (query.UnansweredOnly == true)
        {
            rows = rows.Where(question => question.AnswerCount == 0);
        }

        if (Cursor.TryDecode(query.Cursor, out var key) && Guid.TryParse(key, out var after))
        {
            rows = pending
                ? rows.Where(question => question.Id.CompareTo(after) > 0)
                : rows.Where(question => question.Id.CompareTo(after) < 0);
        }

        rows = pending
            ? rows.OrderBy(question => question.Id)
            : rows.OrderByDescending(question => question.Id);

        var page = await rows.Take(size + 1).ToListAsync(cancellationToken).ConfigureAwait(false);

        var hasMore = page.Count > size;
        var items = page.Take(size).ToList();

        var responses = items.Select(ReviewProjection.ToModerated).ToArray();
        var next = hasMore && items.Count > 0 ? Cursor.Encode(items[^1].Id.ToString()) : null;

        return Result.Success(new PagedResult<ModeratedQuestionResponse>(responses, new PageInfo(size, next)));
    }
}

/// <summary>Approves or refuses a question.</summary>
/// <remarks>
/// Refusing a question does not refuse the answers under it. They stay in whatever state they were
/// in and become invisible because their question is, which is the honest arrangement: an answer
/// that was fine is not made bad by the question being spam, and reinstating the question should not
/// require re-approving everything under it.
/// </remarks>
/// <param name="context">The Reviews data context.</param>
/// <param name="scope">Who is deciding.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ModerateQuestionCommandHandler(ReviewsDbContext context, ReviewScope scope, IClock clock)
    : ICommandHandler<ModerateQuestionCommand, ModeratedQuestionResponse>
{
    public async Task<Result<ModeratedQuestionResponse>> HandleAsync(
        ModerateQuestionCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.Approve && string.IsNullOrWhiteSpace(command.Note))
        {
            return Result.Failure<ModeratedQuestionResponse>(ReviewErrors.ReasonRequired);
        }

        var question = await context.Questions
            .Include(row => row.Answers)
            .FirstOrDefaultAsync(row => row.Id == command.Id, cancellationToken)
            .ConfigureAwait(false);

        if (question is null)
        {
            return Result.Failure<ModeratedQuestionResponse>(ReviewErrors.NotFound("question"));
        }

        var now = clock.UtcNow;

        if (command.Approve)
        {
            question.Approve(scope.ActorId, now);
        }
        else
        {
            question.Reject(scope.ActorId, now, command.Note);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToModerated(question));
    }
}

/// <summary>Approves or refuses one answer.</summary>
/// <param name="context">The Reviews data context.</param>
/// <param name="scope">Who is deciding.</param>
/// <param name="clock">The sanctioned clock.</param>
internal sealed class ModerateAnswerCommandHandler(ReviewsDbContext context, ReviewScope scope, IClock clock)
    : ICommandHandler<ModerateAnswerCommand, ModeratedQuestionResponse>
{
    public async Task<Result<ModeratedQuestionResponse>> HandleAsync(
        ModerateAnswerCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var question = await context.Questions
            .Include(row => row.Answers)
            .FirstOrDefaultAsync(row => row.Id == command.QuestionId, cancellationToken)
            .ConfigureAwait(false);

        var answer = question?.Answers.FirstOrDefault(row => row.Id == command.AnswerId);

        if (question is null || answer is null)
        {
            return Result.Failure<ModeratedQuestionResponse>(ReviewErrors.NotFound("answer"));
        }

        var now = clock.UtcNow;

        if (command.Approve)
        {
            answer.Approve(scope.ActorId, now);
        }
        else
        {
            answer.Reject(scope.ActorId, now);
        }

        // The cached count follows the decision, because the product page orders on it.
        question.RecordAnswerCount(question.Answers.Count(row => row.Status == PostStatus.Approved));

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(ReviewProjection.ToModerated(question));
    }
}
