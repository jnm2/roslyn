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

        protected override (SyntaxNode condition, SyntaxNode whenTrue, SyntaxNode whenFalse) Deconstruct(ConditionalExpressionSyntax conditionalExpression)
        {
            return (conditionalExpression.Condition, conditionalExpression.WhenTrue, conditionalExpression.WhenFalse);
        }

        protected override SyntaxNode ReplaceStatement(SyntaxNode parentNode, StatementSyntax statement, StatementSyntax newStatement)
        {
            if (!(parentNode is BlockSyntax)) throw new System.NotImplementedException();

            return parentNode.ReplaceNode(statement, newStatement);
        }
    }
}
