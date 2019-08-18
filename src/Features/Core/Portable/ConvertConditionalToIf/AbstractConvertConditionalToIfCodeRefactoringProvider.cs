// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Linq;
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
        private static readonly SyntaxAnnotation s_followAnnotation = new SyntaxAnnotation();

        protected abstract string CodeActionTitle { get; }

        public sealed override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var conditionalExpression = await context.TryGetRelevantNodeAsync<TConditionalExpressionSyntax>().ConfigureAwait(false);
            if (conditionalExpression is null)
            {
                return;
            }

            var nodeToReplaceWithIfStatement = GetNodeToReplaceWithIfStatement(conditionalExpression, out var ancestorNeedingConversion);

            if (nodeToReplaceWithIfStatement is null
                || (ancestorNeedingConversion is { } && !CanConvertToStatementBody(ancestorNeedingConversion)))
            {
                return;
            }

            var document = context.Document;

            context.RegisterRefactoring(new ConvertConditionalToIfCodeAction(
                CodeActionTitle,
                cancellationToken => ConvertAsync(
                    document,
                    conditionalExpression,
                    nodeToReplaceWithIfStatement,
                    ancestorNeedingConversion,
                    cancellationToken)));
        }

        private async Task<Document> ConvertAsync(
            Document document,
            TConditionalExpressionSyntax conditionalExpression,
            SyntaxNode nodeToReplaceWithIfStatement,
            SyntaxNode ancestorNeedingConversion,
            CancellationToken cancellationToken)
        {
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var syntaxGenerator = SyntaxGenerator.GetGenerator(document);

            var convertedAncestor = ((SyntaxNode original, SyntaxNode @new)?)null;

            if (!(nodeToReplaceWithIfStatement is TStatementSyntax statementFormOfNodeToReplace))
            {
                var ancestorWithTrackedConditionalExpression = ancestorNeedingConversion.ReplaceNode(
                    conditionalExpression,
                    conditionalExpression.WithAdditionalAnnotations(s_followAnnotation));

                //var syntaxRootWithTrackedConditionalExpression = syntaxRoot.ReplaceNode(ancestorNeedingConversion, ancestorWithTrackedConditionalExpression);

                var newAncestor = ConvertToStatementBody(
                    await document.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false),
                    ancestorWithTrackedConditionalExpression,
                    out statementFormOfNodeToReplace);

                conditionalExpression = (TConditionalExpressionSyntax)statementFormOfNodeToReplace.GetAnnotatedNodes(s_followAnnotation).Single();
                convertedAncestor = (ancestorNeedingConversion, newAncestor);
            }

            var conditionalExpressionParts = Deconstruct(conditionalExpression);

            var ifStatement = (TStatementSyntax)syntaxGenerator
                .IfStatement(
                    conditionalExpressionParts.condition.WithoutTrivia(),
                    trueStatements: new[]
                    {
                        statementFormOfNodeToReplace.ReplaceNode(conditionalExpression, conditionalExpressionParts.whenTrue.WithoutTrivia())
                    },
                    falseStatements: new[]
                    {
                        statementFormOfNodeToReplace.ReplaceNode(conditionalExpression, conditionalExpressionParts.whenFalse.WithoutTrivia())
                    })
                .WithAdditionalAnnotations(Formatter.Annotation);

            return document.WithSyntaxRoot(convertedAncestor is var (original, @new)
                ? syntaxRoot.ReplaceNode(original, @new.ReplaceNode(statementFormOfNodeToReplace, ifStatement))
                : syntaxRoot.ReplaceNode(statementFormOfNodeToReplace, ifStatement));
        }

        private SyntaxNode GetNodeToReplaceWithIfStatement(SyntaxNode node, out SyntaxNode ancestorNeedingConversion)
        {
            foreach (var ancestor in node.Ancestors())
            {
                if (IsInvalidAncestorForRefactoring(ancestor))
                {
                    break;
                }

                if (CanReplaceWithStatement(ancestor, out ancestorNeedingConversion))
                {
                    return ancestor;
                }
            }

            ancestorNeedingConversion = null;
            return null;
        }

        protected abstract bool IsInvalidAncestorForRefactoring(SyntaxNode node);

        protected abstract bool CanReplaceWithStatement(SyntaxNode node, out SyntaxNode ancestorNeedingConversion);

        protected abstract bool CanConvertToStatementBody(SyntaxNode container);

        protected abstract SyntaxNode ConvertToStatementBody(SemanticModel semanticModel, SyntaxNode container, out TStatementSyntax statement);

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
