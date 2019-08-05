// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Composition;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.ConvertConditionalToIf;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp.Utilities;

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

        protected override bool CanReplaceWithStatement(SyntaxNode node, out SyntaxNode ancestorNeedingConversion)
        {
            switch (node)
            {
                case StatementSyntax statement:
                    {
                        ancestorNeedingConversion = null;
                        return true;
                    }
                case ArrowExpressionClauseSyntax _:
                case { Parent: LambdaExpressionSyntax lambda } when lambda.Body == node:
                    {
                        ancestorNeedingConversion = node.Parent;
                        return true;
                    }
            }

            ancestorNeedingConversion = null;
            return false;
        }

        protected override SyntaxNode TryConvertToStatementBody(SyntaxNode container, SemanticModel semanticModel, SyntaxNode containerForSemanticModel, out StatementSyntax statement)
        {
            return CSharpDeclarationBodyHelpers.TryConvertToStatementBody(container, semanticModel, containerForSemanticModel, out statement);
        }

        protected override (SyntaxNode condition, SyntaxNode whenTrue, SyntaxNode whenFalse) Deconstruct(ConditionalExpressionSyntax conditionalExpression)
        {
            return (conditionalExpression.Condition, conditionalExpression.WhenTrue, conditionalExpression.WhenFalse);
        }
    }
}
