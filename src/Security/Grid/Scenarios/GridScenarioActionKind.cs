using Domain.Grid.Commands;
using Domain.Grid.Snapshots;
using Application.Identity;

namespace Security.Grid.Scenarios;

/// <summary>What one scripted scenario step does. Targets are resolved at run start; values are scenario parameters.</summary>
public enum GridScenarioActionKind
{
    /// <summary>training.ForceSwitchAsync — direct breaker operation bypassing select-before-operate. <c>Flag</c> = close (default open).</summary>
    ForceSwitch,
    /// <summary>training.ForceTapAsync with an explicit tap position (<c>Value</c>).</summary>
    ForceTap,
    /// <summary>training.ForceTapAsync to the tap limit (<c>Flag</c> = max, default min).</summary>
    ForceTapLimit,
    /// <summary>training.ForceTapAsync back to the definition's initial tap.</summary>
    RestoreTap,
    /// <summary>training.ChangeProtectionAsync. <c>Value</c> = pickup, <c>SecondaryValue</c> = time setting, <c>Flag</c> = enabled.</summary>
    ChangeProtection,
    /// <summary>training.ChangeProtectionAsync with the definition's original settings (cleanup).</summary>
    RestoreProtection,
    /// <summary>training.InjectFaultAsync. <c>Fault</c> = fault type (default short circuit).</summary>
    InjectFault,
    /// <summary>training.ClearFaultAsync.</summary>
    ClearFault,
    /// <summary>training.SpoofMeasurementAsync. <c>Value</c> = reported value; null clears the spoof.</summary>
    SpoofMeasurement,
    /// <summary>training.SetRtuOnlineAsync. <c>Flag</c> = online (default offline).</summary>
    SetRtuOnline,
    /// <summary>control.ExecuteAsync — a supervisory command as an attacker-controlled or careless actor.</summary>
    Command,
    /// <summary>control.ExecuteAsync re-using a previously captured sequence number (replay).</summary>
    ReplayCommand
}
