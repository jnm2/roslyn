﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.CSharp.Test.Utilities;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.LexicalAndXml
{
    public class RawStringLiteralLexingTests : CSharpTestBase
    {
        [Theory]
        #region Single Line Cases
        [InlineData("\"\"\"{|CS9101:|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" {|CS9101:|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \"{|CS9101:|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \"\"{|CS9101:|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \"\"\"", SyntaxKind.SingleLineRawStringLiteralToken, " ")]
        [InlineData("\"\"\"\t\"\"\"", SyntaxKind.SingleLineRawStringLiteralToken, "\t")]
        [InlineData("\"\"\"a\"\"\"", SyntaxKind.SingleLineRawStringLiteralToken, "a")]
        [InlineData("\"\"\"abc\"\"\"", SyntaxKind.SingleLineRawStringLiteralToken, "abc")]
        [InlineData("\"\"\" abc \"\"\"", SyntaxKind.SingleLineRawStringLiteralToken, " abc ")]
        [InlineData("\"\"\"  abc  \"\"\"", SyntaxKind.SingleLineRawStringLiteralToken, "  abc  ")]
        [InlineData("\"\"\" \" \"\"\"", SyntaxKind.SingleLineRawStringLiteralToken, " \" ")]
        [InlineData("\"\"\" \"\" \"\"\"", SyntaxKind.SingleLineRawStringLiteralToken, " \"\" ")]
        [InlineData("\"\"\"\" \"\"\" \"\"\"\"", SyntaxKind.SingleLineRawStringLiteralToken, " \"\"\" ")]
        [InlineData("\"\"\"'\"\"\"", SyntaxKind.SingleLineRawStringLiteralToken, "'")]
        [InlineData("\"\"\" \"\"\"{|CS9102:\"|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \"\"\"{|CS9102:\"\"|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \"\"\"{|CS9102:\"\"\"|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \"\"\"{|CS9102:\"\"\"\"|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"a{|CS9101:\n|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" a {|CS9101:\n|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \"{|CS9101:\n|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \"\"{|CS9101:\n|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"a{|CS9101:\r\n|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" a {|CS9101:\r\n|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \"{|CS9101:\r\n|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \"\"{|CS9101:\r\n|}", SyntaxKind.SingleLineRawStringLiteralToken, "")]
        #endregion
        #region Multi Line Cases
        [InlineData("\"\"\"\n{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\n\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\n\"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \n\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \n\"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \n\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \n\"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\n \"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\n \"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \n \"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \n  \"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \n  \"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \na{|CS9104:\"\"\"|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \na{|CS9104:\"\"\"\"|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \na\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "a")]
        [InlineData("\"\"\"  \n a\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, " a")]
        [InlineData("\"\"\"  \na \n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "a ")]
        [InlineData("\"\"\"  \n a \n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, " a ")]
        [InlineData("\"\"\"\r\n{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\r\n\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\r\n\"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\r\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \r\n\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \r\n\"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \r\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \r\n\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \r\n\"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \r\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\r\n \"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\r\n \"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\" \r\n \"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \r\n  \"\"{|CS9101:|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \r\n  \"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \r\na{|CS9104:\"\"\"|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \r\na{|CS9104:\"\"\"\"|}", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"  \r\na\r\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "a")]
        [InlineData("\"\"\"  \r\n a\r\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, " a")]
        [InlineData("\"\"\"  \r\na \r\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "a ")]
        [InlineData("\"\"\"  \r\n a \r\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, " a ")]
        [InlineData("\"\"\"  \r\n\r\n a \r\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "\r\n a ")]
        [InlineData("\"\"\"  \r\n a \r\n\r\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, " a \r\n")]
        [InlineData("\"\"\"  \r\n\r\n a \r\n\r\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "\r\n a \r\n")]
        [InlineData("\"\"\"  \n\"\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "\"")]
        [InlineData("\"\"\"  \n\"\"\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "\"\"")]
        [InlineData("\"\"\"\"  \n\"\"\"\n\"\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "\"\"\"")]
        #endregion
        #region Multi Line Indentation Cases
        [InlineData(
@"""""""
 abc
""""""", SyntaxKind.MultiLineRawStringLiteralToken, " abc")]
        [InlineData(
@"""""""
 abc
 """"""", SyntaxKind.MultiLineRawStringLiteralToken, "abc")]
        [InlineData(
@"""""""
  abc
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "abc")]
        [InlineData(
@"""""""
    abc
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "  abc")]
        [InlineData(
@"""""""
{|CS9103:|}abc
 """"""", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData(
@"""""""
 abc
{|CS9103:|}def
 """"""", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData(
@"""""""
  abc
  def
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "abc\r\ndef")]
        [InlineData(
@"""""""
  abc
     def
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "abc\r\n   def")]
        [InlineData(
@"""""""
  abc

  def
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "abc\r\n\r\ndef")]
        [InlineData(
@"""""""
  abc
  
  def
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "abc\r\n\r\ndef")]
        [InlineData(
@"""""""
  abc
   
  def
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "abc\r\n \r\ndef")]
        [InlineData(
@"""""""
  abc
    
  def
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "abc\r\n  \r\ndef")]
        [InlineData(
@"""""""
  abc
     
  def
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "abc\r\n   \r\ndef")]
        [InlineData(
@"""""""
{|CS9103: |}abc
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData(
@"""""""
  ""
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "\"")]
        [InlineData(
@"""""""
  """"
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "\"\"")]
        [InlineData(
@"""""""""
  """"""
  """"""""", SyntaxKind.MultiLineRawStringLiteralToken, "\"\"\"")]
        [InlineData(
@"""""""

  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData(
@"""""""
 
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData(
@"""""""
  
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData(
@"""""""
   
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, " ")]
        [InlineData(
@"""""""
    
  """"""", SyntaxKind.MultiLineRawStringLiteralToken, "  ")]
        [InlineData("\"\"\"\n{|CS9103: |}abc\n\t\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\n{|CS9103: |}abc\n \t\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\n{|CS9103:\t|}abc\n \"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\n \tabc\n \"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "\tabc")]
        [InlineData("\"\"\"\n\n\t\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\n\t\n\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "\t")]
        [InlineData("\"\"\"\n\t\n\t\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\n{|CS9103:\t|}\n \"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        [InlineData("\"\"\"\n{|CS9103: |}\n\t\"\"\"", SyntaxKind.MultiLineRawStringLiteralToken, "")]
        #endregion
        public void TestSingleToken(string markup, SyntaxKind expectedKind, string expectedValue)
        {
            TestSingleToken(markup, expectedKind, expectedValue, leadingTrivia: false);
            TestSingleToken(markup, expectedKind, expectedValue, leadingTrivia: true);
        }

        private void TestSingleToken(string markup, SyntaxKind expectedKind, string expectedValue, bool leadingTrivia)
        {
            if (leadingTrivia)
                markup = " /*leading*/ " + markup;

            MarkupTestFile.GetSpans(markup, out var input, out IDictionary<string, ImmutableArray<TextSpan>> spans);

            Assert.True(spans.Count == 0 || spans.Count == 1);
            if (spans.Count == 1)
                Assert.True(spans.Single().Value.Length == 1);

            var token = SyntaxFactory.ParseToken(input);
            var literal = SyntaxFactory.LiteralExpression(SyntaxKind.MultiLineRawStringLiteralExpression, token);
            token = literal.Token;

            Assert.Equal(expectedKind, token.Kind());
            Assert.Equal(input.Length, token.FullWidth);
            Assert.Equal(input, token.ToFullString());
            Assert.NotNull(token.Value);
            Assert.NotNull(token.ValueText);
            Assert.Equal(expectedValue, token.ValueText);

            if (spans.Count == 0)
            {
                Assert.Empty(token.GetDiagnostics());
            }
            else
            {
                // If we get any diagnostics, then the token's value text should always be empty.
                Assert.Equal("", token.ValueText);

                var diagnostics = token.GetDiagnostics();
                Assert.True(diagnostics.Count() == 1);

                var actualDiagnostic = diagnostics.Single();
                var expectedDiagnostic = spans.Single();

                Assert.Equal(expectedDiagnostic.Key, actualDiagnostic.Id);
                Assert.Equal(expectedDiagnostic.Value.Single(), actualDiagnostic.Location.SourceSpan);
            }
        }

        [Fact]
        public void TestDirectiveWithRawString()
        {
            CreateCompilation(
@"
#line 1 """"""c:\""""""").VerifyDiagnostics(
                // (2,9): error CS9100: Raw string literals are not allowed in preprocessor directives
                // #line 1 """c:\"""
                Diagnostic(ErrorCode.ERR_Raw_string_literals_are_not_allowed_in_preprocessor_directives, "").WithLocation(2, 9));
        }
    }
}
