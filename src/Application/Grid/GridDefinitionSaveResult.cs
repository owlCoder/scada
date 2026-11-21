namespace Application.Grid;

public sealed record GridDefinitionSaveResult(bool Saved, IReadOnlyList<string> Errors, int? Version);
