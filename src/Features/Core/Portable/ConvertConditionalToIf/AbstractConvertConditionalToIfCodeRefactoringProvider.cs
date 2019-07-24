// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.Formatting;

namespace Microsoft.CodeAnalysis.ConvertConditionalToIf
{
    internal abstract class AbstractConvertConditionalToIfCodeRefactoringProvider<TConditionalExpressionSyntax, TStatementSyntax> : CodeRefactoringProvider
        where TConditionalExpressionSyntax : SyntaxNode
        where TStatementSyntax : SyntaxNode
    {
        protected abstract string CodeActionTitle { get; }

        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var conditionalExpression = await context.TryGetRelevantNodeAsync<TConditionalExpressionSyntax>().ConfigureAwait(false);

            if (conditionalExpression is { })
            {
                var document = context.Document;

                context.RegisterRefactoring(new ConvertConditionalToIfCodeAction(
                    CodeActionTitle,
                    cancellationToken => ConvertAsync(document, conditionalExpression, cancellationToken)));
            }
        }

        private async Task<Document> ConvertAsync(Document document, TConditionalExpressionSyntax conditionalExpression, CancellationToken cancellationToken)
        {
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var syntaxGenerator = SyntaxGenerator.GetGenerator(document);

            var statement = conditionalExpression.FirstAncestorOrSelf<TStatementSyntax>();

            var conditionalExpressionParts = Deconstruct(conditionalExpression);

            var ifStatement = (TStatementSyntax)syntaxGenerator
                .IfStatement(
                    conditionalExpressionParts.condition.WithoutTrivia(),
                    GenerateBranch(conditionalExpressionParts.whenTrue),
                    GenerateBranch(conditionalExpressionParts.whenFalse))
                .WithAdditionalAnnotations(Formatter.Annotation);

            SyntaxNode[] GenerateBranch(SyntaxNode conditionalExpressionBranch)
            {
                return new[]
                {
                    statement.ReplaceNode(conditionalExpression, conditionalExpressionBranch.WithoutTrivia())
                };
            }

            var newRoot = syntaxRoot.ReplaceNode(
                statement.Parent,
                ReplaceStatement(statement.Parent, statement, ifStatement));

            return document.WithSyntaxRoot(newRoot);
        }

        protected abstract (SyntaxNode condition, SyntaxNode whenTrue, SyntaxNode whenFalse) Deconstruct(TConditionalExpressionSyntax conditionalExpression);

        protected abstract SyntaxNode ReplaceStatement(SyntaxNode parentNode, TStatementSyntax statement, TStatementSyntax newStatement);

        private sealed class ConvertConditionalToIfCodeAction : CodeAction.DocumentChangeAction
        {
            public ConvertConditionalToIfCodeAction(string title, Func<CancellationToken, Task<Document>> createChangedDocument)
                : base(title, createChangedDocument)
            {
            }
        }
    }
}
