using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using Sparrow.Binary;
using Sparrow.Utils;

namespace Sparrow.Json
{
    public class LazyStringValueComparer : IEqualityComparer<LazyStringValue>
    {
        public static readonly LazyStringValueComparer Instance = new LazyStringValueComparer();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(LazyStringValue x, LazyStringValue y)
        {
            if (x == y)
                return true;
            if (x == null || y == null)
                return false;
            return x.Equals(y);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(LazyStringValue obj)
        {
            unsafe
            {
                //PERF: JIT will remove the corresponding line based on the target architecture using dead code removal.                                 
                if (IntPtr.Size == 4)
                    return (int)Hashing.XXHash32.CalculateInline(obj.Buffer, obj.Size);
                return (int)Hashing.XXHash64.CalculateInline(obj.Buffer, (ulong)obj.Size);
            }
        }
    }

    public struct LazyStringValueStructComparer : IEqualityComparer<LazyStringValue>
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(LazyStringValue x, LazyStringValue y)
        {
            if (x == y)
                return true;
            if (x == null || y == null)
                return false;
            return x.CompareTo(y) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetHashCode(LazyStringValue obj)
        {
            unsafe
            {
                //PERF: JIT will remove the corresponding line based on the target architecture using dead code removal.                                 
                if (IntPtr.Size == 4)
                    return (int)Hashing.XXHash32.CalculateInline(obj.Buffer, obj.Size);
                return (int)Hashing.XXHash64.CalculateInline(obj.Buffer, (ulong)obj.Size);
            }
        }
    }

    // PERF: Sealed because in CoreCLR 2.0 it will devirtualize virtual calls methods like GetHashCode.
    public sealed unsafe class LazyStringValue : IComparable<string>, IEquatable<string>,
        IComparable<LazyStringValue>, IEquatable<LazyStringValue>, IDisposable, IComparable
    {
        internal readonly JsonOperationContext _context;
        private string _string;

        private byte* _buffer;
        public byte this[int index] => Buffer[index];
        public byte* Buffer => _buffer;

        private int _size;
        public int Size => _size;

        private int _length;
        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                // Lazily load the length from the buffer. This is an O(n)
                if (_length == -1 && Buffer != null)
                    _length = Encodings.Utf8.GetCharCount(Buffer, Size);
                return _length;
            }
        }

        // todo: use span here
        [ThreadStatic]
        private static string _lazyStringTempBuffer;

        [ThreadStatic]
        private static LazyStringValue _lastLSV;

        [ThreadStatic]
        private static char[] _lazyCharArrayStringBuffer;

        [ThreadStatic]
        private static byte[] _lazyStringTempComparisonBuffer;

        public int[] EscapePositions;
        public AllocatedMemoryData AllocatedMemoryData;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public LazyStringValue(string str, byte* buffer, int size, JsonOperationContext context)
        {
            Debug.Assert(context != null);
            _context = context;
            _size = size;
            _buffer = buffer;
            _string = str;
            _length = -1;
        }

        static LazyStringValue()
        {
            ThreadLocalCleanup.ReleaseThreadLocalState += CleanBuffers;
        }

