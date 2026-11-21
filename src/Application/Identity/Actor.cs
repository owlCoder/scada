namespace Application.Identity;

/// <summary>The authenticated principal on whose behalf an application operation runs.</summary>
public sealed record Actor(string Name, AccessRole Role)
{
    public static Actor System(string name) => new(name, AccessRole.Administrator);

    public bool HasRole(AccessRole required) => Role >= required;
}
