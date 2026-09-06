using FluentValidation;
using KlaraHome.Contracts.Media;
using KlaraHome.Contracts.Platform;
using KlaraHome.Infrastructure.Authorization;
using KlaraHome.Infrastructure.Messaging;
using KlaraHome.Modules.Vendors.Domain;
using KlaraHome.Modules.Vendors.Infrastructure;
using KlaraHome.Modules.Vendors.Infrastructure.Persistence;
using KlaraHome.SharedKernel.Results;
using KlaraHome.SharedKernel.Time;
using Microsoft.EntityFrameworkCore;

namespace KlaraHome.Modules.Vendors.Application;

/// <summary>One submitted document, as the API states it.</summary>
/// <param name="Id">The document.</param>
/// <param name="VendorId">The seller who submitted it.</param>
/// <param name="DocumentType">What it is.</param>
/// <param name="FileId">The private media file holding the scan.</param>
/// <param name="NumberMasked">The identifier on it, masked.</param>
/// <param name="Status">Whether it has been checked, and what was concluded.</param>
/// <param name="RejectionReason">Why it was refused.</param>
/// <param name="VerifiedAt">When it was checked.</param>
/// <param name="SubmittedAt">When it was submitted.</param>
/// <param name="DownloadUrl">
/// A short-lived link to the scan, minted only for a caller who may read it. Null when the file has
/// gone from the registry.
/// </param>
internal sealed record KycDocumentResponse(
    Guid Id,
    Guid? VendorId,
    KycDocumentType DocumentType,
    Guid FileId,
    string? NumberMasked,
    KycVerificationStatus Status,
    string? RejectionReason,
    DateTimeOffset? VerifiedAt,
    DateTimeOffset SubmittedAt,
    string? DownloadUrl);

/// <summary>What a seller still has to produce.</summary>
/// <param name="Documents">Everything they have submitted.</param>
/// <param name="Required">What their legal form obliges them to show.</param>
/// <param name="Missing">Which of those is not yet verified.</param>
internal sealed record KycSummaryResponse(
    IReadOnlyList<KycDocumentResponse> Documents,
    IReadOnlyList<KycDocumentType> Required,
    IReadOnlyList<KycDocumentType> Missing);

/// <summary>Lists a seller's documents.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
internal sealed record ListKycDocumentsQuery(Guid? VendorId) : IQuery<KycSummaryResponse>;

/// <summary>Submits a document, replacing any previous one of the same kind.</summary>
/// <param name="VendorId">The seller, or null for the caller's own.</param>
/// <param name="DocumentType">What the document is.</param>
/// <param name="FileId">The private media file holding the scan.</param>
/// <param name="Number">The identifier on it. Only its mask is kept.</param>
internal sealed record SubmitKycDocumentCommand(
    Guid? VendorId,
    KycDocumentType DocumentType,
    Guid FileId,
    string? Number) : ICommand<KycDocumentResponse>;

/// <summary>Accepts or refuses a document.</summary>
/// <param name="VendorId">The seller.</param>
/// <param name="DocumentId">The document.</param>
/// <param name="Approve">Whether it is accepted.</param>
/// <param name="RejectionReason">Why it was refused. Required for a refusal.</param>
internal sealed record VerifyKycDocumentCommand(
    Guid VendorId,
    Guid DocumentId,
    bool Approve,
    string? RejectionReason) : ICommand<KycDocumentResponse>;

/// <summary>Rules for a submission.</summary>
internal sealed class SubmitKycDocumentValidator : AbstractValidator<SubmitKycDocumentCommand>
{
    public SubmitKycDocumentValidator()
    {
        RuleFor(command => command.FileId).NotEmpty();
        RuleFor(command => command.DocumentType).IsInEnum();
        RuleFor(command => command.Number!).MaximumLength(64).When(command => command.Number is not null);
    }
}

/// <summary>Rules for a verification.</summary>
internal sealed class VerifyKycDocumentValidator : AbstractValidator<VerifyKycDocumentCommand>
{
    public VerifyKycDocumentValidator()
    {
        RuleFor(command => command.VendorId).NotEmpty();
        RuleFor(command => command.DocumentId).NotEmpty();

        RuleFor(command => command.RejectionReason)
            .NotEmpty()
            .MaximumLength(500)
            .When(command => !command.Approve)
            .WithMessage("Say why it was refused. The seller is shown this so they know what to send again.");
    }
}