        public static void CleanBuffers()
        {
            _lazyStringTempBuffer = null;
            _lazyStringTempComparisonBuffer = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int TranslateIndexFromLSVToTempBuffer(int index)
        {
            return _lazyStringTempBuffer.Length - Length + index;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int TranslateIndexFromTempBufferToLSV(int index)
        {
            var res = index - (_lazyStringTempBuffer.Length - Length);

            if (res < 0)
                return -1;
            return res;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(string other)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (_string != null)
                return string.Equals(_string, other, StringComparison.Ordinal);

            var sizeInBytes = Encodings.Utf8.GetMaxByteCount(other.Length);

            if (_lazyStringTempComparisonBuffer == null || _lazyStringTempComparisonBuffer.Length < other.Length)
                _lazyStringTempComparisonBuffer = new byte[Bits.NextPowerOf2(sizeInBytes)];

            fixed (char* pOther = other)
            fixed (byte* pBufferPtr = _lazyStringTempComparisonBuffer)
            {
                var pBuffer = pBufferPtr + _lazyStringTempComparisonBuffer.Length - Length;
                var tmpSize = Encodings.Utf8.GetBytes(pOther, other.Length, pBuffer, sizeInBytes);
                if (Size != tmpSize)
                    return false;

                return Memory.CompareInline(Buffer, pBuffer, tmpSize) == 0;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(LazyStringValue other)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            int size = Size;
            if (other.Size != size)
                return false;

            return Memory.CompareInline(Buffer, other.Buffer, size) == 0;
        }

        public int CompareTo(string other)
        {
            if (_string != null)
                return string.Compare(_string, other, StringComparison.Ordinal);

            var sizeInBytes = Encodings.Utf8.GetMaxByteCount(other.Length);

            if (_lazyStringTempComparisonBuffer == null || _lazyStringTempComparisonBuffer.Length < other.Length)
                _lazyStringTempComparisonBuffer = new byte[Bits.NextPowerOf2(sizeInBytes)];

            fixed (char* pOther = other)
            fixed (byte* pBuffer = _lazyStringTempComparisonBuffer)
            {
                var tmpSize = Encodings.Utf8.GetBytes(pOther, other.Length, pBuffer, sizeInBytes);
                return Compare(pBuffer, tmpSize);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(LazyStringValue other)
        {
            if (other.Buffer == Buffer && other.Size == Size)
                return 0;
            return Compare(other.Buffer, other.Size);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int Compare(byte* other, int otherSize)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();
            int size = Size;
            var result = Memory.CompareInline(Buffer, other, Math.Min(size, otherSize));
            return result == 0 ? size - otherSize : result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(LazyStringValue self, LazyStringValue str)
        {
            if (ReferenceEquals(self, str))
                return true;

            if (ReferenceEquals(self, null))
                return false;
            if (ReferenceEquals(str, null))
                return false;

            return self.Equals(str);
        }

        public static bool operator !=(LazyStringValue self, LazyStringValue str)
        {
            return !(self == str);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(LazyStringValue self, string str)
        {
            if (ReferenceEquals(self, null) && str == null)
                return true;
            if (ReferenceEquals(self, null) || str == null)
                return false;
            return self.Equals(str);
        }

        public static bool operator !=(LazyStringValue self, string str)
        {
            return !(self == str);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator string(LazyStringValue self)
        {
            if (self == null)
                return null;

            if (self.IsDisposed)
                self.ThrowAlreadyDisposed();

            return self._string ??
                (self._string = Encodings.Utf8.GetString(self._buffer, self._size));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator byte[] (LazyStringValue self)
        {
            var valueAsString = (string)self;
            return Convert.FromBase64String(valueAsString);
        }

        public static explicit operator double(LazyStringValue self)
        {
            var valueAsString = (string)self;
            if (double.TryParse(valueAsString, NumberStyles.Any, CultureInfo.InvariantCulture, out double result))
                return result;

            throw new InvalidCastException($"Couldn't convert {valueAsString} (LazyStringValue) to double");
        }

        public static explicit operator float(LazyStringValue self)
        {
            var valueAsString = (string)self;
            if (float.TryParse(valueAsString, NumberStyles.Any, CultureInfo.InvariantCulture, out float result))
                return result;

            throw new InvalidCastException($"Couldn't convert {valueAsString} (LazyStringValue) to float");
        }

        public override bool Equals(object obj)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (ReferenceEquals(obj, null))
                return false;

            var s = obj as string;
            if (s != null)
                return Equals(s);
            var comparer = obj as LazyStringValue;
            if (comparer != null)
                return Equals(comparer);

            return ReferenceEquals(obj, this);
        }

        public override int GetHashCode()
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (IntPtr.Size == 4)
                return (int)Hashing.XXHash32.CalculateInline(Buffer, Size);
            else
                return (int)Hashing.XXHash64.CalculateInline(Buffer, (ulong)Size);
        }

        public override string ToString()
        {
            return (string)this; // invoke the implicit string conversion
        }

        public int CompareTo(object obj)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (obj == null)
                return 1;

            var lsv = obj as LazyStringValue;

            if (lsv != null)
                return CompareTo(lsv);

            var s = obj as string;

            if (s != null)
                return CompareTo(s);

            throw new NotSupportedException($"Cannot compare LazyStringValue to object of type {obj.GetType().Name}");
        }

        public bool IsDisposed;

        private void ThrowAlreadyDisposed()
        {
            throw new ObjectDisposedException(nameof(LazyStringValue));
        }

        public void Dispose()
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (AllocatedMemoryData != null)
            {
                _context.ReturnMemory(AllocatedMemoryData);
            }
            IsDisposed = true;
        }

        public bool Contains(char value)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (_string != null)
                return _string.Contains(value);


            InitTempBufferFromPtr(Buffer, Length);

            return _lazyStringTempBuffer.IndexOf(value, TranslateIndexFromLSVToTempBuffer(0), Length) >= 0;
        }

#if NETCOREAPP
        public bool Contains(char value, StringComparison comparisonType)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (_string != null)
                return _string.Contains(value, comparisonType);

            return ToString().Contains(value, comparisonType);
        }
#endif

        public bool Contains(string value)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (_string != null)
                return _string.Contains(value);

            InitTempBufferFromPtr(Buffer, Length);

            return _lazyStringTempBuffer.IndexOf(value, TranslateIndexFromLSVToTempBuffer(0), Length) >= 0;
        }

#if NETCOREAPP
        public bool Contains(string value, StringComparison comparisonType)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (_string != null)
                return _string.Contains(value, comparisonType);

            return ToString().Contains(value, comparisonType);
        }
#endif

        public bool EndsWith(string value)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (_string != null)
                return _string.EndsWith(value);

            if (value == null)
                throw new ArgumentNullException(nameof(value));
            // Every UTF8 character uses at least 1 byte
            if (value.Length > Size)
                return false;
            if (value.Length == 0)
                return true;

            // We are assuming these values are going to be relatively constant throughout the object lifespan
            LazyStringValue converted = _context.GetLazyStringForFieldWithCaching(value);
            return EndsWith(converted);
        }

