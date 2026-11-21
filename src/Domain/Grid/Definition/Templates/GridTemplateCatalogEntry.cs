namespace Domain.Grid.Definition.Templates;

/// <summary>One built-in starting point for the network editor: an id, a display name, a description and a factory.</summary>
public sealed record GridTemplateCatalogEntry(string Id, string Name, string Description, Func<GridDefinition> Factory);
