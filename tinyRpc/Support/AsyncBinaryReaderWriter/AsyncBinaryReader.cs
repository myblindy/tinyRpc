﻿using System.Text;
using System.Diagnostics.Contracts;
using System.Linq.Expressions;

namespace Overby.Extensions.AsyncBinaryReaderWriter
{
    public class AsyncBinaryReader : IDisposable
    {
        private const int MaxCharBytesSize = 128;

        private readonly Stream? _stream;
        private readonly byte[] _buffer;
        private readonly Decoder _decoder;
        private byte[]? _charBytes;
        private char[]? _singleChar;
        private char[]? _charBuffer;
        private readonly int _maxCharsSize;  // From MaxCharBytesSize & Encoding

        // Performance optimization for Read() w/ Unicode.  Speeds us up by ~40% 
        private readonly bool _2BytesPerChar;
        private readonly bool _leaveOpen;

        public AsyncBinaryReader(Stream input) : this(input, new UTF8Encoding(), false)
        {
        }

        public AsyncBinaryReader(Stream input, Encoding encoding) : this(input, encoding, false)
        {
        }

        public AsyncBinaryReader(Stream input, Encoding encoding, bool leaveOpen)
        {
            if (input == null)
            {
                throw new ArgumentNullException("input");
            }
            if (encoding == null)
            {
                throw new ArgumentNullException("encoding");
            }
            if (!input.CanRead)
                throw new ArgumentException("stream not readable");
            Contract.EndContractBlock();
            _stream = input;
            _decoder = encoding.GetDecoder()!;
            _maxCharsSize = encoding.GetMaxCharCount(MaxCharBytesSize);
            int minBufferSize = encoding.GetMaxByteCount(1);  // max bytes per one char
            if (minBufferSize < 16)
                minBufferSize = 16;
            _buffer = new byte[minBufferSize];
            // m_charBuffer and m_charBytes will be left null.

            // For Encodings that always use 2 bytes per char (or more), 
            // special case them here to make Read() & Peek() faster.
            _2BytesPerChar = encoding is UnicodeEncoding;
            _leaveOpen = leaveOpen;
        }

        public virtual Stream? BaseStream => _stream;

        public virtual void Close()
        {
            Dispose(true);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                var copyOfStream = _stream;
                if (copyOfStream != null && !_leaveOpen)
                    copyOfStream.Close();
            }
            _charBytes = null;
            _singleChar = null;
            _charBuffer = null;
        }

        public void Dispose()
        {
            Dispose(true);
        }

        public virtual async Task<int> PeekCharAsync(CancellationToken cancellationToken = default)
        {
            Contract.Ensures(Contract.Result<int>() >= -1);

            if (_stream == null) __Error.FileNotOpen();

            if (!_stream!.CanSeek)
                return -1;
            long origPos = _stream.Position;
            int ch = await ReadAsync(cancellationToken).ConfigureAwait(false);
            _stream.Position = origPos;
            return ch;
        }

        public virtual Task<int> ReadAsync(CancellationToken cancellationToken = default)
        {
            Contract.Ensures(Contract.Result<int>() >= -1);

            if (_stream == null)
            {
                __Error.FileNotOpen();
            }
            return InternalReadOneCharAsync(cancellationToken);
        }

        public virtual async Task<bool> ReadBooleanAsync(CancellationToken cancellationToken = default)
        {
            await FillBufferAsync(1, cancellationToken).ConfigureAwait(false);
            return (_buffer[0] != 0);
        }

        public virtual async Task<byte> ReadByteAsync(CancellationToken cancellationToken = default)
        {
            // Inlined to avoid some method call overhead with FillBuffer.
            if (_stream == null) __Error.FileNotOpen();

            int b = await _stream!.ReadByteAsync(cancellationToken).ConfigureAwait(false);
            if (b == -1)
                __Error.EndOfFile();
            return (byte)b;
        }

        public virtual async Task<sbyte> ReadSByteAsync(CancellationToken cancellationToken = default)
        {
            await FillBufferAsync(1, cancellationToken).ConfigureAwait(false);
            return (sbyte)(_buffer[0]);
        }

        public virtual async Task<char> ReadCharAsync(CancellationToken cancellationToken = default)
        {
            int value = await ReadAsync(cancellationToken).ConfigureAwait(false);
            if (value == -1)
            {
                __Error.EndOfFile();
            }
            return (char)value;
        }