        public bool EndsWith(LazyStringValue value)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();
            if (value.Size > Size)
                return false;

            return Memory.Compare(Buffer + (Size - value.Size), value.Buffer, value.Size) == 0;
        }

        public bool EndsWith(string value, StringComparison comparisonType)
        {
            InitTempBufferFromPtr(Buffer, Length);

            ValidateIndexesForBackwardScan(Length - 1, value.Length);

            if (value.Length > Length)
                return false;

            return _lazyStringTempBuffer.EndsWith(value, comparisonType);
        }

#if !NETSTANDARD1_3
        public bool EndsWith(string value, bool ignoreCase, CultureInfo culture)
        {
            InitTempBufferFromPtr(Buffer, Length);

            ValidateIndexesForBackwardScan(Length - 1, value.Length);

            if (value.Length > Length)
                return false;

            return _lazyStringTempBuffer.EndsWith(value, ignoreCase, culture);
        }
#endif

        public bool EndsWith(char value)
        {
            InitTempBufferFromPtr(Buffer, Length);

            return _lazyStringTempBuffer[_lazyStringTempBuffer.Length - 1] == value;
        }

        public int IndexOf(char value)
        {
            return IndexOf(value, 0, Length);
        }

#if NETCOREAPP
        public int IndexOf(char value, StringComparison comparisonType)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (_string != null)
                return _string.IndexOf(value, comparisonType);

            return ToString().IndexOf(value, comparisonType);
        }
