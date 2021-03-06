﻿// <copyright file="Base58.cs" company="Sedat Kapanoglu">
// Copyright (c) 2014-2019 Sedat Kapanoglu
// Licensed under Apache-2.0 License (see LICENSE.txt file for details)
// </copyright>

using System;

namespace SimpleBase
{
    /// <summary>
    /// Base58 Encoding/Decoding implementation.
    /// </summary>
    /// <remarks>
    /// Base58 doesn't implement a Stream-based interface because it's not feasible to use
    /// on large buffers.
    /// </remarks>
    public sealed class Base58 : IBaseEncoder, INonAllocatingBaseEncoder
    {
        private static Lazy<Base58> bitcoin = new Lazy<Base58>(() => new Base58(Base58Alphabet.Bitcoin));
        private static Lazy<Base58> ripple = new Lazy<Base58>(() => new Base58(Base58Alphabet.Ripple));
        private static Lazy<Base58> flickr = new Lazy<Base58>(() => new Base58(Base58Alphabet.Flickr));

        /// <summary>
        /// Initializes a new instance of the <see cref="Base58"/> class
        /// using a custom alphabet.
        /// </summary>
        /// <param name="alphabet">Alphabet to use.</param>
        public Base58(Base58Alphabet alphabet)
        {
            this.Alphabet = alphabet;
        }

        /// <summary>
        /// Gets Bitcoin flavor.
        /// </summary>
        public static Base58 Bitcoin => bitcoin.Value;

        /// <summary>
        /// Gets Ripple flavor.
        /// </summary>
        public static Base58 Ripple => ripple.Value;

        /// <summary>
        /// Gets Flickr flavor.
        /// </summary>
        public static Base58 Flickr => flickr.Value;

        /// <summary>
        /// Gets the encoding alphabet.
        /// </summary>
        public Base58Alphabet Alphabet { get; }

        /// <inheritdoc/>
        public int GetSafeByteCountForDecoding(ReadOnlySpan<char> text)
        {
            const int reductionFactor = 733; // https://github.com/bitcoin/bitcoin/blob/master/src/base58.cpp#L48

            return ((text.Length + 1) * reductionFactor / 1000) + 1;
        }

        /// <inheritdoc/>
        public int GetSafeCharCountForEncoding(ReadOnlySpan<byte> bytes)
        {
            int bytesLen = bytes.Length;
            int numZeroes = getZeroCount(bytes, bytesLen);

            return getSafeCharCountForEncoding(bytesLen, numZeroes);
        }

        /// <summary>
        /// Encode to Base58 representation.
        /// </summary>
        /// <param name="bytes">Bytes to encode.</param>
        /// <returns>Encoded string.</returns>
        public unsafe string Encode(ReadOnlySpan<byte> bytes)
        {
            int bytesLen = bytes.Length;
            if (bytesLen == 0)
            {
                return string.Empty;
            }

            int numZeroes = getZeroCount(bytes, bytesLen);
            int outputLen = getSafeCharCountForEncoding(bytesLen, numZeroes);
            string output = new string('\0', outputLen);

            // 29.70µs (64.9x slower)   | 31.63µs (40.8x slower)
            // 30.93µs (first tryencode impl)
            // 29.36µs (single pass translation/copy + shift over multiply)
            // 31.04µs (70x slower)     | 24.71µs (34.3x slower)
            fixed (byte* inputPtr = bytes)
            fixed (char* outputPtr = output)
            {
                if (!internalEncode(inputPtr, bytesLen, outputPtr, outputLen, numZeroes, out int length))
                {
                    throw new InvalidOperationException("Output buffer with insufficient size generated");
                }

                return output[..length];
            }
        }

        /// <summary>
        /// Decode a Base58 representation.
        /// </summary>
        /// <param name="text">Base58 encoded text.</param>
        /// <returns>Array of decoded bytes.</returns>
        public unsafe Span<byte> Decode(ReadOnlySpan<char> text)
        {
            int textLen = text.Length;
            if (textLen == 0)
            {
                return Array.Empty<byte>();
            }

            int outputLen = GetSafeByteCountForDecoding(text);
            char zeroChar = Alphabet.Value[0];
            int numZeroes = getPrefixCount(text, textLen, zeroChar);
            byte[] output = new byte[outputLen];
            fixed (char* inputPtr = text)
            fixed (byte* outputPtr = output)
            {
                if (!internalDecode(
                    inputPtr,
                    textLen,
                    outputPtr,
                    outputLen,
                    numZeroes,
                    out int numBytesWritten))
                {
                    throw new InvalidOperationException("Output buffer was too small while decoding Base58");
                }

                return output[..numBytesWritten];
            }
        }

        /// <inheritdoc/>
        public unsafe bool TryEncode(ReadOnlySpan<byte> input, Span<char> output, out int numCharsWritten)
        {
            fixed (byte* inputPtr = input)
            fixed (char* outputPtr = output)
            {
                int inputLen = input.Length;
                int numZeroes = getZeroCount(input, inputLen);
                return internalEncode(inputPtr, inputLen, outputPtr, output.Length, numZeroes, out numCharsWritten);
            }
        }

