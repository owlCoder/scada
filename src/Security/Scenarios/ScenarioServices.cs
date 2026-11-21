using Application.Alarms;
using Application.Commands;
using Application.Registers;
using Application.Training;
using Security.Accounts;

namespace Security.Scenarios;

/// <summary>The application services that scenario actions are allowed to use.</summary>
public sealed record ScenarioServices(
    ITrainingEnvironment Training,
    ControlCommandService Commands,
    CommandReplayGuard ReplayGuard,
    RegisterAccessService Registers,
    AlarmService Alarms,
    UserAdministrationService Accounts);
