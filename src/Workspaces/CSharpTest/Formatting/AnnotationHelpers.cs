// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Formatting;

internal static class AnnotationHelpers
{
    public static SyntaxNode InsertAnnotatedZeroWidthWhitespace(SyntaxNode node, SyntaxAnnotation annotation, int position)
    {
        var annotatedTrivia = SyntaxFactory.Whitespace("").WithAdditionalAnnotations(annotation);

        var trivia = node.FindTrivia(position);

        if (!trivia.IsKind(SyntaxKind.None))
        {
            if (position == trivia.SpanStart)
            {
                return node.ReplaceTrivia(trivia, [annotatedTrivia, trivia]);
            }
            else if (position == trivia.Span.End)
            {
                throw ExceptionUtilities.Unreachable();
            }
            else if (!trivia.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                throw new ArgumentException("Trivia cannot be inserted in the middle of a token or non-whitespace trivia.", nameof(position));
            }
            else
            {
                var annotationsBeingSplit = trivia.GetAnnotations();
                var whitespaceBeingSplit = trivia.ToString();
                var splitPositionWithinWhitespace = position - trivia.SpanStart;

                return node.ReplaceTrivia(trivia, [
                    SyntaxFactory.Whitespace(whitespaceBeingSplit[..splitPositionWithinWhitespace]).WithAdditionalAnnotations(annotationsBeingSplit),
                    annotatedTrivia,
                    SyntaxFactory.Whitespace(whitespaceBeingSplit[splitPositionWithinWhitespace..]).WithAdditionalAnnotations(annotationsBeingSplit)]);
            }
        }

        var token = node.FindToken(position);

        if (position == token.SpanStart)
        {
            return node.ReplaceToken(token, token.WithLeadingTrivia(token.LeadingTrivia.Add(annotatedTrivia)));
        }
        else if (position == token.Span.End)
        {
            // If we're inserting into the trailing trivia for a token, 'trivia' would not be 'None' above.
            throw ExceptionUtilities.Unreachable();
        }
        else
        {
            throw new ArgumentException("Trivia cannot be inserted in the middle of a token or non-whitespace trivia.", nameof(position));
        }
    }
}
