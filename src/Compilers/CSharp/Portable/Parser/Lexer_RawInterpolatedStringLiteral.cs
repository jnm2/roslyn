// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis.PooledObjects;

namespace Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax
{
    internal partial class Lexer
    {
        private int ConsumeDollarSignSequence()
        {
            var start = TextWindow.Position;
            while (TextWindow.PeekChar() == '$')
                TextWindow.AdvanceChar();

            return TextWindow.Position - start;
        }

        private void ScanRawInterpolatedStringLiteral(ref TokenInfo info)
        {
            _builder.Length = 0;

            // We reuse the same kind as a normal interpolation.  This token is ephemeral and will not be exposed
            // outside the parser. Once parsing is complete, there will be an actual parsed
            // RawInterpolatedStringLiteralExpression node created with appropriate contents.
            info.Kind = SyntaxKind.InterpolatedStringToken;

            var beforeDollarSignPosition = this.TextWindow.Position;
            var startingDollarSignCount = ConsumeDollarSignSequence();
            Debug.Assert(startingDollarSignCount >= 1);

            var startingQuoteCount = ConsumeQuoteSequence();
            if (startingQuoteCount < 3)
            {
                // Note: 0-2 quotes are possible as we can enter ScanRawInterpolatedStringLiteral after only seeing
                // two or more $$ chars and nothing else.
                Debug.Assert(startingDollarSignCount >= 2);
                this.AddError(beforeDollarSignPosition, width: this.TextWindow.Position - beforeDollarSignPosition, ErrorCode.ERR_NotEnoughQuotesForRawString);
                return;
            }

            // TODO: We could consider looking for mistakes like the user using `@` here to provide them with a special
            // clarifying diagnostic message.

            this.ConsumeWhitespace(builder: null);
            var isMultiLine = SyntaxFacts.IsNewLine(this.TextWindow.PeekChar());

            this.TextWindow.Reset(beforeDollarSignPosition);
            ScanInterpolatedStringLiteralTop(
                interpolations: null,
                isMultiLine ? InterpolatedStringKind.MultiLineRaw : InterpolatedStringKind.SingleLineRaw,
                startingDollarSignCount,
                startingQuoteCount,
                ref info,
                out var error,
                closeQuoteMissing: out _);
            this.AddError(error);
        }
    }
}
