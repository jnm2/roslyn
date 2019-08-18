﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Composition;
using System.Linq;
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

        protected override bool CanConvertToStatementBody(SyntaxNode container)
        {
            return CSharpBodyHelpers.CanConvertToStatementBody(container);
        }

        protected override SyntaxNode ConvertToStatementBody(SemanticModel semanticModel, SyntaxNode container, out StatementSyntax statement)
        {
            var converted = CSharpBodyHelpers.ConvertToStatementBody(semanticModel, container, out var block);
            statement = block?.Statements.Single();
            return converted;
        }

        protected override (SyntaxNode condition, SyntaxNode whenTrue, SyntaxNode whenFalse) Deconstruct(ConditionalExpressionSyntax conditionalExpression)
        {
            return (conditionalExpression.Condition, conditionalExpression.WhenTrue, conditionalExpression.WhenFalse);
        }
    }
}
