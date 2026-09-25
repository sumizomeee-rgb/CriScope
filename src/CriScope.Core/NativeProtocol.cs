using System.Buffers.Binary;
using System.Text;

namespace CriScope.Core;

public sealed record NativeParameter(int Id, string Name, object Value);
public sealed record NativePacket(ushort Command, byte PointerSize, byte LogType, ulong TimeMicroseconds,
    ushort FunctionId, string Function, uint Control, IReadOnlyList<NativeParameter> Parameters)
{
    public string PayloadHex { get; init; } = "";
}

/// <summary>Strict, independently implemented big-endian CRI monitor framing and scalar decoder.</summary>
public static class NativeProtocol
{
    public const int MaximumFrameSize = 8 * 1024 * 1024;
    public static int ReadFrameLength(ReadOnlySpan<byte> prefix)
    {
        if (prefix.Length < 4) throw new InvalidDataException("Truncated native frame length");
        uint size = BinaryPrimitives.ReadUInt32BigEndian(prefix);
        if (size < 28 || size > MaximumFrameSize) throw new InvalidDataException($"Invalid native frame length: {size}");
        return (int)size;
    }

    public static NativePacket Decode(ReadOnlySpan<byte> frame)
    {
        if (ReadFrameLength(frame) != frame.Length) throw new InvalidDataException("Native frame length mismatch");
        byte pointer = frame[6];
        if (pointer is not (4 or 8)) throw new InvalidDataException($"Unsupported pointer width: {pointer}");
        int header = 24 + pointer;
        int padding = BinaryPrimitives.ReadUInt16BigEndian(frame[18..]);
        if (frame.Length < header || padding > 7 || header + padding > frame.Length)
            throw new InvalidDataException("Invalid native header or padding");
        var command = BinaryPrimitives.ReadUInt16BigEndian(frame[4..]);
        var function = BinaryPrimitives.ReadUInt16BigEndian(frame[16..]);
        var parameters = new List<NativeParameter>();
        var reader = new Reader(frame.Slice(header, frame.Length - header - padding));
        if (command == 31)
        {
            while (reader.Remaining > 0)
            {
                var id = reader.U16();
                if (id >= NativeSchema.Parameters.Length) throw new InvalidDataException($"Unknown native parameter {id} in function {function}");
                var schema = NativeSchema.Parameters[id];
                object value = schema.Type switch
                {
                    "INT8" => reader.U8(), "INT16" => reader.U16(), "INT32" => reader.U32(), "INT64" => reader.U64(),
                    "FLOAT32" => reader.Float(), "VECTOR" => new[] { reader.Float(), reader.Float(), reader.Float() },
                    "CHAR" => Encoding.UTF8.GetString(reader.Bytes(reader.U16())).TrimEnd('\0'),
                    "UINTPTR" => "0x" + (pointer == 8 ? reader.U64() : reader.U32()).ToString("x"),
                    "GUID" => Convert.ToHexString(reader.Bytes(16)), "128" => Convert.ToHexString(reader.Bytes(128)),
                    _ => throw new InvalidDataException($"Unsupported native parameter encoding {schema.Type} ({id}/{schema.Name}); packet not interpreted")
                };
                parameters.Add(new(id, schema.Name, value));
            }
        }
        return new(command, pointer, frame[7], BinaryPrimitives.ReadUInt64BigEndian(frame[8..]), function,
            NativeSchema.Functions.GetValueOrDefault(function, $"UnknownFunction:{function}"),
            BinaryPrimitives.ReadUInt32BigEndian(frame[20..]), parameters)
        { PayloadHex = command == 31 ? "" : Convert.ToHexString(frame[header..]) };
    }

    public static byte[] Command(ushort command)
    {
        var result = new byte[command == 22 ? 40 : 32];
        BinaryPrimitives.WriteUInt32BigEndian(result, (uint)result.Length);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(4), command);
        result[6] = 8;
        if (command == 111) BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(20), 0x01000000);
        if (command == 22)
        {
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(18), 2);
            BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(32), 129);
            BinaryPrimitives.WriteUInt32BigEndian(result.AsSpan(34), uint.MaxValue);
        }
        return result;
    }

    private ref struct Reader(ReadOnlySpan<byte> bytes)
    {
        private ReadOnlySpan<byte> remaining = bytes;
        public int Remaining => remaining.Length;
        public ReadOnlySpan<byte> Bytes(int count)
        {
            if (count > remaining.Length) throw new InvalidDataException("Native parameter exceeds packet boundary");
            var value = remaining[..count]; remaining = remaining[count..]; return value;
        }
        public byte U8() => Bytes(1)[0];
        public ushort U16() => BinaryPrimitives.ReadUInt16BigEndian(Bytes(2));
        public uint U32() => BinaryPrimitives.ReadUInt32BigEndian(Bytes(4));
        public ulong U64() => BinaryPrimitives.ReadUInt64BigEndian(Bytes(8));
        public float Float()
        {
            float value = BitConverter.Int32BitsToSingle((int)U32());
            if (!float.IsFinite(value)) throw new InvalidDataException("Non-finite native float; packet retained as diagnostic, not converted to zero");
            return value;
        }
    }
}
