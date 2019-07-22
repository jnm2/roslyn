' Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

Imports System.Composition
Imports Microsoft.CodeAnalysis.CodeRefactorings
Imports Microsoft.CodeAnalysis.ConvertConditionalToIf

Namespace Microsoft.CodeAnalysis.VisualBasic.ConvertConditionalToIf
    <ExportCodeRefactoringProvider(LanguageNames.VisualBasic, Name:=NameOf(VisualBasicConvertConditionalToIfCodeRefactoringProvider)), [Shared]>
    Friend NotInheritable Class VisualBasicConvertConditionalToIfCodeRefactoringProvider
        Inherits AbstractConvertConditionalToIfCodeRefactoringProvider

        Protected Overrides ReadOnly Property CodeActionTitle As String = VBFeaturesResources.Convert_conditional_expression_to_if_statement
    End Class
End Namespace
