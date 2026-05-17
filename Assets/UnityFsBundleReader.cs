using K4os.Compression.LZ4;

namespace derbyhubDb.Assets;

public sealed record UnityFsBundleFile(string Path, byte[] Data)
{
    public string FileName => System.IO.Path.GetFileName(Path);
}

public static class UnityFsBundleReader
{
    private const uint BlocksInfoAtTheEnd = 0x80;
    private const uint BlockInfoNeedPaddingAtStart = 0x200;

    public static IReadOnlyList<UnityFsBundleFile> ReadFiles(byte[] bundleBytes)
    {
        using var input = new MemoryStream(bundleBytes, writable: false);
        using var reader = new BigEndianBinaryReader(input);

        var signature = reader.ReadNullTerminatedString();
        if (!signature.Equals("UnityFS", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"不支持的 AssetBundle 签名: {signature}");
        }

        var version = reader.ReadUInt32();
        _ = reader.ReadNullTerminatedString();
        _ = reader.ReadNullTerminatedString();
        _ = reader.ReadInt64();
        var compressedBlocksInfoSize = reader.ReadUInt32();
        var uncompressedBlocksInfoSize = reader.ReadUInt32();
        var flags = reader.ReadUInt32();

        if (version >= 7)
        {
            Align(input, 16);
        }

        var blocksInfoPosition = input.Position;
        byte[] compressedBlocksInfo;
        if ((flags & BlocksInfoAtTheEnd) != 0)
        {
            input.Position = input.Length - compressedBlocksInfoSize;
            compressedBlocksInfo = reader.ReadBytesChecked((int)compressedBlocksInfoSize);
            input.Position = blocksInfoPosition;
        }
        else
        {
            compressedBlocksInfo = reader.ReadBytesChecked((int)compressedBlocksInfoSize);
        }

        var blocksInfo = Decompress(compressedBlocksInfo, uncompressedBlocksInfoSize, flags);
        var (blocks, nodes) = ReadBlocksAndNodes(blocksInfo);

        if ((flags & BlockInfoNeedPaddingAtStart) != 0)
        {
            Align(input, 16);
        }

        using var data = new MemoryStream();
        foreach (var block in blocks)
        {
            var compressed = reader.ReadBytesChecked((int)block.CompressedSize);
            var decompressed = Decompress(compressed, block.UncompressedSize, block.Flags);
            data.Write(decompressed);
        }

        var result = new List<UnityFsBundleFile>(nodes.Count);
        foreach (var node in nodes)
        {
            if (node.Size > int.MaxValue)
            {
                throw new InvalidDataException($"Bundle 内文件过大: {node.Path}");
            }

            var bytes = new byte[node.Size];
            data.Position = node.Offset;
            var read = data.Read(bytes, 0, bytes.Length);
            if (read != bytes.Length)
            {
                throw new EndOfStreamException($"读取 bundle 内文件失败: {node.Path}");
            }

            result.Add(new UnityFsBundleFile(node.Path, bytes));
        }

        return result;
    }

    private static (List<StorageBlock> Blocks, List<DirectoryNode> Nodes) ReadBlocksAndNodes(byte[] blocksInfo)
    {
        using var stream = new MemoryStream(blocksInfo, writable: false);
        using var reader = new BigEndianBinaryReader(stream);
        _ = reader.ReadBytesChecked(16);

        var blockCount = reader.ReadInt32();
        var blocks = new List<StorageBlock>(blockCount);
        for (var i = 0; i < blockCount; i++)
        {
            blocks.Add(new StorageBlock(
                reader.ReadUInt32(),
                reader.ReadUInt32(),
                reader.ReadUInt16()));
        }

        var nodeCount = reader.ReadInt32();
        var nodes = new List<DirectoryNode>(nodeCount);
        for (var i = 0; i < nodeCount; i++)
        {
            nodes.Add(new DirectoryNode(
                reader.ReadInt64(),
                reader.ReadInt64(),
                reader.ReadUInt32(),
                reader.ReadNullTerminatedString()));
        }

        return (blocks, nodes);
    }

    private static byte[] Decompress(byte[] source, uint uncompressedSize, uint flags)
    {
        var compression = flags & 0x3f;
        return compression switch
        {
            0 => source,
            2 or 3 => DecompressLz4(source, uncompressedSize),
            _ => throw new InvalidDataException($"暂不支持的 UnityFS 压缩类型: {compression}")
        };
    }

    private static byte[] DecompressLz4(byte[] source, uint uncompressedSize)
    {
        if (uncompressedSize > int.MaxValue)
        {
            throw new InvalidDataException("UnityFS LZ4 block 过大");
        }

        var target = new byte[uncompressedSize];
        var written = LZ4Codec.Decode(source, target);
        if (written != target.Length)
        {
            throw new InvalidDataException($"UnityFS LZ4 解压长度不匹配: {written}/{target.Length}");
        }

        return target;
    }

    private static void Align(Stream stream, int alignment)
    {
        var mod = stream.Position % alignment;
        if (mod != 0)
        {
            stream.Position += alignment - mod;
        }
    }

    private sealed record StorageBlock(uint UncompressedSize, uint CompressedSize, uint Flags);
    private sealed record DirectoryNode(long Offset, long Size, uint Flags, string Path);
}

internal sealed class BigEndianBinaryReader : IDisposable
{
    private readonly Stream _stream;
    private readonly byte[] _buffer = new byte[8];

    public BigEndianBinaryReader(Stream stream)
    {
        _stream = stream;
    }

    public uint ReadUInt32()
    {
        ReadExactly(_buffer, 0, 4);
        return ((uint)_buffer[0] << 24) | ((uint)_buffer[1] << 16) | ((uint)_buffer[2] << 8) | _buffer[3];
    }

    public int ReadInt32() => unchecked((int)ReadUInt32());

    public ushort ReadUInt16()
    {
        ReadExactly(_buffer, 0, 2);
        return (ushort)((_buffer[0] << 8) | _buffer[1]);
    }

    public long ReadInt64()
    {
        ReadExactly(_buffer, 0, 8);
        var value =
            ((ulong)_buffer[0] << 56) |
            ((ulong)_buffer[1] << 48) |
            ((ulong)_buffer[2] << 40) |
            ((ulong)_buffer[3] << 32) |
            ((ulong)_buffer[4] << 24) |
            ((ulong)_buffer[5] << 16) |
            ((ulong)_buffer[6] << 8) |
            _buffer[7];
        return unchecked((long)value);
    }

    public byte[] ReadBytesChecked(int count)
    {
        var bytes = new byte[count];
        ReadExactly(bytes, 0, count);
        return bytes;
    }

    public string ReadNullTerminatedString()
    {
        using var bytes = new MemoryStream();
        while (true)
        {
            var value = _stream.ReadByte();
            if (value < 0)
            {
                throw new EndOfStreamException("读取字符串时到达流末尾");
            }

            if (value == 0)
            {
                return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
            }

            bytes.WriteByte((byte)value);
        }
    }

    private void ReadExactly(byte[] buffer, int offset, int count)
    {
        while (count > 0)
        {
            var read = _stream.Read(buffer, offset, count);
            if (read == 0)
            {
                throw new EndOfStreamException();
            }

            offset += read;
            count -= read;
        }
    }

    public void Dispose()
    {
    }
}
