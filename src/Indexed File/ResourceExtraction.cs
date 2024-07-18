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
                case ResourceType.SpriteAnimationAttributes:
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
    }
}
