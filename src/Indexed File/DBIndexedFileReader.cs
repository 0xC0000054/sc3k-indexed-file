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
using Microsoft.Win32.SafeHandles;
using SC3KIxf.IO;
using System.Globalization;

namespace SC3KIxf
{
    internal sealed partial class DBIndexedFileReader : Disposable
    {
        private readonly EndianBinaryReader reader;
        private readonly List<IndexEntry> entries;

        public DBIndexedFileReader(string path)
        {
            this.reader = new EndianBinaryReader(path, Endianess.Little);

            try
            {
                this.entries = ReadIndexEntries(this.reader);
            }
            catch (Exception)
            {
                Dispose();
                throw;
            }
        }

        public IReadOnlyList<IndexEntry> Entries => this.entries;

        private static ReadOnlySpan<byte> CompressedDataSignature => [0x07, 0x01, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00];

        public void WriteEntriesToOutputDirectory(string outputDirectory)
        {
            Directory.CreateDirectory(outputDirectory);

            foreach (IndexEntry entry in this.entries)
            {
                this.reader.Position = entry.Offset;

                using (SpanOwner<byte> spanOwner = SpanOwner<byte>.Allocate(checked((int)entry.Length)))
                {
                    Span<byte> bytes = spanOwner.Span;

                    this.reader.ReadExactly(bytes);

                    string fileName = string.Format(CultureInfo.InvariantCulture,
                                                    "0x{0:X8}_0x{1:X8}_0x{2:X8}{3}",
                                                    entry.Type,
                                                    entry.Group,
                                                    entry.Instance,
                                                    ResourceExtraction.GetFileExtension((ResourceType)entry.Type));

                    // The compressed entries in a DAT/IXF file use a 20 byte header before the start of
                    // the compressed data.
                    // The header appears to consist of 5 4-byte little endian integers, the first and second values
                    // are always the same while the third, fourth and fifth values change from entry to entry.
                    //
                    // The actual compressed data follows this header in the QFS/RefPack format.
                    if (bytes.Length > 20 && bytes.StartsWith(CompressedDataSignature))
                    {
                        ReadOnlySpan<byte> compressedData = bytes.Slice(20);

                        int uncompressedLength = QfsCompression.GetDecompressedSize(compressedData);

                        using (SpanOwner<byte> uncompressedOwner = SpanOwner<byte>.Allocate(uncompressedLength))
                        {
                            Span<byte> uncompressedData = uncompressedOwner.Span;

                            QfsCompression.Decompress(compressedData, uncompressedData);

                            WriteFileData(Path.Combine(outputDirectory, fileName), entry.Type, uncompressedData);
                        }
                    }
                    else
                    {
                        WriteFileData(Path.Combine(outputDirectory, fileName), entry.Type, bytes);
                    }
                }
            }
        }

        private static List<IndexEntry> ReadIndexEntries(EndianBinaryReader reader)
        {
            List<IndexEntry> list = [];

            // A file with a single entry will be at least 24 bytes long.
            // The res/text/dutch/sc3holidaysstringspetitionertickery.ixf file
            // in the SC3KU Linux release is only 4 bytes long, and it only
            // contains the file signature.

            if (reader.Length >= 24)
            {
                const uint FileSignature = 0x80C381D7;
                uint sig = reader.ReadUInt32();

                if (sig != FileSignature)
                {
                    throw new FormatException("Invalid IDX file signature.");
                }

                while (true)
                {
                    IndexEntry entry = new(reader);

                    if (entry.Instance == 0
                        && entry.Group == 0
                        && entry.Type == 0
                        && entry.Offset == 0
                        && entry.Length == 0)
                    {
                        // The end of the directory.
                        break;
                    }
                    else if (entry.Instance == uint.MaxValue
                             && entry.Group == uint.MaxValue
                             && entry.Type == uint.MaxValue
                             && entry.Offset == uint.MaxValue
                             && entry.Length == uint.MaxValue)
                    {
                        // Empty or deleted entry
                        continue;
                    }

                    list.Add(entry);
                }
            }

            return list;
        }

        private static void WriteFileData(string path, uint type, ReadOnlySpan<byte> data)
        {
            ReadOnlySpan<byte> bytes = data;

            switch ((ResourceType)type)
            {
                case ResourceType.String:
                    bytes = ResourceExtraction.GetStringData(data);
                    break;
            }

            using (SafeFileHandle handle = File.OpenHandle(path,
                                                           FileMode.Create,
                                                           FileAccess.Write,
                                                           FileShare.None,
                                                           FileOptions.None,
                                                           bytes.Length))
            {
                RandomAccess.Write(handle, bytes, 0);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.reader?.Dispose();
            }
        }
    }
}
