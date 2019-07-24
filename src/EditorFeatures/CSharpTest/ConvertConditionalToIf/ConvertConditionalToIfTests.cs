// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

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
    void Method(bool cond)
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
    void Method(bool cond)
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
        public async Task LocalDeclarationIsSplitFromInitialization()
        {
            var text = @"
class Test
{
    void Method(bool cond)
    {
        var local = cond [||]? 1 : 2;
    }
}
";
            var expected = @"
class Test
{
    void Method(bool cond)
    {
        int local;
        if (cond)
        {
            local = 1;
        }
        else
        {
            local = 2;
        }
    }
}
";
            await TestInRegularAndScriptAsync(text, expected);
        }

        [Fact]
        public async Task OutVarDeclarationIsSplitFromInitialization()
        {
            var text = @"
class Test
{
    void Method(bool cond)
    {
        _ = int.TryParse(cond [||]? ""1"" : ""2"", out var local);
    }
}
";
            var expected = @"
class Test
{
    void Method(bool cond)
    {
        int local;
        if (cond)
        {
            _ = int.TryParse(""1"", out local);
        }
        else
        {
            _ = int.TryParse(""2"", out local);
        }
    }
}
";
            await TestInRegularAndScriptAsync(text, expected);
        }

        [Fact]
        public async Task NotOfferedOnUsingDeclaration()
        {
            var text = @"
using System;

class Test
{
    void Method(bool cond, IDisposable disposable1, IDisposable disposable2)
    {
        using var local = cond [||]? disposable1 : disposable2;
    }
}
";
            await TestMissingAsync(text);
        }

        [Fact]
        public async Task NotOfferedOnFieldInitializer()
        {
            var text = @"
class Test
{
    static readonly bool cond;
    int field = cond [||]? 1 : 2;
}
";
            await TestMissingAsync(text);
        }

        [Fact]
        public async Task NotOfferedOnMethodParameterDefaultExpression()
        {
            var text = @"
class Test
{
    const bool cond = true;
    void Method(int parameter = cond [||]? 1 : 2) { }
}
";
            await TestMissingAsync(text);
        }

        [Fact]
        public async Task NotOfferedOnLocalFunctionParameterDefaultExpression()
        {
            var text = @"
class Test
{
    void Method()
    {
        const bool cond = true;
        void LocalFunction(int parameter = cond [||]? 1 : 2) { }
    }
}
";
            await TestMissingAsync(text);
        }

        [Fact]
        public async Task MethodExpressionBodyBecomesStatementBody()
        {
            var text = @"
class Test
{
    void Method(bool cond) => _ = cond [||]? 1 : 2;
}
";
            var expected = @"
class Test
{
    void Method(bool cond)
    {
        if (cond)
        {
            _ = 1;
        }
        else
        {
            _ = 2;
        }
    }
}
";
            await TestInRegularAndScriptAsync(text, expected);
        }

        [Fact]
        public async Task PropertyExpressionBodyBecomesStatementBody()
        {
            var text = @"
class Test
{
    private readonly bool cond;

    int Property => cond [||]? 1 : 2;
}
";
            var expected = @"
class Test
{
    private readonly bool cond;

    int Property
    {
        get
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
}
";
            await TestInRegularAndScriptAsync(text, expected);
        }

        [Fact]
        public async Task PropertyGetterExpressionBodyBecomesStatementBody()
        {
            var text = @"
class Test
{
    private readonly bool cond;

    int Property
    {
        get => cond [||]? 1 : 2;
    }
}
";
            var expected = @"
class Test
{
    private readonly bool cond;

    int Property
    {
        get
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
}
";
            await TestInRegularAndScriptAsync(text, expected);
        }

        [Fact]
        public async Task PropertySetterExpressionBodyBecomesStatementBody()
        {
            var text = @"
class Test
{
    bool Property
    {
        get => false;
        set => _ = value [||]? 1 : 2;
    }
}
";
            var expected = @"
class Test
{
    bool Property
    {
        get => false;
        set
        {
            if (value)
            {
                _ = 1;
            }
            else
            {
                _ = 2;
            }
        }
    }
}
";
            await TestInRegularAndScriptAsync(text, expected);
        }

        [Fact]
        public async Task LambdaExpressionBodyBecomesStatementBody()
        {
            var text = @"
class Test
{
    void Method(bool cond)
    {
        new System.Action(() => _ = cond [||]? 1 : 2);
    }
}
";
            var expected = @"
class Test
{
    void Method(bool cond)
    {
        new System.Action(() =>
        {
            if (cond)
            {
                _ = 1;
            }
            else
            {
                _ = 2;
            }
        });
    }
}
";
            await TestInRegularAndScriptAsync(text, expected);
        }

        [Fact]
        public async Task LocalFunctionExpressionBodyBecomesStatementBody()
        {
            var text = @"
class Test
{
    void Method(bool cond)
    {
        void LocalFunction() => _ = cond [||]? 1 : 2;
    }
}
";
            var expected = @"
class Test
{
    void Method(bool cond)
    {
        void LocalFunction()
        {
            if (cond)
            {
                _ = 1;
            }
            else
            {
                _ = 2;
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
