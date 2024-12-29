// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Test.Utilities;
using Roslyn.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Formatting;
[Trait(Traits.Feature, Traits.Features.Formatting)]
public class FormattingElasticTriviaWrappingTests : CSharpFormattingTestBase
{
    private static void TestFormattingElastic(
        [StringSyntax(PredefinedEmbeddedLanguageNames.CSharpTest)] string markup,
        [StringSyntax(PredefinedEmbeddedLanguageNames.CSharpTest)] string expected)
    {
        using var workspace = TestWorkspace.CreateCSharp(markup, isMarkup: true);

        var elasticSpans = workspace.Documents.Single().AnnotatedSpans["elastic"];
        var root = workspace.CurrentSolution.Projects.Single().Documents.Single().GetSyntaxRootSynchronously(CancellationToken.None)!;

        foreach (var span in elasticSpans)
        {
            if (!span.IsEmpty)
                throw new NotImplementedException("Marking existing trivia or tokens as elastic.");

            root = AnnotationHelpers.InsertAnnotatedZeroWidthWhitespace(root, SyntaxAnnotation.ElasticAnnotation, span.Start);
        }

        var formatted = Formatter.Format(root, SyntaxAnnotation.ElasticAnnotation, workspace.Services.SolutionServices, CSharpSyntaxFormattingOptions.Default, CancellationToken.None);
        AssertEx.EqualOrDiff(expected, formatted.ToFullString());
    }

    [Fact]
    public void WrappingExpressionBodiedProperty()
    {
        TestFormattingElastic("""
            class C
            {
                public // a
                {|elastic:|}int // b
                {|elastic:|}Prop // c
                {|elastic:|}=> 1;
            }
            """, """
            class C
            {
                public // a
                    int // b
                    Prop // c
                    => 1;
            }
            """);
    }

    [Fact]
    public void WrappingBlockBodiedProperty()
    {
        TestFormattingElastic("""
            class C
            {
                public // a
                {|elastic:|}int // b
                {|elastic:|}Prop // c
                {|elastic:|}{
                    get => 1;
                };
            }
            """, """
            class C
            {
                public // a
                    int // b
                    Prop // c
                {
                    get => 1;
                };
            }
            """);
    }

    [Fact]
    public void WrappingElseReturn()
    {
        TestFormattingElastic("""
            class C
            {
                void M()
                {
                    if (1 == 0)
                    {
                    }
                    else // a
                    {|elastic:|}return;
                }
            }
            """, """
            class C
            {
                void M()
                {
                    if (1 == 0)
                    {
                    }
                    else // a
                        return;
                }
            }
            """);
    }

    [Fact]
    public void WrappingElseEolIf()
    {
        TestFormattingElastic("""
            class C
            {
                void M()
                {
                    if (1 == 0)
                    {
                    }
                    else // a
                    {|elastic:|}if (1 == 1)
                    {|elastic:|}{
                    {|elastic:|}}
                }
            }
            """, """
            class C
            {
                void M()
                {
                    if (1 == 0)
                    {
                    }
                    else // a
                        if (1 == 1)
                        {
                        }
                }
            }
            """);
    }

    [Fact]
    public void WrappingElseEolAwaitEolForeach()
    {
        TestFormattingElastic("""
            class C
            {
                void M()
                {
                    if (1 == 0)
                    {
                    }
                    else // a
                    await // b
                    {|elastic:|}foreach (var x in SomeCollection)
                    {|elastic:|}{
                    {|elastic:|}}
                }
            }
            """, """
            class C
            {
                void M()
                {
                    if (1 == 0)
                    {
                    }
                    else // a
                        await // b
                            foreach (var x in SomeCollection)
                        {
                        }
                }
            }
            """);
    }

    [Fact]
    public void WrappingElseAwaitEolForeach()
    {
        TestFormattingElastic("""
            class C
            {
                void M()
                {
                    if (1 == 0)
                    {
                    }
                    else await // a
                    {|elastic:|}foreach (var x in SomeCollection)
                    {|elastic:|}{
                    {|elastic:|}}
                }
            }
            """, """
            class C
            {
                void M()
                {
                    if (1 == 0)
                    {
                    }
                    else await // a
                        foreach (var x in SomeCollection)
                    {
                    }
                }
            }
            """);
    }

    [Fact]
    public void WrappingElseAwaitForeach()
    {
        TestFormattingElastic("""
            class C
            {
                void M()
                {
                    if (1 == 0)
                    {
                    }
                    else await foreach (var x in SomeCollection)
                    {|elastic:|}{
                    {|elastic:|}}
                }
            }
            """, """
            class C
            {
                void M()
                {
                    if (1 == 0)
                    {
                    }
                    else await foreach (var x in SomeCollection)
                    {
                    }
                }
            }
            """);
    }
}
