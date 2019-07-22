// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeRefactorings;

namespace Microsoft.CodeAnalysis.ConvertConditionalToIf
{
    internal abstract class AbstractConvertConditionalToIfCodeRefactoringProvider : CodeRefactoringProvider
    {
        protected abstract string CodeActionTitle { get; }

        public sealed override Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            throw new System.NotImplementedException();
        }
    }
}