        public virtual async Task<short> ReadInt16Async(CancellationToken cancellationToken = default)
        {
            await FillBufferAsync(2, cancellationToken).ConfigureAwait(false);
            return (short)(_buffer[0] | _buffer[1] << 8);
        }

        public virtual async Task<ushort> ReadUInt16Async(CancellationToken cancellationToken = default)
        {
            await FillBufferAsync(2, cancellationToken).ConfigureAwait(false);
            return (ushort)(_buffer[0] | _buffer[1] << 8);
        }

        public virtual async Task<int> ReadInt32Async(CancellationToken cancellationToken = default)
        {
            await FillBufferAsync(4, cancellationToken).ConfigureAwait(false);
            return _buffer[0] | _buffer[1] << 8 | _buffer[2] << 16 | _buffer[3] << 24;
        }

        public virtual async Task<uint> ReadUInt32Async(CancellationToken cancellationToken = default)
        {
            await FillBufferAsync(4, cancellationToken).ConfigureAwait(false);
            return (uint)(_buffer[0] | _buffer[1] << 8 | _buffer[2] << 16 | _buffer[3] << 24);
        }

        public virtual async Task<long> ReadInt64Async(CancellationToken cancellationToken = default)
        {
            await FillBufferAsync(8, cancellationToken).ConfigureAwait(false);
            uint lo = (uint)(_buffer[0] | _buffer[1] << 8 |
                             _buffer[2] << 16 | _buffer[3] << 24);
            uint hi = (uint)(_buffer[4] | _buffer[5] << 8 |
                             _buffer[6] << 16 | _buffer[7] << 24);
            return (long)((ulong)hi) << 32 | lo;
        }

        public virtual async Task<ulong> ReadUInt64Async(CancellationToken cancellationToken = default)
        {
            await FillBufferAsync(8, cancellationToken).ConfigureAwait(false);
            uint lo = (uint)(_buffer[0] | _buffer[1] << 8 |
                             _buffer[2] << 16 | _buffer[3] << 24);
            uint hi = (uint)(_buffer[4] | _buffer[5] << 8 |
                             _buffer[6] << 16 | _buffer[7] << 24);
            return ((ulong)hi) << 32 | lo;
        }

        //[SecuritySafeCritical]
        public virtual async Task<float> ReadSingleAsync(CancellationToken cancellationToken = default)
        {
            await FillBufferAsync(4, cancellationToken).ConfigureAwait(false);
            uint tmpBuffer = (uint)(_buffer[0] | _buffer[1] << 8 | _buffer[2] << 16 | _buffer[3] << 24);

            unsafe
            {
                return *((float*)&tmpBuffer);
            }
        }



        //[SecuritySafeCritical]
        public virtual async Task<double> ReadDoubleAsync(CancellationToken cancellationToken = default)
        {
            await FillBufferAsync(8, cancellationToken).ConfigureAwait(false);
            uint lo = (uint)(_buffer[0] | _buffer[1] << 8 |
                    _buffer[2] << 16 | _buffer[3] << 24);
            uint hi = (uint)(_buffer[4] | _buffer[5] << 8 |
                _buffer[6] << 16 | _buffer[7] << 24);

            ulong tmpBuffer = ((ulong)hi) << 32 | lo;
            unsafe
            {
                return *((double*)&tmpBuffer);
            }
        }

        public virtual async Task<decimal> ReadDecimalAsync(CancellationToken cancellationToken = default)
        {
            await FillBufferAsync(16, cancellationToken).ConfigureAwait(false);
            try
            {
                return ToDecimal(_buffer);
            }
            catch (ArgumentException e)
            {
                // ReadDecimal cannot leak out ArgumentException
                throw new IOException("Arg_DecBitCtor", e);
            }

            static decimal ToDecimal(byte[] buffer)
            {
                Contract.Requires((buffer != null && buffer.Length >= 16), "[ToDecimal]buffer != null && buffer.Length >= 16");
                int lo = buffer![0] | buffer[1] << 8 | buffer[2] << 16 | buffer[3] << 24;
                int mid = buffer[4] | buffer[5] << 8 | buffer[6] << 16 | buffer[7] << 24;
                int hi = buffer[8] | buffer[9] << 8 | buffer[10] << 16 | buffer[11] << 24;
                int flags = buffer[12] | buffer[13] << 8 | buffer[14] << 16 | buffer[15] << 24;
                return new decimal([lo, mid, hi, flags]);
            }
        }

