using Opc.Ua;

namespace Protocols.OpcUa.Nodes;

internal sealed class PlantVariableState : BaseDataVariableState
{
    private volatile DataValue _current;

    public PlantVariableState(NodeState parent, object value, DateTime timestamp) : base(parent)
    {
        _current = new DataValue(new Variant(value), StatusCodes.Good, timestamp);
        DataType = value is double ? DataTypeIds.Double : DataTypeIds.String;
        ValueRank = ValueRanks.Scalar;
        AccessLevel = AccessLevels.CurrentRead;
        UserAccessLevel = AccessLevels.CurrentRead;
        OnReadValue = ReadCurrentValue;
    }

    public bool Update(ISystemContext context, object value, DateTime timestamp)
    {
        if (Equals(_current.Value, value))
            return false;

        // The snapshot worker is the only writer. SDK reads see the value and timestamp together.
        _current = new DataValue(new Variant(value), StatusCodes.Good, timestamp);
        ChangeMasks |= NodeStateChangeMasks.Value;
        ClearChangeMasks(context, includeChildren: false);
        return true;
    }

    private ServiceResult ReadCurrentValue(ISystemContext context, NodeState node, NumericRange indexRange,
        QualifiedName dataEncoding, ref object value, ref StatusCode status, ref DateTime timestamp)
    {
        var current = _current;
        value = current.Value;
        status = current.StatusCode;
        timestamp = current.SourceTimestamp;
        return ApplyIndexRangeAndDataEncoding(context, indexRange, dataEncoding, ref value);
    }
}
