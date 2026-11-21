namespace Application.Identity;

public interface IUserAccountStore
{
    Task<UserAccount?> FindAsync(string username, CancellationToken cancellationToken);
    Task<IReadOnlyList<UserAccount>> ListAsync(CancellationToken cancellationToken);
    Task<bool> UpsertAsync(UserAccount account, CancellationToken cancellationToken);
}
