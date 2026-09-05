using KlaraHome.Infrastructure.Errors;
using KlaraHome.SharedKernel.Results;
using Microsoft.AspNetCore.Http;

namespace KlaraHome.Infrastructure.Http;

/// <summary>
/// The one way a <see cref="Result{TValue}"/> becomes an HTTP response, so no endpoint invents its
/// own mapping and no failure escapes the RFC 9457 contract.
/// </summary>
public static class ResultResults
{
    /// <summary>200 with the value, or the error's problem document.</summary>
    /// <typeparam name="TValue">The success value type.</typeparam>
    /// <param name="result">The handler's result.</param>
    /// <param name="context">The request, for the instance path and correlation id.</param>
    public static IResult ToOk<TValue>(this Result<TValue> result, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.Match(Results.Ok, error => error.ToProblemResult(context));
    }

    /// <summary>204 on success, or the error's problem document.</summary>
    /// <param name="result">The handler's result.</param>
    /// <param name="context">The request, for the instance path and correlation id.</param>
    public static IResult ToNoContent(this Result result, HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemResult(context);
    }
}
