using Api.Development;
using Application.Commands;
using Application.Identity;
using Application.Simulation;
using Application.Telemetry;
using Security.Accounts;
using Security.Authentication;
using Security.Commands;
using Security.Detection;
using Security.Detection.Rules;
using Security.Scenarios;
using Security.Scenarios.Catalog;

namespace Api.Configuration.Registration;

public static class SecurityRegistrationExtensions
{
    public static WebApplicationBuilder AddSecurityServices(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var legacyPolicy = new AccessPolicy(new Dictionary<AccessRole, string>
        {
            [AccessRole.Viewer] = configuration["Security:ViewerApiKey"] ?? "",
            [AccessRole.Operator] = configuration["Security:OperatorApiKey"] ?? configuration["Security:CommandApiKey"] ?? "",
            [AccessRole.Administrator] = configuration["Security:AdminApiKey"] ?? ""
        });

        var namedUsers = configuration.GetSection(ConfigurationSections.Users).GetChildren().Select(section =>
        {
            var username = section["Username"] ?? "";
            if (!Enum.TryParse<AccessRole>(section["Role"], true, out var role) || !Enum.IsDefined(role))
                throw new InvalidOperationException($"Invalid role for user {username}.");
            return new UserCredential(username, role, section["ApiKey"] ?? "", section["Password"], section["PasswordHash"]);
        }).ToArray();
        if (namedUsers.Any(user => user.HasApiKey && legacyPolicy.Authenticate(user.ApiKey) is not null))
            throw new InvalidOperationException("A named user cannot reuse a shared API key.");

        services.AddSingleton(legacyPolicy);
        services.AddSingleton(new UserAccessPolicy(namedUsers));
        services.AddSingleton<IReadOnlyList<UserCredential>>(namedUsers);
        services.AddSingleton<AccountSeeder>();
        services.AddSingleton(configuration.Bind<DevelopmentAccountSettings>(ConfigurationSections.DevelopmentAccounts));
        services.AddSingleton<DevelopmentAccountGenerator>();
        services.AddSingleton(sp => new AuthenticationThrottle(sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<ApiKeyAuthenticator>();

        services.AddSingleton(configuration.Bind<CommandRateOptions>(ConfigurationSections.CommandRate));
        services.AddSingleton<CommandMonitor>();
        services.AddSingleton<ICommandRateLimiter>(sp => sp.GetRequiredService<CommandMonitor>());
        services.AddSingleton<UserAdministrationService>();

        services.AddSingleton<IEnumerable<IDetectionRule>>(sp =>
            DefaultDetectionRules.Create(sp.GetRequiredService<SimulationTopology>(), sp.GetRequiredService<TelemetryHistorian>()));
        services.AddSingleton<DetectionEngine>();
        services.AddSingleton<DetectionRuleAdministrationService>();

        services.AddSingleton<IEnumerable<ScenarioDefinition>>(_ => DefaultScenarios.Create());
        services.AddSingleton<ScenarioServices>();
        services.AddSingleton<ScenarioEngine>();
        services.AddSingleton<IActiveScenarioProvider>(sp => sp.GetRequiredService<ScenarioEngine>());
        return builder;
    }
}