        /// <inheritdoc/>
        public unsafe bool TryDecode(ReadOnlySpan<char> input, Span<byte> output, out int numBytesWritten)
        {
            int inputLen = input.Length;
            if (inputLen == 0)
            {
                numBytesWritten = 0;
                return true;
            }

            int zeroCount = getPrefixCount(input, inputLen, Alphabet.Value[0]);
            fixed (char* inputPtr = input)
            fixed (byte* outputPtr = output)
            {
                return internalDecode(
                    inputPtr,
                    input.Length,
                    outputPtr,
                    output.Length,
                    zeroCount,
                    out numBytesWritten);
            }
        }

        private unsafe bool internalDecode(
            char* inputPtr,
            int inputLen,
            byte* outputPtr,
            int outputLen,
            int numZeroes,
            out int numBytesWritten)
        {
            char* pInputEnd = inputPtr + inputLen;
            char* pInput = inputPtr + numZeroes;
            if (pInput == pInputEnd)
            {
                if (numZeroes > outputLen)
                {
                    numBytesWritten = 0;
                    return false;
                }

                byte* pOutput = outputPtr;
                for (int i = 0; i < numZeroes; i++)
                {
                    *pOutput++ = 0;
                }

                numBytesWritten = numZeroes;
                return true;
            }

            var table = Alphabet.ReverseLookupTable;
            byte* pOutputEnd = outputPtr + outputLen - 1;
            byte* pMinOutput = pOutputEnd;
            while (pInput != pInputEnd)
            {
                char c = *pInput;
                int carry = table[c] - 1;
                if (carry < 0)
                {
                    throw EncodingAlphabet.InvalidCharacter(c);
                }

                for (byte* pOutput = pOutputEnd; pOutput >= outputPtr; pOutput--)
                {
                    carry += 58 * (*pOutput);
                    *pOutput = (byte)carry;
                    if (pMinOutput > pOutput && carry != 0)
                    {
                        pMinOutput = pOutput;
                    }

                    carry >>= 8;
                }

                pInput++;
            }

            int startIndex = (int)(pMinOutput - numZeroes - outputPtr);
            numBytesWritten = outputLen - startIndex;
            Buffer.MemoryCopy(outputPtr + startIndex, outputPtr, numBytesWritten, numBytesWritten);
            return true;
        }

        private unsafe bool internalEncode(
            byte* inputPtr,
            int inputLen,
            char* outputPtr,
            int outputLen,
            int numZeroes,
            out int numCharsWritten)
        {
            if (inputLen == 0)
            {
                numCharsWritten = 0;
                return true;
            }

            fixed (char* alphabetPtr = Alphabet.Value)
            {
                byte* pInput = inputPtr + numZeroes;
                byte* pInputEnd = inputPtr + inputLen;
                char zeroChar = alphabetPtr[0];

                // optimized path for an all zero buffer
                if (pInput == pInputEnd)
                {
                    if (outputLen < numZeroes)
                    {
                        numCharsWritten = 0;
                        return false;
                    }

                    for (int i = 0; i < numZeroes; i++)
                    {
                        *outputPtr++ = zeroChar;
                    }

                    numCharsWritten = numZeroes;
                    return true;
                }

                int length = 0;
                char* pOutput = outputPtr;
                char* pLastChar = pOutput + outputLen - 1;
                while (pInput != pInputEnd)
                {
                    int carry = *pInput;
                    int i = 0;
                    for (char* pDigit = pLastChar; (carry != 0 || i < length)
                        && pDigit >= outputPtr; pDigit--, i++)
                    {
                        carry += *pDigit << 8;
                        carry = Math.DivRem(carry, 58, out int remainder);
                        *pDigit = (char)remainder;
                    }

                    length = i;
                    pInput++;
                }

                var pOutputEnd = pOutput + outputLen;

                // copy the characters to the beginning of the buffer
                // and translate them at the same time. if no copying
                // is needed, this only acts as the translation phase.
                for (char* a = outputPtr + numZeroes, b = pOutputEnd - length;
                    b != pOutputEnd;
                    a++, b++)
                {
                    *a = alphabetPtr[*b];
                }

                // translate the zeroes at the start
                while (pOutput != pOutputEnd)
                {
                    char c = *pOutput;
                    if (c != '\0')
                    {
                        break;
                    }

                    *pOutput = alphabetPtr[c];
                    pOutput++;
                }

                int actualLen = numZeroes + length;

                numCharsWritten = actualLen;
                return true;
            }
        }

        private static unsafe int getZeroCount(ReadOnlySpan<byte> bytes, int bytesLen)
        {
            if (bytesLen == 0)
            {
                return 0;
            }

            int numZeroes = 0;
            fixed (byte* inputPtr = bytes)
            {
                var pInput = inputPtr;
                while (*pInput == 0 && numZeroes < bytesLen)
                {
                    numZeroes++;
                    pInput++;
                }
            }

            return numZeroes;
        }

        private static unsafe int getPrefixCount(ReadOnlySpan<char> input, int length, char value)
        {
            if (length == 0)
            {
                return 0;
            }

            int numZeroes = 0;
            fixed (char* inputPtr = input)
            {
                var pInput = inputPtr;
                while (*pInput == value && numZeroes < length)
                {
                    numZeroes++;
                    pInput++;
                }
            }

            return numZeroes;
        }

        private static int getSafeCharCountForEncoding(int bytesLen, int numZeroes)
        {
            const int growthPercentage = 138;

            return numZeroes + ((((bytesLen - numZeroes) * growthPercentage) / 100) + 1);
        }
    }
}