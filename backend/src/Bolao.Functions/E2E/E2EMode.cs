namespace Bolao.Functions.E2E;

public static class E2EMode
{
    public static void EnsureSafe(string? environmentName, string? awsExecutionEnvironment)
    {
        if (string.Equals(environmentName, "E2E", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(awsExecutionEnvironment))
        {
            throw new InvalidOperationException(
                "E2E mode cannot start when AWS_EXECUTION_ENV is present.");
        }
    }
}
