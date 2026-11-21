using Application.Events;

namespace Application.Persistence;

public sealed record SecurityEventWorkItem(SecurityEvent Event) : PersistenceWorkItem("security-event");
