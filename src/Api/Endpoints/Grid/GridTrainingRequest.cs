using Domain.Grid.Snapshots;

namespace Api.Endpoints.Grid;

public sealed record GridTrainingRequest(string Action, string TargetId = "", double? Value = null,
    bool Enabled = true, GridEquipmentFault Fault = GridEquipmentFault.None,
    double? PickupValue = null, double? TimeSetting = null);
