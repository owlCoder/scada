using Domain.Control;

namespace Application.Simulation;

public sealed record SupervisoryStatusChange(string ControllerId, CommunicationState Previous, CommunicationState Current);
