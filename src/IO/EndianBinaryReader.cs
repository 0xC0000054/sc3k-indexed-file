﻿////////////////////////////////////////////////////////////////////////////
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

using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;

namespace SC3KIxf.IO
{
    // Adapted from 'Problem and Solution: The Terrible Inefficiency of FileStream and BinaryReader'
    // https://jacksondunstan.com/articles/3568

    internal sealed class EndianBinaryReader : Disposable
    {
        private const int MaxBufferSize = 4096;

#pragma warning disable IDE0032 // Use auto property
        private readonly Stream stream;
        private int readOffset;
        private int readLength;
        private readonly byte[] buffer;
        private readonly int bufferSize;
        private readonly Endianess endianess;
        private readonly bool leaveOpen;
#pragma warning restore IDE0032 // Use auto property

        /// <summary>
        /// Initializes a new instance of the <see cref="EndianBinaryReader"/> class.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="byteOrder">The byte order of the stream.</param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="stream"/> is null.
        /// </exception>
        public EndianBinaryReader(Stream stream, Endianess byteOrder) : this(stream, byteOrder, false)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EndianBinaryReader"/> class.
        /// </summary>
        /// <param name="stream">The stream.</param>
        /// <param name="byteOrder">The byte order of the stream.</param>
        /// <param name="leaveOpen">
        /// <see langword="true"/> to leave the stream open after the EndianBinaryReader is disposed; otherwise, <see langword="false"/>
        /// </param>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="stream"/> is null.
        /// </exception>
        public EndianBinaryReader(Stream stream, Endianess byteOrder, bool leaveOpen)
        {
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this.bufferSize = (int)Math.Min(stream.Length, MaxBufferSize);
            this.buffer = ArrayPool<byte>.Shared.Rent(this.bufferSize);
            this.endianess = byteOrder;
            this.leaveOpen = leaveOpen;

            this.readOffset = 0;
            this.readLength = 0;
        }

        public EndianBinaryReader(string path, Endianess byteOrder)
        {
            this.stream = File.OpenRead(path);
            this.bufferSize = (int)Math.Min(this.stream.Length, MaxBufferSize);
            this.buffer = ArrayPool<byte>.Shared.Rent(this.bufferSize);
            this.endianess = byteOrder;
            this.leaveOpen = false;

            this.readOffset = 0;
            this.readLength = 0;
        }

        public Endianess Endianess => this.endianess;

        /// <summary>
        /// Gets the length of the stream.
        /// </summary>
        /// <value>
        /// The length of the stream.
        /// </value>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public long Length
        {
            get
            {
                VerifyNotDisposed();

                return this.stream.Length;
            }
        }

        /// <summary>
        /// Gets or sets the position in the stream.
        /// </summary>
        /// <value>
        /// The position in the stream.
        /// </value>
        /// <exception cref="ArgumentOutOfRangeException">value is negative.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public long Position
        {
            get
            {
                VerifyNotDisposed();

                return this.stream.Position - this.readLength + this.readOffset;
            }
            set
            {
                ArgumentOutOfRangeException.ThrowIfNegative(value);
                VerifyNotDisposed();

                long current = this.Position;

                if (value != current)
                {
                    long bufferStartOffset = current - this.readOffset;
                    long bufferEndOffset = bufferStartOffset + this.readLength;

                    // Avoid reading from the stream if the offset is within the current buffer.
                    if (value >= bufferStartOffset && value <= bufferEndOffset)
                    {
                        this.readOffset = (int)(value - bufferStartOffset);
                    }
                    else
                    {
                        // Invalidate the existing buffer.
                        this.readOffset = 0;
                        this.readLength = 0;
                        this.stream.Seek(value, SeekOrigin.Begin);
                    }
                }
            }
        }

        /// <summary>
        /// Reads a sequence of bytes from the stream, starting from a specified point in the byte array.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <param name="offset">The starting offset in the array.</param>
        /// <param name="count">The count.</param>
        /// <returns>The number of bytes read from the stream.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public int Read(byte[] bytes, int offset, int count)
        {
            if (bytes is null)
            {
                throw new ArgumentNullException(nameof(bytes));
            }
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            return Read(new Span<byte>(bytes, offset, count));
        }

        /// <summary>
        /// Reads a sequence of bytes from the stream.
        /// </summary>
        /// <param name="buffer">The destination buffer.</param>
        /// <returns>The number of bytes read from the stream.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public int Read(Span<byte> buffer)
        {
            VerifyNotDisposed();

            return ReadInternal(buffer);
        }

        /// <summary>
        /// Reads the next byte from the current stream.
        /// </summary>
        /// <returns>The next byte read from the current stream.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public byte ReadByte()
        {
            VerifyNotDisposed();

            return ReadByteInternal();
        }

