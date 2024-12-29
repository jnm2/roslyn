// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Formatting;

public class AnnotationHelpersTests
{
    private static void TestInsertAnnotatedZeroWidthWhitespace([StringSyntax(PredefinedEmbeddedLanguageNames.CSharpTest)] string markup)
    {
        MarkupTestFile.GetPosition(markup, out var text, out int position);

        var result = AnnotationHelpers.InsertAnnotatedZeroWidthWhitespace(SyntaxFactory.ParseCompilationUnit(text), SyntaxAnnotation.ElasticAnnotation, position);

        var insertedTrivia = Assert.Single(result.GetAnnotatedTrivia(SyntaxAnnotation.ElasticAnnotation));
        Assert.Equal(0, insertedTrivia.Span.Length);

        var resultMarkup = result.ToFullString().Insert(insertedTrivia.SpanStart, "$$");
        AssertEx.EqualOrDiff(markup, resultMarkup);
    }

    [Fact]
    public void InsertAnnotatedZeroWidthWhitespace_EmptyCompilationUnit()
    {
        TestInsertAnnotatedZeroWidthWhitespace("$$");
    }

    [Fact]
    public void InsertAnnotatedZeroWidthWhitespace_BeforeTrivia()
    {
        TestInsertAnnotatedZeroWidthWhitespace("$$/**/");
    }

    [Fact]
    public void InsertAnnotatedZeroWidthWhitespace_AfterTrivia()
    {
        TestInsertAnnotatedZeroWidthWhitespace("/**/$$");
    }

    [Fact]
    public void InsertAnnotatedZeroWidthWhitespace_BetweenTrivia()
    {
        TestInsertAnnotatedZeroWidthWhitespace("/**/$$/**/");
    }

    [Fact]
    public void InsertAnnotatedZeroWidthWhitespace_SplittingTrivia()
    {
        TestInsertAnnotatedZeroWidthWhitespace(" $$ ");
    }

    [Fact]
    public void InsertAnnotatedZeroWidthWhitespace_Trailing()
    {
        TestInsertAnnotatedZeroWidthWhitespace("""
            class C;$$
            // comment
            """);
    }

    [Fact]
    public void InsertAnnotatedZeroWidthWhitespace_BetweenTokens()
    {
        TestInsertAnnotatedZeroWidthWhitespace("class C$$;");
    }

    [Fact]
    public void InsertAnnotatedZeroWidthWhitespace_FailsInsideCommentTrivia()
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() => TestInsertAnnotatedZeroWidthWhitespace("/*$$*/"));
        Assert.StartsWith("Trivia cannot be inserted in the middle of a token or non-whitespace trivia.", ex.Message);
        Assert.Equal("position", ex.ParamName);
    }

    [Fact]
    public void InsertAnnotatedZeroWidthWhitespace_FailsInsideToken()
    {
        var ex = Assert.ThrowsAny<ArgumentException>(() => TestInsertAnnotatedZeroWidthWhitespace("cla$$ss C;"));
        Assert.StartsWith("Trivia cannot be inserted in the middle of a token or non-whitespace trivia.", ex.Message);
        Assert.Equal("position", ex.ParamName);
    }
}
