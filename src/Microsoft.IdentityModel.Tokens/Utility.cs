//------------------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.
// All rights reserved.
//
// This code is licensed under the MIT License.
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files(the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and / or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions :
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT.IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
// THE SOFTWARE.
//
//------------------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;

namespace Microsoft.IdentityModel.Tokens
{
    /// <summary>
    /// Contains some utility methods.
    /// </summary>
    public static class Utility
    {
        /// <summary>
        /// A string has been set as "empty" value.
        /// </summary>
        public const string Empty = "empty";

        /// <summary>
        /// A string has been set as "null" value.
        /// </summary>
        public const string Null = "null";

        /// <summary>
        /// Creates a copy of the byte array.
        /// </summary>
        /// <param name="src">the resource array.</param>
        /// <returns></returns>
        public static byte[] CloneByteArray(this byte[] src)
        {
            return (byte[])(src.Clone());
        }

        /// <summary>
        /// Serializes the list of strings into string as follows:
        /// 'str1','str2','str3' ...
        /// </summary>
        /// <param name="strings">
        /// The strings used to build a comma delimited string.
        /// </param>
        /// <returns>
        /// The single <see cref="string"/>.
        /// </returns>
        internal static string SerializeAsSingleCommaDelimitedString(IEnumerable<string> strings)
        {
            if (null == strings)
            {
                return Utility.Null;
            }

            StringBuilder sb = new StringBuilder();
            bool first = true;
            foreach (string str in strings)
            {
                if (first)
                {
                    sb.AppendFormat("{0}", str ?? Utility.Null);
                    first = false;
                }
                else
                {
                    sb.AppendFormat(", {0}", str ?? Utility.Null);
                }
            }

            if (first)
            {
                return Utility.Empty;
            }

            return sb.ToString();
        }

        /// <summary>
        /// true if the input string is https; otherwise, false.
        /// </summary>
        /// <param name="address">the input string.</param>
        /// <returns></returns>
        public static bool IsHttps(string address)
        {
            if (string.IsNullOrEmpty(address))
            {
                return false;
            }

            try
            {
                Uri uri = new Uri(address);
                return IsHttps(new Uri(address));
            }
            catch (UriFormatException)
            {
                return false;
            }

        }

        /// <summary>
        /// true if the input uri is https; otherwise, false.
        /// </summary>
        /// <param name="uri">the URI.</param>
        /// <returns></returns>
        public static bool IsHttps(Uri uri)
        {
            if (uri == null)
            {
                return false;
            }
#if NETSTANDARD1_4
            return uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase); //Uri.UriSchemeHttps is internal in dnxcore
#else
            return uri.Scheme.Equals(Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
#endif
        }
    }
}
