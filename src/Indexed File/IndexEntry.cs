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

using System.Diagnostics;
using System.Globalization;
using SC3KIxf.IO;

namespace SC3KIxf
{
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    internal readonly struct IndexEntry
    {
        public IndexEntry(EndianBinaryReader reader)
        {
            this.Group = reader.ReadUInt32();
            this.Instance = reader.ReadUInt32();
            this.Type = reader.ReadUInt32();
            this.Offset = reader.ReadUInt32();
            this.Length = reader.ReadUInt32();
        }

        public uint Group { get; }

        public uint Instance { get; }

        public uint Type { get; }

        public uint Offset { get; }

        public uint Length { get; }

        public override string ToString() => string.Format(CultureInfo.InvariantCulture,
                                                           "T=0x{0:X8}, G=0x{1:X8}, I=0x{2:X8}, Offset=0x{3:X}, Length={4}",
                                                           this.Type,
                                                           this.Group,
                                                           this.Instance,
                                                           this.Offset,
                                                           this.Length);
        private string GetDebuggerDisplay()
        {
            return ToString();
        }
    }
}
