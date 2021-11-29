// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Parsing
{
    public class RawInterpolatedStringLiteralParsingTests : CSharpTestBase
    {
        #region Single Line

        [Fact]
        public void SingleLine1()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $"""""" """""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics();
        }

        [Fact]
        public void SingleLineTooManyCloseQuotes1()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $"""""" """""""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics(
                    // (6,25): error CS9102: Too many closing quotes for raw string literal
                    //         var v = $""" """";
                    Diagnostic(ErrorCode.ERR_TooManyQuotesForRawString, @"""").WithLocation(6, 25));
        }

        [Fact]
        public void SingleLineTooManyCloseQuotes2()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $"""""" """""""""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics(
                // (6,25): error CS9102: Too many closing quotes for raw string literal
                //         var v = $""" """"";
                Diagnostic(ErrorCode.ERR_TooManyQuotesForRawString, @"""""").WithLocation(6, 25));
        }

        #endregion
    }
}
