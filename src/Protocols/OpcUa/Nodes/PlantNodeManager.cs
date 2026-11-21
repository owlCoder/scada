using Application.Commands;
using Application.Simulation;
using Domain.Control;
using Domain.Process.Snapshots;
using Opc.Ua;
using Opc.Ua.Server;

namespace Protocols.OpcUa.Nodes;

internal sealed class PlantNodeManager(
    IServerInternal server, ApplicationConfiguration configuration, SimulationEngine simulation,
    ControlCommandService commands, TimeProvider time)
    : CustomNodeManager2(server, configuration, NamespaceUri)
{
    public const string NamespaceUri = "urn:scada-training:plant";
    private readonly Dictionary<(string Folder, string Device, string Field), PlantVariableState> _variables = [];

    public override void CreateAddressSpace(IDictionary<NodeId, IList<IReference>> externalReferences)
    {
        var (plant, controllers) = simulation.Read(world =>
            (world.Plant.CaptureSnapshot(), world.Controllers.Select(controller => controller.Snapshot()).ToArray()));
        var timestamp = time.GetUtcNow().UtcDateTime;
        var root = CreateFolder(null, "Plant", "Plant");
        root.AddReference(ReferenceTypeIds.Organizes, true, ObjectIds.ObjectsFolder);
        if (!externalReferences.TryGetValue(ObjectIds.ObjectsFolder, out var references))
            externalReferences[ObjectIds.ObjectsFolder] = references = [];
        references.Add(new NodeStateReference(ReferenceTypeIds.Organizes, false, root.NodeId));

        var folders = PlantNodeValues.Folders.ToDictionary(name => name, name => CreateFolder(root, name, $"Plant/{name}"));
        foreach (var device in PlantNodeValues.Map(plant, controllers))
            AddDevice(folders[device.Folder], device, timestamp);

        AddPredefinedNode(SystemContext, root);
    }

    public int Update(PlantSnapshot plant, IReadOnlyList<ControllerSnapshot> controllers, DateTime timestamp)
    {
        var changed = 0;
        foreach (var device in PlantNodeValues.Map(plant, controllers))
        foreach (var (field, value) in device.Values)
        {
            if (_variables[(device.Folder, device.Id, field)].Update(SystemContext, value, timestamp))
                changed++;
        }

        return changed;
    }

    private FolderState CreateFolder(NodeState? parent, string name, string path)
    {
        var folder = new FolderState(parent)
        {
            NodeId = new NodeId(path, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = name,
            TypeDefinitionId = ObjectTypeIds.FolderType,
            ReferenceTypeId = ReferenceTypeIds.Organizes
        };
        parent?.AddChild(folder);
        return folder;
    }

    private BaseObjectState AddDevice(FolderState folder, PlantDevice device, DateTime timestamp)
    {
        var path = $"Plant/{device.Folder}/{Uri.EscapeDataString(device.Id)}";
        var node = new BaseObjectState(folder)
        {
            NodeId = new NodeId(path, NamespaceIndex),
            BrowseName = new QualifiedName(device.Id, NamespaceIndex),
            DisplayName = device.Name,
            TypeDefinitionId = ObjectTypeIds.BaseObjectType,
            ReferenceTypeId = ReferenceTypeIds.Organizes
        };
        folder.AddChild(node);
        foreach (var (field, value) in device.Values)
        {
            var variable = new PlantVariableState(node, value, timestamp)
            {
                NodeId = new NodeId($"{path}/{field}", NamespaceIndex),
                BrowseName = new QualifiedName(field, NamespaceIndex),
                DisplayName = field,
                ReferenceTypeId = ReferenceTypeIds.HasComponent,
                TypeDefinitionId = VariableTypeIds.BaseDataVariableType
            };
            node.AddChild(variable);
            _variables.Add((device.Folder, device.Id, field), variable);
        }

        if (device.Folder is "Pumps" or "Valves")
        {
            AddMethod(node, device.Id, OperatorCommandType.SetManual);
            AddMethod(node, device.Id, OperatorCommandType.SetAutomatic);
            AddMethod(node, device.Id, OperatorCommandType.ResetInterlock);
        }
        if (device.Folder == "Pumps")
        {
            AddMethod(node, device.Id, OperatorCommandType.StartPump);
            AddMethod(node, device.Id, OperatorCommandType.StopPump);
        }
        if (device.Folder == "Valves")
            AddMethod(node, device.Id, OperatorCommandType.SetValvePosition);
        return node;
    }

    private MethodState AddMethod(BaseObjectState parent, string deviceId, OperatorCommandType command)
    {
        var name = command.ToString();
        var path = $"{parent.NodeId.Identifier}/{name}";
        var method = new PlantCommandMethod(parent, deviceId, command, commands)
        {
            NodeId = new NodeId(path, NamespaceIndex),
            BrowseName = new QualifiedName(name, NamespaceIndex),
            DisplayName = name,
            ReferenceTypeId = ReferenceTypeIds.HasComponent,
            Executable = true
        };
        method.OnReadUserExecutable = method.ReadUserExecutable;
        method.OnCallMethod = method.Invoke;
        if (command == OperatorCommandType.SetValvePosition)
        {
            method.InputArguments = new PropertyState<Argument[]>(method)
            {
                NodeId = new NodeId($"{path}/InputArguments", NamespaceIndex),
                BrowseName = BrowseNames.InputArguments,
                DisplayName = BrowseNames.InputArguments,
                ReferenceTypeId = ReferenceTypeIds.HasProperty,
                TypeDefinitionId = VariableTypeIds.PropertyType,
                DataType = DataTypeIds.Argument,
                ValueRank = ValueRanks.OneDimension,
                AccessLevel = AccessLevels.CurrentRead,
                UserAccessLevel = AccessLevels.CurrentRead,
                Value = [new Argument { Name = "Position", Description = "Valve position in percent (0 to 100)",
                    DataType = DataTypeIds.Double, ValueRank = ValueRanks.Scalar }]
            };
        }

        parent.AddChild(method);
        return method;
    }
}
