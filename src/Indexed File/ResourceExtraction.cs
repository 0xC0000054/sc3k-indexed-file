////////////////////////////////////////////////////////////////////////////
//
// This file is part of sc3k-indexed-file, a utility for working with the
// indexed database file format used by SimCity 3000.
//
// Copyright (c) 2024 Nicholas Hayes
//
// This file is licensed under terms of the MIT License.
// See LICENSE.txt for more information.
//
////////////////////////////////////////////////////////////////////////////

using CommunityToolkit.HighPerformance.Buffers;
using System.Buffers;
using System.Buffers.Binary;

namespace SC3KIxf
{
    internal static class ResourceExtraction
    {
        public static string GetFileExtension(ResourceType type)
        {
            switch (type)
            {
                case ResourceType.BufferResource:
                    return ".bmp";
                case ResourceType.BuildingOccupantAttributes:
                case ResourceType.FloraOccupantAttrubutes:
                case ResourceType.OccupantAttributes:
                case ResourceType.OccupantAttributeOverrides:
                case ResourceType.NetworkOccupantAttributes:
                case ResourceType.PortOccupantAttributes:
                    return ".tkb1";
                case ResourceType.HotKey:
                case ResourceType.SerialText:
                case ResourceType.String:
                    return ".txt";
                case ResourceType.SpriteAttributes:
                    return ".sat";
                case ResourceType.SpriteAnimationAttributes:
                    return ".saa";
                case ResourceType.SpriteImage:
                    return ".sim";
                case ResourceType.SpriteImageInfo:
                    return ".sii";
                default:
                    return ".bin";
            }
        }

        public static ReadOnlySpan<byte> GetStringData(ReadOnlySpan<byte> data)
        {
            // The string data is a length-prefixed string.

            int stringLength = BinaryPrimitives.ReadInt32LittleEndian(data);

            return stringLength > 0 ? data.Slice(4, stringLength) : [];
        }

        public static MemoryOwner<byte>? TryDecompressSpriteImage(ReadOnlySpan<byte> input)
        {
            const uint AlphaImageFlag = 0x10000000;
            const uint AlphaImageQFSCompressionFlag = 0x80000;

            if (input.Length > 20)
            {
                uint flags = BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(4, 4));

                if ((flags & (AlphaImageFlag | AlphaImageQFSCompressionFlag)) != 0)
                {
                    ReadOnlySpan<byte> compressedData = input.Slice(20);

                    MemoryOwner<byte> uncompressedData = MemoryOwner<byte>.Allocate(QfsCompression.GetDecompressedSize(compressedData));

                    QfsCompression.Decompress(compressedData, uncompressedData.Span);

                    return uncompressedData;
                }
            }

            return null;
        }
    }
}
