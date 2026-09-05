using KlaraHome.Infrastructure.Errors;
using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Http;

namespace KlaraHome.UnitTests.Errors;

public sealed class ProblemDetailsMappingTests
{
    [Theory]
    [InlineData(ErrorType.Malformed, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    [InlineData(ErrorType.Gone, 410)]
    [InlineData(ErrorType.Validation, 422)]
    [InlineData(ErrorType.RateLimited, 429)]
    [InlineData(ErrorType.Unexpected, 500)]
    [InlineData(ErrorType.Unavailable, 503)]
    public void Maps_every_error_type_to_the_status_the_api_contract_promises(ErrorType type, int expected)
        => Assert.Equal(expected, ProblemTypes.StatusCodeFor(type));

    [Fact]
    public void Every_error_type_has_its_own_type_uri()
    {
        var uris = Enum.GetValues<ErrorType>().Select(ProblemTypes.TypeUriFor).ToArray();

        Assert.Distinct(uris);
        Assert.All(uris, uri => Assert.StartsWith(ProblemTypes.BaseUri, uri, StringComparison.Ordinal));
    }

    [Fact]
    public void Carries_the_machine_readable_code_the_frontend_switches_on()
    {
        var problem = Error.Conflict("ORDER_NOT_CANCELLABLE", "Already dispatched.").ToProblemDetails(null);

        Assert.Equal(409, problem.Status);
        Assert.Equal("ORDER_NOT_CANCELLABLE", problem.Extensions[ProblemDetailsExtensions.CodeExtension]);
        Assert.Equal("Already dispatched.", problem.Detail);
    }

    [Fact]
    public void Carries_field_errors_only_for_validation_failures()
    {
        var validation = Error
            .Validation(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
            {
                ["lines[2].quantity"] = ["Only 3 units are available."],
            })
            .ToProblemDetails(null);

        Assert.Contains(ProblemDetailsExtensions.ErrorsExtension, validation.Extensions);
        Assert.DoesNotContain(
            ProblemDetailsExtensions.ErrorsExtension,
            Error.NotFound("X", "x").ToProblemDetails(null).Extensions);
    }

    [Fact]
    public void Records_the_request_path_as_the_problem_instance()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/v1/store/checkout/1/place-order";

        var problem = Error.Gone("CHECKOUT_EXPIRED", "Expired.").ToProblemDetails(context);

        Assert.Equal("/api/v1/store/checkout/1/place-order", problem.Instance);
    }

    [Fact]
    public void Rewrites_the_framework_status_registry_uri_so_every_problem_points_at_our_docs()
    {
        var problem = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = 404,
            Type = "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        };

        problem.Enrich(null);

        Assert.Equal(ProblemTypes.TypeUriFor(ErrorType.NotFound), problem.Type);
        Assert.Equal("NOT_FOUND", problem.Extensions[ProblemDetailsExtensions.CodeExtension]);
    }

    [Fact]
    public void Never_leaks_an_exception_message_for_an_unexpected_failure()
        => Assert.Equal("An unexpected error occurred.", Error.Unexpected().Message);
}
