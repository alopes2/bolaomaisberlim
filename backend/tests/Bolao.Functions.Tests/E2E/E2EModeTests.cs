using Bolao.Functions.E2E;
using FluentAssertions;

namespace Bolao.Functions.Tests.E2E;

public class E2EModeTests
{
    [Fact]
    public void RefusesToRunInsideAwsLambda()
    {
        var action = () => E2EMode.EnsureSafe("E2E", "AWS_Lambda_dotnet10");

        action.Should().Throw<InvalidOperationException>()
            .WithMessage("*AWS_EXECUTION_ENV*");
    }

    [Fact]
    public void AllowsLocalE2EEnvironment()
    {
        var action = () => E2EMode.EnsureSafe("E2E", null);

        action.Should().NotThrow();
    }
}
