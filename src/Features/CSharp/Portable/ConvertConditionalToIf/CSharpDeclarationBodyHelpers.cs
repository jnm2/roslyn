// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Shared.Extensions;

namespace Microsoft.CodeAnalysis.ConvertConditionalToIf
{
    internal static class CSharpDeclarationBodyHelpers
    {
        public static TSyntaxNode TryConvertToStatementBody<TSyntaxNode>(SemanticModel semanticModel, TSyntaxNode container, out StatementSyntax statement)
            where TSyntaxNode : SyntaxNode
        {
            switch (container)
            {
                case LambdaExpressionSyntax { Body: ExpressionSyntax expressionBody } lambda:
                    if (expressionBody.TryConvertToStatement(
                        semicolonTokenOpt: default,
                        CreateReturnStatementForExpression(semanticModel, lambda),
                        out statement))
                    {
                        // If the user is converting to a block, it's likely they intend to add multiple
                        // statements to it.  So make a multi-line block so that things are formatted properly
                        // for them to do so.
                        return (TSyntaxNode)(SyntaxNode)lambda.WithBody(SyntaxFactory.Block(
                            SyntaxFactory.Token(SyntaxKind.OpenBraceToken).WithAppendedTrailingTrivia(SyntaxFactory.ElasticCarriageReturnLineFeed),
                            SyntaxFactory.SingletonList(statement),
                            SyntaxFactory.Token(SyntaxKind.CloseBraceToken)));
                    }
                    break;
            }

            statement = null;
            return null;
        }

        private static bool CreateReturnStatementForExpression(SemanticModel semanticModel, LambdaExpressionSyntax declaration)
        {
            var lambdaType = (INamedTypeSymbol)semanticModel.GetTypeInfo(declaration).ConvertedType;
            if (lambdaType.DelegateInvokeMethod.ReturnsVoid)
            {
                return false;
            }

            // 'async Task' is effectively a void-returning lambda.  we do not want to create
            // 'return statements' when converting.
            if (declaration.AsyncKeyword != default)
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
    }
}