#endif

        public int IndexOf(char value, int startIndex)
        {
            return IndexOf(value, startIndex, Length - startIndex);
        }

        public int IndexOf(char value, int startIndex, int count)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (_string != null)
                return _string.IndexOf(value, startIndex, count);

            ValidateIndexesForForwardScan(startIndex, count);

            InitTempBufferFromPtr(Buffer, Length);

            return TranslateIndexFromTempBufferToLSV(
                _lazyStringTempBuffer.IndexOf(value, TranslateIndexFromLSVToTempBuffer(startIndex), count));
        }

        public int IndexOf(string value)
        {
            return IndexOf(value, 0, Length, StringComparison.CurrentCulture);
        }

        public int IndexOf(string value, int startIndex)
        {
            return IndexOf(value, startIndex, Length - startIndex, StringComparison.CurrentCulture);
        }

        public int IndexOf(string value, int startIndex, int count)
        {
            return IndexOf(value, startIndex, count, StringComparison.CurrentCulture);
        }

        public int IndexOf(string value, int startIndex, StringComparison comparisonType)
        {
            return IndexOf(value, startIndex, Length - startIndex, comparisonType);
        }

        public int IndexOf(string value, StringComparison comparisonType)
        {
            return IndexOf(value, 0, Length, comparisonType);
        }

        public int IndexOf(string value, int startIndex, int count, StringComparison comparisonType)
        {
            if (_string != null)
                return _string.IndexOf(value, startIndex, count, comparisonType);

            ValidateIndexesForForwardScan(startIndex, count);

            InitTempBufferFromPtr(Buffer, Length);

            return TranslateIndexFromTempBufferToLSV(
                _lazyStringTempBuffer.IndexOf(value, TranslateIndexFromLSVToTempBuffer(startIndex), count, comparisonType));
        }       

        public int IndexOfAny(char[] anyOf)
        {
            return IndexOfAny(anyOf, 0, Length);
        }

        public int IndexOfAny(char[] anyOf, int startIndex)
        {
            return IndexOfAny(anyOf, startIndex, Length - startIndex);
        }

        public int IndexOfAny(char[] anyOf, int startIndex, int count)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (_string != null)
                return _string.IndexOfAny(anyOf, startIndex, count);

            ValidateIndexesForForwardScan(startIndex, count);

            InitTempBufferFromPtr(Buffer, Length);

            return TranslateIndexFromTempBufferToLSV(
                _lazyStringTempBuffer.IndexOfAny(anyOf, TranslateIndexFromLSVToTempBuffer(startIndex), count));
        }        

        public int LastIndexOf(char value)
        {
            return LastIndexOf(value, Length - 1, Length);
        }

        public int LastIndexOf(char value, int startIndex)
        {
            return LastIndexOf(value, startIndex, startIndex + 1);
        }

        public int LastIndexOf(char value, int startIndex, int count)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (_string != null)
                return _string.LastIndexOf(value, startIndex, count);

            ValidateIndexesForBackwardScan(startIndex, count);

            InitTempBufferFromPtr(Buffer, Length);

            return TranslateIndexFromTempBufferToLSV(
                _lazyStringTempBuffer.LastIndexOf(value, TranslateIndexFromLSVToTempBuffer(startIndex), count));
        }

        public int LastIndexOf(string value)
        {
            return LastIndexOf(value, Length - 1, Length,StringComparison.CurrentCulture);            
        }

        public int LastIndexOf(string value, StringComparison comparisonType)
        {
            return LastIndexOf(value, Length - 1, Length, comparisonType);
        }

        public int LastIndexOf(string value, int startIndex)
        {
            return LastIndexOf(value, startIndex, Length - 1, StringComparison.CurrentCulture);
        }

        public int LastIndexOf(string value, int startIndex, int count)
        {
            return LastIndexOf(value, startIndex, count, StringComparison.CurrentCulture);
        }

        public int LastIndexOf(string value, int startIndex, StringComparison comparisonType)
        {
            return LastIndexOf(value, startIndex, Length, comparisonType);
        }

        public int LastIndexOf(string value, int startIndex, int count, StringComparison comparisonType)
        {
            ValidateIndexesForBackwardScan(startIndex, count);

            if (value.Length > Length || 
                startIndex + 1 - value.Length < 0 || 
                count < value.Length)
                return -1;
                       
            InitTempBufferFromPtr(Buffer, Length);

            return TranslateIndexFromTempBufferToLSV(
                _lazyStringTempBuffer.LastIndexOf(value, TranslateIndexFromLSVToTempBuffer(startIndex), count, comparisonType));           
        }

        public int LastIndexOfAny(char[] anyOf)
        {
            return LastIndexOfAny(anyOf, Length - 1, Length);
        }

        public int LastIndexOfAny(char[] anyOf, int startIndex)
        {
            return LastIndexOfAny(anyOf, startIndex, startIndex + 1);
        }

        public int LastIndexOfAny(char[] anyOf, int startIndex, int count)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();
            if (_string != null)
                return _string.LastIndexOfAny(anyOf, startIndex, count);

            InitTempBufferFromPtr(Buffer, Length);

            return TranslateIndexFromTempBufferToLSV(
                _lazyStringTempBuffer.LastIndexOfAny(anyOf, TranslateIndexFromLSVToTempBuffer(startIndex), count));
        }

        public bool StartsWith(string value)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (_string != null)
                return _string.StartsWith(value);

            if (value == null)
                throw new ArgumentNullException(nameof(value));
            // Every UTF8 character uses at least 1 byte
            if (value.Length > Size)
                return false;
            if (value.Length == 0)
                return true;

            // We are assuming these values are going to be relatively constant throughout the object lifespan
            LazyStringValue converted = _context.GetLazyStringForFieldWithCaching(value);
            return StartsWith(converted);
        }

        public bool StartsWith(LazyStringValue value)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (value.Size > Size)
                return false;

            return Memory.Compare(Buffer, value.Buffer, value.Size) == 0;
        }

        public bool StartsWith(string value, StringComparison comparisionType)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (value.Length > Size)
                return false;

            if (_string != null)
                return _string.StartsWith(value, comparisionType);

            InitTempBufferFromPtr(Buffer, Length);

            return TranslateIndexFromTempBufferToLSV(
                _lazyStringTempBuffer.IndexOf(value, TranslateIndexFromLSVToTempBuffer(0), 1, comparisionType)) == 0;
        }

