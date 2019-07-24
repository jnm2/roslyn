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
            if (conditionalExpression is null)
            {
                return;
            }

            var nodeToReplaceWithIfStatement = GetNodeToReplaceWithIfStatement(conditionalExpression);
            if (nodeToReplaceWithIfStatement is null)
            {
                return;
            }

            var document = context.Document;

            context.RegisterRefactoring(new ConvertConditionalToIfCodeAction(
                CodeActionTitle,
                cancellationToken => ConvertAsync(document, conditionalExpression, nodeToReplaceWithIfStatement, cancellationToken)));
        }

        private async Task<Document> ConvertAsync(Document document, TConditionalExpressionSyntax conditionalExpression, SyntaxNode nodeToReplaceWithIfStatement, CancellationToken cancellationToken)
        {
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var syntaxGenerator = SyntaxGenerator.GetGenerator(document);

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
                    nodeToReplaceWithIfStatement.ReplaceNode(conditionalExpression, conditionalExpressionBranch.WithoutTrivia())
                };
            }

            var newRoot = syntaxRoot.ReplaceNode(
                nodeToReplaceWithIfStatement.Parent,
                ReplaceWithStatement(nodeToReplaceWithIfStatement.Parent, nodeToReplaceWithIfStatement, ifStatement));

            return document.WithSyntaxRoot(newRoot);
        }

        private SyntaxNode GetNodeToReplaceWithIfStatement(SyntaxNode node)
        {
            foreach (var ancestor in node.Ancestors())
            {
                if (IsInvalidAncestorForRefactoring(ancestor))
                {
                    break;
                }

                if (CanBeReplacedWithStatement(ancestor))
                {
                    return ancestor;
                }
            }

            return null;
        }

        protected abstract bool IsInvalidAncestorForRefactoring(SyntaxNode node);

        protected abstract bool CanBeReplacedWithStatement(SyntaxNode node);

        protected abstract SyntaxNode ReplaceWithStatement(SyntaxNode parentNode, SyntaxNode nodeToReplace, TStatementSyntax newStatement);

        protected abstract (SyntaxNode condition, SyntaxNode whenTrue, SyntaxNode whenFalse) Deconstruct(TConditionalExpressionSyntax conditionalExpression);

        private sealed class ConvertConditionalToIfCodeAction : CodeAction.DocumentChangeAction
        {
            public ConvertConditionalToIfCodeAction(string title, Func<CancellationToken, Task<Document>> createChangedDocument)
                : base(title, createChangedDocument)
            {
            }
        }
    }
}
