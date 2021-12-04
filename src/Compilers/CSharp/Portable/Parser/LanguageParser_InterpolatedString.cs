// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax
{
    internal partial class LanguageParser
    {
        private ExpressionSyntax ParseInterpolatedStringToken()
        {
            // We don't want to make the scanner stateful (between tokens) if we can possibly avoid it.
            // The approach implemented here is
            //
            // (1) Scan the whole interpolated string literal as a single token. Now the statefulness of
            // the scanner (to match { }'s) is limited to its behavior while scanning a single token.
            //
            // (2) When the parser gets such a token, here, it spins up another scanner / parser on each of
            // the holes and builds a tree for the whole thing (resulting in an InterpolatedStringExpressionSyntax).
            //
            // (3) The parser discards the original token and replaces it with this tree. (In other words,
            // it replaces one token with a different set of tokens that have already been parsed)
            //
            // (4) On an incremental change, we widen the invalidated region to include any enclosing interpolated
            // string nonterminal so that we never reuse tokens inside a changed interpolated string.
            //
            // This has the secondary advantage that it can reasonably be specified.
            // 
            // The substitution will end up being invisible to external APIs and clients such as the IDE, as
            // they have no way to ask for the stream of tokens before parsing.

            Debug.Assert(this.CurrentToken.Kind == SyntaxKind.InterpolatedStringToken);
            var originalToken = this.EatToken();

            var originalText = originalToken.ValueText; // this is actually the source text
            Debug.Assert(originalText[0] == '$' || originalText[0] == '@');

            // compute the positions of the interpolations in the original string literal, if there was an error or not,
            // and where the open and close quotes can be found.
            var interpolations = ArrayBuilder<Lexer.Interpolation>.GetInstance();

            rescanInterpolation(out var kind, out var error, out var openQuoteRange, interpolations, out var closeQuoteRange);

            var result = SyntaxFactory.InterpolatedStringExpression(getOpenQuote(), getContent(), getCloseQuote());

            interpolations.Free();
            if (error != null)
            {
                // Errors are positioned relative to the start of the token that was lexed.  Specifically relative to
                // the starting `$` or `@`.  However, when placed on a node like this, it will be relative to the node's
                // full start.  So we have to adjust the diagnostics taking that into account.
                result = result.WithDiagnosticsGreen(MoveDiagnostics(new[] { error }, originalToken.GetLeadingTrivia()?.FullWidth ?? 0));
            }

            Debug.Assert(originalToken.ToFullString() == result.ToFullString()); // yield from text equals yield from node
            return result;

            void rescanInterpolation(out Lexer.InterpolatedStringKind kind, out SyntaxDiagnosticInfo error, out Range openQuoteRange, ArrayBuilder<Lexer.Interpolation> interpolations, out Range closeQuoteRange)
            {
                using var tempLexer = new Lexer(SourceText.From(originalText), this.Options, allowPreprocessorDirectives: false);
                var info = default(Lexer.TokenInfo);
                tempLexer.ScanInterpolatedStringLiteralTop(ref info, out error, out kind, out openQuoteRange, interpolations, out closeQuoteRange);
            }

            SyntaxToken getOpenQuote()
            {
                var openQuoteText = originalText[openQuoteRange];
                return SyntaxFactory.Token(
                    originalToken.GetLeadingTrivia(),
                    kind switch
                    {
                        Lexer.InterpolatedStringKind.Normal => SyntaxKind.InterpolatedStringStartToken,
                        Lexer.InterpolatedStringKind.Verbatim => SyntaxKind.InterpolatedVerbatimStringStartToken,
                        Lexer.InterpolatedStringKind.SingleLineRaw => SyntaxKind.SingleLineRawInterpolatedStringStartToken,
                        Lexer.InterpolatedStringKind.MultiLineRaw => SyntaxKind.MultiLineRawInterpolatedStringStartToken,
                        _ => throw ExceptionUtilities.UnexpectedValue(kind),
                    },
                    openQuoteText,
                    openQuoteText,
                    trailing: null);
            }

            CodeAnalysis.Syntax.InternalSyntax.SyntaxList<InterpolatedStringContentSyntax> getContent()
            {
                if (kind is Lexer.InterpolatedStringKind.MultiLineRaw)
                {
                    // For a multi-line raw interpolated string, we have to remove indentation whitespace as
                    // appropriate.  So this gets a highly specialized processing path.
                    return getMultiLineRawContent();
                }
                else
                {
                    return getNormalContent();
                }
            }

            CodeAnalysis.Syntax.InternalSyntax.SyntaxList<InterpolatedStringContentSyntax> getNormalContent()
            {
                var builder = _pool.Allocate<InterpolatedStringContentSyntax>();

                if (interpolations.Count == 0)
                {
                    // In the special case when there are no interpolations, we just construct a format string
                    // with no inserts. We must still use String.Format to get its handling of escapes such as {{,
                    // so we still treat it as a composite format string.
                    var text = originalText[new Range(openQuoteRange.End, closeQuoteRange.Start)];
                    if (text.Length > 0)
                        builder.Add(SyntaxFactory.InterpolatedStringText(MakeInterpolatedStringTextToken(text, kind)));
                }
                else
                {
                    for (int i = 0; i < interpolations.Count; i++)
                    {
                        var interpolation = interpolations[i];

                        // Add a token for text preceding the interpolation
                        var text = originalText[new Range(
                            i == 0 ? openQuoteRange.End : interpolations[i - 1].CloseBraceRange.End,
                            interpolation.OpenBraceRange.Start)];
                        if (text.Length > 0)
                            builder.Add(SyntaxFactory.InterpolatedStringText(MakeInterpolatedStringTextToken(text, kind)));

                        builder.Add(ParseInterpolation(this.Options, originalText, interpolation, kind));
                    }

                    // Add a token for text following the last interpolation
                    var lastText = originalText[new Range(interpolations[^1].CloseBraceRange.End, closeQuoteRange.Start)];
                    if (lastText.Length > 0)
                        builder.Add(SyntaxFactory.InterpolatedStringText(MakeInterpolatedStringTextToken(lastText, kind)));
                }

                CodeAnalysis.Syntax.InternalSyntax.SyntaxList<InterpolatedStringContentSyntax> result = builder;
                _pool.Free(builder);
                return result;
            }

            CodeAnalysis.Syntax.InternalSyntax.SyntaxList<InterpolatedStringContentSyntax> getMultiLineRawContent()
            {
                // If we have any errors in the multi-line literal, then don't bother to try to do fancy dedentation.
                // There's no need as it's quite possible we don't even know what the dedent would be.
                if (error != null)
                    return getNormalContent();

                // The indentation-whitespace computed from the very last line of the raw string literal
                var indentationWhitespace = PooledStringBuilder.GetInstance();

                // The leading whitespace of whatever line we are currently on.
                var currentLineWhitespace = PooledStringBuilder.GetInstance();

                // The content we want to create text token out of.  Effectively, what is in the text sections
                // minus leading whitespace.
                var content = PooledStringBuilder.GetInstance();
                try
                {
                    var closeQuoteText = originalText[closeQuoteRange];

                    // A multi-line raw interpolation without errors always ends with a new-line, some number of spaces, and the quotes.
                    Debug.Assert(SyntaxFacts.IsNewLine(closeQuoteText[0]));

                    var currentIndex = GetNewLineLength(closeQuoteText, index: 0);

                    while (currentIndex < closeQuoteText.Length &&
                        SyntaxFacts.IsWhitespace(closeQuoteText[currentIndex]))
                    {
                        indentationWhitespace.Builder.Append(closeQuoteText[currentIndex]);
                        currentIndex++;
                    }

                    Debug.Assert(closeQuoteText[currentIndex] == '"');

                    return getMultiLineRawContentWorker(indentationWhitespace, currentLineWhitespace, content);
                }
                finally
                {
                    indentationWhitespace.Free();
                    currentLineWhitespace.Free();
                    content.Free();
                }
            }

            CodeAnalysis.Syntax.InternalSyntax.SyntaxList<InterpolatedStringContentSyntax> getMultiLineRawContentWorker(
                StringBuilder indentationWhitespace,
                StringBuilder currentLineWhitespace,
                StringBuilder content)
            {
                var builder = _pool.Allocate<InterpolatedStringContentSyntax>();

                if (interpolations.Count == 0)
                {
                    // No interpolations.  Just grab the whole chunk of text and split it as appropriate.
                    addContent(
                        indentationWhitespace, currentLineWhitespace, content, builder, first: true, last: true,
                        originalText[new Range(openQuoteRange.End, closeQuoteRange.Start)]);
                }
                else
                {
                    for (int i = 0; i < interpolations.Count; i++)
                    {
                        var interpolation = interpolations[i];

                        // Add a token for text preceding the interpolation
                        addContent(
                            indentationWhitespace, currentLineWhitespace, content, builder, first: i == 0, last: false,
                            originalText[new Range(
                                i == 0 ? openQuoteRange.End : interpolations[i - 1].CloseBraceRange.End,
                                interpolation.OpenBraceRange.Start)]);

                        builder.Add(ParseInterpolation(this.Options, originalText, interpolation, kind));
                    }

                    // Add a token for text following the last interpolation
                    addContent(
                        indentationWhitespace, currentLineWhitespace, content, builder, first: false, last: true,
                        originalText[new Range(interpolations[^1].CloseBraceRange.End, closeQuoteRange.Start)]);
                }

                CodeAnalysis.Syntax.InternalSyntax.SyntaxList<InterpolatedStringContentSyntax> result = builder;
                _pool.Free(builder);
                return result;
            }

            void addContent(
                StringBuilder indentationWhitespace,
                StringBuilder currentLineWhitespace,
                StringBuilder content,
                CodeAnalysis.Syntax.InternalSyntax.SyntaxListBuilder<InterpolatedStringContentSyntax> result,
                bool first,
                bool last,
                string text)
            {
                if (text.Length == 0)
                    return;

                content.Clear();
                var currentIndex = 0;

                // If we're not the first content chunk, then we came after an interpolation.  In that case, we need to
                // consume up through the next newline as content that is not subject to dedentation.
                if (!first)
                    ConsumeRemainingContentOnLine(content, text, ref currentIndex);

                // We're either the first item, or we consumed up through a newline from the previous line. We're
                // definitely at the start of a newline (or at the end).  Regardless, we want to consume each successive
                // line, making sure it's indentation is correct.

                SyntaxDiagnosticInfo error = null;
                while (currentIndex < text.Length)
                {
                    currentLineWhitespace.Clear();
                    var lineStartPosition = currentIndex;
                    while (currentIndex < text.Length && SyntaxFacts.IsWhitespace(text[currentIndex]))
                    {
                        currentLineWhitespace.Append(text[currentIndex]);
                        currentIndex++;
                    }

                    // Only bother reporting a single error on a text chunk.
                    if (error == null)
                    {
                        var isAtEndOfLastLine = last && currentIndex == text.Length;
                        var isAtNewLine = currentIndex < text.Length && SyntaxFacts.IsNewLine(text[currentIndex]);
                        if (isAtEndOfLastLine || isAtNewLine)
                        {
                            // a whitespace-only content line.  The indentation whitespace must be a prefix of the current line whitespace,
                            // or vice versa.  It is an error otherwise.
                            if (!Lexer.StartsWith(indentationWhitespace, currentLineWhitespace) &&
                                !Lexer.StartsWith(currentLineWhitespace, indentationWhitespace))
                            {
                                error = MakeError(
                                    lineStartPosition,
                                    width: currentIndex - lineStartPosition,
                                    ErrorCode.ERR_LineDoesNotStartWithSameWhitespace);
                            }
                        }
                        else
                        {
                            // a content line with non-whitespace.  The indentation whitespace must be a prefix of the current line
                            // whitespace.  It is an error otherwise.
                            if (!Lexer.StartsWith(currentLineWhitespace, indentationWhitespace))
                            {
                                error ??= MakeError(
                                    lineStartPosition,
                                    width: currentIndex - lineStartPosition,
                                    ErrorCode.ERR_LineDoesNotStartWithSameWhitespace);
                            }
                        }
                    }

                    // Skip the leading whitespace that matches the terminator line and add any whitespace past that to the
                    // string value.
                    for (var i = indentationWhitespace.Length; i < currentLineWhitespace.Length; i++)
                        content.Append(currentLineWhitespace[i]);

                    ConsumeRemainingContentOnLine(content, text, ref currentIndex);
                }

                // if we ran into any errors, don't give this item any special value.  It just has the value of our actual text.
                var value = error == null ? content.ToString() : text;
                var node = SyntaxFactory.InterpolatedStringText(
                    SyntaxFactory.Literal(leading: null, text, SyntaxKind.InterpolatedStringTextToken, value, trailing: null));
                if (error != null)
                    node = node.WithDiagnosticsGreen(new[] { error });

                result.Add(node);
            }

            SyntaxToken getCloseQuote()
            {
                // Make a token for the close quote " (even if it was missing)
                var closeQuoteText = originalText[closeQuoteRange];
                var syntaxKind = kind switch
                {
                    Lexer.InterpolatedStringKind.Normal => SyntaxKind.InterpolatedStringEndToken,
                    Lexer.InterpolatedStringKind.Verbatim => SyntaxKind.InterpolatedStringEndToken,
                    Lexer.InterpolatedStringKind.SingleLineRaw => SyntaxKind.SingleLineRawInterpolatedStringEndToken,
                    Lexer.InterpolatedStringKind.MultiLineRaw => SyntaxKind.MultiLineRawInterpolatedStringEndToken,
                    _ => throw ExceptionUtilities.UnexpectedValue(kind),
                };
                return closeQuoteText == ""
                    ? SyntaxFactory.MissingToken(leading: null, syntaxKind, originalToken.GetTrailingTrivia())
                    : SyntaxFactory.Token(leading: null, syntaxKind, closeQuoteText, closeQuoteText, originalToken.GetTrailingTrivia());
            }
        }

        private static void ConsumeRemainingContentOnLine(StringBuilder content, string text, ref int currentIndex)
        {
            while (currentIndex < text.Length && !SyntaxFacts.IsNewLine(text[currentIndex]))
            {
                content.Append(text[currentIndex]);
                currentIndex++;
            }

            if (currentIndex < text.Length)
            {
                // we must have hit a newline.  Consume it and then move to the core loop.
                ConsumeNewLine(text, ref currentIndex, content);
            }
        }

        private static void ConsumeNewLine(string text, ref int currentIndex, StringBuilder content)
        {
            var newLineLength = GetNewLineLength(text, currentIndex);
            content.Append(text[currentIndex]);

            if (newLineLength == 2)
                content.Append(text[currentIndex + 1]);

            currentIndex += newLineLength;
        }

        private static int GetNewLineLength(string text, int index)
        {
            Debug.Assert(SyntaxFacts.IsNewLine(text[index]));
            return text[index] == '\r' && text[index + 1] == '\n' ? 2 : 1;
        }

        private static InterpolationSyntax ParseInterpolation(
            CSharpParseOptions options,
            string text,
            Lexer.Interpolation interpolation,
            Lexer.InterpolatedStringKind kind)
        {
            // Grab the text from after the { all the way to the start of the } (or the start of the : if present). This
            // will be used to parse out the expression of the interpolation.
            //
            // The parsing of the open brace, close brace and colon is specially handled in ParseInterpolation below.
            var expressionText = text[new Range(
                interpolation.OpenBraceRange.End,
                interpolation.HasColon ? interpolation.ColonRange.Start : interpolation.CloseBraceRange.Start)];

            using var tempLexer = new Lexer(SourceText.From(expressionText), options, allowPreprocessorDirectives: false, interpolationFollowedByColon: interpolation.HasColon);

            // First grab any trivia right after the {, it will be trailing trivia for the { token.
            var openTokenTrailingTrivia = tempLexer.LexSyntaxTrailingTrivia().Node;
            var openTokenText = text[interpolation.OpenBraceRange];

            var openTokenKind = kind is Lexer.InterpolatedStringKind.Normal or Lexer.InterpolatedStringKind.Verbatim
                ? SyntaxKind.OpenBraceToken
                : SyntaxKind.RawInterpolationOpenToken;

            // Now create a parser to actually handle the expression portion of the interpolation
            using var tempParser = new LanguageParser(tempLexer, oldTree: null, changes: null);

            var result = tempParser.ParseInterpolation(
                text, interpolation, kind,
                SyntaxFactory.Token(leading: null, openTokenKind, openTokenText, openTokenText, openTokenTrailingTrivia));

            Debug.Assert(text[new Range(interpolation.OpenBraceRange.Start, interpolation.CloseBraceRange.End)] == result.ToFullString()); // yield from text equals yield from node
            return result;
        }

        private InterpolationSyntax ParseInterpolation(
            string text,
            Lexer.Interpolation interpolation,
            Lexer.InterpolatedStringKind kind,
            SyntaxToken openBraceToken)
        {
            var (expression, alignment) = getExpressionAndAlignment();
            var (format, closeBraceToken) = getFormatAndCloseBrace();

            var result = SyntaxFactory.Interpolation(openBraceToken, expression, alignment, format, closeBraceToken);
#if DEBUG
            Debug.Assert(text[new Range(interpolation.OpenBraceRange.Start, interpolation.CloseBraceRange.End)] == result.ToFullString()); // yield from text equals yield from node
#endif
            return result;

            (ExpressionSyntax expression, InterpolationAlignmentClauseSyntax alignment) getExpressionAndAlignment()
            {
                var expression = this.ParseExpressionCore();

                if (this.CurrentToken.Kind != SyntaxKind.CommaToken)
                {
                    return (this.ConsumeUnexpectedTokens(expression), alignment: null);
                }

                var alignment = SyntaxFactory.InterpolationAlignmentClause(
                    this.EatToken(SyntaxKind.CommaToken),
                    this.ConsumeUnexpectedTokens(this.ParseExpressionCore()));
                return (expression, alignment);
            }

            (InterpolationFormatClauseSyntax format, SyntaxToken closeBraceToken) getFormatAndCloseBrace()
            {
                var leading = this.CurrentToken.GetLeadingTrivia();
                if (interpolation.HasColon)
                {
                    var colonText = text[interpolation.ColonRange];
                    var format = SyntaxFactory.InterpolationFormatClause(
                        SyntaxFactory.Token(leading, SyntaxKind.ColonToken, colonText, colonText, trailing: null),
                        MakeInterpolatedStringTextToken(
                            text[new Range(interpolation.ColonRange.End, interpolation.CloseBraceRange.Start)], kind));
                    return (format, getInterpolationCloseToken(leading: null));
                }
                else
                {
                    return (format: null, getInterpolationCloseToken(leading));
                }
            }

            SyntaxToken getInterpolationCloseToken(GreenNode leading)
            {
                var closeTokenKind = kind is Lexer.InterpolatedStringKind.Normal or Lexer.InterpolatedStringKind.Verbatim
                    ? SyntaxKind.CloseBraceToken
                    : SyntaxKind.RawInterpolationCloseToken;

                var tokenText = text[interpolation.CloseBraceRange];
                if (tokenText == "")
                    return SyntaxFactory.MissingToken(leading, closeTokenKind, trailing: null);

                return SyntaxFactory.Token(leading, closeTokenKind, tokenText, tokenText, trailing: null);
            }
        }

        /// <summary>
        /// Interpret the given raw text from source as an InterpolatedStringTextToken.
        /// </summary>
        /// <param name="text">The text for the full string literal, including the quotes and contents</param>
        /// <param name="kind">The kind of the interpolated string we were processing</param>
        private SyntaxToken MakeInterpolatedStringTextToken(
            string text, Lexer.InterpolatedStringKind kind)
        {
            if (kind is Lexer.InterpolatedStringKind.SingleLineRaw or Lexer.InterpolatedStringKind.MultiLineRaw)
            {
                // with a raw string, we don't do any interpretation of the content.  Note: removal of indentation is
                // handled already in splitContent
                return SyntaxFactory.Literal(leading: null, text, SyntaxKind.InterpolatedStringTextToken, text, trailing: null);
            }
            else
            {
                Debug.Assert(kind is Lexer.InterpolatedStringKind.Normal or Lexer.InterpolatedStringKind.Verbatim);

                // For a normal/verbatim piece of content, process the inner content as if it was in a corresponding
                // *non*-interpolated string to get the correct meaning of all the escapes/diagnostics within.
                var prefix = kind is Lexer.InterpolatedStringKind.Verbatim ? "@\"" : "\"";
                var fakeString = prefix + text + "\"";
                using var tempLexer = new Lexer(SourceText.From(fakeString), this.Options, allowPreprocessorDirectives: false);

                var mode = LexerMode.Syntax;
                var token = tempLexer.Lex(ref mode);
                Debug.Assert(token.Kind == SyntaxKind.StringLiteralToken);
                var result = SyntaxFactory.Literal(null, text, SyntaxKind.InterpolatedStringTextToken, token.ValueText, null);
                if (token.ContainsDiagnostics)
                {
                    result = result.WithDiagnosticsGreen(MoveDiagnostics(token.GetDiagnostics(), -prefix.Length));
                }

                return result;
            }
        }

        private static DiagnosticInfo[] MoveDiagnostics(DiagnosticInfo[] infos, int offset)
        {
            if (offset == 0)
                return infos;

            var builder = ArrayBuilder<DiagnosticInfo>.GetInstance();
            foreach (var info in infos)
            {
                var sd = info as SyntaxDiagnosticInfo;
                builder.Add(sd?.WithOffset(sd.Offset + offset) ?? info);
            }

            return builder.ToArrayAndFree();
        }
    }
}
