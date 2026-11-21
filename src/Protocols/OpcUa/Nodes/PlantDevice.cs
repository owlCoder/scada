namespace Protocols.OpcUa.Nodes;

internal sealed record PlantDevice(string Folder, string Id, string Name, IReadOnlyDictionary<string, object> Values);
