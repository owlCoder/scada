namespace Api.Configuration.Registration;

/// <summary>Resolves all registrations of <typeparamref name="T"/> when enumerated, which breaks constructor-time cycles.</summary>
public sealed class DeferredServices<T>(IServiceProvider services) : IEnumerable<T>
{
    public IEnumerator<T> GetEnumerator() => services.GetServices<T>().GetEnumerator();

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
