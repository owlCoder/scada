namespace Security.Scenarios;

/// <summary>
/// <see cref="Succeeded"/> means the scripted step was carried out. An attack step whose request is rejected by
/// the platform (for example an unauthorised command) still succeeds; <see cref="Detail"/> records the response.
/// </summary>
public sealed record ScenarioActionResult(bool Succeeded, string Detail)
{
    public static ScenarioActionResult Done(string detail) => new(true, detail);

    public static ScenarioActionResult Failed(string detail) => new(false, detail);
}
