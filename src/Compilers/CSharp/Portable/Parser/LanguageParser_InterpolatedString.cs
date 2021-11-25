// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable disable

using System;
using System.Diagnostics;
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

            rescanInterpolation(out var kind, out var openQuoteRange, out var error, out var closeQuoteRange);

            var result = SyntaxFactory.InterpolatedStringExpression(
                getOpenQuote(), getContent(), getCloseQuote());

            interpolations.Free();
            if (error != null)
            {
                result = result.WithDiagnosticsGreen(new[] { error });
            }

            Debug.Assert(originalToken.ToFullString() == result.ToFullString()); // yield from text equals yield from node
            return result;

            void rescanInterpolation(out Lexer.InterpolatedStringKind kind, out Range openQuoteRange, out SyntaxDiagnosticInfo error, out Range closeQuoteRange)
            {
                using var tempLexer = new Lexer(SourceText.From(originalText), this.Options, allowPreprocessorDirectives: false);
                var info = default(Lexer.TokenInfo);
                tempLexer.ScanInterpolatedStringLiteralTop(
                    ref info, out kind, out error, out openQuoteRange, interpolations, out closeQuoteRange);
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

        private static InterpolationSyntax ParseInterpolation(
            CSharpParseOptions options,
            string text,
            Lexer.Interpolation interpolation,
            Lexer.InterpolatedStringKind kind)
        {
            // Grab from after the { all the way to the start of the } (or the start of the : if present).
            // This will be used to parse out the expression of the interpolation.  The lexing/parsing of
            // the remainder is handed specially.
            var expressionText = text[new Range(
                interpolation.OpenBraceRange.End,
                interpolation.HasColon ? interpolation.ColonRange.Start : interpolation.CloseBraceRange.Start)];

            using var tempLexer = new Lexer(SourceText.From(expressionText), options, allowPreprocessorDirectives: false, interpolationFollowedByColon: interpolation.HasColon);

            // First, grab the text after the { that can be treated as trailing trivia.  It will be attached to the {
            // token we create.
            var openTokenTrailingTrivia = tempLexer.LexSyntaxTrailingTrivia();
            var openTokenText = text[interpolation.OpenBraceRange];

            var openTokenKind = kind is Lexer.InterpolatedStringKind.Normal or Lexer.InterpolatedStringKind.Verbatim
                ? SyntaxKind.OpenBraceToken
                : SyntaxKind.RawInterpolationOpenToken;

            // Now, parse the remainder out as an expression
            using var tempParser = new LanguageParser(tempLexer, oldTree: null, changes: null);

            return tempParser.ParseInterpolation(
                text, interpolation, kind,
                SyntaxFactory.Token(leading: null, openTokenKind, openTokenText, openTokenText, openTokenTrailingTrivia.Node));
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
                    var colonToken = SyntaxFactory.Token(leading, SyntaxKind.ColonToken, colonText, colonText, trailing: null);
                    var format = SyntaxFactory.InterpolationFormatClause(
                        colonToken,
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
        /// Take the given text and treat it as the contents of a string literal, returning a token for that.
        /// </summary>
        /// <param name="text">The text for the full string literal, including the quotes and contents</param>
        /// <param name="kind">The kind of the interpolated string we were processing</param>
        private SyntaxToken MakeInterpolatedStringTextToken(
            string text, Lexer.InterpolatedStringKind kind)
        {
            if (kind is Lexer.InterpolatedStringKind.SingleLineRaw or Lexer.InterpolatedStringKind.MultiLineRaw)
            {
                // with a raw string, we don't do any interpretation of the content, except to remove the indentation
                // whitespace.
                // PROTOTYPE: remove the indentation whitespace.
                return SyntaxFactory.Literal(leading: null, text, SyntaxKind.InterpolatedStringTextToken, text, trailing: null);
            }

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

        private static DiagnosticInfo[] MoveDiagnostics(DiagnosticInfo[] infos, int offset)
        {
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