/// <summary>Lists a seller's documents and says what is still outstanding.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="media">Mints the short-lived links to the scans.</param>
internal sealed class ListKycDocumentsQueryHandler(
    VendorsDbContext context,
    VendorScope scope,
    IMediaLibrary media) : IQueryHandler<ListKycDocumentsQuery, KycSummaryResponse>
{
    public async Task<Result<KycSummaryResponse>> HandleAsync(
        ListKycDocumentsQuery query,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        var resolved = scope.Resolve(query.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var vendor = await scope.FindAsync(resolved.Value, cancellationToken).ConfigureAwait(false);

        if (vendor is null)
        {
            return VendorErrors.NotFound;
        }

        var documents = await context.KycDocuments
            .AsNoTracking()
            .Where(document => document.VendorId == vendor.Id)
            .OrderBy(document => document.DocumentType)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var responses = new List<KycDocumentResponse>(documents.Count);

        foreach (var document in documents)
        {
            // One signed URL per document, minted here because reaching this line already means the
            // caller passed the permission check and the vendor scope. The URL is the grant.
            var url = await media.GetSignedUrlAsync(document.FileId, cancellationToken).ConfigureAwait(false);
            responses.Add(KycProjection.ToResponse(document, url));
        }

        var required = VendorReadinessService.RequiredDocuments(vendor);

        var missing = required
            .Where(type => !documents.Exists(document =>
                document.DocumentType == type && document.Status == KycVerificationStatus.Verified))
            .ToList();

        return Result.Success(new KycSummaryResponse(responses, required, missing));
    }
}

/// <summary>
/// Submits a document, or replaces the one already on file.
/// </summary>
/// <remarks>
/// Upsert rather than insert, because a rejected document has to be replaceable and the natural key
/// is (seller, kind). Two pending PAN cards would be a review queue where somebody has to guess
/// which one counts.
/// </remarks>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller the caller may act on.</param>
/// <param name="media">Checks the file exists and is private before it is pointed at.</param>
/// <param name="audit">Records the submission.</param>
internal sealed class SubmitKycDocumentCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    IMediaLibrary media,
    IAuditLogger audit) : ICommandHandler<SubmitKycDocumentCommand, KycDocumentResponse>
{
    /// <summary>The audited action for a submitted document.</summary>
    public const string AuditAction = "vendors.kyc.submitted";

    /// <summary>The entity type recorded against a KYC action.</summary>
    public const string AuditEntityType = "VendorKycDocument";

    public async Task<Result<KycDocumentResponse>> HandleAsync(
        SubmitKycDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var resolved = scope.Resolve(command.VendorId);

        if (resolved.IsFailure)
        {
            return resolved.Error;
        }

        var vendor = await scope.FindAsync(resolved.Value, cancellationToken).ConfigureAwait(false);

        if (vendor is null)
        {
            return VendorErrors.NotFound;
        }

        var file = await media.GetAsync(command.FileId, cancellationToken).ConfigureAwait(false);

        if (file is null)
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["fileId"] = ["No such file. Upload the scan first."],
                });
        }

        // A KYC document in the public bucket is a government identifier served to anybody with the
        // URL. Refusing it here is the only place that check can be made — the media module knows
        // the file is private, not that it is a PAN card.
        if (file.Visibility != MediaVisibility.Private)
        {
            return Error.Validation(
                new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
                {
                    ["fileId"] = ["A KYC document must be uploaded as a private file."],
                });
        }

        var document = await context.KycDocuments
            .FirstOrDefaultAsync(
                candidate => candidate.VendorId == vendor.Id && candidate.DocumentType == command.DocumentType,
                cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            document = VendorKycDocument.Submit(vendor.Id, command.DocumentType, command.FileId, command.Number);
            context.KycDocuments.Add(document);
        }
        else
        {
            document.Resubmit(command.FileId, command.Number);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = AuditEntityType,
                EntityId = document.Id.ToString(),
                After = new
                {
                    vendorId = vendor.Id,
                    documentType = document.DocumentType.ToString(),
                    document.NumberMasked,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(KycProjection.ToResponse(document, url: null));
    }
}

/// <summary>Accepts or refuses a document.</summary>
/// <param name="context">The Vendors data context.</param>
/// <param name="scope">Finds the seller.</param>
/// <param name="caller">Records who checked it.</param>
/// <param name="clock">The sanctioned clock.</param>
/// <param name="audit">Records the decision.</param>
internal sealed class VerifyKycDocumentCommandHandler(
    VendorsDbContext context,
    VendorScope scope,
    ICallerContext caller,
    IClock clock,
    IAuditLogger audit) : ICommandHandler<VerifyKycDocumentCommand, KycDocumentResponse>
{
    /// <summary>The audited action for a verification decision.</summary>
    public const string AuditAction = "vendors.kyc.verified";

    public async Task<Result<KycDocumentResponse>> HandleAsync(
        VerifyKycDocumentCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // A seller verifying their own documents defeats the point of collecting them.
        if (scope.IsVendorCaller)
        {
            return VendorErrors.OutOfScope;
        }

        var document = await context.KycDocuments
            .FirstOrDefaultAsync(
                candidate => candidate.Id == command.DocumentId && candidate.VendorId == command.VendorId,
                cancellationToken)
            .ConfigureAwait(false);

        if (document is null)
        {
            return VendorErrors.ChildNotFound("document");
        }

        var before = document.Status;

        if (command.Approve)
        {
            document.Verify(caller.UserId, clock.UtcNow);
        }
        else
        {
            document.Reject(command.RejectionReason!, caller.UserId, clock.UtcNow);
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await audit.RecordAsync(
            new AuditEntry
            {
                Action = AuditAction,
                EntityType = SubmitKycDocumentCommandHandler.AuditEntityType,
                EntityId = document.Id.ToString(),
                Before = new { status = before.ToString() },
                After = new
                {
                    status = document.Status.ToString(),
                    document.RejectionReason,
                    vendorId = command.VendorId,
                },
            },
            cancellationToken).ConfigureAwait(false);

        return Result.Success(KycProjection.ToResponse(document, url: null));
    }
}

/// <summary>Maps a document onto its API shape.</summary>
internal static class KycProjection
{
    /// <summary>Builds the response.</summary>
    /// <param name="document">The document.</param>
    /// <param name="url">A signed link to the scan, when one has been minted.</param>
    public static KycDocumentResponse ToResponse(VendorKycDocument document, string? url)
    {
        ArgumentNullException.ThrowIfNull(document);

        return new KycDocumentResponse(
            document.Id,
            document.VendorId,
            document.DocumentType,
            document.FileId,
            document.NumberMasked,
            document.Status,
            document.RejectionReason,
            document.VerifiedAt,
            document.CreatedAt,
            url);
    }
}