#if !NETSTANDARD1_3
        public bool StartsWith(string value, bool ignoreCase, CultureInfo culture)
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            if (value.Length > Size)
                return false;

            // yes, here we'll use the "expensive" version, maybe we'll want to 
            // have another buffer for this operation, but it seems rare enough
            // right now
            return ToString().StartsWith(value, ignoreCase, culture);
        }
#endif

        public bool StartsWith(char value)
        {
            if (_string != null)
                return _string.IndexOf(value) == 0;

            InitTempBufferFromPtr(Buffer, Length);

            return _lazyStringTempBuffer.IndexOf(value, TranslateIndexFromLSVToTempBuffer(0), 1) == 0;
        }

        public string Insert(int startIndex, string value)
        {
            return ToString().Insert(startIndex, value);
        }

        public string PadLeft(int totalWidth)
        {
            return ToString().PadLeft(totalWidth);
        }

        public string PadLeft(int totalWidth, char paddingChar)
        {
            return ToString().PadLeft(totalWidth, paddingChar);
        }

        public string PadRight(int totalWidth)
        {
            return ToString().PadRight(totalWidth);
        }

        public string PadRight(int totalWidth, char paddingChar)
        {
            return ToString().PadRight(totalWidth, paddingChar);
        }

        public string Remove(int startIndex)
        {
            return ToString().Remove(startIndex);
        }

        public string Remove(int startIndex, int count)
        {
            return ToString().Remove(startIndex, count);
        }

        public string Replace(char oldChar, char newChar)
        {
            return ToString().Replace(oldChar, newChar);
        }

        public string Replace(string oldValue, string newValue)
        {
            return ToString().Replace(oldValue, newValue);
        }

        public string Replace(string oldValue, string newValue, bool ignoreCase, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        public string Replace(string oldValue, string newValue, StringComparison comparisonType)
        {
            throw new NotSupportedException();
        }

        public string Substring(int startIndex)
        {
            return ToString().Substring(startIndex);
        }

        public string Substring(int startIndex, int length)
        {
            return ToString().Substring(startIndex, length);
        }

        public string[] Split(char separator, StringSplitOptions options = StringSplitOptions.None)
        {
            return Split(new[] { separator }, options);
        }

        public string[] Split(char separator, int count, StringSplitOptions options = StringSplitOptions.None)
        {
            return ToString().Split(new[] { separator }, count, options);
        }

        public string[] Split(string separator, StringSplitOptions options = StringSplitOptions.None)
        {
            return Split(new[] { separator }, options);
        }

        public string[] Split(string separator, int count, StringSplitOptions options = StringSplitOptions.None)
        {
            return ToString().Split(new[] { separator }, count, options);
        }

        public string[] Split(char[] separator)
        {
            return ToString().Split(separator, StringSplitOptions.None);
        }

        public string[] Split(char[] separator, int count)
        {
            return ToString().Split(separator, count, StringSplitOptions.None);
        }

        public string[] Split(char[] separator, StringSplitOptions options)
        {
            return ToString().Split(separator, options);
        }

        public string[] Split(char[] separator, int count, StringSplitOptions options)
        {
            return ToString().Split(separator, count, options);
        }

        public string[] Split(string[] separator, StringSplitOptions options = StringSplitOptions.None)
        {
            return ToString().Split(separator, options);
        }

        public string[] Split(string[] separator, int count, StringSplitOptions options = StringSplitOptions.None)
        {
            return ToString().Split(separator, count, options);
        }        

        public char[] ToCharArray()
        {
            return ToString().ToCharArray();
        }

        public char[] ToCharArray(int startIndex, int length)
        {
            return ToString().ToCharArray(startIndex, length);
        }

        public string ToLower()
        {
            return ToString().ToLower();
        }

#if !NETSTANDARD1_3
        public string ToLower(CultureInfo culture)
        {
            return ToString().ToLower(culture);
        }
#endif

        public string ToLowerInvariant()
        {
            return ToString().ToLowerInvariant();
        }

        public string ToUpper()
        {
            return ToString().ToUpper();
        }

#if !NETSTANDARD1_3
        public string ToUpper(CultureInfo culture)
        {
            return ToString().ToUpper(culture);
        }
#endif

        public string ToUpperInvariant()
        {
            return ToString().ToUpperInvariant();
        }

        public string Trim()
        {
            return ToString().Trim();
        }

        public string Trim(char trimChar)
        {
            return ToString().Trim(trimChar);
        }

        public string Trim(params char[] trimChars)
        {
            return ToString().Trim(trimChars);
        }

        public string TrimEnd()
        {
            return ToString().TrimEnd();
        }

        public string TrimEnd(char trimChar)
        {
            return ToString().TrimEnd(trimChar);
        }

        public string TrimEnd(params char[] trimChars)
        {
            return ToString().TrimEnd(trimChars);
        }

        public string TrimStart()
        {
            return ToString().TrimStart();
        }

        public string TrimStart(char trimChar)
        {
            return ToString().TrimStart(trimChar);
        }

        public string TrimStart(params char[] trimChars)
        {
            return ToString().TrimStart(trimChars);
        }

        public string Reverse()
        {
            if (IsDisposed)
                ThrowAlreadyDisposed();

            var buffer = _buffer;

            // in case we received a string, with no _buffer
            if (buffer == null)
            {
                fixed (char* stringBuffer = _string)
                {
                    buffer = (byte*)stringBuffer;
                    return GetReversedStringFromBuffer(buffer);
                }
            }

            return GetReversedStringFromBuffer(buffer);
        }

        private string GetReversedStringFromBuffer(byte* buffer)
        {

            // todo: improve this befoe PR!! we don't really need the buffer
            var maxCharCount = Encodings.Utf8.GetMaxCharCount(Length);

            if (_lazyCharArrayStringBuffer == null ||
                _lazyCharArrayStringBuffer.Length < maxCharCount)
            {
                _lazyCharArrayStringBuffer = new char[Bits.NextPowerOf2(maxCharCount)];
            }

            fixed (char* pCharsFromArray = _lazyCharArrayStringBuffer)
            {
                var chars = Encodings.Utf8.GetChars(buffer, Length, pCharsFromArray, _lazyCharArrayStringBuffer.Length);

                Array.Reverse(_lazyCharArrayStringBuffer, 0, chars);
                return new string(_lazyCharArrayStringBuffer, 0, chars);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Renew(string str, byte* buffer, int size)
        {
            Debug.Assert(size >= 0);
            _size = size;
            _buffer = buffer;
            _string = str;
            _length = -1;
            EscapePositions = null;
            IsDisposed = false;
            AllocatedMemoryData = null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsControlCodeCharacter(out byte b)
        {
            Debug.Assert(Size == 1);

            b = Buffer[0];
            // control code characters
            return b < 32 || (b >= 127 && b <= 159);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateIndexesForBackwardScan(int startIndex, int count)
        {
            if (startIndex < 0 || count < 0)
                ThrowArgumentOutOfRangeException("count or startIndex is negative.");

            if (startIndex >= Length || count > Length)
                ThrowArgumentOutOfRangeException("count or startIndex is bigger then string size.");
           
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ValidateIndexesForForwardScan(int startIndex, int count)
        {
            if (startIndex < 0 || count < 0)
                ThrowArgumentOutOfRangeException("count or startIndex is negative.");

            if (startIndex >= Length)
                ThrowArgumentOutOfRangeException(nameof(startIndex), "startIndex is greater than the length of this string.");
            
        }

        public void ThrowArgumentOutOfRangeException(string message)
        {
            throw new ArgumentOutOfRangeException(message);
        }
        public void ThrowArgumentOutOfRangeException(string field, string message)
        {
            throw new ArgumentOutOfRangeException(field, message);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InitTempBufferFromPtr(byte* ptr, int length, bool alignToStart = false)
        {
            // we've already initalized the buffer with the neede values
            if (_lastLSV == this && _lazyStringTempBuffer != null)
                return;

            _lastLSV = this;

            // resize buffer if needed
            if (_lazyStringTempBuffer == null || _lazyStringTempBuffer.Length < length)
                _lazyStringTempBuffer = new string('\0', Bits.NextPowerOf2(length));

            // we want to copy to the end of the buffer, so all the "end" oriented functions
            // will be supported, the satrt oriented functions has support for "startsWith"
            // anyway
            fixed (char* pChars = _lazyStringTempBuffer)
            {
                if (alignToStart)
                {
                    Encodings.Utf8.GetChars(ptr, Size, pChars, length);
                }
                else
                {
                    Encodings.Utf8.GetChars(ptr, Size, pChars + _lazyStringTempBuffer.Length - length, length);
                }
            }
        }
    }
}
