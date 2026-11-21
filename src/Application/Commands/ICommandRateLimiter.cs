namespace Application.Commands;

public interface ICommandRateLimiter
{
    bool TryAcquire(string identity);
}
