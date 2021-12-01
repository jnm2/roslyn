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
