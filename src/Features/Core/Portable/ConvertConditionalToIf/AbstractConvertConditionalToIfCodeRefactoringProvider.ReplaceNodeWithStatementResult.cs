// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;

namespace Microsoft.CodeAnalysis.ConvertConditionalToIf
{
    internal abstract partial class AbstractConvertConditionalToIfCodeRefactoringProvider<TConditionalExpressionSyntax, TStatementSyntax> where TConditionalExpressionSyntax : SyntaxNode
        where TStatementSyntax : SyntaxNode
    {
        protected readonly struct ReplaceNodeWithStatementResult
        {
            private ReplaceNodeWithStatementResult(bool isConversionPossibleInTheory, TStatementSyntax statementFormOfNode, SyntaxNode originalAncestor, SyntaxNode convertedAncestor)
            {
                IsPossibleInTheory = isConversionPossibleInTheory;
                StatementFormOfNode = statementFormOfNode;
                OriginalAncestor = originalAncestor;
                ConvertedAncestor = convertedAncestor;
            }

            public static ReplaceNodeWithStatementResult NotPossibleInTheory { get; } = new ReplaceNodeWithStatementResult(false, null, null, null);

            public static ReplaceNodeWithStatementResult PossibleButConversionFailed { get; } = new ReplaceNodeWithStatementResult(true, null, null, null);

            public static ReplaceNodeWithStatementResult Success(TStatementSyntax statementFormOfNode)
            {
                return new ReplaceNodeWithStatementResult(
                    isConversionPossibleInTheory: true,
                    statementFormOfNode ?? throw new ArgumentNullException(nameof(statementFormOfNode)),
                    originalAncestor: null,
                    convertedAncestor: null);
            }

            public static ReplaceNodeWithStatementResult Success(TStatementSyntax statementFormOfNode, SyntaxNode originalAncestor, SyntaxNode convertedAncestor)
            {
                return new ReplaceNodeWithStatementResult(
                    isConversionPossibleInTheory: true,
                    statementFormOfNode ?? throw new ArgumentNullException(nameof(statementFormOfNode)),
                    originalAncestor ?? throw new ArgumentNullException(nameof(originalAncestor)),
                    convertedAncestor ?? throw new ArgumentNullException(nameof(convertedAncestor)));
            }

            public bool IsPossibleInTheory { get; }
            public TStatementSyntax StatementFormOfNode { get; }
            public SyntaxNode OriginalAncestor { get; }
            public SyntaxNode ConvertedAncestor { get; }
        }
    }
}
