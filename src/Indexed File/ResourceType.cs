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

namespace SC3KIxf
{
    internal enum ResourceType : uint
    {
        BufferResource = 0x62B9DA24, // BMP file
        BuildingOccupantAttributes = 0x207EDC0E,
        FloraOccupantAttrubutes = 0xFFD30C03,
        HotKey = 0xA2E3D533,
        OccupantAttributes = 0xC179C042,
        OccupantAttributeOverrides = 0x856CD19A,
        NetworkOccupantAttributes = 0xE223741F,
        PortOccupantAttributes = 0x220055E1,
        SerializedSC3City = 0x00000FA1,
        SerialText = 0x81F53D09, // TXT!
        SpriteAttributes = 0x6300,
        SpriteAnimationAttributes = 0x6301,
        SpriteImage  = 0,
        SpriteImageInfo = 1,
        String = 0x2026960b,
    }
}