        public virtual async Task<string> ReadStringAsync(CancellationToken cancellationToken = default)
        {
            Contract.Ensures(Contract.Result<string>() != null);

            if (_stream == null)
                __Error.FileNotOpen();

            int currPos = 0;
            int n;
            int stringLength;
            int readLength;
            int charsRead;

            // Length of the string in bytes, not chars
            stringLength = await Read7BitEncodedIntAsync(cancellationToken).ConfigureAwait(false);
            if (stringLength < 0)
            {
                throw new IOException("invalid string length", stringLength);
            }

            if (stringLength == 0)
            {
                return string.Empty;
            }

            if (_charBytes == null)
            {
                _charBytes = new byte[MaxCharBytesSize];
            }

            if (_charBuffer == null)
            {
                _charBuffer = new char[_maxCharsSize];
            }

            StringBuilder? sb = null;
            do
            {
                readLength = ((stringLength - currPos) > MaxCharBytesSize) ? MaxCharBytesSize : (stringLength - currPos);

                n = await _stream!.ReadAsync(_charBytes, 0, readLength, cancellationToken).ConfigureAwait(false);
                if (n == 0)
                {
                    __Error.EndOfFile();
                }

                charsRead = _decoder.GetChars(_charBytes, 0, n, _charBuffer, 0);

                if (currPos == 0 && n == stringLength)
                    return new string(_charBuffer, 0, charsRead);

                if (sb == null)
                    sb = StringBuilderCache.Acquire(stringLength); // Actual string length in chars may be smaller.
                sb.Append(_charBuffer, 0, charsRead);
                currPos += n;

            } while (currPos < stringLength);

            return StringBuilderCache.GetStringAndRelease(sb);
        }

        //[SecuritySafeCritical]
        public virtual Task<int> ReadAsync(char[] buffer, int index, int count, CancellationToken cancellationToken = default)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            if (index < 0)
            {
                throw new ArgumentOutOfRangeException("index");
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count");
            }
            if (buffer.Length - index < count)
            {
                throw new ArgumentException("invalid offset length");
            }
            Contract.Ensures(Contract.Result<int>() >= 0);
            Contract.Ensures(Contract.Result<int>() <= count);
            Contract.EndContractBlock();

            if (_stream == null)
                __Error.FileNotOpen();

