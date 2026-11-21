namespace Security.Scenarios;

public sealed record ScenarioDescriptor(
    string Id,
    string Name,
    string Category,
    string Description,
    double DurationSeconds,
    IReadOnlyList<string> ExpectedDetections,
    IReadOnlyList<string> Steps);
