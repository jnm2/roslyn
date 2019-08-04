// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.CSharp.Utilities
{
    internal static class CSharpDeclarationBodyHelpers
    {
        public static SyntaxNode TryConvertToStatementBody(
            SyntaxNode container,
            SemanticModel semanticModel,
            SyntaxNode containerForSemanticModel)
        {
            return container switch
            {
                LambdaExpressionSyntax lambda
                    => TryConvertToStatementBody(lambda, semanticModel, (LambdaExpressionSyntax)containerForSemanticModel),

                AccessorDeclarationSyntax { ExpressionBody: { } expressionBody } accessor
                    => expressionBody.TryConvertToBlock(
                        accessor.SemicolonToken,
                        createReturnStatementForExpression: container.IsKind(SyntaxKind.GetAccessorDeclaration),
                        out var block)
                    ? accessor
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(block)
                    : null,

                ConstructorDeclarationSyntax { ExpressionBody: { } expressionBody } constructor
                    => expressionBody.TryConvertToBlock(
                        constructor.SemicolonToken,
                        createReturnStatementForExpression: false,
                        out var block)
                    ? constructor
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(block)
                    : null,

                ConversionOperatorDeclarationSyntax { ExpressionBody: { } expressionBody } conversionOperator
                    => expressionBody.TryConvertToBlock(
                        conversionOperator.SemicolonToken,
                        createReturnStatementForExpression: true,
                        out var block)
                    ? conversionOperator
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(block)
                    : null,

                IndexerDeclarationSyntax { ExpressionBody: { } expressionBody } indexer
                    => expressionBody.TryConvertToBlock(
                        indexer.SemicolonToken,
                        createReturnStatementForExpression: true,
                        out var block)
                    ? indexer
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithAccessorList(CreateStatementBodiedGetAccessorList(block))
                    : null,

                LocalFunctionStatementSyntax { ExpressionBody: { } expressionBody } localFunction
                    => expressionBody.TryConvertToBlock(
                        localFunction.SemicolonToken,
                        CreateReturnStatementForExpression(semanticModel, localFunction),
                        out var block)
                    ? localFunction
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(block)
                    : null,

                MethodDeclarationSyntax { ExpressionBody: { } expressionBody } method
                    => expressionBody.TryConvertToBlock(
                        method.SemicolonToken,
                        CreateReturnStatementForExpression(semanticModel, method),
                        out var block)
                    ? method
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(block)
                    : null,

                OperatorDeclarationSyntax { ExpressionBody: { } expressionBody } @operator
                    => expressionBody.TryConvertToBlock(
                        @operator.SemicolonToken,
                        createReturnStatementForExpression: true,
                        out var block)
                    ? @operator
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithBody(block)
                    : null,

                PropertyDeclarationSyntax { ExpressionBody: { } expressionBody } property
                    => expressionBody.TryConvertToBlock(
                        property.SemicolonToken,
                        createReturnStatementForExpression: true,
                        out var block)
                    ? property
                        .WithExpressionBody(null)
                        .WithSemicolonToken(default)
                        .WithAccessorList(CreateStatementBodiedGetAccessorList(block))
                    : null,

                _ => (SyntaxNode)null
            };
        }

        private static AccessorListSyntax CreateStatementBodiedGetAccessorList(BlockSyntax block)
        {
            // When converting an expression-bodied property to a block body, always attempt to
            // create an accessor with a block body (even if the user likes expression bodied
            // accessors.  While this technically doesn't match their preferences, it fits with
            // the far more likely scenario that the user wants to convert this property into
            // a full property so that they can flesh out the body contents.  If we keep around
            // an expression bodied accessor they'll just have to convert that to a block as well
            // and that means two steps to take instead of one.

            var getAccessor = SyntaxFactory.AccessorDeclaration(SyntaxKind.GetAccessorDeclaration).WithBody(block);

            return SyntaxFactory.AccessorList(SyntaxFactory.SingletonList(getAccessor));
        }

        public static LambdaExpressionSyntax TryConvertToStatementBody(
            LambdaExpressionSyntax container,
            SemanticModel semanticModel,
            LambdaExpressionSyntax containerForSemanticModel)
        {
            if (container is { Body: ExpressionSyntax expressionBody }
                && expressionBody.TryConvertToStatement(
                    semicolonTokenOpt: default,
                    CreateReturnStatementForExpression(semanticModel, containerForSemanticModel),
                    out var statement))
            {
                // If the user is converting to a block, it's likely they intend to add multiple
                // statements to it.  So make a multi-line block so that things are formatted properly
                // for them to do so.
                return container.WithBody(SyntaxFactory.Block(
                    SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithAppendedTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed),
                    SyntaxFactory.SingletonList(statement),
                    SyntaxFactory.Token(SyntaxKind.CloseBraceToken)));
            }

            return null;
        }

        private static bool CreateReturnStatementForExpression(SemanticModel semanticModel, LambdaExpressionSyntax lambdaExpression)
        {
            var lambdaType = (INamedTypeSymbol)semanticModel.GetTypeInfo(lambdaExpression).ConvertedType;
            if (lambdaType.DelegateInvokeMethod.ReturnsVoid)
            {
                return false;
            }

            // 'async Task' is effectively a void-returning lambda.  we do not want to create
            // 'return statements' when converting.
            if (lambdaExpression.AsyncKeyword != default)
            {
                var returnType = lambdaType.DelegateInvokeMethod.ReturnType;
                if (returnType.IsErrorType())
                {
                    // "async Goo" where 'Goo' failed to bind.  If 'Goo' is 'Task' then it's
                    // reasonable to assume this is just a missing 'using' and that this is a true
                    // "async Task" lambda.  If the name isn't 'Task', then this looks like a
                    // real return type, and we should use return statements.
                    return returnType.Name != nameof(Task);
                }

                var taskType = semanticModel.Compilation.GetTypeByMetadataName(typeof(Task).FullName);
                if (returnType.Equals(taskType))
                {
                    // 'async Task'.  definitely do not create a 'return' statement;
                    return false;
                }
            }

            return true;
        }

        private static bool CreateReturnStatementForExpression(SemanticModel semanticModel, LocalFunctionStatementSyntax statement)
        {
            if (statement.Modifiers.Any(SyntaxKind.AsyncKeyword))
            {
                // if it's 'async TaskLike' (where TaskLike is non-generic) we do *not* want to
                // create a return statement.  This is just the 'async' version of a 'void' local function.
                var symbol = semanticModel.GetDeclaredSymbol(statement);
                return symbol is IMethodSymbol methodSymbol &&
                    methodSymbol.ReturnType is INamedTypeSymbol namedType &&
                    namedType.Arity != 0;
            }

            return !statement.ReturnType.IsVoid();
        }

        private static bool CreateReturnStatementForExpression(SemanticModel semanticModel, MethodDeclarationSyntax declaration)
        {
            if (declaration.Modifiers.Any(SyntaxKind.AsyncKeyword))
            {
                // if it's 'async TaskLike' (where TaskLike is non-generic) we do *not* want to
                // create a return statement.  This is just the 'async' version of a 'void' method.
                var method = semanticModel.GetDeclaredSymbol(declaration);
                return method.ReturnType is INamedTypeSymbol namedType && namedType.Arity != 0;
            }

            return !declaration.ReturnType.IsVoid();
        }
    }
}
