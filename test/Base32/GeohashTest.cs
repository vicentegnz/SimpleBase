﻿using NUnit.Framework;
using SimpleBase;
using System;
using System.Collections.Generic;
using System.Text;

namespace SimpleBaseTest.Base32Test
{
    [TestFixture]
    class GeohashTest
    {
        [Test]
        public void Decode_SmokeTest()
        {
            const string input = "ezs42";
            var result = Base32.Geohash.Decode(input);
            var expected = new byte[] { 0b01101111, 0b11110000, 0b01000001 };
            Assert.AreEqual(expected, result.ToArray());
        }

        [Test]
        public void Encode_SmokeTest()
        {
            const string expected = "ezs42";
            var input = new byte[] { 0b01101111, 0b11110000, 0b01000001 };
            var result = Base32.Geohash.Encode(input);
            Assert.AreEqual(expected, result);
        }
    }
}
