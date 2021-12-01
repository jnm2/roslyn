// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Diagnostics;

namespace Microsoft.CodeAnalysis.CSharp.Syntax.InternalSyntax
{
    internal partial class Lexer
    {
        private void ScanRawInterpolatedStringLiteral(ref TokenInfo info)
        {
            _builder.Length = 0;

            // We reuse the same kind as a normal interpolation.  This token is ephemeral and will not be exposed
            // outside the parser. Once parsing is complete, there will be an actual parsed
            // RawInterpolatedStringLiteralExpression node created with appropriate contents.
            info.Kind = SyntaxKind.InterpolatedStringToken;

            var start = this.TextWindow.Position;

            var prefixAtCount = ConsumeAtSignSequence();
            var dollarSignCount = ConsumeDollarSignSequence();
            var suffixAtCount = ConsumeAtSignSequence();
            var quoteCount = ConsumeQuoteSequence();

            var totalAtCount = prefixAtCount + suffixAtCount;

            if (totalAtCount > 0)
            {
                if (dollarSignCount > 0 || quoteCount > 0)
                {
                    // user had @'s mixed with $'s or "'s.  They're definitely trying to make some sort of weird
                    // verbatim/raw hybrid.  Give an explicit error that this is not ok.
                    this.AddError(start, width: this.TextWindow.Position - start, ErrorCode.ERR_CannotMixVerbatimAndRawStrings);
                    return;
                }
                else
                {
                    // There were multiple @'s but no $'s or "'s.  
                    Debug.Assert(totalAtCount >= 2);
                    this.AddError(start, width: 1, ErrorCode.ERR_ExpectedVerbatimLiteral);
                    return;
                }
            }

            Debug.Assert(dollarSignCount > 0);

            if (quoteCount < 3)
            {
                // Note: 0-2 quotes are possible as we can enter ScanRawInterpolatedStringLiteral after only seeing
                // two or more $$ chars and nothing else.
                this.AddError(start, width: this.TextWindow.Position - start, ErrorCode.ERR_NotEnoughQuotesForRawString);
                return;
            }

            this.TextWindow.Reset(start);
            ScanInterpolatedStringLiteralTop(
                ref info,
                out var error,
                kind: out _,
                openQuoteRange: out _,
                interpolations: null,
                closeQuoteRange: out _);
            this.AddError(error);
        }
    }
}
