// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Diagnostics;
using System.Net;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax
{
    internal partial class Lexer
    {
        private void ScanStringLiteral(ref TokenInfo info, bool inDirective)
        {
            var quoteCharacter = TextWindow.PeekChar();
            Debug.Assert(quoteCharacter == '\'' || quoteCharacter == '"');

            if (TextWindow.PeekChar() == '"' &&
                TextWindow.PeekChar(1) == '"' &&
                TextWindow.PeekChar(2) == '"')
            {
                ScanRawStringLiteral(ref info);
                if (inDirective)
                {
                    // Reinterpret this as just a string literal so that the directive parser can consume this.  
                    // But report this is illegal so that the user knows to fix this up to be a normal string.
                    info.Kind = SyntaxKind.StringLiteralToken;
                    info.StringValue = "";
                    this.AddError(ErrorCode.ERR_RawStringNotInDirectives);
                }
                return;
            }

            TextWindow.AdvanceChar();
            _builder.Length = 0;

            while (true)
            {
                char ch = TextWindow.PeekChar();

                // Normal string & char constants can have escapes. Strings in directives cannot.
                if (ch == '\\' && !inDirective)
                {
                    ch = this.ScanEscapeSequence(out var c2);
                    _builder.Append(ch);
                    if (c2 != SlidingTextWindow.InvalidCharacter)
                    {
                        _builder.Append(c2);
                    }
                }
                else if (ch == quoteCharacter)
                {
                    TextWindow.AdvanceChar();
                    break;
                }
                else if (SyntaxFacts.IsNewLine(ch) ||
                        (ch == SlidingTextWindow.InvalidCharacter && TextWindow.IsReallyAtEnd()))
                {
                    //String and character literals can contain any Unicode character. They are not limited
                    //to valid UTF-16 characters. So if we get the SlidingTextWindow's sentinel value,
                    //double check that it was not real user-code contents. This will be rare.
                    Debug.Assert(TextWindow.Width > 0);
                    this.AddError(ErrorCode.ERR_NewlineInConst);
                    break;
                }
                else
                {
                    TextWindow.AdvanceChar();
                    _builder.Append(ch);
                }
            }

            info.Text = TextWindow.GetText(intern: true);
            if (quoteCharacter == '\'')
            {
                info.Kind = SyntaxKind.CharacterLiteralToken;
                if (_builder.Length != 1)
                {
                    this.AddError((_builder.Length != 0) ? ErrorCode.ERR_TooManyCharsInConst : ErrorCode.ERR_EmptyCharConst);
                }

                if (_builder.Length > 0)
                {
                    info.StringValue = TextWindow.Intern(_builder);
                    info.CharValue = info.StringValue[0];
                }
                else
                {
                    info.StringValue = string.Empty;
                    info.CharValue = SlidingTextWindow.InvalidCharacter;
                }
            }
            else
            {
                info.Kind = SyntaxKind.StringLiteralToken;
                if (_builder.Length > 0)
                {
                    info.StringValue = TextWindow.Intern(_builder);
                }
                else
                {
                    info.StringValue = string.Empty;
                }
            }
        }

        private char ScanEscapeSequence(out char surrogateCharacter)
        {
            var start = TextWindow.Position;
            surrogateCharacter = SlidingTextWindow.InvalidCharacter;
            char ch = TextWindow.NextChar();
            Debug.Assert(ch == '\\');

            ch = TextWindow.NextChar();
            switch (ch)
            {
                // escaped characters that translate to themselves
                case '\'':
                case '"':
                case '\\':
                    break;
                // translate escapes as per C# spec 2.4.4.4
                case '0':
                    ch = '\u0000';
                    break;
                case 'a':
                    ch = '\u0007';
                    break;
                case 'b':
                    ch = '\u0008';
                    break;
                case 'f':
                    ch = '\u000c';
                    break;
                case 'n':
                    ch = '\u000a';
                    break;
                case 'r':
                    ch = '\u000d';
                    break;
                case 't':
                    ch = '\u0009';
                    break;
                case 'v':
                    ch = '\u000b';
                    break;
                case 'x':
                case 'u':
                case 'U':
                    TextWindow.Reset(start);
                    SyntaxDiagnosticInfo error;
                    ch = TextWindow.NextUnicodeEscape(surrogateCharacter: out surrogateCharacter, info: out error);
                    AddError(error);
                    break;
                default:
                    this.AddError(start, TextWindow.Position - start, ErrorCode.ERR_IllegalEscape);
                    break;
            }

            return ch;
        }

        /// <summary>
        /// Returns an appropriate error code if scanning this verbatim literal ran into an error.
        /// </summary>
        private ErrorCode? ScanVerbatimStringLiteral(ref TokenInfo info)
        {
            _builder.Length = 0;

            Debug.Assert(TextWindow.PeekChar() == '@' && TextWindow.PeekChar(1) == '"');
            TextWindow.AdvanceChar(2);

            ErrorCode? error = null;
            while (true)
            {
                var ch = TextWindow.PeekChar();
                if (ch == '"')
                {
                    TextWindow.AdvanceChar();
                    if (TextWindow.PeekChar() == '"')
                    {
                        // Doubled quote -- skip & put the single quote in the string and keep going.
                        TextWindow.AdvanceChar();
                        _builder.Append(ch);
                        continue;
                    }

                    // otherwise, the string is finished.
                    break;
                }

                if (ch == SlidingTextWindow.InvalidCharacter && TextWindow.IsReallyAtEnd())
                {
                    // Reached the end of the source without finding the end-quote.  Give an error back at the
                    // starting point. And finish lexing this string.
                    error ??= ErrorCode.ERR_UnterminatedStringLit;
                    break;
                }

                TextWindow.AdvanceChar();
                _builder.Append(ch);
            }

            info.Kind = SyntaxKind.StringLiteralToken;
            info.Text = TextWindow.GetText(intern: false);
            info.StringValue = _builder.ToString();

            return error;
        }

        private void ScanInterpolatedStringLiteral(ref TokenInfo info)
        {
            // We have a string of the form
            //                $" ... "
            // or, if isVerbatim is true, of possible forms
            //                $@" ... "
            //                @$" ... "
            // Where the contents contains zero or more sequences
            //                { STUFF }
            // where these curly braces delimit STUFF in expression "holes".
            // In order to properly find the closing quote of the whole string,
            // we need to locate the closing brace of each hole, as strings
            // may appear in expressions in the holes. So we
            // need to match up any braces that appear between them.
            // But in order to do that, we also need to match up any
            // /**/ comments, ' characters quotes, () parens
            // [] brackets, and "" strings, including interpolated holes in the latter.

            if (TextWindow.PeekChar(0) == '$' &&
                TextWindow.PeekChar(1) == '"' &&
                TextWindow.PeekChar(2) == '"' &&
                TextWindow.PeekChar(3) == '"')
            {
                ScanRawInterpolatedStringLiteral(ref info);
                return;
            }

            ScanInterpolatedStringLiteralTop(ref info, out var error, openQuoteRange: out _, interpolations: null, closeQuoteRange: out _);
            this.AddError(error);
        }

        internal void ScanInterpolatedStringLiteralTop(
            ref TokenInfo info,
            out SyntaxDiagnosticInfo? error,
            out Range openQuoteRange,
            ArrayBuilder<Interpolation>? interpolations,
            out Range closeQuoteRange)
        {
            var subScanner = new InterpolatedStringScanner(this);
            subScanner.ScanInterpolatedStringLiteralTop(out openQuoteRange, interpolations, out closeQuoteRange);
            error = subScanner.Error;
            info.Kind = SyntaxKind.InterpolatedStringToken;
            info.Text = TextWindow.GetText(intern: false);
        }

        /// <summary>
        /// Turn a (parsed) interpolated string nonterminal into an interpolated string token.
        /// </summary>
        /// <param name="interpolatedString"></param>
        internal static SyntaxToken RescanInterpolatedString(InterpolatedStringExpressionSyntax interpolatedString)
        {
            var text = interpolatedString.ToString();
            var kind = SyntaxKind.InterpolatedStringToken;
            // TODO: scan the contents (perhaps using ScanInterpolatedStringLiteralContents) to reconstruct any lexical
            // errors such as // inside an expression hole
            return SyntaxFactory.Literal(
                interpolatedString.GetFirstToken().GetLeadingTrivia(),
                text,
                kind,
                text,
                interpolatedString.GetLastToken().GetTrailingTrivia());
        }

        internal enum InterpolatedStringKind
        {
            /// <summary>
            /// Normal interpolated string that just starts with $"
            /// </summary>
            Normal,
            /// <summary>
            /// Verbatim interpolated string that starts with $@" or @$"
            /// </summary>
            Verbatim,
            /// <summary>
            /// Single-line raw interpolated string that starts with some number of $ and at least three """.
            /// </summary>
            SingleLineRaw,
            /// <summary>
            /// Multi-line raw interpolated string that starts with some number of $ and at least three """.
            /// </summary>
            MultiLineRaw,
        }

        [NonCopyable]
        private struct InterpolatedStringScanner
        {
            private readonly Lexer _lexer;

            private readonly InterpolatedStringKind _kind;

            /// <summary>
            /// Number of '$' characters this interpolated string started with.  We'll need to see that many '{' in a
            /// row to start an interpolation.  Any less and we'll treat that as just text.  Note if this count is '1'
            /// then this is a normal (non-raw) interpolation and `{{` is treated as an escape.
            /// </summary>
            private readonly int _startingDollarSignCount;

            /// <summary>
            /// Number of '"' characters this interpolated string started with.  Will 
            /// </summary>
            private readonly int _startingQuoteCount;

            /// <summary>
            /// There are two types of errors we can encounter when trying to scan out an interpolated string (and its
            /// interpolations).  The first are true syntax errors where we do not know what it is going on and have no
            /// good strategy to get back on track.  This happens when we see things in the interpolation we truly do
            /// not know what to do with, or when we find we've gotten into an unbalanced state with the bracket pairs
            /// we're consuming.  In this case, we will often choose to bail out rather than go on and potentially make
            /// things worse.
            /// </summary>
            public SyntaxDiagnosticInfo? Error = null;
            private bool EncounteredUnrecoverableError = false;

            public InterpolatedStringScanner(Lexer lexer)
            {
                _lexer = lexer;
                (_kind, _startingDollarSignCount, _startingQuoteCount) = DetermineStringInfo(lexer);

#if DEBUG
                if (_kind is InterpolatedStringKind.Normal or InterpolatedStringKind.Verbatim)
                {
                    Debug.Assert(_startingDollarSignCount == 1);
                    Debug.Assert(_startingQuoteCount == 1);
                }

                if (_kind is InterpolatedStringKind.SingleLineRaw or InterpolatedStringKind.MultiLineRaw)
                {
                    Debug.Assert(_startingDollarSignCount >= 1);
                    Debug.Assert(_startingQuoteCount >= 3);
                }
#endif
            }

            private static (InterpolatedStringKind _kind, int startingDollarSignCount, int startingQuoteCount) DetermineStringInfo(Lexer lexer)
            {
                if ((lexer.TextWindow.PeekChar(0) == '$' && lexer.TextWindow.PeekChar(1) == '@') ||
                    (lexer.TextWindow.PeekChar(0) == '@' && lexer.TextWindow.PeekChar(1) == '$'))
                {
                    return (InterpolatedStringKind.Verbatim, startingDollarSignCount: 1, startingQuoteCount: 1);
                }

                Debug.Assert(lexer.TextWindow.PeekChar(0) == '$');
                if (lexer.TextWindow.PeekChar(1) == '$' ||
                    (lexer.TextWindow.PeekChar(1) == '"' &&
                     lexer.TextWindow.PeekChar(2) == '"' &&
                     lexer.TextWindow.PeekChar(3) == '"'))
                {
                    var start = lexer.TextWindow.Position;
                    var startingDollarSignCount = lexer.ConsumeDollarSignSequence();
                    var startingQuoteCount = lexer.ConsumeQuoteSequence();
                    lexer.ConsumeWhitespace(builder: null);
                    var isMultiLine = SyntaxFacts.IsNewLine(lexer.TextWindow.PeekChar());

                    lexer.TextWindow.Reset(start);
                    return (isMultiLine ? InterpolatedStringKind.MultiLineRaw : InterpolatedStringKind.SingleLineRaw, startingDollarSignCount, startingQuoteCount);
                }

                Debug.Assert(lexer.TextWindow.PeekChar(1) == '"');
                return (InterpolatedStringKind.Normal, startingDollarSignCount: 1, startingQuoteCount: 1);
            }

            private bool IsAtEnd()
            {
                return IsAtEnd(allowNewline: _kind is InterpolatedStringKind.Verbatim or InterpolatedStringKind.MultiLineRaw);
            }

            private bool IsAtEnd(bool allowNewline)
            {
                char ch = _lexer.TextWindow.PeekChar();
                return
                    (!allowNewline && SyntaxFacts.IsNewLine(ch)) ||
                    (ch == SlidingTextWindow.InvalidCharacter && _lexer.TextWindow.IsReallyAtEnd());
            }

            private void TrySetUnrecoverableError(SyntaxDiagnosticInfo error)
            {
                // only need to record the first error we hit
                Error ??= error;

                // No matter what, ensure that we know we hit an error we can't recover from.
                EncounteredUnrecoverableError = true;
            }

            private void TrySetRecoverableError(SyntaxDiagnosticInfo error)
            {
                // only need to record the first error we hit
                Error ??= error;

                // Do not touch 'EncounteredUnrecoverableError'.  If we already encountered something unrecoverable,
                // that doesn't change.  And if we haven't hit something unrecoverable then we stay in that mode as this
                // is a recoverable error.
            }

            internal void ScanInterpolatedStringLiteralTop(
                out Range openQuoteRange,
                ArrayBuilder<Interpolation>? interpolations,
                out Range closeQuoteRange)
            {
                ScanInterpolatedStringLiteralStart(out openQuoteRange);
                ScanInterpolatedStringLiteralContents(interpolations);
                ScanInterpolatedStringLiteralEnd(out closeQuoteRange);
            }

            private void ScanInterpolatedStringLiteralStart(out Range openQuoteRange)
            {
                // Handles reading the start of the interpolated string literal (up to where the content begins)
                var start = _lexer.TextWindow.Position;

                if (_kind == InterpolatedStringKind.Normal)
                {
                    // skip past $
                    _lexer.TextWindow.AdvanceChar();
                }
                else if (_kind == InterpolatedStringKind.Verbatim)
                {
                    // skip past @$ or $!
                    _lexer.TextWindow.AdvanceChar(2);
                }
                else if (_kind == InterpolatedStringKind.SingleLineRaw)
                {
                    // skip past the initial $$""" piece
                    _lexer.ConsumeDollarSignSequence();
                    _lexer.ConsumeQuoteSequence();
                }
                else
                {
                    Debug.Assert(_kind == InterpolatedStringKind.MultiLineRaw);

                    // skip past the initial $$"""<whitespace><newline> piece
                    _lexer.ConsumeDollarSignSequence();
                    _lexer.ConsumeQuoteSequence();
                    _lexer.ConsumeWhitespace(builder: null);
                    _lexer.TextWindow.AdvanceChar(_lexer.GetNewLineWidth(_lexer.TextWindow.PeekChar()));
                }

                openQuoteRange = new Range(start, _lexer.TextWindow.Position);
            }

            private void ScanInterpolatedStringLiteralEnd(out Range closeQuoteRange)
            {
                // Handles reading the end of the interpolated string literal (after where the content ends)

                var closeQuotePosition = _lexer.TextWindow.Position;

                if (_kind is InterpolatedStringKind.Normal or InterpolatedStringKind.Verbatim)
                {
                    ScanNormalOrVerbatimInterpolatedStringLiteralEnd();
                }
                else
                {
                    Debug.Assert(_kind is InterpolatedStringKind.SingleLineRaw or InterpolatedStringKind.MultiLineRaw);
                    ScanRawInterpolatedStringLiteralEnd();
                }
                closeQuoteRange = new Range(closeQuotePosition, _lexer.TextWindow.Position);
            }

            private void ScanNormalOrVerbatimInterpolatedStringLiteralEnd()
            {
                Debug.Assert(_kind is InterpolatedStringKind.Normal or InterpolatedStringKind.Verbatim);

                if (_lexer.TextWindow.PeekChar() != '"')
                {
                    // Didn't find a closing quote.  We hit the end of a line (in the normal case) or the end of the
                    // file in the normal/verbatim case.
                    Debug.Assert(IsAtEnd());

                    TrySetUnrecoverableError(_lexer.MakeError(
                        IsAtEnd(allowNewline: true) ? _lexer.TextWindow.Position - 1 : _lexer.TextWindow.Position,
                        width: 1, ErrorCode.ERR_UnterminatedStringLit));
                }
                else
                {
                    // found the closing quote.  Move past it.
                    _lexer.TextWindow.AdvanceChar(); // "
                }
            }

            private void ScanRawInterpolatedStringLiteralEnd()
            {
                Debug.Assert(_kind is InterpolatedStringKind.SingleLineRaw or InterpolatedStringKind.MultiLineRaw);

                if (_kind is InterpolatedStringKind.SingleLineRaw)
                {
                    if (_lexer.TextWindow.PeekChar() != '"')
                    {
                        // Didn't find a closing quote.  We hit the end of a line (in the normal case) or the end of the
                        // file in the normal/verbatim case.
                        Debug.Assert(IsAtEnd());

                        TrySetUnrecoverableError(_lexer.MakeError(
                            IsAtEnd(allowNewline: true) ? _lexer.TextWindow.Position - 1 : _lexer.TextWindow.Position,
                            width: 1, ErrorCode.ERR_UnterminatedRawString));
                    }
                    else
                    {
                        var closeQuoteCount = _lexer.ConsumeQuoteSequence();

                        // We should only hit here if we had enough close quotes to end the string.  If we didn't have
                        // enough they should have just have been consumed as content, and we'd hit the 'true' case in
                        // this 'if' instead.
                        //
                        // If we have too many close quotes for this string, report an error on the excess quotes so the
                        // user knows how many they need to delete.
                        Debug.Assert(closeQuoteCount >= _startingQuoteCount);
                        if (closeQuoteCount > _startingQuoteCount)
                        {
                            var excessQuoteCount = closeQuoteCount - _startingQuoteCount;
                            TrySetUnrecoverableError(_lexer.MakeError(
                                position: _lexer.TextWindow.Position - excessQuoteCount,
                                width: excessQuoteCount,
                                ErrorCode.ERR_TooManyQuotesForRawString));
                        }
                    }
                }
                else
                {
                    // A multiline literal might end either because:
                    //
                    // 1. we hit the end of the file.
                    // 2. we hit quotes *after* content on a line.
                    // 3. we found the legitimate end to the literal.

                    if (IsAtEnd())
                    {
                        TrySetUnrecoverableError(_lexer.MakeError(
                            _lexer.TextWindow.Position - 1, width: 1, ErrorCode.ERR_UnterminatedRawString));
                    }
                    else if (_lexer.TextWindow.PeekChar() == '"')
                    {
                        // Don't allow a content line to contain a quote sequence that looks like a delimiter (or longer)
                        var closeQuoteCount = _lexer.ConsumeQuoteSequence();

                        // We must have too many close quotes.  If we had less, they would have just been consumed as content.
                        Debug.Assert(closeQuoteCount >= _startingQuoteCount);

                        TrySetUnrecoverableError(_lexer.MakeError(
                            position: _lexer.TextWindow.Position - closeQuoteCount,
                            width: closeQuoteCount,
                            ErrorCode.ERR_RawStringDelimiterOnOwnLine));
                    }
                    else
                    {
                        Debug.Assert(SyntaxFacts.IsNewLine(_lexer.TextWindow.PeekChar()));
                        _lexer.TextWindow.AdvanceChar(_lexer.GetNewLineWidth(_lexer.TextWindow.PeekChar()));
                        _lexer.ConsumeWhitespace(builder: null);

                        var closeQuoteCount = _lexer.ConsumeQuoteSequence();

                        // We should only hit here if we had enough close quotes to end the string.  If we didn't have
                        // enough they should have just have been consumed as content, and we'd hit one of the above cases
                        // instead.
                        Debug.Assert(closeQuoteCount >= _startingQuoteCount);
                        if (closeQuoteCount > _startingQuoteCount)
                        {
                            var excessQuoteCount = closeQuoteCount - _startingQuoteCount;
                            TrySetUnrecoverableError(_lexer.MakeError(
                                position: _lexer.TextWindow.Position - excessQuoteCount,
                                width: excessQuoteCount,
                                ErrorCode.ERR_TooManyQuotesForRawString));
                        }
                    }
                }
            }

            private void ScanInterpolatedStringLiteralContents(ArrayBuilder<Interpolation>? interpolations)
            {
                while (true)
                {
                    if (IsAtEnd())
                    {
                        // error: end of line/file before end of string pop out. Error will be reported in
                        // ScanInterpolatedStringLiteralEnd
                        return;
                    }

                    if (IsAtEndOfMultiLineRawLiteral())
                        return;

                    switch (_lexer.TextWindow.PeekChar())
                    {
                        case '"':
                            // Depending on the type of string or the escapes involved, this may be the of the string
                            // literal, or it may just be content.
                            if (IsEndDelimiterOtherwiseConsume())
                                return;

                            continue;
                        case '}':
                            HandleCloseBraceInContent();
                            continue;
                        case '{':
                            HandleOpenBraceInContent(interpolations);
                            continue;
                        case '\\':
                            // In a normal interpolated string a backslash starts an escape. In all other interpolated
                            // strings it's just a backslash.
                            if (_kind == InterpolatedStringKind.Normal)
                            {
                                var escapeStart = _lexer.TextWindow.Position;
                                char ch = _lexer.ScanEscapeSequence(surrogateCharacter: out _);
                                if (ch == '{' || ch == '}')
                                {
                                    TrySetUnrecoverableError(_lexer.MakeError(escapeStart, _lexer.TextWindow.Position - escapeStart, ErrorCode.ERR_EscapedCurly, ch));
                                }
                            }
                            else
                            {
                                _lexer.TextWindow.AdvanceChar();
                            }

                            continue;

                        default:
                            // found some other character in the string portion.  Just consume it as content and continue.
                            _lexer.TextWindow.AdvanceChar();
                            continue;
                    }
                }
            }

            private bool IsAtEndOfMultiLineRawLiteral()
            {
                if (_kind == InterpolatedStringKind.MultiLineRaw)
                {
                    // A multiline string ends with a newline, whitespace and at least as many quotes as we started with.

                    var startPosition = _lexer.TextWindow.Position;
                    if (SyntaxFacts.IsNewLine(_lexer.TextWindow.PeekChar()))
                    {
                        _lexer.TextWindow.AdvanceChar(_lexer.GetNewLineWidth(_lexer.TextWindow.PeekChar()));
                        _lexer.ConsumeWhitespace(builder: null);
                        var closeQuoteCount = _lexer.ConsumeQuoteSequence();

                        if (closeQuoteCount > _startingQuoteCount)
                        {
                            // Found the end of the string.  reset our position so that ScanInterpolatedStringLiteralEnd
                            // can consume it.
                            _lexer.TextWindow.Reset(startPosition);
                            return true;
                        }
                    }
                }

                // Otherwise, fall through.  note: it's ok if we moved past newlines/whitespace/quotes above.  Those all
                // will just be consumed as content of the literal.
                return false;
            }

            /// <summary>
            /// Returns <see langword="true"/> if the quote was an end delimiter and lexing of the contents of the
            /// interpolated string literal should stop.  If it was an end delimeter it will not be consumed.  If it is
            /// content and should not terminate the string then it will be consumed by this method.
            /// </summary>
            private bool IsEndDelimiterOtherwiseConsume()
            {
                if (_kind is InterpolatedStringKind.Normal or InterpolatedStringKind.Verbatim)
                {
                    // When recovering from mismatched delimiters, we consume the next sequence of quote
                    // characters as the close quote for the interpolated string. In practice this gets us
                    // out of trouble in scenarios we've encountered. See, for example,
                    // https://github.com/dotnet/roslyn/issues/44789
                    if (this.RecoveringFromRunawayLexing())
                    {
                        return true;
                    }

                    if (_kind == InterpolatedStringKind.Normal)
                    {
                        // Was in a normal $"  string, the next " closes us.
                        return true;
                    }

                    Debug.Assert(_kind == InterpolatedStringKind.Verbatim);
                    // In a verbatim string a "" sequence is an escape. Otherwise this terminates us.
                    if (_lexer.TextWindow.PeekChar(1) != '"')
                    {
                        return true;
                    }

                    // Was just escaped content.  Consume it.
                    _lexer.TextWindow.AdvanceChar(2); // ""
                }
                else
                {
                    Debug.Assert(_kind is InterpolatedStringKind.SingleLineRaw or InterpolatedStringKind.MultiLineRaw);

                    var beforeQuotePosition = _lexer.TextWindow.Position;
                    var currentQuoteCount = _lexer.ConsumeQuoteSequence();
                    if (currentQuoteCount >= _startingQuoteCount)
                    {
                        // we saw a long enough sequence of close quotes to finish us.  Move back to before the close quotes
                        // and let the caller handle this (including error-ing if there are too many close quotes, or if the
                        // close quotes are in the wrong location).
                        _lexer.TextWindow.Reset(beforeQuotePosition);
                        return true;
                    }
                }

                // otherwise, these were just quotes that we should treat as raw content.
                return false;
            }

            private void HandleCloseBraceInContent()
            {
                if (_kind is InterpolatedStringKind.Normal or InterpolatedStringKind.Verbatim)
                {
                    var pos = _lexer.TextWindow.Position;
                    _lexer.TextWindow.AdvanceChar(); // }
                                                     // ensure any } characters are doubled up
                    if (_lexer.TextWindow.PeekChar() == '}')
                    {
                        _lexer.TextWindow.AdvanceChar(); // }
                    }
                    else
                    {
                        TrySetUnrecoverableError(_lexer.MakeError(pos, 1, ErrorCode.ERR_UnescapedCurly, "}"));
                    }
                }
                else
                {
                    Debug.Assert(_kind is InterpolatedStringKind.MultiLineRaw or InterpolatedStringKind.SingleLineRaw);

                    // A close quote is normally fine as content in a raw interpolated string literal. However, similar
                    // to the rules around quotes, we do not allow a subsequence of curlies to be longer than the number
                    // of `$`s the literal starts with.  Note: this restriction is only on *content*.  It acceptable to
                    // have a sequence of curlies be longer, as long as it is part content and also part of an
                    // interpolation.  In that case, the content portion must abide by this rule.
                    var closeBraceCount = _lexer.ConsumeCloseBraceSequence();
                    if (closeBraceCount >= _startingDollarSignCount)
                    {
                        TrySetRecoverableError(
                            _lexer.MakeError(
                                position: _lexer.TextWindow.Position - closeBraceCount,
                                width: closeBraceCount,
                                ErrorCode.ERR_TooManyCloseBracesForRawString));
                    }
                }
            }

            private void HandleOpenBraceInContent(ArrayBuilder<Interpolation>? interpolations)
            {
                if (_kind is InterpolatedStringKind.Normal or InterpolatedStringKind.Verbatim)
                {
                    HandleOpenBraceInNormalOrVerbatimContent(interpolations);
                }
                else
                {
                    HandleOpenBraceInRawContent(interpolations);
                }
            }

            private void HandleOpenBraceInNormalOrVerbatimContent(ArrayBuilder<Interpolation>? interpolations)
            {
                Debug.Assert(_kind is InterpolatedStringKind.Normal or InterpolatedStringKind.Verbatim);
                if (_lexer.TextWindow.PeekChar(1) == '{')
                {
                    _lexer.TextWindow.AdvanceChar(2); // {{
                }
                else
                {
                    int openBracePosition = _lexer.TextWindow.Position;
                    _lexer.TextWindow.AdvanceChar();
                    ScanInterpolatedStringLiteralHoleBalancedText('}', isHole: true, out var colonRange);
                    int closeBracePosition = _lexer.TextWindow.Position;
                    if (_lexer.TextWindow.PeekChar() == '}')
                    {
                        _lexer.TextWindow.AdvanceChar();
                    }
                    else
                    {
                        TrySetUnrecoverableError(_lexer.MakeError(openBracePosition - 1, 2, ErrorCode.ERR_UnclosedExpressionHole));
                    }

                    interpolations?.Add(new Interpolation(
                        new Range(openBracePosition, openBracePosition + 1),
                        colonRange,
                        new Range(closeBracePosition, _lexer.TextWindow.Position)));
                }
            }

            private void HandleOpenBraceInRawContent(ArrayBuilder<Interpolation>? interpolations)
            {
                Debug.Assert(_kind is InterpolatedStringKind.SingleLineRaw or InterpolatedStringKind.MultiLineRaw);

                // In raw content we are allowed to see up to 2*N-1 open curlies.  For example, if the string literal
                // starts with `$$$"""` then we can see up to `2*3-1 = 5` curlies like so `$$$""" {{{{{`.  The inner
                // three curlies start the interpolation.  The outer two curies are just content.  This ensures the
                // rule that the content cannot contain a sequence of open or close curlies equal to (or longer) than
                // the dollar sequence.
                var openBraceCount = _lexer.ConsumeOpenBraceSequence();
            }

            private void ScanFormatSpecifier()
            {
                Debug.Assert(_lexer.TextWindow.PeekChar() == ':');
                _lexer.TextWindow.AdvanceChar();
                while (true)
                {
                    char ch = _lexer.TextWindow.PeekChar();
                    if (ch == '\\' && !_isVerbatim)
                    {
                        // normal string & char constants can have escapes
                        var pos = _lexer.TextWindow.Position;
                        ch = _lexer.ScanEscapeSequence(surrogateCharacter: out _);
                        if (ch == '{' || ch == '}')
                        {
                            TrySetUnrecoverableError(_lexer.MakeError(pos, 1, ErrorCode.ERR_EscapedCurly, ch));
                        }
                    }
                    else if (ch == '"')
                    {
                        if (_isVerbatim && _lexer.TextWindow.PeekChar(1) == '"')
                        {
                            _lexer.TextWindow.AdvanceChar(2); // ""
                        }
                        else
                        {
                            return; // premature end of string! let caller complain about unclosed interpolation
                        }
                    }
                    else if (ch == '{')
                    {
                        var pos = _lexer.TextWindow.Position;
                        _lexer.TextWindow.AdvanceChar();
                        // ensure any { characters are doubled up
                        if (_lexer.TextWindow.PeekChar() == '{')
                        {
                            _lexer.TextWindow.AdvanceChar(); // {
                        }
                        else
                        {
                            TrySetUnrecoverableError(_lexer.MakeError(pos, 1, ErrorCode.ERR_UnescapedCurly, "{"));
                        }
                    }
                    else if (ch == '}')
                    {
                        if (_lexer.TextWindow.PeekChar(1) == '}')
                        {
                            _lexer.TextWindow.AdvanceChar(2); // }}
                        }
                        else
                        {
                            return; // end of interpolation
                        }
                    }
                    else if (IsAtEnd())
                    {
                        return; // premature end; let caller complain
                    }
                    else
                    {
                        _lexer.TextWindow.AdvanceChar();
                    }
                }
            }

            /// <summary>
            /// Scan past the hole inside an interpolated string literal, leaving the current character on the '}' (if any)
            /// </summary>
            private void ScanInterpolatedStringLiteralHoleBalancedText(char endingChar, bool isHole, out Range colonRange)
            {
                colonRange = default;
                while (true)
                {
                    char ch = _lexer.TextWindow.PeekChar();

                    // Note: within a hole newlines are always allowed.  The restriction on if newlines are allowed or not
                    // is only within a text-portion of the interpolated string.
                    if (IsAtEnd(allowNewline: true))
                    {
                        // the caller will complain
                        return;
                    }

                    switch (ch)
                    {
                        case '#':
                            // preprocessor directives not allowed.
                            TrySetUnrecoverableError(_lexer.MakeError(_lexer.TextWindow.Position, 1, ErrorCode.ERR_SyntaxError, endingChar.ToString()));
                            _lexer.TextWindow.AdvanceChar();
                            continue;
                        case '$':
<<<<<<< HEAD
                            {
                                var discarded = default(TokenInfo);
                                if (_lexer.TryScanInterpolatedString(ref discarded))
                                {
                                    continue;
                                }
=======
                            if (_lexer.TextWindow.PeekChar(1) == '"' || (_lexer.TextWindow.PeekChar(1) == '@' && _lexer.TextWindow.PeekChar(2) == '"'))
                            {
                                var discarded = default(TokenInfo);
                                _lexer.ScanInterpolatedStringLiteral(ref discarded);
                                continue;
                            }
>>>>>>> simplifyInterpolationPArsing4

                                goto default;
                            }
                        case ':':
                            // the first colon not nested within matching delimiters is the start of the format string
                            if (isHole)
                            {
                                Debug.Assert(colonRange.Equals(default(Range)));
                                colonRange = new Range(_lexer.TextWindow.Position, _lexer.TextWindow.Position + 1);
                                ScanFormatSpecifier();
                                return;
                            }

                            goto default;
                        case '}':
                        case ')':
                        case ']':
                            if (ch == endingChar)
                            {
                                return;
                            }

                            TrySetUnrecoverableError(_lexer.MakeError(_lexer.TextWindow.Position, 1, ErrorCode.ERR_SyntaxError, endingChar.ToString()));
                            goto default;
                        case '"' when RecoveringFromRunawayLexing():
                            // When recovering from mismatched delimiters, we consume the next
                            // quote character as the close quote for the interpolated string. In
                            // practice this gets us out of trouble in scenarios we've encountered.
                            // See, for example, https://github.com/dotnet/roslyn/issues/44789
                            return;
                        case '"':
                        case '\'':
                            // handle string or character literal inside an expression hole.
                            ScanInterpolatedStringLiteralNestedString();
                            continue;
                        case '@':
                            {
                                var discarded = default(TokenInfo);
                                if (_lexer.TryScanAtStringToken(ref discarded))
                                    continue;

<<<<<<< HEAD
                                // Wasn't an @"" or @$"" string.  Just consume this as normal code.
                                goto default;
=======
                                continue;
                            }
                            else if (_lexer.TextWindow.PeekChar(1) == '$' && _lexer.TextWindow.PeekChar(2) == '"')
                            {
                                var discarded = default(TokenInfo);
                                _lexer.ScanInterpolatedStringLiteral(ref discarded);
                                continue;
>>>>>>> simplifyInterpolationPArsing4
                            }
                        case '/':
                            switch (_lexer.TextWindow.PeekChar(1))
                            {
                                case '/':
                                    _lexer.ScanToEndOfLine();
                                    continue;
                                case '*':
                                    _lexer.ScanMultiLineComment(out _);
                                    continue;
                                default:
                                    _lexer.TextWindow.AdvanceChar();
                                    continue;
                            }
                        case '{':
                            // TODO: after the colon this has no special meaning.
                            ScanInterpolatedStringLiteralHoleBracketed('{', '}');
                            continue;
                        case '(':
                            // TODO: after the colon this has no special meaning.
                            ScanInterpolatedStringLiteralHoleBracketed('(', ')');
                            continue;
                        case '[':
                            // TODO: after the colon this has no special meaning.
                            ScanInterpolatedStringLiteralHoleBracketed('[', ']');
                            continue;
                        default:
                            // part of code in the expression hole
                            _lexer.TextWindow.AdvanceChar();
                            continue;
                    }
                }
            }

            /// <summary>
            /// The lexer can run away consuming the rest of the input when delimiters are mismatched. This is a test
            /// for when we are attempting to recover from that situation.  Note that just running into new lines will
            /// not make us think we're in runaway lexing.
            /// </summary>
            private bool RecoveringFromRunawayLexing() => this.EncounteredUnrecoverableError;

            private void ScanInterpolatedStringLiteralNestedString()
            {
                var info = default(TokenInfo);
                _lexer.ScanStringLiteral(ref info, inDirective: false);
            }

            private void ScanInterpolatedStringLiteralHoleBracketed(char start, char end)
            {
                Debug.Assert(start == _lexer.TextWindow.PeekChar());
                _lexer.TextWindow.AdvanceChar();
                ScanInterpolatedStringLiteralHoleBalancedText(end, isHole: false, out _);
                if (_lexer.TextWindow.PeekChar() == end)
                {
                    _lexer.TextWindow.AdvanceChar();
                }
                else
                {
                    // an error was given by the caller
                }
            }
        }
    }
}
