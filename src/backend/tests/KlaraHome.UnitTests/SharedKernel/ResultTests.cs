using KlaraHome.SharedKernel.Results;

namespace KlaraHome.UnitTests.SharedKernel;

public sealed class ResultTests
{
    [Fact]
    public void Success_carries_its_value()
    {
        var result = Result.Success(42);

        Assert.True(result.IsSuccess);
        Assert.False(result.IsFailure);
        Assert.Equal(42, result.Value);
        Assert.Equal(Error.None, result.Error);
    }

    [Fact]
    public void Reading_the_value_of_a_failure_throws_rather_than_returning_a_default()
    {
        var result = Result.Failure<int>(Error.NotFound("X_NOT_FOUND", "Gone."));

        Assert.Throws<InvalidOperationException>(() => result.Value);
    }

    [Fact]
    public void A_failure_must_carry_an_error()
        => Assert.ThrowsAny<ArgumentException>(() => Result.Failure(Error.None));

    [Fact]
    public void Match_collapses_both_branches()
    {
        Assert.Equal("ok", Result.Success("ok").Match(value => value, error => error.Code));
        Assert.Equal(
            "BUSY",
            Result.Failure<string>(Error.Conflict("BUSY", "Busy.")).Match(value => value, error => error.Code));
    }

    [Fact]
    public void An_error_converts_implicitly_into_a_failed_result()
    {
        Result<int> result = Error.Validation("BAD", "Bad.");

        Assert.True(result.IsFailure);
        Assert.Equal("BAD", result.Error.Code);
    }

    [Fact]
    public void Field_errors_are_empty_for_every_error_type_except_validation()
    {
        Assert.Empty(Error.Conflict("C", "c").FieldErrors);

        var validation = Error.Validation(new Dictionary<string, IReadOnlyList<string>> { ["a"] = ["bad"] });
        Assert.Contains("a", validation.FieldErrors);
    }
}
