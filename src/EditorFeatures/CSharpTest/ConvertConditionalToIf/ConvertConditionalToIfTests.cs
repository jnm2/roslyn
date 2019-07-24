﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.CodeStyle;
using Microsoft.CodeAnalysis.CSharp.ConvertConditionalToIf;
using Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.CodeRefactorings;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Test.Utilities;
using Xunit;

namespace Microsoft.CodeAnalysis.Editor.CSharp.UnitTests.ConvertConditionalToIf
{
    [Trait(Traits.Feature, Traits.Features.CodeActionsConvertConditionalToIf)]
    public sealed class ConvertConditionalToIfTests : AbstractCSharpCodeActionTest
    {
        protected override CodeRefactoringProvider CreateCodeRefactoringProvider(Workspace workspace, TestParameters parameters)
        {
            return new CSharpConvertConditionalToIfCodeRefactoringProvider();
        }

        private IDictionary<OptionKey, object> PreferBracesWhenMultiline => OptionsSet(SingleOption(
            CSharpCodeStyleOptions.PreferBraces,
            new CodeStyleOption<PreferBracesPreference>(PreferBracesPreference.WhenMultiline, NotificationOption.None)));

        private IDictionary<OptionKey, object> PreferBracesNone => OptionsSet(SingleOption(
            CSharpCodeStyleOptions.PreferBraces,
            new CodeStyleOption<PreferBracesPreference>(PreferBracesPreference.None, NotificationOption.None)));

        [Fact]
        public async Task ForeachLoopIsDuplicated()
        {
            var text = @"
class Test
{
    int Method(bool cond)
    {
        foreach (var _ in cond [||]? new[] { 1 } : new[] { 2, 3 })
        {
            // ...
        }
    }
}
";
            var expected = @"
class Test
{
    int Method(bool cond)
    {
        if (cond)
        {
            foreach (var _ in new[] { 1 })
            {
                // ...
            }
        }
        else
        {
            foreach (var _ in new[] { 2, 3 })
            {
                // ...
            }
        }
    }
}
";
            await TestInRegularAndScriptAsync(text, expected);
        }

        [Fact]
        public async Task BracesAreGeneratedForSingleLineOperandsWithPreferBracesAlways()
        {
            var text = @"
class Test
{
    int Method(bool cond)
    {
        return cond [||]? 1 : 2;
    }
}
";
            var expected = @"
class Test
{
    int Method(bool cond)
    {
        if (cond)
        {
            return 1;
        }
        else
        {
            return 2;
        }
    }
}
";
            await TestInRegularAndScriptAsync(text, expected);
        }

        [Fact(Skip = "Possible SyntaxGenerator.IfStatement bug")]
        public async Task BracesAreNotGeneratedForSingleLineOperandsWithPreferBracesWhenMultiline()
        {
            var text = @"
class Test
{
    int Method(bool cond)
    {
        return cond [||]? 1 : 2;
    }
}
";
            var expected = @"
class Test
{
    int Method(bool cond)
    {
        if (cond)
            return 1;
        else
            return 2;
    }
}
";
            await TestInRegularAndScriptAsync(text, expected, options: PreferBracesWhenMultiline);
        }

        [Fact(Skip = "Possible formatter bug")]
        public async Task BracesAreGeneratedForMultiLineOperandsWithPreferBracesWhenMultiline()
        {
            var text = @"
class Test
{
    int Method(bool cond)
    {
        return cond
            [||]? 1
                + 1
            : 2
                + 2;
    }
}
";
            var expected = @"
class Test
{
    int Method(bool cond)
    {
        if (cond)
        {
            return 1
                + 1;
        }
        else
        {
            return 2
                + 2;
        }
    }
}
";
            await TestInRegularAndScriptAsync(text, expected, options: PreferBracesWhenMultiline);
        }

        [Fact(Skip = "Possible formatter bug")]
        public async Task BracesAreNotGeneratedForMultilineOperandsWithPreferBracesNone()
        {
            var text = @"
class Test
{
    int Method(bool cond)
    {
        return cond
            [||]? 1
                + 1
            : 2
                + 2;
    }
}
";
            var expected = @"
class Test
{
    int Method(bool cond)
    {
        if (cond)
            return 1
                + 1;
        else
            return 2
                + 2;
    }
}
";
            await TestInRegularAndScriptAsync(text, expected, options: PreferBracesNone);
        }
    }
}
