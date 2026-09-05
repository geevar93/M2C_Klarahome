using System.Collections.Concurrent;
using System.Reflection;
using KlaraHome.SharedKernel.Results;

namespace KlaraHome.Infrastructure.Messaging;

/// <summary>
/// Builds a failed <c>Result</c> or <c>Result&lt;T&gt;</c> when only the closed response type is
/// known — needed by open-generic pipeline behaviours, which cannot name <c>T</c>.
/// </summary>
internal static class ResultFactory
{
    private static readonly ConcurrentDictionary<Type, MethodInfo> GenericFailure = new();

    private static readonly MethodInfo FailureDefinition = typeof(Result)
        .GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(method => method is { Name: nameof(Result.Failure), IsGenericMethodDefinition: true });

    public static TResponse Failure<TResponse>(Error error)
        where TResponse : Result
    {
        if (typeof(TResponse) == typeof(Result))
        {
            return (TResponse)Result.Failure(error);
        }

        var closed = GenericFailure.GetOrAdd(
            typeof(TResponse),
            static type => FailureDefinition.MakeGenericMethod(type.GetGenericArguments()[0]));

        return (TResponse)closed.Invoke(null, [error])!;
    }
}
