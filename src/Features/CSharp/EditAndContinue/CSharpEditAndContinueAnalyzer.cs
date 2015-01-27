// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Extensions;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Differencing;
using Microsoft.CodeAnalysis.EditAndContinue;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.CSharp.EditAndContinue
{
    [ExportLanguageService(typeof(IEditAndContinueAnalyzer), LanguageNames.CSharp), Shared]
    internal sealed class CSharpEditAndContinueAnalyzer : AbstractEditAndContinueAnalyzer
    {
        #region Syntax Analysis

        private enum ConstructorPart
        {
            None = 0,
            DefaultBaseConstructorCall = 1,
        }

        private enum BlockPart
        {
            None = 0,
            OpenBrace = 1,
            CloseBrace = 2,
        }

        private enum ForEachPart
        {
            None = 0,
            ForEach = 1,
            VariableDeclaration = 2,
            In = 3,
            Expression = 4,
        }

        /// <returns>
        /// <see cref="BaseMethodDeclarationSyntax"/> for methods, operators, constructors, destructors and accessors.
        /// <see cref="VariableDeclaratorSyntax"/> for field initializers.
        /// <see cref="PropertyDeclarationSyntax"/> for property initializers and expression bodies.
        /// <see cref="IndexerDeclarationSyntax"/> for indexer expression bodies.
        /// </returns>
        internal override SyntaxNode FindMemberDeclaration(SyntaxNode root, SyntaxNode node)
        {
            while (node != root)
            {
                switch (node.Kind())
                {
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.ConversionOperatorDeclaration:
                    case SyntaxKind.OperatorDeclaration:
                    case SyntaxKind.SetAccessorDeclaration:
                    case SyntaxKind.AddAccessorDeclaration:
                    case SyntaxKind.RemoveAccessorDeclaration:
                    case SyntaxKind.GetAccessorDeclaration:
                    case SyntaxKind.ConstructorDeclaration:
                    case SyntaxKind.DestructorDeclaration:
                        return node;

                    case SyntaxKind.PropertyDeclaration:
                        // int P { get; } = [|initializer|];
                        Debug.Assert(((PropertyDeclarationSyntax)node).Initializer != null);
                        return node;

                    case SyntaxKind.FieldDeclaration:
                    case SyntaxKind.EventFieldDeclaration:
                        // Active statements encompassing modifiers or type correspond to the first initialized field.
                        // [|public static int F = 1|], G = 2;
                        return ((BaseFieldDeclarationSyntax)node).Declaration.Variables.First();

                    case SyntaxKind.VariableDeclarator:
                        // public static int F = 1, [|G = 2|];
                        Debug.Assert(node.Parent.IsKind(SyntaxKind.VariableDeclaration));

                        switch (node.Parent.Parent.Kind())
                        {
                            case SyntaxKind.FieldDeclaration:
                            case SyntaxKind.EventFieldDeclaration:
                                return node;
                        }

                        node = node.Parent;
                        break;
                }

                node = node.Parent;
            }

            return null;
        }

        /// <returns>
        /// Given a node representing a declaration (<paramref name="isMember"/> = true) or a top-level match node (<paramref name="isMember"/> = false) returns:
        /// - <see cref="BlockSyntax"/> for method-like member declarations with block bodies (methods, operators, constructors, destructors, accessors).
        /// - <see cref="ExpressionSyntax"/> for variable declarators of fields, properties with an initializer expression, or 
        ///   for method-like member declarations with expression bodies (methods, properties, indexers, operators)
        /// 
        /// A null reference otherwise.
        /// </returns>
        internal override SyntaxNode TryGetDeclarationBody(SyntaxNode node, bool isMember)
        {
            if (node.IsKind(SyntaxKind.VariableDeclarator))
            {
                return (((VariableDeclaratorSyntax)node).Initializer)?.Value;
            }

            return SyntaxUtilities.TryGetMethodDeclarationBody(node);
        }

        /// <returns>
        /// If <paramref name="node"/> is a method, accessor, operator, destructor, or constructor without an initializer,
        /// tokens of its block body, or tokens of the expression body if applicable.
        /// 
        /// If <paramref name="node"/> is an indexer declaration the tokens of its expression body.
        /// 
        /// If <paramref name="node"/> is a property declaration the tokens of its expression body or initializer.
        ///   
        /// If <paramref name="node"/> is a constructor with an initializer, 
        /// tokens of the initializer concatenated with tokens of the constructor body.
        /// 
        /// If <paramref name="node"/> is a variable declarator of a field with an initializer,
        /// tokens of the field initializer.
        /// 
        /// Null reference otherwise.
        /// </returns>
        internal override IEnumerable<SyntaxToken> TryGetActiveTokens(SyntaxNode node)
        {
            if (node.IsKind(SyntaxKind.VariableDeclarator))
            {
                // TODO: The logic is similar to BreakpointSpans.TryCreateSpanForVariableDeclaration. Can we abstract it?

                var declarator = node;
                var fieldDeclaration = (BaseFieldDeclarationSyntax)declarator.Parent.Parent;
                var variableDeclaration = fieldDeclaration.Declaration;

                if (fieldDeclaration.Modifiers.Any(SyntaxKind.ConstKeyword))
                {
                    return null;
                }

                if (variableDeclaration.Variables.Count == 1)
                {
                    if (variableDeclaration.Variables[0].Initializer == null)
                    {
                        return null;
                    }

                    return fieldDeclaration.Modifiers.Concat(variableDeclaration.DescendantTokens()).Concat(fieldDeclaration.SemicolonToken);
                }

                if (declarator == variableDeclaration.Variables[0])
                {
                    return fieldDeclaration.Modifiers.Concat(variableDeclaration.Type.DescendantTokens()).Concat(node.DescendantTokens());
                }

                return declarator.DescendantTokens();
            }

            if (node.IsKind(SyntaxKind.ConstructorDeclaration))
            {
                var ctor = (ConstructorDeclarationSyntax)node;
                if (ctor.Initializer != null)
                {
                    return ctor.Initializer.DescendantTokens().Concat(ctor.Body.DescendantTokens());
                }

                return ctor.Body.DescendantTokens();
            }

            return SyntaxUtilities.TryGetMethodDeclarationBody(node)?.DescendantTokens();
        }

        protected override SyntaxNode GetEncompassingAncestorImpl(SyntaxNode bodyOrMatchRoot)
        {
            // Constructor may contain active nodes outside of its body (constructor initializer),
            // but within the body of the member declaration (the parent).
            if (bodyOrMatchRoot.Parent.IsKind(SyntaxKind.ConstructorDeclaration))
            {
                return bodyOrMatchRoot.Parent;
            }

            // Field initializer match root -- an active statement may include the modifiers 
            // and type specification of the field declaration.
            if (bodyOrMatchRoot.IsKind(SyntaxKind.EqualsValueClause) &&
                bodyOrMatchRoot.Parent.IsKind(SyntaxKind.VariableDeclarator) &&
                bodyOrMatchRoot.Parent.Parent.IsKind(SyntaxKind.FieldDeclaration))
            {
                return bodyOrMatchRoot.Parent.Parent;
            }

            // Field initializer body -- an active statement may include the modifiers 
            // and type specification of the field declaration.
            if (bodyOrMatchRoot.Parent.IsKind(SyntaxKind.EqualsValueClause) &&
                bodyOrMatchRoot.Parent.Parent.IsKind(SyntaxKind.VariableDeclarator) &&
                bodyOrMatchRoot.Parent.Parent.Parent.IsKind(SyntaxKind.FieldDeclaration))
            {
                return bodyOrMatchRoot.Parent.Parent.Parent;
            }

            // otherwise all active statements are covered by the body/match root itself:
            return bodyOrMatchRoot;
        }

        protected override SyntaxNode FindStatementAndPartner(SyntaxNode declarationBody, int position, SyntaxNode partnerDeclarationBodyOpt, out SyntaxNode partnerOpt, out int statementPart)
        {
            SyntaxUtilities.AssertIsBody(declarationBody, allowLambda: false);

            if (position < declarationBody.SpanStart)
            {
                // Only constructors and the field initializers may have an [|active statement|] starting outside of the <<body>>.
                // Constructor:                          [|public C()|] <<{ }>>
                // Constructor initializer:              public C() : [|base(expr)|] <<{ }>>
                // Constructor initializer with lambda:  public C() : base(() => { [|...|] }) <<{ }>>
                // Field initializers:                   [|public int a = <<expr>>|], [|b = <<expr>>|];

                // No need to special case property initializers here, the actiave statement always spans the initializer expression.

                if (declarationBody.Parent.Kind() == SyntaxKind.ConstructorDeclaration)
                {
                    var constructor = (ConstructorDeclarationSyntax)declarationBody.Parent;
                    var partnerConstructor = (ConstructorDeclarationSyntax)partnerDeclarationBodyOpt?.Parent;

                    if (constructor.Initializer == null || position < constructor.Initializer.ColonToken.SpanStart)
                    {
                        statementPart = (int)ConstructorPart.DefaultBaseConstructorCall;
                        partnerOpt = partnerConstructor;
                        return constructor;
                    }

                    declarationBody = constructor.Initializer;
                    partnerDeclarationBodyOpt = partnerConstructor?.Initializer;
                }
                else
                {
                    Debug.Assert(!(declarationBody is BlockSyntax));

                    // let's find a labeled node that encompasses the body:
                    position = declarationBody.SpanStart;
                }
            }

            SyntaxNode node;
            if (partnerDeclarationBodyOpt != null)
            {
                SyntaxUtilities.FindLeafNodeAndPartner(declarationBody, position, partnerDeclarationBodyOpt, out node, out partnerOpt);
            }
            else
            {
                node = declarationBody.FindToken(position).Parent;
                partnerOpt = null;
            }

            while (node != declarationBody && !StatementSyntaxComparer.HasLabel(node) && !SyntaxFacts.IsLambdaBody(node))
            {
                node = node.Parent;
                if (partnerOpt != null)
                {
                    partnerOpt = partnerOpt.Parent;
                }
            }

            switch (node.Kind())
            {
                case SyntaxKind.Block:
                    statementPart = (int)GetStatementPart((BlockSyntax)node, position);
                    break;

                case SyntaxKind.ForEachStatement:
                    statementPart = (int)GetStatementPart((ForEachStatementSyntax)node, position);
                    break;

                case SyntaxKind.VariableDeclaration:
                    // VariableDeclaration ::= TypeSyntax CommaSeparatedList(VariableDeclarator)
                    // 
                    // The compiler places sequence points after each local variable initialization.
                    // The TypeSyntax is considered to be part of the first sequence span.
                    node = ((VariableDeclarationSyntax)node).Variables.First();

                    if (partnerOpt != null)
                    {
                        partnerOpt = ((VariableDeclarationSyntax)partnerOpt).Variables.First();
                    }

                    statementPart = 0;
                    break;

                default:
                    statementPart = 0;
                    break;
            }

            return node;
        }

        private static BlockPart GetStatementPart(BlockSyntax node, int position)
        {
            return position < node.OpenBraceToken.Span.End ? BlockPart.OpenBrace : BlockPart.CloseBrace;
        }

        private static TextSpan GetActiveSpan(BlockSyntax node, BlockPart part)
        {
            switch (part)
            {
                case BlockPart.OpenBrace:
                    return node.OpenBraceToken.Span;

                case BlockPart.CloseBrace:
                    return node.CloseBraceToken.Span;

                default:
                    throw ExceptionUtilities.UnexpectedValue(part);
            }
        }

        private static ForEachPart GetStatementPart(ForEachStatementSyntax node, int position)
        {
            return position < node.OpenParenToken.SpanStart ? ForEachPart.ForEach :
                   position < node.InKeyword.SpanStart ? ForEachPart.VariableDeclaration :
                   position < node.Expression.SpanStart ? ForEachPart.In :
                   ForEachPart.Expression;
        }

        private static TextSpan GetActiveSpan(ForEachStatementSyntax node, ForEachPart part)
        {
            switch (part)
            {
                case ForEachPart.ForEach:
                    return node.ForEachKeyword.Span;

                case ForEachPart.VariableDeclaration:
                    return TextSpan.FromBounds(node.Type.SpanStart, node.Identifier.Span.End);

                case ForEachPart.In:
                    return node.InKeyword.Span;

                case ForEachPart.Expression:
                    return node.Expression.Span;

                default:
                    throw ExceptionUtilities.UnexpectedValue(part);
            }
        }

        protected override SyntaxNode FindEnclosingLambdaBody(SyntaxNode containerOpt, SyntaxNode node)
        {
            SyntaxNode root = GetEncompassingAncestor(containerOpt);

            while (node != root)
            {
                if (SyntaxFacts.IsLambdaBody(node))
                {
                    return node;
                }

                node = node.Parent;
            }

            return null;
        }

        protected override SyntaxNode GetPartnerLambdaBody(SyntaxNode oldBody, SyntaxNode newLambda)
        {
            return SyntaxUtilities.GetPartnerLambdaBody(oldBody, newLambda);
        }

        protected override Match<SyntaxNode> ComputeTopLevelMatch(SyntaxNode oldCompilationUnit, SyntaxNode newCompilationUnit)
        {
            return TopSyntaxComparer.Instance.ComputeMatch(oldCompilationUnit, newCompilationUnit);
        }

        protected override Match<SyntaxNode> ComputeBodyMatch(SyntaxNode oldBody, SyntaxNode newBody, IEnumerable<KeyValuePair<SyntaxNode, SyntaxNode>> knownMatches)
        {
            SyntaxUtilities.AssertIsBody(oldBody, allowLambda: true);
            SyntaxUtilities.AssertIsBody(newBody, allowLambda: true);

            if (oldBody is ExpressionSyntax || newBody is ExpressionSyntax)
            {
                Debug.Assert(oldBody is ExpressionSyntax || oldBody is BlockSyntax);
                Debug.Assert(newBody is ExpressionSyntax || newBody is BlockSyntax);

                // The matching algorithm requires the roots to match each other.
                // Lambda bodies, field/property initializers, and method/property/indexer/operator expression-bodies may also be lambda expressions.
                // Say we have oldBody 'x => x' and newBody 'F(x => x + 1)', then 
                // the algorithm would match 'x => x' to 'F(x => x + 1)' instead of 
                // matching 'x => x' to 'x => x + 1'.

                // We use the parent node as a root:
                // - for field/property initializers the root is EqualsValueClause. 
                // - for expression-bodies the root is ArrowExpressionClauseSyntax. 
                // - for block bodies the root is a method/operator/accessor declaration (only happens when matching expression body with a block body)
                // - for lambdas the root is a LambdaExpression.
                // - for query lambdas the root is the query clause containing the lambda (e.g. where).

                return new StatementSyntaxComparer(oldBody, newBody).ComputeMatch(oldBody.Parent, newBody.Parent, knownMatches);
            }

            return StatementSyntaxComparer.Default.ComputeMatch(oldBody, newBody, knownMatches);
        }

        protected override bool TryMatchActiveStatement(
            SyntaxNode oldStatement,
            int statementPart,
            SyntaxNode oldBody,
            SyntaxNode newBody,
            out SyntaxNode newStatement)
        {
            SyntaxUtilities.AssertIsBody(oldBody, allowLambda: true);
            SyntaxUtilities.AssertIsBody(newBody, allowLambda: true);

            switch (oldStatement.Kind())
            {
                case SyntaxKind.ThisConstructorInitializer:
                case SyntaxKind.BaseConstructorInitializer:
                case SyntaxKind.ConstructorDeclaration:
                    var newConstructor = (ConstructorDeclarationSyntax)newBody.Parent;
                    newStatement = (SyntaxNode)newConstructor.Initializer ?? newConstructor;
                    return true;

                default:
                    // TODO: Consider mapping an expression body to an equivalent statement expression or return statement and vice versa.
                    // It would benefit transformations of expression bodies to block bodies of lambdas, methods, operators and properties.

                    // field initializer, lambda and query expressions:
                    if (oldStatement == oldBody && !newBody.IsKind(SyntaxKind.Block))
                    {
                        newStatement = newBody;
                        return true;
                    }

                    newStatement = null;
                    return false;
            }
        }

        #endregion

        #region Syntax and Semantic Utils

        protected override IEnumerable<SequenceEdit> GetSyntaxSequenceEdits(ImmutableArray<SyntaxNode> oldNodes, ImmutableArray<SyntaxNode> newNodes)
        {
            return SyntaxComparer.GetSequenceEdits(oldNodes, newNodes);
        }

        internal override SyntaxNode EmptyCompilationUnit
        {
            get
            {
                return SyntaxFactory.CompilationUnit();
            }
        }

        internal override bool ExperimentalFeaturesEnabled(SyntaxTree tree)
        {
            // there are no experimental features at this time.
            return false;
        }

        protected override bool StatementLabelEquals(SyntaxNode node1, SyntaxNode node2)
        {
            return StatementSyntaxComparer.GetLabelImpl(node1) == StatementSyntaxComparer.GetLabelImpl(node2);
        }

        protected override bool TryGetEnclosingBreakpointSpan(SyntaxNode root, int position, out TextSpan span)
        {
            return root.TryGetClosestBreakpointSpan(position, out span);
        }

        protected override bool TryGetActiveSpan(SyntaxNode node, int statementPart, out TextSpan span)
        {
            switch (node.Kind())
            {
                case SyntaxKind.Block:
                    span = GetActiveSpan((BlockSyntax)node, (BlockPart)statementPart);
                    return true;

                case SyntaxKind.ForEachStatement:
                    span = GetActiveSpan((ForEachStatementSyntax)node, (ForEachPart)statementPart);
                    return true;

                case SyntaxKind.DoStatement:
                    // The active statement of DoStatement node is the while condition,
                    // which is lexically not the closest breakpoint span (the body is).
                    // do { ... } [|while (condition);|]
                    var doStatement = (DoStatementSyntax)node;
                    return node.TryGetClosestBreakpointSpan(doStatement.WhileKeyword.SpanStart, out span);

                case SyntaxKind.PropertyDeclaration:
                    // The active span corresponding to a property declaration is the span corresponding to its initializer (if any),
                    // not the span correspoding to the accessor.
                    // int P { [|get;|] } = [|<initializer>|];
                    var propertyDeclaration = (PropertyDeclarationSyntax)node;

                    if (propertyDeclaration.Initializer != null &&
                        node.TryGetClosestBreakpointSpan(propertyDeclaration.Initializer.SpanStart, out span))
                    {
                        return true;
                    }
                    else
                    {
                        span = default(TextSpan);
                        return false;
                    }

                default:
                    return node.TryGetClosestBreakpointSpan(node.SpanStart, out span);
            }
        }

        protected override IEnumerable<KeyValuePair<SyntaxNode, int>> EnumerateNearStatements(SyntaxNode statement)
        {
            int direction = +1;
            SyntaxNodeOrToken nodeOrToken = statement;
            var fieldOrPropertyModifiers = SyntaxUtilities.TryGetFieldOrPropertyModifiers(statement);

            while (true)
            {
                nodeOrToken = (direction < 0) ? nodeOrToken.GetPreviousSibling() : nodeOrToken.GetNextSibling();

                if (nodeOrToken.RawKind == 0)
                {
                    var parent = statement.Parent;
                    if (parent == null)
                    {
                        yield break;
                    }

                    if (parent.IsKind(SyntaxKind.Block))
                    {
                        yield return KeyValuePair.Create(parent, (int)(direction > 0 ? BlockPart.CloseBrace : BlockPart.OpenBrace));
                    }
                    else if (parent.IsKind(SyntaxKind.ForEachStatement))
                    {
                        yield return KeyValuePair.Create(parent, (int)ForEachPart.ForEach);
                    }

                    if (direction > 0)
                    {
                        nodeOrToken = statement;
                        direction = -1;
                        continue;
                    }

                    if (fieldOrPropertyModifiers.HasValue)
                    {
                        // We enumerated all members and none of them has an initializer.
                        // We don't have any better place where to place the span than the initial field.
                        // Consider: in nonpartial classes we could find a single constructor. 
                        // Otherwise, it would be confusing to select one arbitrarily.
                        yield return KeyValuePair.Create(statement, -1);
                    }

                    nodeOrToken = statement = parent;
                    fieldOrPropertyModifiers = SyntaxUtilities.TryGetFieldOrPropertyModifiers(statement);
                    direction = +1;

                    yield return KeyValuePair.Create(nodeOrToken.AsNode(), 0);
                }
                else
                {
                    var node = nodeOrToken.AsNode();
                    if (node == null)
                    {
                        continue;
                    }

                    if (fieldOrPropertyModifiers.HasValue)
                    {
                        var nodeModifiers = SyntaxUtilities.TryGetFieldOrPropertyModifiers(node);

                        if (!nodeModifiers.HasValue ||
                            nodeModifiers.Value.Any(SyntaxKind.StaticKeyword) != fieldOrPropertyModifiers.Value.Any(SyntaxKind.StaticKeyword))
                        {
                            continue;
                        }
                    }

                    if (node.IsKind(SyntaxKind.Block))
                    {
                        yield return KeyValuePair.Create(node, (int)(direction > 0 ? BlockPart.OpenBrace : BlockPart.CloseBrace));
                    }
                    else if (node.IsKind(SyntaxKind.ForEachStatement))
                    {
                        yield return KeyValuePair.Create(node, (int)ForEachPart.ForEach);
                    }

                    yield return KeyValuePair.Create(node, 0);
                }
            }
        }

        protected override bool AreEquivalentActiveStatements(SyntaxNode oldStatement, SyntaxNode newStatement, int statementPart)
        {
            if (oldStatement.Kind() != newStatement.Kind())
            {
                return false;
            }

            switch (oldStatement.Kind())
            {
                case SyntaxKind.Block:
                    Debug.Assert(statementPart != 0);
                    return true;

                case SyntaxKind.ConstructorDeclaration:
                    Debug.Assert(statementPart != 0);

                    // The call could only change if the base type of the containing class changed.
                    return true;

                case SyntaxKind.ForEachStatement:
                    Debug.Assert(statementPart != 0);

                    // only check the expression, edits in the body and the variable declaration are allowed:
                    return AreEquivalentActiveStatements((ForEachStatementSyntax)oldStatement, (ForEachStatementSyntax)newStatement);

                case SyntaxKind.IfStatement:
                    // only check the condition, edits in the body are allowed:
                    return AreEquivalentActiveStatements((IfStatementSyntax)oldStatement, (IfStatementSyntax)newStatement);

                case SyntaxKind.WhileStatement:
                    // only check the condition, edits in the body are allowed:
                    return AreEquivalentActiveStatements((WhileStatementSyntax)oldStatement, (WhileStatementSyntax)newStatement);

                case SyntaxKind.DoStatement:
                    // only check the condition, edits in the body are allowed:
                    return AreEquivalentActiveStatements((DoStatementSyntax)oldStatement, (DoStatementSyntax)newStatement);

                case SyntaxKind.SwitchStatement:
                    return AreEquivalentActiveStatements((SwitchStatementSyntax)oldStatement, (SwitchStatementSyntax)newStatement);

                case SyntaxKind.LockStatement:
                    return AreEquivalentActiveStatements((LockStatementSyntax)oldStatement, (LockStatementSyntax)newStatement);

                case SyntaxKind.UsingStatement:
                    return AreEquivalentActiveStatements((UsingStatementSyntax)oldStatement, (UsingStatementSyntax)newStatement);

                // fixed and for statements don't need special handling since the active statement is a variable declaration
                default:
                    return SyntaxFactory.AreEquivalent(oldStatement, newStatement);
            }
        }

        private static bool AreEquivalentActiveStatements(IfStatementSyntax oldNode, IfStatementSyntax newNode)
        {
            // only check the condition, edits in the body are allowed:
            return SyntaxFactory.AreEquivalent(oldNode.Condition, newNode.Condition);
        }

        private static bool AreEquivalentActiveStatements(WhileStatementSyntax oldNode, WhileStatementSyntax newNode)
        {
            // only check the condition, edits in the body are allowed:
            return SyntaxFactory.AreEquivalent(oldNode.Condition, newNode.Condition);
        }

        private static bool AreEquivalentActiveStatements(DoStatementSyntax oldNode, DoStatementSyntax newNode)
        {
            // only check the condition, edits in the body are allowed:
            return SyntaxFactory.AreEquivalent(oldNode.Condition, newNode.Condition);
        }

        private static bool AreEquivalentActiveStatements(SwitchStatementSyntax oldNode, SwitchStatementSyntax newNode)
        {
            // only check the expression, edits in the body are allowed:
            return SyntaxFactory.AreEquivalent(oldNode.Expression, newNode.Expression);
        }

        private static bool AreEquivalentActiveStatements(LockStatementSyntax oldNode, LockStatementSyntax newNode)
        {
            // only check the expression, edits in the body are allowed:
            return SyntaxFactory.AreEquivalent(oldNode.Expression, newNode.Expression);
        }

        private static bool AreEquivalentActiveStatements(FixedStatementSyntax oldNode, FixedStatementSyntax newNode)
        {
            return SyntaxFactory.AreEquivalent(oldNode.Declaration, newNode.Declaration);
        }

        private static bool AreEquivalentActiveStatements(UsingStatementSyntax oldNode, UsingStatementSyntax newNode)
        {
            // only check the expression/declaration, edits in the body are allowed:
            return SyntaxFactory.AreEquivalent(
                (SyntaxNode)oldNode.Declaration ?? oldNode.Expression,
                (SyntaxNode)newNode.Declaration ?? newNode.Expression);
        }

        private static bool AreEquivalentActiveStatements(ForEachStatementSyntax oldNode, ForEachStatementSyntax newNode)
        {
            // This is conservative, we might be able to allow changing the type.
            return SyntaxFactory.AreEquivalent(oldNode.Type, newNode.Type)
                && SyntaxFactory.AreEquivalent(oldNode.Expression, newNode.Expression);
        }

        internal override bool IsMethod(SyntaxNode declaration)
        {
            return SyntaxUtilities.IsMethod(declaration);
        }

        internal override SyntaxNode TryGetContainingTypeDeclaration(SyntaxNode memberDeclaration)
        {
            return memberDeclaration.Parent.FirstAncestorOrSelf<TypeDeclarationSyntax>();
        }

        internal override bool HasBackingField(SyntaxNode propertyDeclaration)
        {
            return SyntaxUtilities.HasBackingField((PropertyDeclarationSyntax)propertyDeclaration);
        }

        internal override bool HasInitializer(SyntaxNode declaration, out bool isStatic)
        {
            switch (declaration.Kind())
            {
                case SyntaxKind.VariableDeclarator:
                    var fieldDeclaration = (VariableDeclaratorSyntax)declaration;
                    if (fieldDeclaration.Initializer != null)
                    {
                        isStatic = ((FieldDeclarationSyntax)declaration.Parent.Parent).Modifiers.Any(SyntaxKind.StaticKeyword);
                        return true;
                    }

                    isStatic = false;
                    return false;

                case SyntaxKind.PropertyDeclaration:
                    var propertyDeclaration = (PropertyDeclarationSyntax)declaration;
                    if (propertyDeclaration.Initializer != null)
                    {
                        isStatic = propertyDeclaration.Modifiers.Any(SyntaxKind.StaticKeyword);
                        return true;
                    }

                    isStatic = false;
                    return false;

                default:
                    isStatic = false;
                    return false;
            }
        }

        internal override bool IncludesInitializers(SyntaxNode constructorDeclaration)
        {
            var ctor = (ConstructorDeclarationSyntax)constructorDeclaration;
            return ctor.Initializer == null || ctor.Initializer.IsKind(SyntaxKind.BaseConstructorInitializer);
        }

        internal override bool IsPartial(INamedTypeSymbol type)
        {
            var syntaxRefs = type.DeclaringSyntaxReferences;
            return syntaxRefs.Length > 1
                || ((TypeDeclarationSyntax)syntaxRefs.Single().GetSyntax()).Modifiers.Any(SyntaxKind.PartialKeyword);
        }

        protected override ISymbol GetSymbolForEdit(SemanticModel model, SyntaxNode node, EditKind editKind, Dictionary<SyntaxNode, EditKind> editMap, CancellationToken cancellationToken)
        {
            if (editKind == EditKind.Update)
            {
                if (node.IsKind(SyntaxKind.EnumDeclaration))
                {
                    // Enum declaration update that removes/adds a trailing comma.
                    return null;
                }

                if (node.IsKind(SyntaxKind.IndexerDeclaration) || node.IsKind(SyntaxKind.PropertyDeclaration))
                {
                    // The only legitimate update of an indexer/property declaration is an update of its expression body.
                    // The expression body itself may have been updated, replaced with an explicit getter, or added to replace an explicit getter.
                    // In any case, the update is to the property getter symbol.
                    var propertyOrIndexer = model.GetDeclaredSymbol(node, cancellationToken);
                    return ((IPropertySymbol)propertyOrIndexer).GetMethod;
                }
            }

            if (IsGetterToExpressionBodyTransformation(editKind, node, editMap))
            {
                return null;
            }

            return model.GetDeclaredSymbol(node, cancellationToken);
        }

        protected override bool TryGetDeclarationBodyEdit(Edit<SyntaxNode> edit, Dictionary<SyntaxNode, EditKind> editMap, out SyntaxNode oldBody, out SyntaxNode newBody)
        {
            // Detect a transition between a property/indexer with an expression body and with an explicit getter.
            // int P => old_body;              <->      int P { get { new_body } } 
            // int this[args] => old_body;     <->      int this[args] { get { new_body } }     

            // First, return getter or expression body for property/indexer update:
            if (edit.Kind == EditKind.Update && (edit.OldNode.IsKind(SyntaxKind.PropertyDeclaration) || edit.OldNode.IsKind(SyntaxKind.IndexerDeclaration)))
            {
                oldBody = SyntaxUtilities.TryGetEffectiveGetterBody(edit.OldNode);
                newBody = SyntaxUtilities.TryGetEffectiveGetterBody(edit.NewNode);

                if (oldBody != null && newBody != null)
                {
                    return true;
                }
            }

            // Second, ignore deletion of a getter body:
            if (IsGetterToExpressionBodyTransformation(edit.Kind, edit.OldNode ?? edit.NewNode, editMap))
            {
                oldBody = newBody = null;
                return false;
            }

            return base.TryGetDeclarationBodyEdit(edit, editMap, out oldBody, out newBody);
        }

        private static bool IsGetterToExpressionBodyTransformation(EditKind editKind, SyntaxNode node, Dictionary<SyntaxNode, EditKind> editMap)
        {
            if ((editKind == EditKind.Insert || editKind == EditKind.Delete) && node.IsKind(SyntaxKind.GetAccessorDeclaration))
            {
                Debug.Assert(node.Parent.IsKind(SyntaxKind.AccessorList));
                Debug.Assert(node.Parent.Parent.IsKind(SyntaxKind.PropertyDeclaration) || node.Parent.Parent.IsKind(SyntaxKind.IndexerDeclaration));

                EditKind parentEdit;
                return editMap.TryGetValue(node.Parent, out parentEdit) && parentEdit == editKind &&
                       editMap.TryGetValue(node.Parent.Parent, out parentEdit) && parentEdit == EditKind.Update;
            }

            return false;
        }

        #endregion

        #region Diagnostic Info

        protected override TextSpan GetDiagnosticSpan(SyntaxNode node, EditKind editKind)
        {
            return GetDiagnosticSpanImpl(node, editKind);
        }

        private static TextSpan GetDiagnosticSpanImpl(SyntaxNode node, EditKind editKind)
        {
            return GetDiagnosticSpanImpl(node.Kind(), node, editKind);
        }

        // internal for testing; kind is passed explicitly for testing as well
        internal static TextSpan GetDiagnosticSpanImpl(SyntaxKind kind, SyntaxNode node, EditKind editKind)
        {
            switch (kind)
            {
                case SyntaxKind.CompilationUnit:
                    return default(TextSpan);

                case SyntaxKind.GlobalStatement:
                    // TODO:
                    return default(TextSpan);

                case SyntaxKind.ExternAliasDirective:
                case SyntaxKind.UsingDirective:
                    return node.Span;

                case SyntaxKind.NamespaceDeclaration:
                    var ns = (NamespaceDeclarationSyntax)node;
                    return TextSpan.FromBounds(ns.NamespaceKeyword.SpanStart, ns.Name.Span.End);

                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.StructDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                    var typeDeclaration = (TypeDeclarationSyntax)node;
                    return GetDiagnosticSpan(typeDeclaration.Modifiers, typeDeclaration.Keyword,
                        typeDeclaration.TypeParameterList ?? (SyntaxNodeOrToken)typeDeclaration.Identifier);

                case SyntaxKind.EnumDeclaration:
                    var enumDeclaration = (EnumDeclarationSyntax)node;
                    return GetDiagnosticSpan(enumDeclaration.Modifiers, enumDeclaration.EnumKeyword, enumDeclaration.Identifier);

                case SyntaxKind.DelegateDeclaration:
                    var delegateDeclaration = (DelegateDeclarationSyntax)node;
                    return GetDiagnosticSpan(delegateDeclaration.Modifiers, delegateDeclaration.DelegateKeyword, delegateDeclaration.ParameterList);

                case SyntaxKind.FieldDeclaration:
                    var fieldDeclaration = (BaseFieldDeclarationSyntax)node;
                    return GetDiagnosticSpan(fieldDeclaration.Modifiers, fieldDeclaration.Declaration, fieldDeclaration.Declaration);

                case SyntaxKind.EventFieldDeclaration:
                    var eventFieldDeclaration = (EventFieldDeclarationSyntax)node;
                    return GetDiagnosticSpan(eventFieldDeclaration.Modifiers, eventFieldDeclaration.EventKeyword, eventFieldDeclaration.Declaration);

                case SyntaxKind.VariableDeclaration:
                    return GetDiagnosticSpanImpl(node.Parent, editKind);

                case SyntaxKind.VariableDeclarator:
                    return node.Span;

                case SyntaxKind.MethodDeclaration:
                    var methodDeclaration = (MethodDeclarationSyntax)node;
                    return GetDiagnosticSpan(methodDeclaration.Modifiers, methodDeclaration.ReturnType, methodDeclaration.ParameterList);

                case SyntaxKind.ConversionOperatorDeclaration:
                    var conversionOperatorDeclaration = (ConversionOperatorDeclarationSyntax)node;
                    return GetDiagnosticSpan(conversionOperatorDeclaration.Modifiers, conversionOperatorDeclaration.ImplicitOrExplicitKeyword, conversionOperatorDeclaration.ParameterList);

                case SyntaxKind.OperatorDeclaration:
                    var operatorDeclaration = (OperatorDeclarationSyntax)node;
                    return GetDiagnosticSpan(operatorDeclaration.Modifiers, operatorDeclaration.ReturnType, operatorDeclaration.ParameterList);

                case SyntaxKind.ConstructorDeclaration:
                    var constructorDeclaration = (ConstructorDeclarationSyntax)node;
                    return GetDiagnosticSpan(constructorDeclaration.Modifiers, constructorDeclaration.Identifier, constructorDeclaration.ParameterList);

                case SyntaxKind.DestructorDeclaration:
                    var destructorDeclaration = (DestructorDeclarationSyntax)node;
                    return GetDiagnosticSpan(destructorDeclaration.Modifiers, destructorDeclaration.TildeToken, destructorDeclaration.ParameterList);

                case SyntaxKind.PropertyDeclaration:
                    var propertyDeclaration = (PropertyDeclarationSyntax)node;
                    return GetDiagnosticSpan(propertyDeclaration.Modifiers, propertyDeclaration.Type, propertyDeclaration.Identifier);

                case SyntaxKind.IndexerDeclaration:
                    var indexerDeclaration = (IndexerDeclarationSyntax)node;
                    return GetDiagnosticSpan(indexerDeclaration.Modifiers, indexerDeclaration.Type, indexerDeclaration.ParameterList);

                case SyntaxKind.EventDeclaration:
                    var eventDeclaration = (EventDeclarationSyntax)node;
                    return GetDiagnosticSpan(eventDeclaration.Modifiers, eventDeclaration.EventKeyword, eventDeclaration.Identifier);

                case SyntaxKind.EnumMemberDeclaration:
                    return node.Span;

                case SyntaxKind.GetAccessorDeclaration:
                case SyntaxKind.SetAccessorDeclaration:
                case SyntaxKind.AddAccessorDeclaration:
                case SyntaxKind.RemoveAccessorDeclaration:
                case SyntaxKind.UnknownAccessorDeclaration:
                    var accessorDeclaration = (AccessorDeclarationSyntax)node;
                    return GetDiagnosticSpan(accessorDeclaration.Modifiers, accessorDeclaration.Keyword, accessorDeclaration.Keyword);

                case SyntaxKind.TypeParameterConstraintClause:
                    var constraint = (TypeParameterConstraintClauseSyntax)node;
                    return TextSpan.FromBounds(constraint.WhereKeyword.SpanStart, constraint.Constraints.Last().Span.End);

                case SyntaxKind.TypeParameter:
                    var typeParameter = (TypeParameterSyntax)node;
                    return typeParameter.Identifier.Span;

                case SyntaxKind.AccessorList:
                case SyntaxKind.TypeParameterList:
                case SyntaxKind.ParameterList:
                case SyntaxKind.BracketedParameterList:
                    if (editKind == EditKind.Delete)
                    {
                        return GetDiagnosticSpanImpl(node.Parent, editKind);
                    }
                    else
                    {
                        return node.Span;
                    }

                case SyntaxKind.Parameter:
                    // We ignore anonymous methods and lambdas, 
                    // we only care about parameters of member declarations.
                    var parameter = (ParameterSyntax)node;
                    return GetDiagnosticSpan(parameter.Modifiers, parameter.Type, parameter);

                case SyntaxKind.AttributeList:
                    var attributeList = (AttributeListSyntax)node;
                    if (editKind == EditKind.Update)
                    {
                        return (attributeList.Target != null) ? attributeList.Target.Span : attributeList.Span;
                    }
                    else
                    {
                        return attributeList.Span;
                    }

                case SyntaxKind.Attribute:
                case SyntaxKind.ArrowExpressionClause:
                    return node.Span;

                // We only need a diagnostic span if reporting an error for a child statement.
                // The following statements may have child statements.

                case SyntaxKind.Block:
                    return ((BlockSyntax)node).OpenBraceToken.Span;

                case SyntaxKind.UsingStatement:
                    var usingStatement = (UsingStatementSyntax)node;
                    return TextSpan.FromBounds(usingStatement.UsingKeyword.SpanStart, usingStatement.CloseParenToken.Span.End);

                case SyntaxKind.FixedStatement:
                    var fixedStatement = (FixedStatementSyntax)node;
                    return TextSpan.FromBounds(fixedStatement.FixedKeyword.SpanStart, fixedStatement.CloseParenToken.Span.End);

                case SyntaxKind.LockStatement:
                    var lockStatement = (LockStatementSyntax)node;
                    return TextSpan.FromBounds(lockStatement.LockKeyword.SpanStart, lockStatement.CloseParenToken.Span.End);

                case SyntaxKind.StackAllocArrayCreationExpression:
                    return ((StackAllocArrayCreationExpressionSyntax)node).StackAllocKeyword.Span;

                case SyntaxKind.TryStatement:
                    return ((TryStatementSyntax)node).TryKeyword.Span;

                case SyntaxKind.CatchClause:
                    return ((CatchClauseSyntax)node).CatchKeyword.Span;

                case SyntaxKind.CatchFilterClause:
                    return node.Span;

                case SyntaxKind.FinallyClause:
                    return ((FinallyClauseSyntax)node).FinallyKeyword.Span;

                case SyntaxKind.IfStatement:
                    var ifStatement = (IfStatementSyntax)node;
                    return TextSpan.FromBounds(ifStatement.IfKeyword.SpanStart, ifStatement.CloseParenToken.Span.End);

                case SyntaxKind.ElseClause:
                    return ((ElseClauseSyntax)node).ElseKeyword.Span;

                case SyntaxKind.SwitchStatement:
                    var switchStatement = (SwitchStatementSyntax)node;
                    return TextSpan.FromBounds(switchStatement.SwitchKeyword.SpanStart, switchStatement.CloseParenToken.Span.End);

                case SyntaxKind.SwitchSection:
                    return ((SwitchSectionSyntax)node).Labels.Last().Span;

                case SyntaxKind.WhileStatement:
                    var whileStatement = (WhileStatementSyntax)node;
                    return TextSpan.FromBounds(whileStatement.WhileKeyword.SpanStart, whileStatement.CloseParenToken.Span.End);

                case SyntaxKind.DoStatement:
                    return ((DoStatementSyntax)node).DoKeyword.Span;

                case SyntaxKind.ForStatement:
                    var forStatement = (ForStatementSyntax)node;
                    return TextSpan.FromBounds(forStatement.ForKeyword.SpanStart, forStatement.CloseParenToken.Span.End);

                case SyntaxKind.ForEachStatement:
                    var forEachStatement = (ForEachStatementSyntax)node;
                    return TextSpan.FromBounds(forEachStatement.ForEachKeyword.SpanStart, forEachStatement.CloseParenToken.Span.End);

                case SyntaxKind.LabeledStatement:
                    return ((LabeledStatementSyntax)node).Identifier.Span;

                case SyntaxKind.CheckedStatement:
                case SyntaxKind.UncheckedStatement:
                    return ((CheckedStatementSyntax)node).Keyword.Span;

                case SyntaxKind.UnsafeStatement:
                    return ((UnsafeStatementSyntax)node).UnsafeKeyword.Span;

                case SyntaxKind.YieldBreakStatement:
                case SyntaxKind.YieldReturnStatement:
                case SyntaxKind.ReturnStatement:
                case SyntaxKind.ThrowStatement:
                case SyntaxKind.ExpressionStatement:
                case SyntaxKind.LocalDeclarationStatement:
                case SyntaxKind.GotoStatement:
                case SyntaxKind.GotoCaseStatement:
                case SyntaxKind.GotoDefaultStatement:
                case SyntaxKind.BreakStatement:
                case SyntaxKind.ContinueStatement:
                    return node.Span;

                case SyntaxKind.AwaitExpression:
                    return ((AwaitExpressionSyntax)node).AwaitKeyword.Span;

                case SyntaxKind.AnonymousObjectCreationExpression:
                    return ((AnonymousObjectCreationExpressionSyntax)node).NewKeyword.Span;

                case SyntaxKind.ParenthesizedLambdaExpression:
                    return ((ParenthesizedLambdaExpressionSyntax)node).ParameterList.Span;

                case SyntaxKind.SimpleLambdaExpression:
                    return ((SimpleLambdaExpressionSyntax)node).Parameter.Span;

                case SyntaxKind.AnonymousMethodExpression:
                    return ((AnonymousMethodExpressionSyntax)node).DelegateKeyword.Span;

                case SyntaxKind.QueryExpression:
                    return ((QueryExpressionSyntax)node).FromClause.FromKeyword.Span;

                case SyntaxKind.FromClause:
                    return ((FromClauseSyntax)node).FromKeyword.Span;

                case SyntaxKind.JoinClause:
                    return ((JoinClauseSyntax)node).JoinKeyword.Span;

                case SyntaxKind.LetClause:
                    return ((LetClauseSyntax)node).LetKeyword.Span;

                case SyntaxKind.WhereClause:
                    return ((WhereClauseSyntax)node).WhereKeyword.Span;

                case SyntaxKind.AscendingOrdering:
                case SyntaxKind.DescendingOrdering:
                    return node.Span;

                case SyntaxKind.SelectClause:
                    return ((SelectClauseSyntax)node).SelectKeyword.Span;

                case SyntaxKind.GroupClause:
                    return ((GroupClauseSyntax)node).GroupKeyword.Span;

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        private static TextSpan GetDiagnosticSpan(SyntaxTokenList modifiers, SyntaxNodeOrToken start, SyntaxNodeOrToken end)
        {
            return TextSpan.FromBounds((modifiers.Count != 0) ? modifiers.First().SpanStart : start.SpanStart, end.Span.End);
        }

        protected override string GetTopLevelDisplayName(SyntaxNode node, EditKind editKind)
        {
            return GetTopLevelDisplayNameImpl(node, editKind);
        }

        protected override string GetStatementDisplayName(SyntaxNode node, EditKind editKind)
        {
            return GetStatementDisplayNameImpl(node);
        }

        protected override string GetLambdaDisplayName(SyntaxNode lambda)
        {
            return GetStatementDisplayNameImpl(lambda);
        }

        // internal for testing
        internal static string GetTopLevelDisplayNameImpl(SyntaxNode node, EditKind editKind)
        {
            switch (node.Kind())
            {
                case SyntaxKind.GlobalStatement:
                    return "global statement";

                case SyntaxKind.ExternAliasDirective:
                    return "using namespace";

                case SyntaxKind.UsingDirective:
                    // Dev12 distinguishes using alias from using namespace and reports different errors for removing alias.
                    // None of these changes are allowed anyways, so let's keep it simple.
                    return "using directive";

                case SyntaxKind.NamespaceDeclaration:
                    return "namespace";

                case SyntaxKind.ClassDeclaration:
                    return "class";

                case SyntaxKind.StructDeclaration:
                    return "struct";

                case SyntaxKind.InterfaceDeclaration:
                    return "interface";

                case SyntaxKind.EnumDeclaration:
                    return "enum";

                case SyntaxKind.DelegateDeclaration:
                    return "delegate";

                case SyntaxKind.FieldDeclaration:
                    return "field";

                case SyntaxKind.EventFieldDeclaration:
                    return "event field";

                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.VariableDeclarator:
                    return GetTopLevelDisplayNameImpl(node.Parent, editKind);

                case SyntaxKind.MethodDeclaration:
                    return "method";

                case SyntaxKind.ConversionOperatorDeclaration:
                    return "conversion operator";

                case SyntaxKind.OperatorDeclaration:
                    return "operator";

                case SyntaxKind.ConstructorDeclaration:
                    return "constructor";

                case SyntaxKind.DestructorDeclaration:
                    return "destructor";

                case SyntaxKind.PropertyDeclaration:
                    return SyntaxUtilities.HasBackingField((PropertyDeclarationSyntax)node) ? "auto-property" : "property";

                case SyntaxKind.IndexerDeclaration:
                    return "indexer";

                case SyntaxKind.EventDeclaration:
                    return "event";

                case SyntaxKind.EnumMemberDeclaration:
                    return "enum value";

                case SyntaxKind.GetAccessorDeclaration:
                    if (node.Parent.Parent.IsKind(SyntaxKind.PropertyDeclaration))
                    {
                        return "property getter";
                    }
                    else
                    {
                        Debug.Assert(node.Parent.Parent.IsKind(SyntaxKind.IndexerDeclaration));
                        return "indexer getter";
                    }

                case SyntaxKind.SetAccessorDeclaration:
                    if (node.Parent.Parent.IsKind(SyntaxKind.PropertyDeclaration))
                    {
                        return "property setter";
                    }
                    else
                    {
                        Debug.Assert(node.Parent.Parent.IsKind(SyntaxKind.IndexerDeclaration));
                        return "indexer setter";
                    }

                case SyntaxKind.AddAccessorDeclaration:
                case SyntaxKind.RemoveAccessorDeclaration:
                    return "event accessor";

                case SyntaxKind.TypeParameterConstraintClause:
                    return "type constraint";

                case SyntaxKind.TypeParameterList:
                case SyntaxKind.TypeParameter:
                    return "type parameter";

                case SyntaxKind.Parameter:
                    return "parameter";

                case SyntaxKind.AttributeList:
                    return (editKind == EditKind.Update) ? "attribute target" : "attribute";

                case SyntaxKind.Attribute:
                    return "attribute";

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        // internal for testing
        internal static string GetStatementDisplayNameImpl(SyntaxNode node)
        {
            switch (node.Kind())
            {
                case SyntaxKind.TryStatement:
                    return "try block";

                case SyntaxKind.CatchClause:
                    return "catch clause";

                case SyntaxKind.CatchFilterClause:
                    return "filter clause";

                case SyntaxKind.FinallyClause:
                    return "finally clause";

                case SyntaxKind.FixedStatement:
                    return "fixed statement";

                case SyntaxKind.UsingStatement:
                    return "using statement";

                case SyntaxKind.LockStatement:
                    return "lock statement";

                case SyntaxKind.ForEachStatement:
                    return "foreach statement";

                case SyntaxKind.CheckedStatement:
                    return "checked statement";

                case SyntaxKind.UncheckedStatement:
                    return "unchecked statement";

                case SyntaxKind.YieldBreakStatement:
                case SyntaxKind.YieldReturnStatement:
                    return "yield statement";

                case SyntaxKind.AwaitExpression:
                    return "await expression";

                case SyntaxKind.ParenthesizedLambdaExpression:
                case SyntaxKind.SimpleLambdaExpression:
                    return "lambda";

                case SyntaxKind.AnonymousMethodExpression:
                    return "anonymous method";

                case SyntaxKind.FromClause:
                    return "from clause";

                case SyntaxKind.JoinClause:
                    return "join clause";

                case SyntaxKind.LetClause:
                    return "let clause";

                case SyntaxKind.WhereClause:
                    return "where clause";

                case SyntaxKind.AscendingOrdering:
                case SyntaxKind.DescendingOrdering:
                    return "orderby clause";

                case SyntaxKind.SelectClause:
                    return "select clause";

                case SyntaxKind.GroupClause:
                    return "groupby clause";

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        #endregion

        #region Top-Level Syntactic Rude Edits

        private struct EditClassifier
        {
            private readonly CSharpEditAndContinueAnalyzer analyzer;
            private readonly List<RudeEditDiagnostic> diagnostics;
            private readonly Match<SyntaxNode> match;
            private readonly SyntaxNode oldNode;
            private readonly SyntaxNode newNode;
            private readonly EditKind kind;
            private readonly TextSpan? span;

            public EditClassifier(
                CSharpEditAndContinueAnalyzer analyzer,
                List<RudeEditDiagnostic> diagnostics,
                SyntaxNode oldNode,
                SyntaxNode newNode,
                EditKind kind,
                Match<SyntaxNode> match = null,
                TextSpan? span = null)
            {
                this.analyzer = analyzer;
                this.diagnostics = diagnostics;
                this.oldNode = oldNode;
                this.newNode = newNode;
                this.kind = kind;
                this.span = span;
                this.match = match;
            }

            private void ReportError(RudeEditKind kind, SyntaxNode spanNode = null, SyntaxNode displayNode = null)
            {
                var span = (spanNode != null) ? GetDiagnosticSpanImpl(spanNode, this.kind) : GetSpan();
                var node = displayNode ?? newNode ?? oldNode;
                var displayName = (displayNode != null) ? GetTopLevelDisplayNameImpl(displayNode, this.kind) : GetDisplayName();

                diagnostics.Add(new RudeEditDiagnostic(kind, span, node, arguments: new[] { displayName }));
            }

            private string GetDisplayName()
            {
                return GetTopLevelDisplayNameImpl(newNode ?? oldNode, this.kind);
            }

            private TextSpan GetSpan()
            {
                if (span.HasValue)
                {
                    return span.Value;
                }

                if (newNode == null)
                {
                    return analyzer.GetDeletedNodeDiagnosticSpan(match, oldNode);
                }
                else
                {
                    return GetDiagnosticSpanImpl(newNode, kind);
                }
            }

            public void ClassifyEdit()
            {
                switch (kind)
                {
                    case EditKind.Delete:
                        ClassifyDelete(oldNode);
                        return;

                    case EditKind.Update:
                        ClassifyUpdate(oldNode, newNode);
                        return;

                    case EditKind.Move:
                        ClassifyMove(oldNode, newNode);
                        return;

                    case EditKind.Insert:
                        ClassifyInsert(newNode);
                        return;

                    case EditKind.Reorder:
                        ClassifyReorder(oldNode, newNode);
                        return;

                    default:
                        throw ExceptionUtilities.Unreachable;
                }
            }

            #region Move and Reorder

            private void ClassifyMove(SyntaxNode oldNode, SyntaxNode newNode)
            {
                // We could perhaps allow moving a type declaration to a different namespace syntax node
                // as long as it represents semantically the same namespace as the one of the original type declaration.
                ReportError(RudeEditKind.Move);
            }

            private void ClassifyReorder(SyntaxNode oldNode, SyntaxNode newNode)
            {
                switch (newNode.Kind())
                {
                    case SyntaxKind.GlobalStatement:
                        // TODO:
                        ReportError(RudeEditKind.Move);
                        return;

                    case SyntaxKind.ExternAliasDirective:
                    case SyntaxKind.UsingDirective:
                    case SyntaxKind.NamespaceDeclaration:
                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.StructDeclaration:
                    case SyntaxKind.InterfaceDeclaration:
                    case SyntaxKind.EnumDeclaration:
                    case SyntaxKind.DelegateDeclaration:
                    case SyntaxKind.VariableDeclaration:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.ConversionOperatorDeclaration:
                    case SyntaxKind.OperatorDeclaration:
                    case SyntaxKind.ConstructorDeclaration:
                    case SyntaxKind.DestructorDeclaration:
                    case SyntaxKind.IndexerDeclaration:
                    case SyntaxKind.EventDeclaration:
                    case SyntaxKind.GetAccessorDeclaration:
                    case SyntaxKind.SetAccessorDeclaration:
                    case SyntaxKind.AddAccessorDeclaration:
                    case SyntaxKind.RemoveAccessorDeclaration:
                    case SyntaxKind.TypeParameterConstraintClause:
                    case SyntaxKind.AttributeList:
                    case SyntaxKind.Attribute:
                        // We'll ignore these edits. A general policy is to ignore edits that are only discoverable via reflection.
                        return;

                    case SyntaxKind.PropertyDeclaration:
                    case SyntaxKind.FieldDeclaration:
                    case SyntaxKind.EventFieldDeclaration:
                    case SyntaxKind.VariableDeclarator:
                        // Maybe we could allow changing order of field declarations unless the containing type layout is sequential.
                        ReportError(RudeEditKind.Move);
                        return;

                    case SyntaxKind.EnumMemberDeclaration:
                        // To allow this change we would need to check that values of all fields of the enum 
                        // are preserved, or make sure we can update all method bodies that accessed those that changed.
                        ReportError(RudeEditKind.Move);
                        return;

                    case SyntaxKind.TypeParameter:
                    case SyntaxKind.Parameter:
                        ReportError(RudeEditKind.Move);
                        return;

                    default:
                        throw ExceptionUtilities.Unreachable;
                }
            }

            #endregion

            #region Insert

            private void ClassifyInsert(SyntaxNode node)
            {
                switch (node.Kind())
                {
                    case SyntaxKind.GlobalStatement:
                        // TODO:
                        ReportError(RudeEditKind.Insert);
                        return;

                    case SyntaxKind.ExternAliasDirective:
                    case SyntaxKind.UsingDirective:
                    case SyntaxKind.NamespaceDeclaration:
                    case SyntaxKind.DestructorDeclaration:
                        ReportError(RudeEditKind.Insert);
                        return;

                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.StructDeclaration:
                        ClassifyTypeWithPossibleExternMembersInsert((TypeDeclarationSyntax)node);
                        return;

                    case SyntaxKind.InterfaceDeclaration:
                    case SyntaxKind.EnumDeclaration:
                    case SyntaxKind.DelegateDeclaration:
                        return;

                    case SyntaxKind.PropertyDeclaration:
                    case SyntaxKind.IndexerDeclaration:
                    case SyntaxKind.EventDeclaration:
                        ClassifyModifiedMemberInsert(((BasePropertyDeclarationSyntax)node).Modifiers);
                        return;

                    case SyntaxKind.ConversionOperatorDeclaration:
                    case SyntaxKind.OperatorDeclaration:
                        ReportError(RudeEditKind.InsertOperator);
                        return;

                    case SyntaxKind.MethodDeclaration:
                        ClassifyMethodInsert((MethodDeclarationSyntax)node);
                        return;

                    case SyntaxKind.ConstructorDeclaration:
                        // Allow adding parameterless constructor.
                        // Semantic analysis will determine if it's an actual addition or 
                        // just an update of an existing implicit constructor.
                        if (SyntaxUtilities.IsParameterlessConstructor(node))
                        {
                            return;
                        }

                        ClassifyModifiedMemberInsert(((BaseMethodDeclarationSyntax)node).Modifiers);
                        return;

                    case SyntaxKind.GetAccessorDeclaration:
                    case SyntaxKind.SetAccessorDeclaration:
                    case SyntaxKind.AddAccessorDeclaration:
                    case SyntaxKind.RemoveAccessorDeclaration:
                        ClassifyAccessorInsert((AccessorDeclarationSyntax)node);
                        return;

                    case SyntaxKind.AccessorList:
                        // an error will be reported for each accessor
                        return;

                    case SyntaxKind.FieldDeclaration:
                    case SyntaxKind.EventFieldDeclaration:
                        // allowed: private fields in classes
                        ClassifyFieldInsert((BaseFieldDeclarationSyntax)node);
                        return;

                    case SyntaxKind.VariableDeclarator:
                        // allowed: private fields in classes
                        ClassifyFieldInsert((VariableDeclaratorSyntax)node);
                        return;

                    case SyntaxKind.VariableDeclaration:
                        // allowed: private fields in classes
                        ClassifyFieldInsert((VariableDeclarationSyntax)node);
                        return;

                    case SyntaxKind.EnumMemberDeclaration:
                    case SyntaxKind.TypeParameter:
                    case SyntaxKind.TypeParameterConstraintClause:
                    case SyntaxKind.TypeParameterList:
                    case SyntaxKind.Parameter:
                    case SyntaxKind.Attribute:
                    case SyntaxKind.AttributeList:
                        ReportError(RudeEditKind.Insert);
                        return;

                    default:
                        throw ExceptionUtilities.UnexpectedValue(node.Kind());
                }
            }

            private bool ClassifyModifiedMemberInsert(SyntaxTokenList modifiers)
            {
                if (modifiers.Any(SyntaxKind.ExternKeyword))
                {
                    ReportError(RudeEditKind.InsertExtern);
                    return false;
                }

                if (modifiers.Any(SyntaxKind.VirtualKeyword) || modifiers.Any(SyntaxKind.AbstractKeyword) || modifiers.Any(SyntaxKind.OverrideKeyword))
                {
                    ReportError(RudeEditKind.InsertVirtual);
                    return false;
                }

                return true;
            }

            private void ClassifyTypeWithPossibleExternMembersInsert(TypeDeclarationSyntax type)
            {
                // extern members are not allowed, even in a new type
                foreach (var member in type.Members)
                {
                    var modifiers = default(SyntaxTokenList);

                    switch (member.Kind())
                    {
                        case SyntaxKind.PropertyDeclaration:
                        case SyntaxKind.IndexerDeclaration:
                        case SyntaxKind.EventDeclaration:
                            modifiers = ((BasePropertyDeclarationSyntax)member).Modifiers;
                            break;

                        case SyntaxKind.ConversionOperatorDeclaration:
                        case SyntaxKind.OperatorDeclaration:
                        case SyntaxKind.MethodDeclaration:
                        case SyntaxKind.ConstructorDeclaration:
                            modifiers = ((BaseMethodDeclarationSyntax)member).Modifiers;
                            break;
                    }

                    if (modifiers.Any(SyntaxKind.ExternKeyword))
                    {
                        ReportError(RudeEditKind.InsertExtern, member, member);
                    }
                }
            }

            private void ClassifyMethodInsert(MethodDeclarationSyntax method)
            {
                ClassifyModifiedMemberInsert(method.Modifiers);

                if (method.Arity > 0)
                {
                    ReportError(RudeEditKind.InsertGenericMethod);
                }
            }

            private void ClassifyAccessorInsert(AccessorDeclarationSyntax accessor)
            {
                var baseProperty = (BasePropertyDeclarationSyntax)accessor.Parent.Parent;
                ClassifyModifiedMemberInsert(baseProperty.Modifiers);
            }

            private void ClassifyFieldInsert(BaseFieldDeclarationSyntax field)
            {
                ClassifyModifiedMemberInsert(field.Modifiers);
            }

            private void ClassifyFieldInsert(VariableDeclaratorSyntax fieldVariable)
            {
                ClassifyFieldInsert((VariableDeclarationSyntax)fieldVariable.Parent);
            }

            private void ClassifyFieldInsert(VariableDeclarationSyntax fieldVariable)
            {
                ClassifyFieldInsert((BaseFieldDeclarationSyntax)fieldVariable.Parent);
            }

            #endregion

            #region Delete

            private void ClassifyDelete(SyntaxNode oldNode)
            {
                switch (oldNode.Kind())
                {
                    case SyntaxKind.GlobalStatement:
                        // TODO:
                        ReportError(RudeEditKind.Delete);
                        return;

                    case SyntaxKind.ExternAliasDirective:
                    case SyntaxKind.UsingDirective:
                    case SyntaxKind.NamespaceDeclaration:
                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.StructDeclaration:
                    case SyntaxKind.InterfaceDeclaration:
                    case SyntaxKind.EnumDeclaration:
                    case SyntaxKind.DelegateDeclaration:
                    case SyntaxKind.MethodDeclaration:
                    case SyntaxKind.ConversionOperatorDeclaration:
                    case SyntaxKind.OperatorDeclaration:
                    case SyntaxKind.DestructorDeclaration:
                    case SyntaxKind.PropertyDeclaration:
                    case SyntaxKind.IndexerDeclaration:
                    case SyntaxKind.EventDeclaration:
                    case SyntaxKind.FieldDeclaration:
                    case SyntaxKind.EventFieldDeclaration:
                    case SyntaxKind.VariableDeclarator:
                    case SyntaxKind.VariableDeclaration:
                        // To allow removal of declarations we would need to update method bodies that 
                        // were previously binding to them but now are binding to another symbol that was previously hidden.
                        ReportError(RudeEditKind.Delete);
                        return;

                    case SyntaxKind.ConstructorDeclaration:
                        // Allow deletion of a parameterless constructor.
                        // Semantic analysis reports an error if the parameterless ctor isn't replaced by a default ctor.
                        if (!SyntaxUtilities.IsParameterlessConstructor(oldNode))
                        {
                            ReportError(RudeEditKind.Delete);
                        }

                        return;

                    case SyntaxKind.GetAccessorDeclaration:
                    case SyntaxKind.SetAccessorDeclaration:
                    case SyntaxKind.AddAccessorDeclaration:
                    case SyntaxKind.RemoveAccessorDeclaration:
                        // An accessor can be removed. Accessors are not hiding other symbols.
                        // If the new compilation still uses the removed accessor a semantic error will be reported.
                        // For simplicity though we disallow deletion of accessors for now. 
                        // The compiler would need to remember that the accessor has been deleted,
                        // so that its addition back is interpreted as an update. 
                        // Additional issues might involve changing accessibility of the accessor.
                        ReportError(RudeEditKind.Delete);
                        return;

                    case SyntaxKind.AccessorList:
                        Debug.Assert(
                            oldNode.Parent.IsKind(SyntaxKind.PropertyDeclaration) ||
                            oldNode.Parent.IsKind(SyntaxKind.IndexerDeclaration));

                        var accessorList = (AccessorListSyntax)oldNode;
                        var setter = accessorList.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.SetAccessorDeclaration));
                        if (setter != null)
                        {
                            ReportError(RudeEditKind.Delete, accessorList.Parent, setter);
                        }

                        return;

                    case SyntaxKind.AttributeList:
                    case SyntaxKind.Attribute:
                        // To allow removal of attributes we would need to check if the removed attribute
                        // is a pseudo-custom attribute that CLR allows us to change, or if it is a compiler well-know attribute
                        // that affects the generated IL.
                        ReportError(RudeEditKind.Delete);
                        return;

                    case SyntaxKind.EnumMemberDeclaration:
                        // We could allow removing enum member if it didn't affect the values of other enum members.
                        // If the updated compilation binds without errors it means that the enum value wasn't used.
                        ReportError(RudeEditKind.Delete);
                        return;

                    case SyntaxKind.TypeParameter:
                    case SyntaxKind.TypeParameterList:
                    case SyntaxKind.Parameter:
                    case SyntaxKind.ParameterList:
                    case SyntaxKind.TypeParameterConstraintClause:
                        ReportError(RudeEditKind.Delete);
                        return;

                    default:
                        throw ExceptionUtilities.UnexpectedValue(oldNode.Kind());
                }
            }

            #endregion

            #region Update

            private void ClassifyUpdate(SyntaxNode oldNode, SyntaxNode newNode)
            {
                switch (newNode.Kind())
                {
                    case SyntaxKind.GlobalStatement:
                        ReportError(RudeEditKind.Update);
                        return;

                    case SyntaxKind.ExternAliasDirective:
                        ReportError(RudeEditKind.Update);
                        return;

                    case SyntaxKind.UsingDirective:
                        // Dev12 distinguishes using alias from using namespace and reports different errors for removing alias.
                        // None of these changes are allowed anyways, so let's keep it simple.
                        ReportError(RudeEditKind.Update);
                        return;

                    case SyntaxKind.NamespaceDeclaration:
                        ClassifyUpdate((NamespaceDeclarationSyntax)oldNode, (NamespaceDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.StructDeclaration:
                    case SyntaxKind.InterfaceDeclaration:
                        ClassifyUpdate((TypeDeclarationSyntax)oldNode, (TypeDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.EnumDeclaration:
                        ClassifyUpdate((EnumDeclarationSyntax)oldNode, (EnumDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.DelegateDeclaration:
                        ClassifyUpdate((DelegateDeclarationSyntax)oldNode, (DelegateDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.FieldDeclaration:
                        ClassifyUpdate((BaseFieldDeclarationSyntax)oldNode, (BaseFieldDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.EventFieldDeclaration:
                        ClassifyUpdate((BaseFieldDeclarationSyntax)oldNode, (BaseFieldDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.VariableDeclaration:
                        ClassifyUpdate((VariableDeclarationSyntax)oldNode, (VariableDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.VariableDeclarator:
                        ClassifyUpdate((VariableDeclaratorSyntax)oldNode, (VariableDeclaratorSyntax)newNode);
                        return;

                    case SyntaxKind.MethodDeclaration:
                        ClassifyUpdate((MethodDeclarationSyntax)oldNode, (MethodDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.ConversionOperatorDeclaration:
                        ClassifyUpdate((ConversionOperatorDeclarationSyntax)oldNode, (ConversionOperatorDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.OperatorDeclaration:
                        ClassifyUpdate((OperatorDeclarationSyntax)oldNode, (OperatorDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.ConstructorDeclaration:
                        ClassifyUpdate((ConstructorDeclarationSyntax)oldNode, (ConstructorDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.DestructorDeclaration:
                        ClassifyUpdate((DestructorDeclarationSyntax)oldNode, (DestructorDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.PropertyDeclaration:
                        ClassifyUpdate((PropertyDeclarationSyntax)oldNode, (PropertyDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.IndexerDeclaration:
                        ClassifyUpdate((IndexerDeclarationSyntax)oldNode, (IndexerDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.EventDeclaration:
                        return;

                    case SyntaxKind.EnumMemberDeclaration:
                        ClassifyUpdate((EnumMemberDeclarationSyntax)oldNode, (EnumMemberDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.GetAccessorDeclaration:
                    case SyntaxKind.SetAccessorDeclaration:
                    case SyntaxKind.AddAccessorDeclaration:
                    case SyntaxKind.RemoveAccessorDeclaration:
                        ClassifyUpdate((AccessorDeclarationSyntax)oldNode, (AccessorDeclarationSyntax)newNode);
                        return;

                    case SyntaxKind.TypeParameterConstraintClause:
                        ClassifyUpdate((TypeParameterConstraintClauseSyntax)oldNode, (TypeParameterConstraintClauseSyntax)newNode);
                        return;

                    case SyntaxKind.TypeParameter:
                        ClassifyUpdate((TypeParameterSyntax)oldNode, (TypeParameterSyntax)newNode);
                        return;

                    case SyntaxKind.Parameter:
                        ClassifyUpdate((ParameterSyntax)oldNode, (ParameterSyntax)newNode);
                        return;

                    case SyntaxKind.AttributeList:
                        ClassifyUpdate((AttributeListSyntax)oldNode, (AttributeListSyntax)newNode);
                        return;

                    case SyntaxKind.Attribute:
                        // Dev12 reports "Rename" if the attribute type name is changed. 
                        // But such update is actually not renaming the attribute, it's changing what attribute is applied.
                        ReportError(RudeEditKind.Update);
                        return;

                    case SyntaxKind.TypeParameterList:
                    case SyntaxKind.ParameterList:
                    case SyntaxKind.BracketedParameterList:
                    case SyntaxKind.AccessorList:
                        return;

                    default:
                        throw ExceptionUtilities.Unreachable;
                }
            }

            private void ClassifyUpdate(NamespaceDeclarationSyntax oldNode, NamespaceDeclarationSyntax newNode)
            {
                Debug.Assert(!SyntaxFactory.AreEquivalent(oldNode.Name, newNode.Name));
                ReportError(RudeEditKind.Renamed);
            }

            private void ClassifyUpdate(TypeDeclarationSyntax oldNode, TypeDeclarationSyntax newNode)
            {
                if (oldNode.Kind() != newNode.Kind())
                {
                    ReportError(RudeEditKind.TypeKindUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.Modifiers, newNode.Modifiers))
                {
                    ReportError(RudeEditKind.ModifiersUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.Identifier, newNode.Identifier))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                Debug.Assert(!SyntaxFactory.AreEquivalent(oldNode.BaseList, newNode.BaseList));
                ReportError(RudeEditKind.BaseTypeOrInterfaceUpdate);
            }

            private void ClassifyUpdate(EnumDeclarationSyntax oldNode, EnumDeclarationSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Identifier, newNode.Identifier))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.Modifiers, newNode.Modifiers))
                {
                    ReportError(RudeEditKind.ModifiersUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.BaseList, newNode.BaseList))
                {
                    ReportError(RudeEditKind.EnumUnderlyingTypeUpdate);
                    return;
                }

                // The list of members has been updated (separators added).
                // We report a Rude Edit for each updated member.
            }

            private void ClassifyUpdate(DelegateDeclarationSyntax oldNode, DelegateDeclarationSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Modifiers, newNode.Modifiers))
                {
                    ReportError(RudeEditKind.ModifiersUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.ReturnType, newNode.ReturnType))
                {
                    ReportError(RudeEditKind.TypeUpdate);
                    return;
                }

                Debug.Assert(!SyntaxFactory.AreEquivalent(oldNode.Identifier, newNode.Identifier));
                ReportError(RudeEditKind.Renamed);
            }

            private void ClassifyUpdate(BaseFieldDeclarationSyntax oldNode, BaseFieldDeclarationSyntax newNode)
            {
                if (oldNode.Kind() != newNode.Kind())
                {
                    ReportError(RudeEditKind.FieldKindUpdate);
                    return;
                }

                Debug.Assert(!SyntaxFactory.AreEquivalent(oldNode.Modifiers, newNode.Modifiers));
                ReportError(RudeEditKind.ModifiersUpdate);
                return;
            }

            private void ClassifyUpdate(VariableDeclarationSyntax oldNode, VariableDeclarationSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Type, newNode.Type))
                {
                    ReportError(RudeEditKind.TypeUpdate);
                    return;
                }

                // separators may be added/removed:
            }

            private void ClassifyUpdate(VariableDeclaratorSyntax oldNode, VariableDeclaratorSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Identifier, newNode.Identifier))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                // If the argument lists are mismatched the field must have mismatched "fixed" modifier, 
                // which is reported by the field declaration.
                if ((oldNode.ArgumentList == null) == (newNode.ArgumentList == null))
                {
                    if (!SyntaxFactory.AreEquivalent(oldNode.ArgumentList, newNode.ArgumentList))
                    {
                        ReportError(RudeEditKind.FixedSizeFieldUpdate);
                        return;
                    }
                }

                var typeDeclaration = (TypeDeclarationSyntax)oldNode.Parent.Parent.Parent;
                if (typeDeclaration.Arity > 0)
                {
                    ReportError(RudeEditKind.GenericTypeInitializerUpdate);
                    return;
                }

                ClassifyDeclarationBodyRudeUpdates(newNode);
            }

            private void ClassifyUpdate(MethodDeclarationSyntax oldNode, MethodDeclarationSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Identifier, newNode.Identifier))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                if (!ClassifyMethodModifierUpdate(oldNode.Modifiers, newNode.Modifiers))
                {
                    ReportError(RudeEditKind.ModifiersUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.ReturnType, newNode.ReturnType))
                {
                    ReportError(RudeEditKind.TypeUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.ExplicitInterfaceSpecifier, newNode.ExplicitInterfaceSpecifier))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                ClassifyMethodBodyRudeUpdate(
                    (SyntaxNode)oldNode.Body ?? oldNode.ExpressionBody?.Expression,
                    (SyntaxNode)newNode.Body ?? newNode.ExpressionBody?.Expression,
                    containingMethodOpt: newNode,
                    containingType: (TypeDeclarationSyntax)newNode.Parent);
            }

            private bool ClassifyMethodModifierUpdate(SyntaxTokenList oldModifiers, SyntaxTokenList newModifiers)
            {
                var oldAsyncIndex = oldModifiers.IndexOf(SyntaxKind.AsyncKeyword);
                var newAsyncIndex = newModifiers.IndexOf(SyntaxKind.AsyncKeyword);

                if (oldAsyncIndex >= 0)
                {
                    oldModifiers = oldModifiers.RemoveAt(oldAsyncIndex);
                }

                if (newAsyncIndex >= 0)
                {
                    newModifiers = newModifiers.RemoveAt(newAsyncIndex);
                }

                return SyntaxFactory.AreEquivalent(oldModifiers, newModifiers);
            }

            private void ClassifyUpdate(ConversionOperatorDeclarationSyntax oldNode, ConversionOperatorDeclarationSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Modifiers, newNode.Modifiers))
                {
                    ReportError(RudeEditKind.ModifiersUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.ImplicitOrExplicitKeyword, newNode.ImplicitOrExplicitKeyword))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.Type, newNode.Type))
                {
                    ReportError(RudeEditKind.TypeUpdate);
                    return;
                }

                ClassifyMethodBodyRudeUpdate(
                    (SyntaxNode)oldNode.Body ?? oldNode.ExpressionBody?.Expression,
                    (SyntaxNode)newNode.Body ?? newNode.ExpressionBody?.Expression,
                    containingMethodOpt: null,
                    containingType: (TypeDeclarationSyntax)newNode.Parent);
            }

            private void ClassifyUpdate(OperatorDeclarationSyntax oldNode, OperatorDeclarationSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Modifiers, newNode.Modifiers))
                {
                    ReportError(RudeEditKind.ModifiersUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.OperatorToken, newNode.OperatorToken))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.ReturnType, newNode.ReturnType))
                {
                    ReportError(RudeEditKind.TypeUpdate);
                    return;
                }

                ClassifyMethodBodyRudeUpdate(
                    (SyntaxNode)oldNode.Body ?? oldNode.ExpressionBody?.Expression,
                    (SyntaxNode)newNode.Body ?? newNode.ExpressionBody?.Expression,
                    containingMethodOpt: null,
                    containingType: (TypeDeclarationSyntax)newNode.Parent);
            }

            private void ClassifyUpdate(AccessorDeclarationSyntax oldNode, AccessorDeclarationSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Modifiers, newNode.Modifiers))
                {
                    ReportError(RudeEditKind.ModifiersUpdate);
                    return;
                }

                if (oldNode.Kind() != newNode.Kind())
                {
                    ReportError(RudeEditKind.AccessorKindUpdate);
                    return;
                }

                Debug.Assert(newNode.Parent is AccessorListSyntax);
                Debug.Assert(newNode.Parent.Parent is BasePropertyDeclarationSyntax);

                ClassifyMethodBodyRudeUpdate(
                    oldNode.Body,
                    newNode.Body,
                    containingMethodOpt: null,
                    containingType: (TypeDeclarationSyntax)newNode.Parent.Parent.Parent);
            }

            private void ClassifyUpdate(EnumMemberDeclarationSyntax oldNode, EnumMemberDeclarationSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Identifier, newNode.Identifier))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                Debug.Assert(!SyntaxFactory.AreEquivalent(oldNode.EqualsValue, newNode.EqualsValue));
                ReportError(RudeEditKind.InitializerUpdate);
            }

            private void ClassifyUpdate(ConstructorDeclarationSyntax oldNode, ConstructorDeclarationSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Modifiers, newNode.Modifiers))
                {
                    ReportError(RudeEditKind.ModifiersUpdate);
                    return;
                }

                ClassifyMethodBodyRudeUpdate(
                    oldNode.Body,
                    newNode.Body,
                    containingMethodOpt: null,
                    containingType: (TypeDeclarationSyntax)newNode.Parent);
            }

            private void ClassifyUpdate(DestructorDeclarationSyntax oldNode, DestructorDeclarationSyntax newNode)
            {
                ClassifyMethodBodyRudeUpdate(
                    oldNode.Body,
                    newNode.Body,
                    containingMethodOpt: null,
                    containingType: (TypeDeclarationSyntax)newNode.Parent);
            }

            private void ClassifyUpdate(PropertyDeclarationSyntax oldNode, PropertyDeclarationSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Modifiers, newNode.Modifiers))
                {
                    ReportError(RudeEditKind.ModifiersUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.Type, newNode.Type))
                {
                    ReportError(RudeEditKind.TypeUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.Identifier, newNode.Identifier))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.ExplicitInterfaceSpecifier, newNode.ExplicitInterfaceSpecifier))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                var containingType = (TypeDeclarationSyntax)newNode.Parent;

                // TODO: We currently don't support switching from auto-props to properties with accessors and vice versa.
                // If we do we should also allow it for expression bodies.

                if (!SyntaxFactory.AreEquivalent(oldNode.ExpressionBody, newNode.ExpressionBody))
                {
                    var oldBody = SyntaxUtilities.TryGetEffectiveGetterBody(oldNode.ExpressionBody, oldNode.AccessorList);
                    var newBody = SyntaxUtilities.TryGetEffectiveGetterBody(newNode.ExpressionBody, newNode.AccessorList);

                    ClassifyMethodBodyRudeUpdate(
                       oldBody,
                       newBody,
                       containingMethodOpt: null,
                       containingType: containingType);

                    return;
                }

                Debug.Assert(!SyntaxFactory.AreEquivalent(oldNode.Initializer, newNode.Initializer));

                if (containingType.Arity > 0)
                {
                    ReportError(RudeEditKind.GenericTypeInitializerUpdate);
                    return;
                }

                if (newNode.Initializer != null)
                {
                    ClassifyDeclarationBodyRudeUpdates(newNode.Initializer);
                }
            }

            private void ClassifyUpdate(IndexerDeclarationSyntax oldNode, IndexerDeclarationSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Modifiers, newNode.Modifiers))
                {
                    ReportError(RudeEditKind.ModifiersUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.Type, newNode.Type))
                {
                    ReportError(RudeEditKind.TypeUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.ExplicitInterfaceSpecifier, newNode.ExplicitInterfaceSpecifier))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                Debug.Assert(!SyntaxFactory.AreEquivalent(oldNode.ExpressionBody, newNode.ExpressionBody));

                var oldBody = SyntaxUtilities.TryGetEffectiveGetterBody(oldNode.ExpressionBody, oldNode.AccessorList);
                var newBody = SyntaxUtilities.TryGetEffectiveGetterBody(newNode.ExpressionBody, newNode.AccessorList);

                ClassifyMethodBodyRudeUpdate(
                   oldBody,
                   newBody,
                   containingMethodOpt: null,
                   containingType: (TypeDeclarationSyntax)newNode.Parent);
            }

            private void ClassifyUpdate(TypeParameterSyntax oldNode, TypeParameterSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Identifier, newNode.Identifier))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                Debug.Assert(!SyntaxFactory.AreEquivalent(oldNode.VarianceKeyword, newNode.VarianceKeyword));
                ReportError(RudeEditKind.VarianceUpdate);
            }

            private void ClassifyUpdate(TypeParameterConstraintClauseSyntax oldNode, TypeParameterConstraintClauseSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Name, newNode.Name))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                Debug.Assert(!SyntaxFactory.AreEquivalent(oldNode.Constraints, newNode.Constraints));
                ReportError(RudeEditKind.TypeUpdate);
            }

            private void ClassifyUpdate(ParameterSyntax oldNode, ParameterSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Identifier, newNode.Identifier))
                {
                    ReportError(RudeEditKind.Renamed);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.Modifiers, newNode.Modifiers))
                {
                    ReportError(RudeEditKind.ModifiersUpdate);
                    return;
                }

                if (!SyntaxFactory.AreEquivalent(oldNode.Type, newNode.Type))
                {
                    ReportError(RudeEditKind.TypeUpdate);
                    return;
                }

                Debug.Assert(!SyntaxFactory.AreEquivalent(oldNode.Default, newNode.Default));
                ReportError(RudeEditKind.InitializerUpdate);
            }

            private void ClassifyUpdate(AttributeListSyntax oldNode, AttributeListSyntax newNode)
            {
                if (!SyntaxFactory.AreEquivalent(oldNode.Target, newNode.Target))
                {
                    ReportError(RudeEditKind.Update);
                    return;
                }

                // changes in attribute separators are not interesting:
            }

            private void ClassifyMethodBodyRudeUpdate(
                SyntaxNode oldBody,
                SyntaxNode newBody,
                MethodDeclarationSyntax containingMethodOpt,
                TypeDeclarationSyntax containingType)
            {
                Debug.Assert(oldBody is BlockSyntax || oldBody is ExpressionSyntax || oldBody == null);
                Debug.Assert(newBody is BlockSyntax || newBody is ExpressionSyntax || newBody == null);

                if ((oldBody == null) != (newBody == null))
                {
                    if (oldBody == null)
                    {
                        ReportError(RudeEditKind.MethodBodyAdd);
                        return;
                    }
                    else
                    {
                        ReportError(RudeEditKind.MethodBodyDelete);
                        return;
                    }
                }

                ClassifyMemberBodyRudeUpdate(containingMethodOpt, containingType, isTriviaUpdate: false);

                if (newBody != null)
                {
                    ClassifyDeclarationBodyRudeUpdates(newBody);
                }
            }

            public void ClassifyMemberBodyRudeUpdate(
                MethodDeclarationSyntax containingMethodOpt,
                TypeDeclarationSyntax containingTypeOpt,
                bool isTriviaUpdate)
            {
                if (SyntaxUtilities.Any(containingMethodOpt?.TypeParameterList))
                {
                    ReportError(isTriviaUpdate ? RudeEditKind.GenericMethodTriviaUpdate : RudeEditKind.GenericMethodUpdate);
                    return;
                }

                if (SyntaxUtilities.Any(containingTypeOpt?.TypeParameterList))
                {
                    ReportError(isTriviaUpdate ? RudeEditKind.GenericTypeTriviaUpdate : RudeEditKind.GenericTypeUpdate);
                    return;
                }
            }

            public void ClassifyDeclarationBodyRudeUpdates(SyntaxNode newDeclarationOrBody)
            {
                foreach (var node in newDeclarationOrBody.DescendantNodesAndSelf(ChildrenCompiledInBody))
                {
                    switch (node.Kind())
                    {
                        case SyntaxKind.StackAllocArrayCreationExpression:
                            ReportError(RudeEditKind.StackAllocUpdate, node, newNode);
                            return;

                        case SyntaxKind.ParenthesizedLambdaExpression:
                        case SyntaxKind.SimpleLambdaExpression:
                            // TODO (tomat): allow 
                            ReportError(RudeEditKind.RUDE_EDIT_LAMBDA_EXPRESSION, node, newNode);
                            return;

                        case SyntaxKind.AnonymousMethodExpression:
                            // TODO (tomat): allow 
                            ReportError(RudeEditKind.RUDE_EDIT_ANON_METHOD, node, newNode);
                            return;

                        case SyntaxKind.QueryExpression:
                            // TODO (tomat): allow 
                            ReportError(RudeEditKind.RUDE_EDIT_QUERY_EXPRESSION, node, newNode);
                            return;

                        case SyntaxKind.AnonymousObjectCreationExpression:
                            // TODO (tomat): allow 
                            ReportError(RudeEditKind.RUDE_EDIT_ANONYMOUS_TYPE, node, newNode);
                            return;
                    }
                }
            }

            private static bool ChildrenCompiledInBody(SyntaxNode node)
            {
                return node.Kind() != SyntaxKind.ParenthesizedLambdaExpression
                    && node.Kind() != SyntaxKind.SimpleLambdaExpression;
            }

            #endregion
        }

        internal override void ReportSyntacticRudeEdits(
            List<RudeEditDiagnostic> diagnostics,
            Match<SyntaxNode> match,
            Edit<SyntaxNode> edit,
            Dictionary<SyntaxNode, EditKind> editMap)
        {
            if (HasParentEdit(editMap, edit))
            {
                return;
            }

            var classifier = new EditClassifier(this, diagnostics, edit.OldNode, edit.NewNode, edit.Kind, match);
            classifier.ClassifyEdit();
        }

        internal override void ReportMemberUpdateRudeEdits(List<RudeEditDiagnostic> diagnostics, SyntaxNode newMember, TextSpan? span)
        {
            var classifier = new EditClassifier(this, diagnostics, null, newMember, EditKind.Update, span: span);

            classifier.ClassifyMemberBodyRudeUpdate(
                newMember as MethodDeclarationSyntax,
                newMember.FirstAncestorOrSelf<TypeDeclarationSyntax>(),
                isTriviaUpdate: true);

            classifier.ClassifyDeclarationBodyRudeUpdates(newMember);
        }

        #endregion

        #region Semantic Rude Edits

        internal override void ReportInsertedMemberSymbolRudeEdits(List<RudeEditDiagnostic> diagnostics, ISymbol newSymbol)
        {
            // We rejected all exported methods during syntax analysis, so no additional work is needed here.
        }

        #endregion

        #region Exception Handling Rude Edits

        protected override List<SyntaxNode> GetExceptionHandlingAncestors(SyntaxNode node, bool isLeaf)
        {
            var result = new List<SyntaxNode>();

            while (node != null)
            {
                var kind = node.Kind();

                switch (kind)
                {
                    case SyntaxKind.TryStatement:
                        if (!isLeaf)
                        {
                            result.Add(node);
                        }

                        break;

                    case SyntaxKind.CatchClause:
                    case SyntaxKind.FinallyClause:
                        result.Add(node);

                        // skip try:
                        Debug.Assert(node.Parent.Kind() == SyntaxKind.TryStatement);
                        node = node.Parent;

                        break;

                    // stop at type declaration:
                    case SyntaxKind.ClassDeclaration:
                    case SyntaxKind.StructDeclaration:
                        return result;
                }

                // stop at lambda:
                if (SyntaxUtilities.IsLambda(kind))
                {
                    return result;
                }

                node = node.Parent;
            }

            return result;
        }

        internal override void ReportEnclosingExceptionHandlingRudeEdits(
            List<RudeEditDiagnostic> diagnostics,
            IEnumerable<Edit<SyntaxNode>> exceptionHandlingEdits,
            SyntaxNode oldStatement,
            SyntaxNode newStatement)
        {
            foreach (var edit in exceptionHandlingEdits)
            {
                // try/catch/finally have distinct labels so only the nodes of the same kind may match:
                Debug.Assert(edit.Kind != EditKind.Update || edit.OldNode.RawKind == edit.NewNode.RawKind);

                if (edit.Kind != EditKind.Update || !AreExceptionClausesEquivalent(edit.OldNode, edit.NewNode))
                {
                    AddRudeDiagnostic(diagnostics, edit.OldNode, edit.NewNode, newStatement);
                }
            }
        }

        private static bool AreExceptionClausesEquivalent(SyntaxNode oldNode, SyntaxNode newNode)
        {
            switch (oldNode.Kind())
            {
                case SyntaxKind.TryStatement:
                    var oldTryStatement = (TryStatementSyntax)oldNode;
                    var newTryStatement = (TryStatementSyntax)newNode;
                    return SyntaxFactory.AreEquivalent(oldTryStatement.Finally, newTryStatement.Finally)
                        && SyntaxFactory.AreEquivalent(oldTryStatement.Catches, newTryStatement.Catches);

                case SyntaxKind.CatchClause:
                case SyntaxKind.FinallyClause:
                    return SyntaxFactory.AreEquivalent(oldNode, newNode);

                default:
                    throw ExceptionUtilities.UnexpectedValue(oldNode.Kind());
            }
        }

        /// <summary>
        /// An active statement (leaf or not) inside a "catch" makes the catch block read-only.
        /// An active statement (leaf or not) inside a "finally" makes the whole try/catch/finally block read-only.
        /// An active statement (non leaf)    inside a "try" makes the catch/finally block read-only.
        /// </summary>
        protected override TextSpan GetExceptionHandlingRegion(SyntaxNode node, out bool coversAllChildren)
        {
            TryStatementSyntax tryStatement;
            switch (node.Kind())
            {
                case SyntaxKind.TryStatement:
                    tryStatement = (TryStatementSyntax)node;
                    coversAllChildren = false;

                    if (tryStatement.Catches.Count == 0)
                    {
                        Debug.Assert(tryStatement.Finally != null);
                        return tryStatement.Finally.Span;
                    }

                    return TextSpan.FromBounds(
                        tryStatement.Catches.First().SpanStart,
                        (tryStatement.Finally != null) ?
                            tryStatement.Finally.Span.End :
                            tryStatement.Catches.Last().Span.End);

                case SyntaxKind.CatchClause:
                    coversAllChildren = true;
                    return node.Span;

                case SyntaxKind.FinallyClause:
                    coversAllChildren = true;
                    tryStatement = (TryStatementSyntax)node.Parent;
                    return tryStatement.Span;

                default:
                    throw ExceptionUtilities.UnexpectedValue(node.Kind());
            }
        }

        #endregion

        #region State Machines

        protected override ImmutableArray<SyntaxNode> GetStateMachineSuspensionPoints(SyntaxNode body)
        {
            if (SyntaxUtilities.IsAsyncMethodOrLambda(body.Parent))
            {
                return SyntaxUtilities.GetAwaitExpressions(body);
            }
            else
            {
                return SyntaxUtilities.GetYieldStatements(body);
            }
        }

        internal override void ReportStateMachineSuspensionPointRudeEdits(List<RudeEditDiagnostic> diagnostics, SyntaxNode oldNode, SyntaxNode newNode)
        {
            // TODO: changes around suspension points (foreach, lock, using, etc.)

            if (oldNode.RawKind != newNode.RawKind)
            {
                Debug.Assert(oldNode is YieldStatementSyntax && newNode is YieldStatementSyntax);

                // changing yield return to yield break
                diagnostics.Add(new RudeEditDiagnostic(
                    RudeEditKind.Update,
                    newNode.Span,
                    newNode,
                    new[] { GetStatementDisplayName(newNode, EditKind.Update) }));
            }
            else if (newNode.IsKind(SyntaxKind.AwaitExpression))
            {
                var oldContainingStatementPart = FindContainingStatementPart(oldNode);
                var newContainingStatementPart = FindContainingStatementPart(newNode);

                // If the old statememnt has spilled state and the new doesn't the edit is ok. We'll just not use the spilled state.
                if (!SyntaxFactory.AreEquivalent(oldContainingStatementPart, newContainingStatementPart) &&
                    !HasNoSpilledState(newNode, newContainingStatementPart))
                {
                    diagnostics.Add(new RudeEditDiagnostic(RudeEditKind.AwaitStatementUpdate, newContainingStatementPart.Span));
                }
            }
        }

        private static SyntaxNode FindContainingStatementPart(SyntaxNode node)
        {
            while (true)
            {
                var statement = node as StatementSyntax;
                if (statement != null)
                {
                    return statement;
                }

                switch (node.Parent.Kind())
                {
                    case SyntaxKind.ForStatement:
                    case SyntaxKind.ForEachStatement:
                    case SyntaxKind.IfStatement:
                    case SyntaxKind.WhileStatement:
                    case SyntaxKind.DoStatement:
                    case SyntaxKind.SwitchStatement:
                    case SyntaxKind.LockStatement:
                    case SyntaxKind.UsingStatement:
                    case SyntaxKind.ArrowExpressionClause:
                        return node;
                }

                if (SyntaxFacts.IsLambdaBody(node))
                {
                    return node;
                }

                node = node.Parent;
            }
        }

        private static bool HasNoSpilledState(SyntaxNode awaitExpression, SyntaxNode containingStatementPart)
        {
            Debug.Assert(awaitExpression.IsKind(SyntaxKind.AwaitExpression));

            // There is nothing within the statement part surrounding the await expression.
            if (containingStatementPart == awaitExpression)
            {
                return true;
            }

            switch (containingStatementPart.Kind())
            {
                case SyntaxKind.ExpressionStatement:
                case SyntaxKind.ReturnStatement:
                    var expression = GetExpressionFromStatementPart(containingStatementPart);

                    // await expr;
                    // return await expr;
                    if (expression == awaitExpression)
                    {
                        return true;
                    }

                    // identifier = await expr; 
                    // return identifier = await expr; 
                    return IsSimpleAwaitAssignment(expression, awaitExpression);

                case SyntaxKind.VariableDeclaration:
                    // var idf = await expr in using, for, etc.
                    // EqualsValueClause -> VariableDeclarator -> VariableDeclaration
                    return awaitExpression.Parent.Parent.Parent == containingStatementPart;

                case SyntaxKind.LocalDeclarationStatement:
                    // var idf = await expr;
                    // EqualsValueClause -> VariableDeclarator -> VariableDeclaration -> LocalDeclarationStatement
                    return awaitExpression.Parent.Parent.Parent.Parent == containingStatementPart;
            }

            return IsSimpleAwaitAssignment(containingStatementPart, awaitExpression);
        }

        private static ExpressionSyntax GetExpressionFromStatementPart(SyntaxNode statement)
        {
            switch (statement.Kind())
            {
                case SyntaxKind.ExpressionStatement:
                    return ((ExpressionStatementSyntax)statement).Expression;

                case SyntaxKind.ReturnStatement:
                    return ((ReturnStatementSyntax)statement).Expression;

                default:
                    throw ExceptionUtilities.Unreachable;
            }
        }

        private static bool IsSimpleAwaitAssignment(SyntaxNode node, SyntaxNode awaitExpression)
        {
            if (node.IsKind(SyntaxKind.SimpleAssignmentExpression))
            {
                var assignment = (AssignmentExpressionSyntax)node;
                return assignment.Left.IsKind(SyntaxKind.IdentifierName) && assignment.Right == awaitExpression;
            }

            return false;
        }

        #endregion

        #region Rude Edits around Active Statement

        internal override void ReportOtherRudeEditsAroundActiveStatement(
            List<RudeEditDiagnostic> diagnostics,
            Match<SyntaxNode> match,
            SyntaxNode oldActiveStatement,
            SyntaxNode newActiveStatement,
            bool isLeaf)
        {
            ReportRudeEditsForAncestorsDeclaringInterStatementTemps(diagnostics, match, oldActiveStatement, newActiveStatement, isLeaf);
            ReportRudeEditsForCheckedStatements(diagnostics, oldActiveStatement, newActiveStatement, isLeaf);
        }

        private void ReportRudeEditsForCheckedStatements(
            List<RudeEditDiagnostic> diagnostics,
            SyntaxNode oldActiveStatement,
            SyntaxNode newActiveStatement,
            bool isLeaf)
        {
            // checked context can be changed around leaf active statement:
            if (isLeaf)
            {
                return;
            }

            // Changing checked context around an internal active statement may change the instructions
            // executed after method calls in the active statement but before the next sequence point.
            // Since the debugger remaps the IP at the first sequence point following a call instruction
            // allowing overflow context to be changed may lead to execution of code with old semantics.

            var oldCheckedStatement = TryGetCheckedStatementAncestor(oldActiveStatement);
            var newCheckedStatement = TryGetCheckedStatementAncestor(newActiveStatement);

            bool isRude;
            if (oldCheckedStatement == null || newCheckedStatement == null)
            {
                isRude = oldCheckedStatement != newCheckedStatement;
            }
            else
            {
                isRude = oldCheckedStatement.Kind() != newCheckedStatement.Kind();
            }

            if (isRude)
            {
                AddRudeDiagnostic(diagnostics, oldCheckedStatement, newCheckedStatement, newActiveStatement);
            }
        }

        private static CheckedStatementSyntax TryGetCheckedStatementAncestor(SyntaxNode node)
        {
            // Ignoring lambda boundaries since checked context flows thru.

            while (node != null)
            {
                switch (node.Kind())
                {
                    case SyntaxKind.CheckedStatement:
                    case SyntaxKind.UncheckedStatement:
                        return (CheckedStatementSyntax)node;
                }
                 
                node = node.Parent;
            }

            return null;
        }

        private void ReportRudeEditsForAncestorsDeclaringInterStatementTemps(
            List<RudeEditDiagnostic> diagnostics,
            Match<SyntaxNode> match,
            SyntaxNode oldActiveStatement,
            SyntaxNode newActiveStatement,
            bool isLeaf)
        {
            // Rude Edits for fixed/using/lock/foreach statements that are added/updated around an active statement.
            // Although such changes are technically possible, they might lead to confusion since 
            // the temporary variables these statements generate won't be properly initialized.
            //
            // We use a simple algorithm to match each new node with its old counterpart.
            // If all nodes match this algorithm is linear, otherwise it's quadratic.
            // 
            // Unlike exception regions matching where we use LCS, we allow reordering of the statements.

            ReportUnmatchedStatements<LockStatementSyntax>(diagnostics, match, (int)SyntaxKind.LockStatement, oldActiveStatement, newActiveStatement,
                areEquivalent: AreEquivalentActiveStatements,
                areSimilar: null);

            ReportUnmatchedStatements<FixedStatementSyntax>(diagnostics, match, (int)SyntaxKind.FixedStatement, oldActiveStatement, newActiveStatement,
                areEquivalent: AreEquivalentActiveStatements,
                areSimilar: (n1, n2) => DeclareSameIdentifiers(n1.Declaration.Variables, n2.Declaration.Variables));

            ReportUnmatchedStatements<UsingStatementSyntax>(diagnostics, match, (int)SyntaxKind.UsingStatement, oldActiveStatement, newActiveStatement,
                areEquivalent: AreEquivalentActiveStatements,
                areSimilar: (using1, using2) =>
                {
                    return using1.Declaration != null && using2.Declaration != null &&
                        DeclareSameIdentifiers(using1.Declaration.Variables, using2.Declaration.Variables);
                });

            ReportUnmatchedStatements<ForEachStatementSyntax>(diagnostics, match, (int)SyntaxKind.ForEachStatement, oldActiveStatement, newActiveStatement,
                areEquivalent: AreEquivalentActiveStatements,
                areSimilar: (n1, n2) => SyntaxFactory.AreEquivalent(n1.Identifier, n2.Identifier));
        }

        private static bool DeclareSameIdentifiers(SeparatedSyntaxList<VariableDeclaratorSyntax> oldVariables, SeparatedSyntaxList<VariableDeclaratorSyntax> newVariables)
        {
            if (oldVariables.Count != newVariables.Count)
            {
                return false;
            }

            for (int i = 0; i < oldVariables.Count; i++)
            {
                if (!SyntaxFactory.AreEquivalent(oldVariables[i].Identifier, newVariables[i].Identifier))
                {
                    return false;
                }
            }

            return true;
        }

        #endregion
    }
}
