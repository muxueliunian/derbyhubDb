namespace derbyhubDb.Assets;

public static class UmaViewerAssetBundleCrypto
{
    private static readonly byte[] AssetBundleBaseKey =
    [
        0x53, 0x2B, 0x46, 0x31, 0xE4, 0xA7, 0xB9, 0x47, 0x3E, 0x7C, 0xFB
    ];

    public static byte[] ReadAndDecryptIfNeeded(string path, long? encryptionKey)
    {
        var bytes = File.ReadAllBytes(path);
        if (encryptionKey is null or 0 || bytes.Length <= 256)
        {
            return bytes;
        }

        var key = BuildFileKey(encryptionKey.Value);
        for (var i = 256; i < bytes.Length; i++)
        {
            bytes[i] ^= key[i % key.Length];
        }

        return bytes;
    }

    private static byte[] BuildFileKey(long encryptionKey)
    {
        var keyBytes = BitConverter.GetBytes(encryptionKey);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(keyBytes);
        }

        var result = new byte[AssetBundleBaseKey.Length * 8];
        for (var i = 0; i < AssetBundleBaseKey.Length; i++)
        {
            var offset = i * 8;
            for (var j = 0; j < 8; j++)
            {
                result[offset + j] = (byte)(AssetBundleBaseKey[i] ^ keyBytes[j]);
            }
        }

        return result;
    }
}
