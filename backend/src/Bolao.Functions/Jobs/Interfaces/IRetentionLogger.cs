namespace Bolao.Functions.Jobs;

public interface IRetentionLogger
{
    void Log(RetentionRun run);
}
