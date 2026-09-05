using System.Globalization;

namespace KlaraHome.Api;

/// <summary>
/// The container HEALTHCHECK. The runtime image is chiseled: no shell, no curl, nothing to probe
/// with except the application itself (docs/06-infrastructure-devops.md §3).
/// </summary>
internal static class ContainerHealthProbe
{
    private const string DefaultUrl = "http://127.0.0.1:8080/health/live";

    /// <summary>Exit code 0 when the liveness endpoint answers 200, 1 otherwise.</summary>
    public static async Task<int> RunAsync()
    {
        var url = Environment.GetEnvironmentVariable("HEALTHCHECK_URL") ?? DefaultUrl;
        var timeout = TimeSpan.FromSeconds(
            double.TryParse(
                Environment.GetEnvironmentVariable("HEALTHCHECK_TIMEOUT_SECONDS"),
                CultureInfo.InvariantCulture,
                out var seconds)
                ? seconds
                : 5);

        try
        {
            using var client = new HttpClient { Timeout = timeout };
            using var response = await client.GetAsync(new Uri(url)).ConfigureAwait(false);
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            await Console.Error.WriteLineAsync($"healthcheck: {exception.Message}").ConfigureAwait(false);
            return 1;
        }
    }
}