        /// <summary>
        /// Reads the specified number of bytes from the stream.
        /// </summary>
        /// <param name="count">The number of bytes to read..</param>
        /// <returns>An array containing the specified bytes.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public byte[] ReadBytes(int count)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(count);
            VerifyNotDisposed();

            if (count == 0)
            {
                return Array.Empty<byte>();
            }

            byte[] bytes = new byte[count];

            ReadExactlyInternal(bytes);

            return bytes;
        }

        /// <summary>
        /// Reads a 8-byte floating point value.
        /// </summary>
        /// <returns>The 8-byte floating point value.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public unsafe double ReadDouble()
        {
            ulong temp = ReadUInt64();

            return *(double*)&temp;
        }

        /// <summary>
        /// Reads the specified number of bytes from the stream, starting from a specified point in the byte array.
        /// </summary>
        /// <param name="bytes">The bytes.</param>
        /// <param name="offset">The starting offset in the array.</param>
        /// <param name="count">The count.</param>
        /// <exception cref="ArgumentNullException"><paramref name="bytes"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public unsafe void ReadExactly(byte[] bytes, int offset, int count)
        {
            ArgumentNullException.ThrowIfNull(bytes, nameof(bytes));
            ArgumentOutOfRangeException.ThrowIfNegative(count);

            ReadExactly(new Span<byte>(bytes, offset, count));
        }

        /// <summary>
        /// Reads the specified number of bytes from the stream.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public unsafe void ReadExactly(Span<byte> buffer)
        {
            VerifyNotDisposed();

            ReadExactlyInternal(buffer);
        }

        /// <summary>
        /// Reads a 2-byte signed integer.
        /// </summary>
        /// <returns>The 2-byte signed integer.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public short ReadInt16()
        {
            return (short)ReadUInt16();
        }

        /// <summary>
        /// Reads a 4-byte signed integer.
        /// </summary>
        /// <returns>The 4-byte signed integer.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public int ReadInt32()
        {
            return (int)ReadUInt32();
        }

        /// <summary>
        /// Reads a 8-byte signed integer.
        /// </summary>
        /// <returns>The 8-byte signed integer.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public long ReadInt64()
        {
            return (long)ReadUInt64();
        }

        /// <summary>
        /// Reads a 4-byte floating point value.
        /// </summary>
        /// <returns>The 4-byte floating point value.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public unsafe float ReadSingle()
        {
            uint temp = ReadUInt32();

            return *(float*)&temp;
        }

