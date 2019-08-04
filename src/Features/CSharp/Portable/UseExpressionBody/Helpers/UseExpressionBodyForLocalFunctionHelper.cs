// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Microsoft.CodeAnalysis.CSharp.UseExpressionBody
{
    internal class UseExpressionBodyForLocalFunctionHelper :
        UseExpressionBodyHelper<LocalFunctionStatementSyntax>
    {
        public static readonly UseExpressionBodyForLocalFunctionHelper Instance = new UseExpressionBodyForLocalFunctionHelper();

        private UseExpressionBodyForLocalFunctionHelper()
            : base(IDEDiagnosticIds.UseExpressionBodyForLocalFunctionsDiagnosticId,
                   new LocalizableResourceString(nameof(FeaturesResources.Use_expression_body_for_local_functions), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
                   new LocalizableResourceString(nameof(FeaturesResources.Use_block_body_for_local_functions), FeaturesResources.ResourceManager, typeof(FeaturesResources)),
                   CSharpCodeStyleOptions.PreferExpressionBodiedLocalFunctions,
                   ImmutableArray.Create(SyntaxKind.LocalFunctionStatement))
        {
        }

        protected override BlockSyntax GetBody(LocalFunctionStatementSyntax statement)
            => statement.Body;

        protected override ArrowExpressionClauseSyntax GetExpressionBody(LocalFunctionStatementSyntax statement)
            => statement.ExpressionBody;

        protected override LocalFunctionStatementSyntax WithSemicolonToken(LocalFunctionStatementSyntax statement, SyntaxToken token)
            => statement.WithSemicolonToken(token);

        protected override LocalFunctionStatementSyntax WithExpressionBody(LocalFunctionStatementSyntax statement, ArrowExpressionClauseSyntax expressionBody)
            => statement.WithExpressionBody(expressionBody);

        protected override LocalFunctionStatementSyntax WithBody(LocalFunctionStatementSyntax statement, BlockSyntax body)
            => statement.WithBody(body);
    }
}
