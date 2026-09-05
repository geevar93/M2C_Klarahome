using KlaraHome.Infrastructure.Persistence.Seeding;
using KlaraHome.Modules.Platform.Domain;
using KlaraHome.Modules.Platform.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace KlaraHome.Modules.Platform.Infrastructure.Seeding;

/// <summary>
/// Imports the India Post PIN code dataset from a file an operator supplies.
/// </summary>
/// <remarks>
/// <para>
/// The dataset is roughly nineteen thousand rows and it changes without notice. Compiling it into
/// the product would mean shipping a release to correct a district name, and a stale copy inside a
/// container is worse than an empty table an operator knows to fill — so the file is mounted, not
/// built in. <c>Platform:PincodeDataPath</c> points at it.
/// </para>
/// <para>
/// No file configured, or no file at the configured path, is not an error: it is the state every
/// deployment starts in, and the <c>platform.pincode-lookup</c> feature flag exists so the lookup
/// endpoint can stay off until the import has run.
/// </para>
/// <para>
/// The format is a header row followed by <c>pincode,city,district,stateCode,zone</c>. Rows whose
/// state code is not a jurisdiction we know are counted and skipped rather than failing the import:
/// one bad row in nineteen thousand must not stop a deploy.
/// </para>
/// </remarks>
/// <param name="context">The Platform module's context.</param>
/// <param name="options">Supplies the dataset path.</param>
/// <param name="logger">Reports what was imported, so a deploy log answers "did the data land".</param>
internal sealed partial class PincodeSeeder(
    PlatformDbContext context,
    IOptions<PlatformOptions> options,
    ILogger<PincodeSeeder> logger) : IDataSeeder
{
    /// <summary>Columns expected in the CSV, in order.</summary>
    private const int ExpectedColumns = 5;

    /// <inheritdoc />
    public string Name => "Platform.Pincodes";

    /// <inheritdoc />
    public int Order => 50;

    /// <inheritdoc />
    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var path = options.Value.PincodeDataPath;

        if (string.IsNullOrWhiteSpace(path))
        {
            PincodeImportSkipped(logger, "no Platform:PincodeDataPath is configured", "<none>");
            return;
        }

        if (!File.Exists(path))
        {
            PincodeImportSkipped(logger, "no file at the configured path", path);
            return;
        }

        var states = await context.States
            .AsNoTracking()
            .ToDictionaryAsync(state => state.Code, state => state.Id, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var existing = await context.Pincodes
            .ToDictionaryAsync(pincode => pincode.Code, StringComparer.Ordinal, cancellationToken)
            .ConfigureAwait(false);

        var imported = 0;
        var skipped = 0;
        var lineNumber = 0;

        using var reader = new StreamReader(path);

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            lineNumber++;

            // The header, and any blank line the file happens to carry.
            if (lineNumber == 1 || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!TryParse(line, states, out var code, out var city, out var district, out var stateId, out var zone))
            {
                skipped++;
                continue;
            }

            if (existing.TryGetValue(code, out var row))
            {
                row.Update(city, district, stateId, zone);
            }
            else
            {
                context.Pincodes.Add(Pincode.Define(code, city, district, stateId, zone));
            }

            imported++;
        }

        await context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        PincodeImportCompleted(logger, imported, skipped, path);
    }

    private static bool TryParse(
        string line,
        Dictionary<string, Guid> states,
        out string code,
        out string city,
        out string district,
        out Guid stateId,
        out string zone)
    {
        code = city = district = zone = string.Empty;
        stateId = Guid.Empty;

        var columns = line.Split(',');
        if (columns.Length < ExpectedColumns)
        {
            return false;
        }

        code = columns[0].Trim();
        city = columns[1].Trim();
        district = columns[2].Trim();
        zone = columns[4].Trim();

        var stateCode = columns[3].Trim();

        if (code.Length != 6 || !code.All(char.IsAsciiDigit) || code[0] == '0' || city.Length == 0)
        {
            return false;
        }

        // Padded so a file that dropped the leading zero on Delhi still resolves.
        return states.TryGetValue(stateCode.PadLeft(2, '0'), out stateId);
    }

    [LoggerMessage(EventId = 1400, Level = LogLevel.Information,
        Message = "PIN code import skipped ({DataPath}): {SkipReason}. The pincode-lookup feature flag "
                  + "should stay off until the dataset is imported.")]
    private static partial void PincodeImportSkipped(ILogger logger, string skipReason, string dataPath);

    [LoggerMessage(EventId = 1401, Level = LogLevel.Information,
        Message = "PIN code import complete: {ImportedCount} imported, {SkippedCount} skipped, from {DataPath}")]
    private static partial void PincodeImportCompleted(
        ILogger logger,
        int importedCount,
        int skippedCount,
        string dataPath);
}
