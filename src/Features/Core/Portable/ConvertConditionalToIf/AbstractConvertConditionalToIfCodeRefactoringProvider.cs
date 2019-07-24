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
    internal abstract partial class AbstractConvertConditionalToIfCodeRefactoringProvider<TConditionalExpressionSyntax, TStatementSyntax> : CodeRefactoringProvider
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

            var semanticModel = await context.Document.GetSemanticModelAsync(context.CancellationToken).ConfigureAwait(false);

            var result = GetNodeToReplaceWithIfStatement(semanticModel, conditionalExpression.WithAdditionalAnnotations(s_followAnnotation));
            if (result.StatementFormOfNode is null)
            {
                // Either not possible in theory or we bailed for some reason when trying to convert something to
                // statement form.
                return;
            }

            var document = context.Document;

            context.RegisterRefactoring(new ConvertConditionalToIfCodeAction(
                CodeActionTitle,
                cancellationToken => ConvertAsync(
                    document,
                    result,
                    cancellationToken)));
        }

        private async Task<Document> ConvertAsync(
            Document document,
            ReplaceNodeWithStatementResult result,
            CancellationToken cancellationToken)
        {
            var syntaxRoot = await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false);
            var syntaxGenerator = SyntaxGenerator.GetGenerator(document);

            var conditionalExpressionAfterConversion = (TConditionalExpressionSyntax)result.StatementFormOfNode.GetAnnotatedNodes(s_followAnnotation).Single();

            var conditionalExpressionParts = Deconstruct(conditionalExpressionAfterConversion);

            var ifStatement = (TStatementSyntax)syntaxGenerator
                .IfStatement(
                    conditionalExpressionParts.condition.WithoutTrivia(),
                    trueStatements: new[]
                    {
                        result.StatementFormOfNode.ReplaceNode(conditionalExpressionAfterConversion, conditionalExpressionParts.whenTrue.WithoutTrivia())
                    },
                    falseStatements: new[]
                    {
                        result.StatementFormOfNode.ReplaceNode(conditionalExpressionAfterConversion, conditionalExpressionParts.whenFalse.WithoutTrivia())
                    })
                .WithAdditionalAnnotations(Formatter.Annotation);

            if (result.ConvertedAncestor is { })
            {
                syntaxRoot = syntaxRoot.ReplaceNode(result.OriginalAncestor, result.ConvertedAncestor);
            }

            syntaxRoot = syntaxRoot.ReplaceNode(result.StatementFormOfNode, ifStatement);

            return document.WithSyntaxRoot(syntaxRoot);
        }

        private ReplaceNodeWithStatementResult GetNodeToReplaceWithIfStatement(SemanticModel semanticModel, SyntaxNode node)
        {
            foreach (var ancestor in node.Ancestors())
            {
                if (IsInvalidAncestorForRefactoring(ancestor))
                {
                    break;
                }

                var result = CanReplaceWithStatement(semanticModel, ancestor);
                if (result.IsPossibleInTheory)
                {
                    // If possible in theory, stop searching even if we bailed for some reason when trying to convert
                    // something to statement form.
                    return result;
                }

                // If not possible in theory, continue searching higher up.
            }

            return ReplaceNodeWithStatementResult.NotPossibleInTheory;
        }

        protected abstract bool IsInvalidAncestorForRefactoring(SyntaxNode node);

        protected abstract ReplaceNodeWithStatementResult CanReplaceWithStatement(SemanticModel semanticModel, SyntaxNode node);

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
