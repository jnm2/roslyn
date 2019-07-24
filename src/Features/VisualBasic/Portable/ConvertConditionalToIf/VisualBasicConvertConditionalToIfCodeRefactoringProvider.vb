' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Composition
Imports Microsoft.CodeAnalysis.CodeRefactorings
Imports Microsoft.CodeAnalysis.ConvertConditionalToIf
Imports Microsoft.CodeAnalysis.VisualBasic.Syntax

Namespace Microsoft.CodeAnalysis.VisualBasic.ConvertConditionalToIf
    <ExportCodeRefactoringProvider(LanguageNames.VisualBasic, Name:=NameOf(VisualBasicConvertConditionalToIfCodeRefactoringProvider)), [Shared]>
    Friend NotInheritable Class VisualBasicConvertConditionalToIfCodeRefactoringProvider
        Inherits AbstractConvertConditionalToIfCodeRefactoringProvider(Of TernaryConditionalExpressionSyntax, StatementSyntax)

        Protected Overrides ReadOnly Property CodeActionTitle As String = VBFeaturesResources.Convert_conditional_expression_to_if_statement

        Protected Overrides Function Deconstruct(conditionalExpression As TernaryConditionalExpressionSyntax) As (condition As SyntaxNode, whenTrue As SyntaxNode, whenFalse As SyntaxNode)
            Return (conditionalExpression.Condition, conditionalExpression.WhenTrue, conditionalExpression.WhenFalse)
        End Function

        Protected Overrides Function ReplaceStatement(parentNode As SyntaxNode, statement As StatementSyntax, newStatement As StatementSyntax) As SyntaxNode
            Throw New NotImplementedException()
        End Function
    End Class
End Namespace
