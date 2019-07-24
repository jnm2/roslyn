// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Composition;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.ConvertConditionalToIf;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Microsoft.CodeAnalysis.CSharp.ConvertConditionalToIf
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(CSharpConvertConditionalToIfCodeRefactoringProvider)), Shared]
    internal sealed class CSharpConvertConditionalToIfCodeRefactoringProvider : AbstractConvertConditionalToIfCodeRefactoringProvider<ConditionalExpressionSyntax, StatementSyntax>
    {
        protected override string CodeActionTitle => CSharpFeaturesResources.Convert_conditional_expression_to_if_statement;

        protected override bool IsInvalidAncestorForRefactoring(SyntaxNode node)
        {
            return node is LocalDeclarationStatementSyntax || node is ParameterSyntax;
        }

        protected override ReplaceNodeWithStatementResult CanReplaceWithStatement(SemanticModel semanticModel, SyntaxNode node)
        {
            switch (node)
            {
                case StatementSyntax statement:
                    {
                        return ReplaceNodeWithStatementResult.Success(statement);
                    }
                case ArrowExpressionClauseSyntax _:
                case { Parent: LambdaExpressionSyntax lambda } when lambda.Body == node:
                    {
                        return ConvertParentAndGetSingleStatement(semanticModel, node);
                    }
                default:
                    {
                        return ReplaceNodeWithStatementResult.NotPossibleInTheory;
                    }
            }
        }

        private static ReplaceNodeWithStatementResult ConvertParentAndGetSingleStatement(SemanticModel semanticModel, SyntaxNode node)
        {
            var originalAncestor = node.Parent;
            var convertedAncestor = CSharpDeclarationBodyHelpers.TryConvertToStatementBody(semanticModel, originalAncestor, out var statement);

            return convertedAncestor is null
                ? ReplaceNodeWithStatementResult.PossibleButConversionFailed
                : ReplaceNodeWithStatementResult.Success(statement, originalAncestor, convertedAncestor);
        }

        protected override (SyntaxNode condition, SyntaxNode whenTrue, SyntaxNode whenFalse) Deconstruct(ConditionalExpressionSyntax conditionalExpression)
        {
            return (conditionalExpression.Condition, conditionalExpression.WhenTrue, conditionalExpression.WhenFalse);
        }
    }
}