        /// <summary>
        /// Reads a 2-byte unsigned integer.
        /// </summary>
        /// <returns>The 2-byte unsigned integer.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public ushort ReadUInt16()
        {
            VerifyNotDisposed();

            EnsureBuffer(sizeof(ushort));

            ushort value = Unsafe.ReadUnaligned<ushort>(ref this.buffer[this.readOffset]);

            switch (this.endianess)
            {
                case Endianess.Big:
                    if (BitConverter.IsLittleEndian)
                    {
                        value = BinaryPrimitives.ReverseEndianness(value);
                    }
                    break;
                case Endianess.Little:
                    if (!BitConverter.IsLittleEndian)
                    {
                        value = BinaryPrimitives.ReverseEndianness(value);
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unsupported byte order: " + this.endianess.ToString());
            }

            this.readOffset += sizeof(ushort);

            return value;
        }

        /// <summary>
        /// Reads a 4-byte unsigned integer.
        /// </summary>
        /// <returns>The 4-byte unsigned integer.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public uint ReadUInt32()
        {
            VerifyNotDisposed();

            EnsureBuffer(sizeof(uint));

            uint value = Unsafe.ReadUnaligned<uint>(ref this.buffer[this.readOffset]);

            switch (this.endianess)
            {
                case Endianess.Big:
                    if (BitConverter.IsLittleEndian)
                    {
                        value = BinaryPrimitives.ReverseEndianness(value);
                    }
                    break;
                case Endianess.Little:
                    if (!BitConverter.IsLittleEndian)
                    {
                        value = BinaryPrimitives.ReverseEndianness(value);
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unsupported byte order: " + this.endianess.ToString());
            }

            this.readOffset += sizeof(uint);

            return value;
        }

        /// <summary>
        /// Reads a 8-byte unsigned integer.
        /// </summary>
        /// <returns>The 8-byte unsigned integer.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="ObjectDisposedException">The object has been disposed.</exception>
        public ulong ReadUInt64()
        {
            VerifyNotDisposed();

            EnsureBuffer(sizeof(ulong));

            ulong value = Unsafe.ReadUnaligned<ulong>(ref this.buffer[this.readOffset]);

            switch (this.endianess)
            {
                case Endianess.Big:
                    if (BitConverter.IsLittleEndian)
                    {
                        value = BinaryPrimitives.ReverseEndianness(value);
                    }
                    break;
                case Endianess.Little:
                    if (!BitConverter.IsLittleEndian)
                    {
                        value = BinaryPrimitives.ReverseEndianness(value);
                    }
                    break;
                default:
                    throw new InvalidOperationException("Unsupported byte order: " + this.endianess.ToString());
            }

            this.readOffset += sizeof(ulong);

            return value;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                ArrayPool<byte>.Shared.Return(this.buffer);

                if (!this.leaveOpen)
                {
                    this.stream.Dispose();
                }
            }
        }

        /// <summary>
        /// Ensures that the buffer contains at least the number of bytes requested.
        /// </summary>
        /// <param name="count">The minimum number of bytes the buffer should contain.</param>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        private void EnsureBuffer(int count)
        {
            if (this.readOffset + count > this.readLength)
            {
                FillBuffer(count);
            }
        }

        /// <summary>
        /// Fills the buffer with at least the number of bytes requested.
        /// </summary>
        /// <param name="minBytes">The minimum number of bytes to place in the buffer.</param>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        private void FillBuffer(int minBytes)
        {
            int bytesUnread = this.readLength - this.readOffset;

            if (bytesUnread > 0)
            {
                Buffer.BlockCopy(this.buffer, this.readOffset, this.buffer, 0, bytesUnread);
            }

            int numBytesToRead = this.bufferSize - bytesUnread;
            int numBytesRead = bytesUnread;
            do
            {
                int n = this.stream.Read(this.buffer, numBytesRead, numBytesToRead);

                if (n == 0)
                {
                    throw new EndOfStreamException();
                }

                numBytesRead += n;
                numBytesToRead -= n;

            } while (numBytesRead < minBytes);

            this.readOffset = 0;
            this.readLength = numBytesRead;
        }

        /// <summary>
        /// Gets the length of the string.
        /// </summary>
        /// <param name="endOffset">The offset that marks the end of the null-terminator search area.</param>
        /// <param name="hasNullTerminator"><c>true</c> if the string has a null terminator; otherwise, <c>false</c>.</param>
        /// <returns>The string length.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        /// <exception cref="IOException">The string is longer than <see cref="int.MaxValue"/>.</exception>
        private int GetStringLength(long endOffset, out bool hasNullTerminator)
        {
            hasNullTerminator = false;

            long oldPosition = this.Position;

            while (this.Position < endOffset)
            {
                if (ReadByteInternal() == 0)
                {
                    hasNullTerminator = true;
                    break;
                }
            }

            long length = this.Position - oldPosition;
            if (hasNullTerminator)
            {
                // Subtract the null terminator from the string length.
                length--;
            }

            this.Position = oldPosition;

            if (length > int.MaxValue)
            {
                throw new IOException($"The string is longer than {int.MaxValue}.");
            }

            return (int)length;
        }

        /// <summary>
        /// Reads the next byte from the current stream.
        /// </summary>
        /// <returns>The next byte read from the current stream.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        private byte ReadByteInternal()
        {
            return this.readOffset < this.readLength ? this.buffer[this.readOffset++] : ReadByteSlow();
        }

        /// <summary>
        /// Reads the next byte from the current stream.
        /// </summary>
        /// <returns>The next byte read from the current stream.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        private byte ReadByteSlow()
        {
            FillBuffer(sizeof(byte));

            return this.buffer[this.readOffset++];
        }

        /// <summary>
        /// Reads the specified number of bytes from the stream, starting from a specified point in the byte array.
        /// </summary>
        /// <param name="buffer">The destination buffer.</param>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        private unsafe void ReadExactlyInternal(Span<byte> buffer)
        {
            Span<byte> destination = buffer;

            while (destination.Length > 0)
            {
                int bytesRead = ReadInternal(destination);

                if (bytesRead == 0)
                {
                    throw new EndOfStreamException();
                }

                destination = destination.Slice(bytesRead);
            }
        }

        /// <summary>
        /// Reads a sequence of bytes from the stream.
        /// </summary>
        /// <param name="buffer">The destination buffer.</param>
        /// <returns>The number of bytes read from the stream.</returns>
        /// <exception cref="EndOfStreamException">The end of the stream has been reached.</exception>
        private int ReadInternal(Span<byte> buffer)
        {
            int count = buffer.Length;
            if (count == 0)
            {
                return 0;
            }

            if (this.readOffset + count <= this.readLength)
            {
                new ReadOnlySpan<byte>(this.buffer, this.readOffset, count).CopyTo(buffer);
                this.readOffset += count;

                return count;
            }
            else
            {
                // Ensure that any bytes at the end of the current buffer are included.
                int bytesUnread = this.readLength - this.readOffset;

                if (bytesUnread > 0)
                {
                    new ReadOnlySpan<byte>(this.buffer, this.readOffset, bytesUnread).CopyTo(buffer);
                }

                // Invalidate the existing buffer.
                this.readOffset = 0;
                this.readLength = 0;

                int totalBytesRead = bytesUnread;

                totalBytesRead += this.stream.Read(buffer.Slice(bytesUnread));

                return totalBytesRead;
            }
        }
    }
}
