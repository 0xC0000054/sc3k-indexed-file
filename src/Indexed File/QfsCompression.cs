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
    internal static class QfsCompression
    {
        /// <summary>
        /// The minimum size in bytes of an uncompressed buffer that can be compressed with QFS compression.
        /// </summary>
        private const int UncompressedDataMinSize = 10;

        /// <summary>
        /// The maximum size in bytes of an uncompressed buffer that can be compressed with QFS compression.
        /// </summary>
        private const int UncompressedDataMaxSize = 16777215;

        /// <summary>
        /// Compresses the input byte array with QFS compression
        /// </summary>
        /// <param name="input">The input byte array to compress</param>
        /// <param name="prefixLength">If set to <c>true</c> prefix the size of the compressed data, as is used by SC4; otherwise <c>false</c>.</param>
        /// <returns>A byte array containing the compressed data or null if the data cannot be compressed.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="input" /> is null.</exception>
        /// <exception cref="FormatException">The length of <paramref name="input"/> is larger than 16777215 bytes.</exception>
        public static byte[]? Compress(byte[] input, bool prefixLength)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }

            if (input.Length > UncompressedDataMaxSize)
            {
                throw new FormatException("The uncompressed data must be less than 16777215 bytes.");
            }

            if (input.Length < UncompressedDataMinSize)
            {
                return null;
            }

            ZlibQFS qfs = new ZlibQFS(input, prefixLength);
            return qfs.Compress();
        }

        /// <summary>
        /// Decompresses QFS compressed data.
        /// </summary>
        /// <param name="compressedData">The compressed bytes.</param>
        /// <param name="uncompressedData">The destination for the uncompressed data.</param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="uncompressedData"/> is too small.</exception>
        /// <exception cref="NotSupportedException"><paramref name="compressedData"/> uses an unsupported compression format.</exception>
        public static void Decompress(ReadOnlySpan<byte> compressedData, Span<byte> uncompressedData)
        {
            ReadCompressedDataHeader(compressedData, out int index, out int outLength);

            ArgumentOutOfRangeException.ThrowIfLessThan(uncompressedData.Length, outLength, nameof(uncompressedData));

            byte ccbyte1; // control char 0
            byte ccbyte2; // control char 1
            byte ccbyte3; // control char 2
            byte ccbyte4; // control char 3
            int plainCount;
            int copyCount;
            int copyOffset;

            int length = compressedData.Length;
            int outIndex = 0;

            while (index < length && compressedData[index] < 0xFC)
            {
                ccbyte1 = compressedData[index];
                index++;

                if (ccbyte1 >= 0xE0) // 1 byte literal op code 0xE0 - 0xFB
                {
                    plainCount = ((ccbyte1 & 0x1F) << 2) + 4;
                    copyCount = 0;
                    copyOffset = 0;
                }
                else if (ccbyte1 >= 0xC0) // 4 byte op code 0xC0 - 0xDF
                {
                    ccbyte2 = compressedData[index];
                    index++;
                    ccbyte3 = compressedData[index];
                    index++;
                    ccbyte4 = compressedData[index];
                    index++;

                    plainCount = (ccbyte1 & 3);
                    copyCount = ((ccbyte1 & 0x0C) << 6) + ccbyte4 + 5;
                    copyOffset = (((ccbyte1 & 0x10) << 12) + (ccbyte2 << 8)) + ccbyte3 + 1;
                }
                else if (ccbyte1 >= 0x80) // 3 byte op code 0x80 - 0xBF
                {
                    ccbyte2 = compressedData[index];
                    index++;
                    ccbyte3 = compressedData[index];
                    index++;

                    plainCount = (ccbyte2 & 0xC0) >> 6;
                    copyCount = (ccbyte1 & 0x3F) + 4;
                    copyOffset = ((ccbyte2 & 0x3F) << 8) + ccbyte3 + 1;
                }
                else // 2 byte op code 0x00 - 0x7F
                {
                    ccbyte2 = compressedData[index];
                    index++;

                    plainCount = (ccbyte1 & 3);
                    copyCount = ((ccbyte1 & 0x1C) >> 2) + 3;
                    copyOffset = ((ccbyte1 & 0x60) << 3) + ccbyte2 + 1;
                }


                for (int i = 0; i < plainCount; i++)
                {
                    uncompressedData[outIndex] = compressedData[index];
                    index++;
                    outIndex++;
                }

                if (copyCount > 0)
                {
                    int srcIndex = outIndex - copyOffset;

                    for (int i = 0; i < copyCount; i++)
                    {
                        uncompressedData[outIndex] = uncompressedData[srcIndex];
                        srcIndex++;
                        outIndex++;
                    }
                }
            }

            // Write the trailing bytes.
            if (index < length && outIndex < outLength)
            {
                // 1 byte EOF op code 0xFC - 0xFF.
                plainCount = (compressedData[index] & 3);
                index++;

                for (int i = 0; i < plainCount; i++)
                {
                    uncompressedData[outIndex] = compressedData[index];
                    index++;
                    outIndex++;
                }
            }
        }

        /// <summary>
        /// Gets the decompressed data size.
        /// </summary>
        /// <param name="compressedData">The compressed bytes.</param>
        /// <returns>The uncompressed size.</returns>
        /// <exception cref="NotSupportedException"><paramref name="compressedData"/> uses an unsupported compression format.</exception>
        public static int GetDecompressedSize(ReadOnlySpan<byte> compressedData)
        {
            ReadCompressedDataHeader(compressedData, out int _, out int uncompressedSize);

            return uncompressedSize;
        }

        /// <summary>
        /// Reads the compressed data header
        /// </summary>
        /// <param name="compressedData">The compressed data</param>
        /// <param name="dataStartIndex">The start of the compressed data.</param>
        /// <param name="uncompressedSize">The uncompressed size.</param>
        /// <exception cref="NotSupportedException"><paramref name="compressedData"/> uses an unsupported compression format.</exception>
        private static void ReadCompressedDataHeader(ReadOnlySpan<byte> compressedData, out int dataStartIndex, out int uncompressedSize)
        {
            int headerStartOffset = 0;

            if ((compressedData[0] & HeaderFlags.Mask) != 0x10 || compressedData[1] != 0xFB)
            {
                if ((compressedData[4] & HeaderFlags.Mask) != 0x10 || compressedData[5] != 0xFB)
                {
                    throw new NotSupportedException("Unsupported compression format.");
                }
                headerStartOffset = 4;
            }

            // The first byte contains flags that describes the information in the header.
            int headerFlags = compressedData[headerStartOffset];

            bool largeSizeFields = (headerFlags & HeaderFlags.LargeSizeFields) != 0;
            dataStartIndex = headerStartOffset + 2;

            if ((headerFlags & HeaderFlags.CompressedSizePresent) != 0)
            {
                // Some files may write the compressed size after the signature.
                dataStartIndex += largeSizeFields ? 4 : 3;
            }

            // The next 3 or 4 bytes are the uncompressed size as a 24-bit or 32-bit integer
            if ((headerFlags & HeaderFlags.LargeSizeFields) != 0)
            {
                uncompressedSize = BinaryPrimitives.ReadInt32BigEndian(compressedData.Slice(dataStartIndex, 4));
                dataStartIndex += 4;
            }
            else
            {
                uncompressedSize = (compressedData[dataStartIndex] << 16) | (compressedData[dataStartIndex + 1] << 8) | compressedData[dataStartIndex + 2];
                dataStartIndex += 3;
            }
        }

        /// <summary>
        /// The flags may be present in the first byte of the compression signature.
        /// </summary>
        /// <remarks>
        /// See http://wiki.niotso.org/RefPack#Header
        /// These values may not be used by SC4.
        /// </remarks>
        private static class HeaderFlags
        {
            /// <summary>
            /// The uncompressed size field and compressed size field (if present) are 4-byte values.
            /// </summary>
            /// <remarks>
            /// If this flag is unset then the fields are 3-byte values.
            /// This may not be used by SC4.
            /// </remarks>
            internal const int LargeSizeFields = 128;
            internal const int Unknown1 = 64;
            // Other values are unused, with 16 reserved for the compression signature.

            /// <summary>
            /// The compressed size follows the header.
            /// </summary>
            /// <remarks>
            /// This may be unused by SC4 as many of the game files
            /// place the compressed size before the QFS header.
            /// </remarks>
            internal const int CompressedSizePresent = 1;

            internal const int Mask = ~(LargeSizeFields | Unknown1 | CompressedSizePresent);
        }

        private sealed class ZlibQFS
        {
            private readonly byte[] input;
            private byte[] output;
            private readonly int inputLength;
            private readonly int outputLength;
            private int outIndex;
            private int readPosition;
            private int lastWritePosition;
            private int remaining;
            private readonly bool prefixLength;

            private const int QfsHeaderSize = 5;
            /// <summary>
            /// The maximum length of a literal run.
            /// </summary>
            private const int LiteralRunMaxLength = 112;

            private int hash;
            private readonly int[] head;
            private readonly int[] prev;

            private const int MaxWindowSize = 131072;
            private const int MaxHashSize = 65536;

            private readonly int windowSize;
            private readonly int windowMask;
            private readonly int maxWindowOffset;
            private readonly int hashSize;
            private readonly int hashMask;
            private readonly int hashShift;

            private const int GoodLength = 32;
            private const int MaxLazy = 258;
            private const int NiceLength = 258;
            private const int MaxChain = 4096;
            private const int MIN_MATCH = 3;
            private const int MAX_MATCH = 1028;

            private int match_start;
            private int match_length;
            private int prev_length;

            private static int HighestOneBit(int value)
            {
                value--;
                value |= (value >> 1);
                value |= (value >> 2);
                value |= (value >> 4);
                value |= (value >> 8);
                value |= (value >> 16);
                value++;

                return value - (value >> 1);
            }

            private static int NumberOfTrailingZeros(int value)
            {
                uint v = (uint)value; // 32-bit word input to count zero bits on right
                int count;            // count will be the number of zero bits on the right,
                // so if v is 1101000 (base 2), then count will be 3

                if (v == 0)
                {
                    return 32;
                }

                if ((v & 0x1) != 0)
                {
                    // special case for odd v (assumed to happen half of the time)
                    count = 0;
                }
                else
                {
                    count = 1;
                    if ((v & 0xffff) == 0)
                    {
                        v >>= 16;
                        count += 16;
                    }
                    if ((v & 0xff) == 0)
                    {
                        v >>= 8;
                        count += 8;
                    }
                    if ((v & 0xf) == 0)
                    {
                        v >>= 4;
                        count += 4;
                    }
                    if ((v & 0x3) == 0)
                    {
                        v >>= 2;
                        count += 2;
                    }
                    if ((v & 0x1) != 0)
                    {
                        count--;
                    }
                }

                return count;
            }

            public ZlibQFS(byte[] input, bool prefixLength)
            {
                ArgumentNullException.ThrowIfNull(input);

                this.input = input;
                this.inputLength = input.Length;
                this.output = new byte[this.inputLength - 1];
                this.outputLength = this.output.Length;

                if (this.inputLength < MaxWindowSize)
                {
                    this.windowSize = HighestOneBit(this.inputLength);
                    this.hashSize = Math.Max(this.windowSize / 2, 32);
                    this.hashShift = (NumberOfTrailingZeros(this.hashSize) + MIN_MATCH - 1) / MIN_MATCH;
                }
                else
                {
                    this.windowSize = MaxWindowSize;
                    this.hashSize = MaxHashSize;
                    this.hashShift = 6;
                }
                this.maxWindowOffset = this.windowSize - 1;
                this.windowMask = this.maxWindowOffset;
                this.hashMask = this.hashSize - 1;

                this.hash = 0;
                this.head = new int[this.hashSize];
                this.prev = new int[this.windowSize];
                this.readPosition = 0;
                this.remaining = this.inputLength;
                this.outIndex = QfsHeaderSize;
                this.lastWritePosition = 0;
                this.prefixLength = prefixLength;

                for (int i = 0; i < this.head.Length; i++)
                {
                    this.head[i] = -1;
                }
            }

            private bool WriteCompressedData(int startOffset)
            {
                int endOffset = this.readPosition - 1;
                int run = endOffset - this.lastWritePosition;

                while (run > 3) // 1 byte literal op code 0xE0 - 0xFB
                {
                    int blockLength = Math.Min(run & ~3, LiteralRunMaxLength);

                    if ((this.outIndex + blockLength + 1) >= this.outputLength)
                    {
                        return false; // data did not compress
                    }

                    this.output[this.outIndex] = (byte)(0xE0 + ((blockLength / 4) - 1));
                    this.outIndex++;

                    // A for loop is faster than Buffer.BlockCopy for data less than or equal to 32 bytes.
                    if (blockLength <= 32)
                    {
                        for (int i = 0; i < blockLength; i++)
                        {
                            this.output[this.outIndex] = this.input[this.lastWritePosition];
                            this.lastWritePosition++;
                            this.outIndex++;
                        }
                    }
                    else
                    {
                        Buffer.BlockCopy(this.input, this.lastWritePosition, this.output, this.outIndex, blockLength);
                        this.lastWritePosition += blockLength;
                        this.outIndex += blockLength;
                    }

                    run -= blockLength;
                }

                int copyLength = this.prev_length;
                // Subtract one before encoding the copy offset, the QFS decompression algorithm adds it back when decoding.
                int copyOffset = endOffset - startOffset - 1;

                if (copyLength <= 10 && copyOffset < 1024) // 2 byte op code  0x00 - 0x7f
                {
                    if ((this.outIndex + run + 2) >= this.outputLength)
                    {
                        return false;
                    }

                    this.output[this.outIndex] = (byte)((((copyOffset >> 8) << 5) + ((copyLength - 3) << 2)) + run);
                    this.output[this.outIndex + 1] = (byte)(copyOffset & 0xff);
                    this.outIndex += 2;
                }
                else if (copyLength <= 67 && copyOffset < 16384)  // 3 byte op code 0x80 - 0xBF
                {
                    if ((this.outIndex + run + 3) >= this.outputLength)
                    {
                        return false;
                    }

                    this.output[this.outIndex] = (byte)(0x80 + (copyLength - 4));
                    this.output[this.outIndex + 1] = (byte)((run << 6) + (copyOffset >> 8));
                    this.output[this.outIndex + 2] = (byte)(copyOffset & 0xff);
                    this.outIndex += 3;
                }
                else // 4 byte op code 0xC0 - 0xDF
                {
                    if ((this.outIndex + run + 4) >= this.outputLength)
                    {
                        return false;
                    }

                    this.output[this.outIndex] = (byte)(((0xC0 + ((copyOffset >> 16) << 4)) + (((copyLength - 5) >> 8) << 2)) + run);
                    this.output[this.outIndex + 1] = (byte)((copyOffset >> 8) & 0xff);
                    this.output[this.outIndex + 2] = (byte)(copyOffset & 0xff);
                    this.output[this.outIndex + 3] = (byte)((copyLength - 5) & 0xff);
                    this.outIndex += 4;
                }


                for (int i = 0; i < run; i++)
                {
                    this.output[this.outIndex] = this.input[this.lastWritePosition];
                    this.lastWritePosition++;
                    this.outIndex++;
                }
                this.lastWritePosition += copyLength;

                return true;
            }

            private bool WriteEndData()
            {
                int run = this.readPosition - this.lastWritePosition;

                while (run > 3) // 1 byte literal op code 0xE0 - 0xFB
                {
                    int blockLength = Math.Min(run & ~3, LiteralRunMaxLength);

                    if ((this.outIndex + blockLength + 1) >= this.outputLength)
                    {
                        return false; // data did not compress
                    }

                    this.output[this.outIndex] = (byte)(0xE0 + ((blockLength / 4) - 1));
                    this.outIndex++;

                    // A for loop is faster than Buffer.BlockCopy for data less than or equal to 32 bytes.
                    if (blockLength <= 32)
                    {
                        for (int i = 0; i < blockLength; i++)
                        {
                            this.output[this.outIndex] = this.input[this.lastWritePosition];
                            this.lastWritePosition++;
                            this.outIndex++;
                        }
                    }
                    else
                    {
                        Buffer.BlockCopy(this.input, this.lastWritePosition, this.output, this.outIndex, blockLength);
                        this.lastWritePosition += blockLength;
                        this.outIndex += blockLength;
                    }
                    run -= blockLength;
                }

                if ((this.outIndex + run + 1) >= this.outputLength)
                {
                    return false;
                }
                this.output[this.outIndex] = (byte)(0xFC + run);
                this.outIndex++;

                for (int i = 0; i < run; i++)
                {
                    this.output[this.outIndex] = this.input[this.lastWritePosition];
                    this.lastWritePosition++;
                    this.outIndex++;
                }

                return true;
            }

            // longest_match and Compress are adapted from deflate.c in zlib 1.2.3 which is licensed as follows:
            /* zlib.h -- interface of the 'zlib' general purpose compression library
              version 1.2.3, July 18th, 2005

              Copyright (C) 1995-2005 Jean-loup Gailly and Mark Adler

              This software is provided 'as-is', without any express or implied
              warranty.  In no event will the authors be held liable for any damages
              arising from the use of this software.

              Permission is granted to anyone to use this software for any purpose,
              including commercial applications, and to alter it and redistribute it
              freely, subject to the following restrictions:

              1. The origin of this software must not be misrepresented; you must not
                 claim that you wrote the original software. If you use this software
                 in a product, an acknowledgment in the product documentation would be
                 appreciated but is not required.
              2. Altered source versions must be plainly marked as such, and must not be
                 misrepresented as being the original software.
              3. This notice may not be removed or altered from any source distribution.

              Jean-loup Gailly        Mark Adler
              jloup@gzip.org          madler@alumni.caltech.edu


              The data format used by the zlib library is described by RFCs (Request for
              Comments) 1950 to 1952 in the files http://www.ietf.org/rfc/rfc1950.txt
              (zlib format), rfc1951.txt (deflate format) and rfc1952.txt (gzip format).
            */

            private int longest_match(int cur_match)
            {
                int chain_length = MaxChain;
                int scan = this.readPosition;
                int bestLength = this.prev_length;

                if (bestLength >= this.remaining)
                {
                    return this.remaining;
                }

                byte scan_end1 = this.input[scan + bestLength - 1];
                byte scan_end = this.input[scan + bestLength];

                // Do not waste too much time if we already have a good match:
                if (this.prev_length >= GoodLength)
                {
                    chain_length >>= 2;
                }
                int niceLength = NiceLength;

                // Do not look for matches beyond the end of the input. This is necessary
                // to make deflate deterministic.
                if (niceLength > this.remaining)
                {
                    niceLength = this.remaining;
                }
                int maxLength = Math.Min(this.remaining, MAX_MATCH);
                int limit = this.readPosition > this.maxWindowOffset ? this.readPosition - this.maxWindowOffset : 0;

                do
                {
                    int match = cur_match;

                    // Skip to next match if the match length cannot increase
                    // or if the match length is less than 2:
                    if (this.input[match + bestLength] != scan_end ||
                        this.input[match + bestLength - 1] != scan_end1 ||
                        this.input[match] != this.input[scan] ||
                        this.input[match + 1] != this.input[scan + 1])
                    {
                        continue;
                    }


                    int len = 2;
                    do
                    {
                        len++;
                    }
                    while (len < maxLength && this.input[scan + len] == this.input[match + len]);

                    if (len > bestLength)
                    {
                        this.match_start = cur_match;
                        bestLength = len;
                        if (len >= niceLength)
                        {
                            break;
                        }
                        scan_end1 = this.input[scan + bestLength - 1];
                        scan_end = this.input[scan + bestLength];
                    }
                }
                while ((cur_match = this.prev[cur_match & this.windowMask]) >= limit && --chain_length > 0);

                return bestLength;
            }

            public byte[]? Compress()
            {
                this.hash = this.input[0];
                this.hash = ((this.hash << this.hashShift) ^ this.input[1]) & this.hashMask;

                int lastMatch = this.inputLength - MIN_MATCH;

                while (this.remaining > 0)
                {
                    this.prev_length = this.match_length;
                    int prev_match = this.match_start;
                    this.match_length = MIN_MATCH - 1;

                    int hash_head = -1;

                    // Insert the string window[readPosition .. readPosition+2] in the
                    // dictionary, and set hash_head to the head of the hash chain:
                    if (this.remaining >= MIN_MATCH)
                    {
                        this.hash = ((this.hash << this.hashShift) ^ this.input[this.readPosition + MIN_MATCH - 1]) & this.hashMask;

                        hash_head = this.head[this.hash];
                        this.prev[this.readPosition & this.windowMask] = hash_head;
                        this.head[this.hash] = this.readPosition;
                    }

                    if (hash_head >= 0 && this.prev_length < MaxLazy && this.readPosition - hash_head <= this.windowSize)
                    {
                        int bestLength = longest_match(hash_head);

                        if (bestLength >= MIN_MATCH)
                        {
                            int bestOffset = this.readPosition - this.match_start;

                            if (bestOffset <= 1024 ||
                                bestOffset <= 16384 && bestLength >= 4 ||
                                bestOffset <= this.windowSize && bestLength >= 5)
                            {
                                this.match_length = bestLength;
                            }
                        }
                    }

                    // If there was a match at the previous step and the current
                    // match is not better, output the previous match:
                    if (this.prev_length >= MIN_MATCH && this.match_length <= this.prev_length)
                    {
                        if (!WriteCompressedData(prev_match))
                        {
                            return null;
                        }

                        // Insert in hash table all strings up to the end of the match.
                        // readPosition-1 and readPosition are already inserted. If there is not
                        // enough lookahead, the last two strings are not inserted in
                        // the hash table.

                        this.remaining -= (this.prev_length - 1);
                        this.prev_length -= 2;

                        do
                        {
                            this.readPosition++;

                            if (this.readPosition < lastMatch)
                            {
                                this.hash = ((this.hash << this.hashShift) ^ this.input[this.readPosition + MIN_MATCH - 1]) & this.hashMask;

                                hash_head = this.head[this.hash];
                                this.prev[this.readPosition & this.windowMask] = hash_head;
                                this.head[this.hash] = this.readPosition;
                            }
                            this.prev_length--;
                        }
                        while (this.prev_length > 0);

                        this.match_length = MIN_MATCH - 1;
                        this.readPosition++;
                    }
                    else
                    {
                        this.readPosition++;
                        this.remaining--;
                    }
                }

                if (!WriteEndData())
                {
                    return null;
                }

                // Write the compressed data header.
                this.output[0] = 0x10;
                this.output[1] = 0xFB;
                this.output[2] = (byte)((this.inputLength >> 16) & 0xff);
                this.output[3] = (byte)((this.inputLength >> 8) & 0xff);
                this.output[4] = (byte)(this.inputLength & 0xff);

                // Trim the output array to its actual size.
                if (this.prefixLength)
                {
                    int finalLength = this.outIndex + 4;
                    if (finalLength >= this.inputLength)
                    {
                        return null;
                    }

                    byte[] temp = new byte[finalLength];
                    // Write the compressed data length in little endian byte order.
                    temp[0] = (byte)(this.outIndex & 0xff);
                    temp[1] = (byte)((this.outIndex >> 8) & 0xff);
                    temp[2] = (byte)((this.outIndex >> 16) & 0xff);
                    temp[3] = (byte)((this.outIndex >> 24) & 0xff);

                    Buffer.BlockCopy(this.output, 0, temp, 4, this.outIndex);
                    this.output = temp;
                }
                else
                {
                    byte[] temp = new byte[this.outIndex];
                    Buffer.BlockCopy(this.output, 0, temp, 0, this.outIndex);

                    this.output = temp;
                }

                return this.output;
            }

        }
    }
}
