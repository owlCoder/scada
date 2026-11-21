using Application.Detection;
using Domain.Incidents;

namespace Security.Detection;

public sealed record RuleDescriptor(string Id, string Name, string Description, IncidentCategory Category, DetectionRuleSettings Settings);
