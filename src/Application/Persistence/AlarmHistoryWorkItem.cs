using Application.Alarms;

namespace Application.Persistence;

public sealed record AlarmHistoryWorkItem(AlarmHistoryRecord Record) : PersistenceWorkItem("alarm");
