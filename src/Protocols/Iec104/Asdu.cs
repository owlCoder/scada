namespace Protocols.Iec104;

/// <summary>
/// One application service data unit: the 6-octet header plus the raw payload. <see cref="Objects"/> holds the parsed
/// information objects when the type is known and the payload length matches; for unknown types the payload is kept
/// raw so it can be echoed back with COT 44.
/// </summary>
public sealed record Asdu(AsduHeader Header, byte[] Payload, IReadOnlyList<Iec104InformationObject> Objects)
{
    public const int MaxPayload = Apdu.MaxLength - Apci.Size - AsduHeader.Size;

    public static Asdu Build(AsduHeader header, IReadOnlyList<Iec104InformationObject> objects)
    {
        ArgumentNullException.ThrowIfNull(objects);
        if (objects.Count is < 1 or > AsduHeader.MaxElements)
            throw new ArgumentOutOfRangeException(nameof(objects), "An ASDU carries 1 to 127 information objects.");
        var payload = SerializePayload(header.SingleStructure, objects);
        if (payload.Length > MaxPayload)
            throw new ArgumentOutOfRangeException(nameof(objects), "ASDU payload does not fit into one APDU.");
        return new Asdu(header with { ElementCount = objects.Count }, payload, objects);
    }

    public static Asdu? TryParse(ReadOnlySpan<byte> data)
    {
        var header = AsduHeader.TryParse(data);
        if (header is null)
            return null;
        var payload = data.Slice(AsduHeader.Size).ToArray();
        var size = Iec104ElementCodec.DataSize(header.Value.Type);
        if (size is null)
            return new Asdu(header.Value, payload, []);
        var expected = header.Value.ElementCount * (header.Value.SingleStructure ? size.Value : size.Value + Iec104InformationObject.AddressSize);
        if (payload.Length != expected)
            return null;
        var objects = new List<Iec104InformationObject>(header.Value.ElementCount);
        var offset = 0;
        for (var i = 0; i < header.Value.ElementCount; i++)
        {
            int ioa;
            if (header.Value.SingleStructure)
            {
                ioa = i;
                objects.Add(new Iec104InformationObject(ioa, payload.AsSpan(offset, size.Value).ToArray()));
            }
            else
            {
                ioa = Iec104InformationObject.ReadAddress(payload.AsSpan(offset));
                objects.Add(new Iec104InformationObject(ioa, payload.AsSpan(offset + Iec104InformationObject.AddressSize, size.Value).ToArray()));
            }

            offset += header.Value.SingleStructure ? size.Value : size.Value + Iec104InformationObject.AddressSize;
        }

        return new Asdu(header.Value, payload, objects);
    }

    /// <summary>The full ASDU: 6-octet header followed by the payload.</summary>
    public byte[] Serialize()
    {
        var header = Header.Serialize();
        var payload = SerializePayload(Header.SingleStructure, Objects);
        var result = new byte[header.Length + payload.Length];
        header.CopyTo(result, 0);
        payload.CopyTo(result, header.Length);
        return result;
    }

    private static byte[] SerializePayload(bool singleStructure, IReadOnlyList<Iec104InformationObject> objects)
    {
        var payload = new byte[objects.Sum(o => singleStructure ? o.Data.Length : o.Data.Length + Iec104InformationObject.AddressSize)];
        var offset = 0;
        foreach (var item in objects)
        {
            if (!singleStructure)
            {
                Iec104InformationObject.WriteAddressTo(item.InformationObjectAddress, payload.AsSpan(offset));
                offset += Iec104InformationObject.AddressSize;
            }

            item.Data.CopyTo(payload, offset);
            offset += item.Data.Length;
        }

        return payload;
    }
}
