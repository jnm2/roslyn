// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Composition;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.ConvertConditionalToIf;

namespace Microsoft.CodeAnalysis.CSharp.ConvertConditionalToIf
{
    [ExportCodeRefactoringProvider(LanguageNames.CSharp, Name = nameof(CSharpConvertConditionalToIfCodeRefactoringProvider)), Shared]
    internal sealed class CSharpConvertConditionalToIfCodeRefactoringProvider : AbstractConvertConditionalToIfCodeRefactoringProvider
    {
        protected override string CodeActionTitle => CSharpFeaturesResources.Convert_conditional_expression_to_if_statement;
    }
}
