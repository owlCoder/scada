using Domain.Control;
using Domain.Network;
using Domain.Process.Commands;
using Domain.Process.Snapshots;

namespace Application.Training;

/// <summary>
/// Instructor and scenario hooks that manipulate the simulated environment directly. These model attacks and
/// failures; they never touch real equipment or networks.
/// </summary>
public interface ITrainingEnvironment : IPlantView
{
    CommandResult SpoofSensor(string sensorId, double? value);
    CommandResult InjectFault(FaultInjection fault);
    CommandResult ClearFault(string deviceId);
    CommandResult ForceActuator(string deviceId, double value, string actor);
    CommandResult SetInterlockBypass(bool bypassed, string actor);
    CommandResult SetTankLevel(string tankId, double levelPercent);
    bool SetLinkConnected(string linkId, bool connected);
    bool SetLinkImpairment(string linkId, double latencyMilliseconds, double packetLossPercent);
    bool ForceRegister(string controllerId, RegisterArea area, ushort address, ushort value);
    int ReleaseRegisterForces(string controllerId);
    bool SetControllerRunning(string controllerId, bool running);
    bool ResetController(string controllerId);
    BlockedConnectionAttempt? AttemptConnection(string sourceNode, string sourceSegmentId, string targetNode, string targetSegmentId);
    bool TamperHistorian(string tagId, double value);
    int RestoreDefaults();
}