            // SafeCritical: index and count have already been verified to be a valid range for the buffer
            return InternalReadCharsAsync(buffer, index, count, cancellationToken);
        }

        //[SecurityCritical]
        private async Task<int> InternalReadCharsAsync(char[] buffer, int index, int count, CancellationToken cancellationToken)
        {
            Contract.Requires(buffer != null);
            Contract.Requires(index >= 0 && count >= 0);
            Contract.Assert(_stream != null);

            int numBytes = 0;
            int charsRemaining = count;

            if (_charBytes == null)
            {
                _charBytes = new byte[MaxCharBytesSize];
            }

            while (charsRemaining > 0)
            {
                int charsRead = 0;
                // We really want to know what the minimum number of bytes per char
                // is for our encoding.  Otherwise for UnicodeEncoding we'd have to
                // do ~1+log(n) reads to read n characters.
                numBytes = charsRemaining;

                // special case for DecoderNLS subclasses when there is a hanging byte from the previous loop
                if (CheckDecoderNLS_And_HasState(_decoder) && numBytes > 1)
                {
                    numBytes -= 1;
                }

                if (_2BytesPerChar)
                    numBytes <<= 1;
                if (numBytes > MaxCharBytesSize)
                    numBytes = MaxCharBytesSize;

                int position = 0;
                byte[]? byteBuffer = null;

                numBytes = await _stream!.ReadAsync(_charBytes, 0, numBytes, cancellationToken).ConfigureAwait(false);
                byteBuffer = _charBytes;

                if (numBytes == 0)
                {
                    return (count - charsRemaining);
                }

                Contract.Assert(byteBuffer != null, "expected byteBuffer to be non-null");

                checked
                {

                    if (position < 0 || numBytes < 0 || position + numBytes > byteBuffer!.Length)
                    {
                        throw new ArgumentOutOfRangeException("byteCount");
                    }

                    if (index < 0 || charsRemaining < 0 || index + charsRemaining > buffer!.Length)
                    {
                        throw new ArgumentOutOfRangeException("charsRemaining");
                    }

                    unsafe
                    {
                        fixed (byte* pBytes = byteBuffer)
                        {
                            fixed (char* pChars = buffer)
                            {
                                charsRead = _decoder.GetChars(pBytes + position, numBytes, pChars + index, charsRemaining, false);
                            }
                        }
                    }
                }

                charsRemaining -= charsRead;
                index += charsRead;
            }

            // this should never fail
            Contract.Assert(charsRemaining >= 0, "We read too many characters.");

            // we may have read fewer than the number of characters requested if end of stream reached 
            // or if the encoding makes the char count too big for the buffer (e.g. fallback sequence)
            return (count - charsRemaining);
        }



        private async Task<int> InternalReadOneCharAsync(CancellationToken cancellationToken)
        {
            // I know having a separate InternalReadOneChar method seems a little 
            // redundant, but this makes a scenario like the security parser code
            // 20% faster, in addition to the optimizations for UnicodeEncoding I
            // put in InternalReadChars.   
            int charsRead = 0;
            long posSav = 0;

            if (_stream!.CanSeek)
                posSav = _stream.Position;

            if (_charBytes == null)
            {
                _charBytes = new byte[MaxCharBytesSize]; //
            }
            if (_singleChar == null)
            {
                _singleChar = new char[1];
            }

            while (charsRead == 0)
            {
                // We really want to know what the minimum number of bytes per char
                // is for our encoding.  Otherwise for UnicodeEncoding we'd have to
                // do ~1+log(n) reads to read n characters.
                // Assume 1 byte can be 1 char unless m_2BytesPerChar is true.
                int numBytes = _2BytesPerChar ? 2 : 1;

                int r = await _stream.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                _charBytes[0] = (byte)r;
                if (r == -1)
                    numBytes = 0;
                if (numBytes == 2)
                {
                    r = await _stream.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                    _charBytes[1] = (byte)r;
                    if (r == -1)
                        numBytes = 1;
                }

                if (numBytes == 0)
                {
                    // Console.WriteLine("Found no bytes.  We're outta here.");
                    return -1;
                }

                Contract.Assert(numBytes == 1 || numBytes == 2, "BinaryReader::InternalReadOneChar assumes it's reading one or 2 bytes only.");

                try
                {

                    charsRead = _decoder.GetChars(_charBytes, 0, numBytes, _singleChar, 0);
                }
                catch
                {
                    // Handle surrogate char 

                    if (_stream.CanSeek)
                        _stream.Seek((posSav - _stream.Position), SeekOrigin.Current);
                    // else - we can't do much here

                    throw;
                }

                Contract.Assert(charsRead < 2, "InternalReadOneChar - assuming we only got 0 or 1 char, not 2!");
                //                Console.WriteLine("That became: " + charsRead + " characters.");
            }
            if (charsRead == 0)
                return -1;
            return _singleChar[0];
        }

        //[SecuritySafeCritical]
        public virtual async Task<char[]> ReadCharsAsync(int count, CancellationToken cancellationToken = default)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException("count");
            }
            Contract.Ensures(Contract.Result<char[]>() != null);
            Contract.Ensures(Contract.Result<char[]>().Length <= count);
            Contract.EndContractBlock();
            if (_stream == null)
            {
                __Error.FileNotOpen();
            }

            if (count == 0)
            {
                return Array.Empty<char>();
            }

            // SafeCritical: we own the chars buffer, and therefore can guarantee that the index and count are valid
            char[] chars = new char[count];
            int n = await InternalReadCharsAsync(chars, 0, count, cancellationToken).ConfigureAwait(false);
            if (n != count)
            {
                char[] copy = new char[n];
                Buffer.BlockCopy(chars, 0, copy, 0, 2 * n); // sizeof(char)
                chars = copy;
            }

            return chars;
        }

        public virtual Task<int> ReadAsync(byte[] buffer, int index, int count, CancellationToken cancellationToken = default)
        {
            if (buffer == null)
                throw new ArgumentNullException("buffer");
            if (index < 0)
                throw new ArgumentOutOfRangeException("index");
            if (count < 0)
                throw new ArgumentOutOfRangeException("count");
            if (buffer.Length - index < count)
                throw new ArgumentException("invalid offset length");
            Contract.Ensures(Contract.Result<int>() >= 0);
            Contract.Ensures(Contract.Result<int>() <= count);
            Contract.EndContractBlock();

            if (_stream == null) __Error.FileNotOpen();
            return _stream!.ReadAsync(buffer, index, count, cancellationToken);
        }

        public virtual async Task<byte[]> ReadBytesAsync(int count, CancellationToken cancellationToken = default)
        {
            if (count < 0) throw new ArgumentOutOfRangeException("count");
            Contract.Ensures(Contract.Result<byte[]>() != null);
            Contract.Ensures(Contract.Result<byte[]>().Length <= Contract.OldValue(count));
            Contract.EndContractBlock();
            if (_stream == null) __Error.FileNotOpen();

            if (count == 0)
            {
                return Array.Empty<byte>();
            }

            byte[] result = new byte[count];

            int numRead = 0;
            do
            {
                int n = await _stream!.ReadAsync(result, numRead, count, cancellationToken).ConfigureAwait(false);
                if (n == 0)
                    break;
                numRead += n;
                count -= n;
            } while (count > 0);

            if (numRead != result.Length)
            {
                // Trim array.  This should happen on EOF & possibly net streams.
                byte[] copy = new byte[numRead];
                Buffer.BlockCopy(result, 0, copy, 0, numRead);
                result = copy;
            }

            return result;
        }

        protected virtual async Task FillBufferAsync(int numBytes, CancellationToken cancellationToken)
        {
            if (_buffer != null && (numBytes < 0 || numBytes > _buffer.Length))
            {
                throw new ArgumentOutOfRangeException("numBytes");
            }
            int bytesRead = 0;
            if (_stream == null) __Error.FileNotOpen();

            int n;
            // Need to find a good threshold for calling ReadByte() repeatedly
            // vs. calling Read(byte[], int, int) for both buffered & unbuffered
            // streams.
            if (numBytes == 1)
            {
                n = await _stream!.ReadByteAsync(cancellationToken).ConfigureAwait(false);
                if (n == -1)
                    __Error.EndOfFile();
                _buffer![0] = (byte)n;
                return;
            }

            do
            {
                n = await _stream!.ReadAsync(_buffer, bytesRead, numBytes - bytesRead, cancellationToken).ConfigureAwait(false);
                if (n == 0)
                {
                    __Error.EndOfFile();
                }
                bytesRead += n;
            } while (bytesRead < numBytes);
        }

        internal protected async Task<int> Read7BitEncodedIntAsync(CancellationToken ct)
        {
            // Read out an Int32 7 bits at a time.  The high bit
            // of the byte when on means to continue reading more bytes.
            int count = 0;
            int shift = 0;
            byte b;
            do
            {
                // Check for a corrupted stream.  Read a max of 5 bytes.
                // In a future version, add a DataFormatException.
                if (shift == 5 * 7)  // 5 bytes max per Int32, shift += 7
                    throw new FormatException("Format_Bad7BitInt32");

                // ReadByte handles end of stream cases for us.
                b = await ReadByteAsync(ct).ConfigureAwait(false);
                count |= (b & 0x7F) << shift;
                shift += 7;
            } while ((b & 0x80) != 0);
            return count;
        }

        #region DecoderNLS

        // code here is used to to deal with accessing the HasState property of the non-public DecoderNLS class
        // which most popular encodings appear to be using

        private static readonly Type T_DECODER_NLS = typeof(Decoder).Assembly.GetType("System.Text.DecoderNLS");
        private static readonly Lazy<Func<Decoder, bool>> DecoderNLS_HasState = new(CreateDecoderNLS_HasState_Delegate);

        private static Func<Decoder, bool> CreateDecoderNLS_HasState_Delegate()
        {
            var exp_dec = Expression.Parameter(typeof(Decoder));
            var exp_convertToNLS = Expression.Convert(exp_dec, T_DECODER_NLS);
            var exp_HasState = Expression.Property(exp_convertToNLS, "HasState");
            var lambda = Expression.Lambda(exp_HasState, exp_dec);
            return (Func<Decoder, bool>)lambda.Compile();
        }

        private static readonly Dictionary<Type, bool> DecoderNLS_Cache = new();
        private static bool CheckDecoderNLS_And_HasState(Decoder decoderInQuestion)
        {
            if (!DecoderNLS_Cache.TryGetValue(decoderInQuestion.GetType(), out bool isNLS))
            {
                DecoderNLS_Cache[decoderInQuestion.GetType()] = isNLS =
                    T_DECODER_NLS.IsAssignableFrom(decoderInQuestion.GetType());
            }

            if (!isNLS)
                return false;

            return DecoderNLS_HasState.Value(decoderInQuestion);
        }
        #endregion
    }

}