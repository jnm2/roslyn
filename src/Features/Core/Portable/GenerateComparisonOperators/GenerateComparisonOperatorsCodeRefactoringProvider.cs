// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

#nullable enable

using System;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeGeneration;
using Microsoft.CodeAnalysis.CodeRefactorings;
using Microsoft.CodeAnalysis.Editing;
using Microsoft.CodeAnalysis.LanguageServices;
using Microsoft.CodeAnalysis.PooledObjects;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.GenerateComparisonOperators
{
    using static CodeGenerationSymbolFactory;

    [ExportCodeRefactoringProvider(LanguageNames.CSharp, LanguageNames.VisualBasic), Shared]
    internal class GenerateComparisonOperatorsCodeRefactoringProvider : CodeRefactoringProvider
    {
        private const string LeftName = "left";
        private const string RightName = "right";

        private static ImmutableArray<CodeGenerationOperatorKind> s_operatorKinds =
            ImmutableArray.Create(
                CodeGenerationOperatorKind.LessThan,
                CodeGenerationOperatorKind.LessThanOrEqual,
                CodeGenerationOperatorKind.GreaterThan,
                CodeGenerationOperatorKind.GreaterThanOrEqual);

        [ImportingConstructor]
        [SuppressMessage("RoslynDiagnosticsReliability", "RS0033:Importing constructor should be [Obsolete]", Justification = "Used in test code: https://github.com/dotnet/roslyn/issues/42814")]
        public GenerateComparisonOperatorsCodeRefactoringProvider()
        {
        }

        public override async Task ComputeRefactoringsAsync(CodeRefactoringContext context)
        {
            var (document, textSpan, cancellationToken) = context;

            var syntaxFacts = document.GetRequiredLanguageService<ISyntaxFactsService>();
            var sourceText = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var root = await document.GetRequiredSyntaxRootAsync(cancellationToken).ConfigureAwait(false);

            // We offer the refactoring when the user is either on the header of a class/struct,
            // or if they're between any members of a class/struct and are on a blank line.
            if (!syntaxFacts.IsOnTypeHeader(root, textSpan.Start, fullHeader: true, out var typeDeclaration) &&
                !syntaxFacts.IsBetweenTypeMembers(sourceText, root, textSpan.Start, out typeDeclaration))
            {
                return;
            }

            var semanticModel = await document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);
            var compilation = semanticModel.Compilation;

            var comparableType = compilation.GetTypeByMetadataName(typeof(IComparable<>).FullName!);
            if (comparableType == null)
                return;

            var containingType = semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken) as INamedTypeSymbol;
            if (containingType == null)
                return;

            using var _1 = ArrayBuilder<INamedTypeSymbol>.GetInstance(out var missingComparableTypes);

            foreach (var iface in containingType.Interfaces)
            {
                if (!iface.OriginalDefinition.Equals(comparableType))
                    continue;

                var comparedType = iface.TypeArguments[0];
                if (comparedType.IsErrorType())
                    continue;

                var compareMethod = TryGetCompareMethodImpl(containingType, iface);
                if (compareMethod == null)
                    continue;

                if (HasAllComparisonOperators(containingType, comparedType))
                    continue;

                missingComparableTypes.Add(iface);
            }

            if (missingComparableTypes.Count == 0)
                return;

            if (missingComparableTypes.Count == 1)
            {
                var missingType = missingComparableTypes[0];
                context.RegisterRefactoring(new MyCodeAction(
                    FeaturesResources.Generate_comparison_operators,
                    c => GenerateComparisonOperatorsAsync(document, typeDeclaration, missingType, c)));
                return;
            }

            using var _2 = ArrayBuilder<CodeAction>.GetInstance(out var nestedActions);

            foreach (var missingType in missingComparableTypes)
            {
                var typeArg = missingType.TypeArguments[0];
                var displayString = typeArg.ToMinimalDisplayString(semanticModel, textSpan.Start);
                nestedActions.Add(new MyCodeAction(
                    string.Format(FeaturesResources.Generate_for_0, displayString),
                    c => GenerateComparisonOperatorsAsync(document, typeDeclaration, missingType, c)));
            }

            context.RegisterRefactoring(new CodeAction.CodeActionWithNestedActions(
                FeaturesResources.Generate_comparison_operators,
                nestedActions.ToImmutable(),
                isInlinable: false));
        }

        private static IMethodSymbol? TryGetCompareMethodImpl(INamedTypeSymbol containingType, ITypeSymbol comparableType)
        {
            foreach (var member in comparableType.GetMembers(nameof(IComparable<int>.CompareTo)))
            {
                if (member is IMethodSymbol method)
                    return (IMethodSymbol?)containingType.FindImplementationForInterfaceMember(method);
            }

            return null;
        }

        private static async Task<Document> GenerateComparisonOperatorsAsync(
            Document document,
            SyntaxNode typeDeclaration,
            INamedTypeSymbol comparableType,
            CancellationToken cancellationToken)
        {
            var options = await document.GetOptionsAsync(cancellationToken).ConfigureAwait(false);
            var semanticModel = await document.GetRequiredSemanticModelAsync(cancellationToken).ConfigureAwait(false);

            var containingType = (INamedTypeSymbol)semanticModel.GetDeclaredSymbol(typeDeclaration, cancellationToken);
            var compareMethod = TryGetCompareMethodImpl(containingType, comparableType)!;

            var generator = document.GetRequiredLanguageService<SyntaxGenerator>();

            var codeGenService = document.GetRequiredLanguageService<ICodeGenerationService>();
            var operators = GenerateComparisonOperators(
                generator, semanticModel, containingType, comparableType,
                GenerateLeftExpression(generator, comparableType, compareMethod));

            return await codeGenService.AddMembersAsync(
                document.Project.Solution,
                containingType,
                operators,
                new CodeGenerationOptions(
                    contextLocation: typeDeclaration.GetLocation(),
                    options: options,
                    parseOptions: typeDeclaration.SyntaxTree.Options), cancellationToken).ConfigureAwait(false);
        }

        private static SyntaxNode GenerateLeftExpression(
            SyntaxGenerator generator,
            INamedTypeSymbol comparableType,
            IMethodSymbol compareMethod)
        {
            var thisExpression = generator.IdentifierName(LeftName);
            var generateCast =
                compareMethod != null &&
                compareMethod.DeclaredAccessibility != Accessibility.Public &&
                compareMethod.Name != nameof(IComparable.CompareTo);

            return generateCast
                ? generator.CastExpression(comparableType, thisExpression)
                : thisExpression;
        }

        private static ImmutableArray<IMethodSymbol> GenerateComparisonOperators(
            SyntaxGenerator generator,
            SemanticModel semanticModel,
            INamedTypeSymbol containingType,
            INamedTypeSymbol comparableType,
            SyntaxNode thisExpression)
        {
            using var _ = ArrayBuilder<IMethodSymbol>.GetInstance(out var operators);

            var boolType = semanticModel.Compilation.GetSpecialType(SpecialType.System_Boolean);
            var comparedType = comparableType.TypeArguments[0];

            var parameters = ImmutableArray.Create(
                CreateParameterSymbol(containingType, LeftName),
                CreateParameterSymbol(comparedType, RightName));

            foreach (var kind in s_operatorKinds)
            {
                if (!HasComparisonOperator(containingType, comparedType, kind))
                {
                    operators.Add(CreateOperatorSymbol(
                        attributes: default,
                        Accessibility.Public,
                        DeclarationModifiers.Static,
                        boolType,
                        kind,
                        parameters,
                        ImmutableArray.Create(GenerateStatements(generator, semanticModel.SyntaxTree.Options, kind, thisExpression, comparedType))));
                }
            }

            return operators.ToImmutable();
        }

        private static SyntaxNode GenerateStatements(
            SyntaxGenerator generator, ParseOptions parseOptions, CodeGenerationOperatorKind kind, SyntaxNode leftExpression, ITypeSymbol comparedType)
        {
            var zero = generator.LiteralExpression(0);

            var rightExpression = generator.IdentifierName(RightName);

            var compareToCall = generator.InvocationExpression(
                generator.MemberAccessExpression(leftExpression, nameof(IComparable.CompareTo)),
                rightExpression);

            var comparison = kind switch
            {
                CodeGenerationOperatorKind.LessThan => generator.LessThanExpression(compareToCall, zero),
                CodeGenerationOperatorKind.LessThanOrEqual => generator.LessThanOrEqualExpression(compareToCall, zero),
                CodeGenerationOperatorKind.GreaterThan => generator.GreaterThanExpression(compareToCall, zero),
                CodeGenerationOperatorKind.GreaterThanOrEqual => generator.GreaterThanOrEqualExpression(compareToCall, zero),
                _ => throw ExceptionUtilities.Unreachable,
            };

            // https://docs.microsoft.com/en-us/dotnet/api/system.icomparable-1.compareto#remarks:
            // By definition, any object compares greater than null, and two null references compare equal to each other.

            return generator.ReturnStatement(kind switch
            {
                CodeGenerationOperatorKind.LessThan => comparedType.IsValueType && !comparedType.IsNullable()
                    ? generator.LogicalOrExpression( // Right can't be null
                        generator.CreateNullCheck(parseOptions, leftExpression),
                        comparison)
                    : generator.ConditionalExpression(
                        generator.CreateNullCheck(parseOptions, leftExpression),
                        generator.CreateNotNullCheck(parseOptions, rightExpression), // Null is only < non-null
                        comparison),

                CodeGenerationOperatorKind.LessThanOrEqual => generator.LogicalOrExpression( // Null is <= everything
                    generator.CreateNullCheck(parseOptions, leftExpression),
                    comparison),

                CodeGenerationOperatorKind.GreaterThan => generator.LogicalAndExpression( // Null is > nothing
                    generator.CreateNotNullCheck(parseOptions, leftExpression),
                    comparison),

                CodeGenerationOperatorKind.GreaterThanOrEqual => comparedType.IsValueType && !comparedType.IsNullable()
                    ? generator.LogicalAndExpression( // Right can't be null
                        generator.CreateNotNullCheck(parseOptions, leftExpression),
                        comparison)
                    : generator.ConditionalExpression(
                        generator.CreateNullCheck(parseOptions, leftExpression),
                        generator.CreateNullCheck(parseOptions, rightExpression), // Null is only >= null
                        comparison),

                _ => throw ExceptionUtilities.Unreachable,
            });
        }

        private static bool HasAllComparisonOperators(INamedTypeSymbol containingType, ITypeSymbol comparedType)
        {
            foreach (var op in s_operatorKinds)
            {
                if (!HasComparisonOperator(containingType, comparedType, op))
                    return false;
            }

            return true;
        }

        private static bool HasComparisonOperator(INamedTypeSymbol containingType, ITypeSymbol comparedType, CodeGenerationOperatorKind kind)
        {
            // Look for an `operator <(... c1, ComparedType c2)` member.
            foreach (var member in containingType.GetMembers(GetOperatorName(kind)))
            {
                if (member is IMethodSymbol method &&
                    method.Parameters.Length >= 2 &&
                    comparedType.Equals(method.Parameters[1].Type))
                {
                    return true;
                }
            }

            return false;
        }

        private static string GetOperatorName(CodeGenerationOperatorKind kind)
            => kind switch
            {
                CodeGenerationOperatorKind.LessThan => WellKnownMemberNames.LessThanOperatorName,
                CodeGenerationOperatorKind.LessThanOrEqual => WellKnownMemberNames.LessThanOrEqualOperatorName,
                CodeGenerationOperatorKind.GreaterThan => WellKnownMemberNames.GreaterThanOperatorName,
                CodeGenerationOperatorKind.GreaterThanOrEqual => WellKnownMemberNames.GreaterThanOrEqualOperatorName,
                _ => throw ExceptionUtilities.Unreachable,
            };

        private class MyCodeAction : CodeAction.DocumentChangeAction
        {
            public MyCodeAction(string title, Func<CancellationToken, Task<Document>> createChangedDocument)
                : base(title, createChangedDocument)
            {
            }
        }
    }
}
