using Security;

namespace Api.Configuration.Registration;

public static class SecurityRegistrationExtensions
{
    public static WebApplicationBuilder AddSecurityServices(this WebApplicationBuilder builder)
    {
        var legacyPolicy = new AccessPolicy(new Dictionary<AccessRole, string>
        {
            [AccessRole.Viewer] = builder.Configuration["Security:ViewerApiKey"] ?? "",
            [AccessRole.Operator] = builder.Configuration["Security:OperatorApiKey"]
                ?? builder.Configuration["Security:CommandApiKey"] ?? "",
            [AccessRole.Administrator] = builder.Configuration["Security:AdminApiKey"] ?? ""
        });
        builder.Services.AddSingleton(legacyPolicy);

        var namedUsers = builder.Configuration.GetSection("Security:Users").GetChildren().Select(section =>
        {
            var username = section["Username"] ?? "";
            var key = section["ApiKey"] ?? "";
            if (!Enum.TryParse<AccessRole>(section["Role"], true, out var role) ||
                !Enum.IsDefined(role))
                throw new InvalidOperationException($"Invalid role for user {username}.");
            return new UserCredential(username, role, key);
        }).ToArray();

        if (namedUsers.Any(user => legacyPolicy.Authenticate(user.ApiKey) is not null))
            throw new InvalidOperationException("A named user cannot reuse a shared API key.");

        builder.Services.AddSingleton(new UserAccessPolicy(namedUsers));
        builder.Services.AddSingleton<AuthorizationService>();
        return builder;
    }
}
