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

        [Fact]
        public void SingleLineSingleQuoteInside()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $"""""" "" """""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics();
        }

        [Fact]
        public void SingleLineDoubleQuoteInside()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $"""""" """" """""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics();
        }

        [Fact]
        public void SingleLineInterpolationInside()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $""""""{0}"""""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics();
        }

        [Fact]
        public void SingleLineInterpolationInsideSpacesOutside()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $"""""" {0} """""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics();
        }

        [Fact]
        public void SingleLineInterpolationInsideSpacesInside()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $""""""{ 0 }"""""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics();
        }

        [Fact]
        public void SingleLineInterpolationInsideSpacesInsideAndOutside()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $"""""" { 0 } """""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics();
        }

        [Fact]
        public void SingleLineInterpolationMultipleCurliesNotAllowed1()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $""""""{{0}}"""""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics(
                // (6,21): error CS9122: Too many open braces for raw string literal
                //         var v = $"""{{0}}""";
                Diagnostic(ErrorCode.ERR_TooManyOpenBracesForRawString, "{").WithLocation(6, 21));
        }

        [Fact]
        public void SingleLineInterpolationMultipleCurliesNotAllowed2()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $$""""""{{{{0}}}}"""""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics(
                // (6,22): error CS9122: Too many open braces for raw string literal
                //         var v = $$"""{{{{0}}}}""";
                Diagnostic(ErrorCode.ERR_TooManyOpenBracesForRawString, "{{").WithLocation(6, 22));
        }

        [Fact]
        public void SingleLineInterpolationMultipleCurliesNotAllowed3()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $""""""{0}}}"""""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics(
                // (6,24): error CS9123: Too many closing braces for raw string literal
                //         var v = $"""{0}}}""";
                Diagnostic(ErrorCode.ERR_TooManyCloseBracesForRawString, "}}").WithLocation(6, 24));
        }

        [Fact]
        public void SingleLineInterpolationMultipleCurliesNotAllowed4()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $$""""""{{{0}}}}"""""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics(
                // (6,28): error CS9123: Too many closing braces for raw string literal
                //         var v = $$"""{{{0}}}}""";
                Diagnostic(ErrorCode.ERR_TooManyCloseBracesForRawString, "}}").WithLocation(6, 28));
        }

        [Fact]
        public void SingleLineInterpolationMultipleCurliesAllowed1()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $$""""""{{0}}"""""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics();
        }

        [Fact]
        public void SingleLineInterpolationMultipleCurliesAllowed2()
        {
            var text = @"
class C
{
    void M()
    {
        var v = $$""""""{{{0}}}"""""";
    }
}";

            CreateCompilation(text).VerifyDiagnostics();
        }

        #endregion
    }
}
