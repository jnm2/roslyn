// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.CodeAnalysis.CSharp.Symbols;
using Xunit;

namespace Microsoft.CodeAnalysis.CSharp.UnitTests.Semantics.BackingFieldAccess
{
    public sealed class BindingTests : SemanticModelTestBase
    {
        // TODO: Test with field already defined in various scopes (local, local function, indexer parameter- oh, test
        // indexers too- field of immediate class, field outside nested class, non-field objects like methods, classes,
        // namespaces)

        // TODO: Emit with one auto and one non-auto accessor to flush out diagnostics work

        // TODO: Diagnostic for use in interface

        [Fact]
        public void BackingFieldIsNotAccessibleInPropertyInitializer()
        {
            var text = @"
class C
{
    string Property { get => field; } = nameof(/*<bind>*/ field /*</bind>*/);
}
";
            VerifyFieldErrorBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldReadInGetter_NoSetter()
        {
            var text = @"
class C
{
    string Property => /*<bind>*/ field /*</bind>*/;
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldAssignedInGetter_NoSetter()
        {
            var text = @"
class C
{
    string Property => /*<bind>*/ field /*</bind>*/ ??= string.Empty;
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldRefTakenInGetter_NoSetter()
        {
            var text = @"
class C
{
    string Property => M(ref /*<bind>*/ field /*</bind>*/);

    string M(ref string p) { }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_NameofInGetter_NoSetter()
        {
            var text = @"
class C
{
    string Property => nameof(/*<bind>*/ field /*</bind>*/);
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldReadInGetter_AutoSetter()
        {
            var text = @"
class C
{
    string Property { get => /*<bind>*/ field /*</bind>*/; set; }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldAssignedInGetter_AutoSetter()
        {
            var text = @"
class C
{
    string Property { get => /*<bind>*/ field /*</bind>*/ ??= string.Empty; set; }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldRefTakenInGetter_AutoSetter()
        {
            var text = @"
class C
{
    string Property { get => M(ref /*<bind>*/ field /*</bind>*/); set; }

    string M(ref string p) { }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_NameofInGetter_AutoSetter()
        {
            var text = @"
class C
{
    string Property { get => nameof(/*<bind>*/ field /*</bind>*/); set; }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldReadInGetter_NormalSetter()
        {
            var text = @"
class C
{
    string Property { get => /*<bind>*/ field /*</bind>*/; set { } }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldAssignedInGetter_NormalSetter()
        {
            var text = @"
class C
{
    string Property { get => /*<bind>*/ field /*</bind>*/ ??= string.Empty; set { } }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldRefTakenInGetter_NormalSetter()
        {
            var text = @"
class C
{
    string Property { get => M(ref /*<bind>*/ field /*</bind>*/); set { } }

    string M(ref string p) { }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_NameofInGetter_NormalSetter()
        {
            var text = @"
class C
{
    string Property { get => nameof(/*<bind>*/ field /*</bind>*/); set { } }
}
";
            VerifyFieldBinding(text);
        }


        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldReadInGetter_NameofInSetter()
        {
            var text = @"
class C
{
    string Property { get => /*<bind>*/ field /*</bind>*/; set { _ = nameof(field); } }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldAssignedInGetter_NameofInSetter()
        {
            var text = @"
class C
{
    string Property { get => /*<bind>*/ field /*</bind>*/ ??= string.Empty; set { _ = nameof(field); } }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_FieldRefTakenInGetter_NameofInSetter()
        {
            var text = @"
class C
{
    string Property { get => M(ref /*<bind>*/ field /*</bind>*/); set { _ = nameof(field); } }

    string M(ref string p) { }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void GetterFieldReferenceIsBoundToBackingField_NameofInGetter_NameofInSetter()
        {
            var text = @"
class C
{
    string Property { get => nameof(/*<bind>*/ field /*</bind>*/); set { _ = nameof(field); } }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NoGetter_FieldReadInSetter()
        {
            var text = @"
class C
{
    string Property { set => _ = /*<bind>*/ field /*</bind>*/; }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NoGetter_FieldAssignedInSetter()
        {
            var text = @"
class C
{
    string Property { set => /*<bind>*/ field /*</bind>*/ = value ?? string.Empty; }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NoGetter_FieldRefTakenInSetter()
        {
            var text = @"
class C
{
    string Property { set => M(ref /*<bind>*/ field /*</bind>*/); }

    void M(ref string p) { }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NoGetter_NameofInSetter()
        {
            var text = @"
class C
{
    string Property { set => _ = nameof(/*<bind>*/ field /*</bind>*/); }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_AutoGetter_FieldReadInSetter()
        {
            var text = @"
class C
{
    string Property { get; set => _ = /*<bind>*/ field /*</bind>*/; }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_AutoGetter_FieldAssignedInSetter()
        {
            var text = @"
class C
{
    string Property { get; set => /*<bind>*/ field /*</bind>*/ = value ?? string.Empty; }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_AutoGetter_FieldRefTakenInSetter()
        {
            var text = @"
class C
{
    string Property { get; set => M(ref /*<bind>*/ field /*</bind>*/); }

    void M(ref string p) { }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_AutoGetter_NameofInSetter()
        {
            var text = @"
class C
{
    string Property { get; set => _ = nameof(/*<bind>*/ field /*</bind>*/); }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NormalGetter_FieldReadInSetter()
        {
            var text = @"
class C
{
    string Property { get => default; set => _ = /*<bind>*/ field /*</bind>*/; }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NormalGetter_FieldAssignedInSetter()
        {
            var text = @"
class C
{
    string Property { get => default; set => /*<bind>*/ field /*</bind>*/ = value ?? string.Empty; }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NormalGetter_FieldRefTakenInSetter()
        {
            var text = @"
class C
{
    string Property { get => default; set => M(ref /*<bind>*/ field /*</bind>*/); }

    void M(ref string p) { }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NormalGetter_NameofInSetter()
        {
            var text = @"
class C
{
    string Property { get => default; set => _ = nameof(/*<bind>*/ field /*</bind>*/); }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NameofInGetter_FieldReadInSetter()
        {
            var text = @"
class C
{
    string Property { get => nameof(field); set => _ = /*<bind>*/ field /*</bind>*/; }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NameofInGetter_FieldAssignedInSetter()
        {
            var text = @"
class C
{
    string Property { get => nameof(field); set => /*<bind>*/ field /*</bind>*/ = value ?? string.Empty; }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NameofInGetter_FieldRefTakenInSetter()
        {
            var text = @"
class C
{
    string Property { get => nameof(field); set => M(ref /*<bind>*/ field /*</bind>*/); }

    void M(ref string p) { }
}
";
            VerifyFieldBinding(text);
        }

        [Fact]
        public void SetterFieldReferenceIsBoundToBackingField_NameofInGetter_NameofInSetter()
        {
            var text = @"
class C
{
    string Property { get => nameof(field); set => _ = nameof(/*<bind>*/ field /*</bind>*/); }
}
";
            VerifyFieldBinding(text);
        }

        private void VerifyFieldErrorBinding(string text)
        {
            var tree = Parse(text);
            var comp = CreateCompilation(tree);
            var model = comp.GetSemanticModel(tree);

            var expr = GetExprSyntaxForBinding(GetExprSyntaxList(tree));
            var sym = model.GetSymbolInfo(expr);
            Assert.Null(sym.Symbol);

            var info = model.GetTypeInfo(expr);
            Assert.Equal(SymbolKind.ErrorType, info.Type.Kind);
        }

        private void VerifyFieldBinding(string text)
        {
            var tree = Parse(text);
            var comp = CreateCompilation(tree);
            var model = comp.GetSemanticModel(tree);

            var expr = GetExprSyntaxForBinding(GetExprSyntaxList(tree));
            var sym = model.GetSymbolInfo(expr);
            Assert.Equal(SymbolKind.Field, sym.Symbol.Kind);

            var propertySym = (SourcePropertySymbol)comp.GetTypeByMetadataName("C").GetMember("Property");
            Assert.Equal(propertySym.GetPublicSymbol(), ((IFieldSymbol)sym.Symbol).AssociatedSymbol);
            Assert.Equal(propertySym.BackingField.GetPublicSymbol(), sym.Symbol);

            var info = model.GetTypeInfo(expr);
            Assert.Equal(propertySym.Type.GetPublicSymbol(), info.Type);
        }
    }
}
