// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.CSharp.Utilities
{
    internal static class CSharpBodyHelpers
    {
        public static SyntaxNode TryConvertToStatementBody(
              SyntaxNode container,
              SemanticModel semanticModel,
              SyntaxNode containerForSemanticModel)
        {
            return TryConvertToStatementBody(container, semanticModel, containerForSemanticModel, out _);
        }

        public static SyntaxNode TryConvertToStatementBody(
            SyntaxNode container,
            SemanticModel semanticModel,
            SyntaxNode containerForSemanticModel,
            out StatementSyntax statement)
        {
            switch (container)
            {
                case LambdaExpressionSyntax lambda:
                    return TryConvertToStatementBody(lambda, semanticModel, (LambdaExpressionSyntax)containerForSemanticModel, out statement);

                case AccessorDeclarationSyntax accessor:
                    return TryConvertToBlock(
                        accessor,
                        accessor.ExpressionBody,
                        accessor.SemicolonToken,
                        createReturnStatementForExpression: container.IsKind(SyntaxKind.GetAccessorDeclaration),
                        (syntax, block) => syntax
                            .WithExpressionBody(null)
                            .WithSemicolonToken(default)
                            .WithBody(block),
                        syntax => syntax.Body,
                        out statement);

                case ConstructorDeclarationSyntax constructor:
                    return TryConvertToBlock(
                        constructor,
                        constructor.ExpressionBody,
                        constructor.SemicolonToken,
                        createReturnStatementForExpression: false,
                        (syntax, block) => syntax
                            .WithExpressionBody(null)
                            .WithSemicolonToken(default)
                            .WithBody(block),
                        syntax => syntax.Body,
                        out statement);

                case ConversionOperatorDeclarationSyntax conversionOperator:
                    return TryConvertToBlock(
                        conversionOperator,
                        conversionOperator.ExpressionBody,
                        conversionOperator.SemicolonToken,
                        createReturnStatementForExpression: true,
                        (syntax, block) => syntax
                            .WithExpressionBody(null)
                            .WithSemicolonToken(default)
                            .WithBody(block),
                        syntax => syntax.Body,
                        out statement);

                case IndexerDeclarationSyntax indexer:
                    return TryConvertToBlock(
                        indexer,
                        indexer.ExpressionBody,
                        indexer.SemicolonToken,
                        createReturnStatementForExpression: true,
                        (syntax, block) => syntax
                            .WithExpressionBody(null)
                            .WithSemicolonToken(default)
                            .WithAccessorList(CreateStatementBodiedGetAccessorList(block)),
                        syntax => syntax.AccessorList.Accessors.Single().Body,
                        out statement);

                case LocalFunctionStatementSyntax localFunction:
                    return TryConvertToBlock(
                        localFunction,
                        localFunction.ExpressionBody,
                        localFunction.SemicolonToken,
                        CreateReturnStatementForExpression(semanticModel, localFunction),
                        (syntax, block) => syntax
                            .WithExpressionBody(null)
                            .WithSemicolonToken(default)
                            .WithBody(block),
                        syntax => syntax.Body,
                        out statement);

                case MethodDeclarationSyntax method:
                    return TryConvertToBlock(
                        method,
                        method.ExpressionBody,
                        method.SemicolonToken,
                        CreateReturnStatementForExpression(semanticModel, method),
                        (syntax, block) => syntax
                            .WithExpressionBody(null)
                            .WithSemicolonToken(default)
                            .WithBody(block),
                        syntax => syntax.Body,
                        out statement);

                case OperatorDeclarationSyntax @operator:
                    return TryConvertToBlock(
                        @operator,
                        @operator.ExpressionBody,
                        @operator.SemicolonToken,
                        createReturnStatementForExpression: true,
                        (syntax, block) => syntax
                            .WithExpressionBody(null)
                            .WithSemicolonToken(default)
                            .WithBody(block),
                        syntax => syntax.Body,
                        out statement);

                case PropertyDeclarationSyntax property:
                    return TryConvertToBlock(
                        property,
                        property.ExpressionBody,
                        property.SemicolonToken,
                        createReturnStatementForExpression: true,
                        (syntax, block) => syntax
                            .WithExpressionBody(null)
                            .WithSemicolonToken(default)
                            .WithAccessorList(CreateStatementBodiedGetAccessorList(block)),
                        syntax => syntax.AccessorList.Accessors.Single().Body,
                        out statement);

                default:
                    statement = null;
                    return null;
            }
        }

        private static TSyntaxNode TryConvertToBlock<TSyntaxNode>(
            TSyntaxNode container,
            ArrowExpressionClauseSyntax arrowExpression,
            SyntaxToken semicolonToken,
            bool createReturnStatementForExpression,
            Func<TSyntaxNode, BlockSyntax, TSyntaxNode> withOnlyBody,
            Func<TSyntaxNode, BlockSyntax> getBody,
            out StatementSyntax statement)
            where TSyntaxNode : SyntaxNode
        {
            if (arrowExpression != null
                && arrowExpression.TryConvertToBlock(semicolonToken, createReturnStatementForExpression, out var block))
            {
                var result = withOnlyBody.Invoke(container, block);
                statement = getBody.Invoke(result).Statements.Single();
                return result;
            }

            statement = null;
            return null;
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
            return TryConvertToStatementBody(container, semanticModel, containerForSemanticModel, out _);
        }

        public static LambdaExpressionSyntax TryConvertToStatementBody(
            LambdaExpressionSyntax container,
            SemanticModel semanticModel,
            LambdaExpressionSyntax containerForSemanticModel,
            out StatementSyntax statement)
        {
            if (container is { Body: ExpressionSyntax expressionBody }
                && expressionBody.TryConvertToStatement(
                    semicolonTokenOpt: default,
                    CreateReturnStatementForExpression(semanticModel, containerForSemanticModel),
                    out var convertedStatement))
            {
                // If the user is converting to a block, it's likely they intend to add multiple
                // statements to it.  So make a multi-line block so that things are formatted properly
                // for them to do so.
                var result = container.WithBody(SyntaxFactory.Block(
                    SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithAppendedTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed),
                    SyntaxFactory.SingletonList(convertedStatement),
                    SyntaxFactory.Token(SyntaxKind.CloseBraceToken)));

                statement = ((BlockSyntax)result.Body).Statements.Single();
                return result;
            }

            statement = null;
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
