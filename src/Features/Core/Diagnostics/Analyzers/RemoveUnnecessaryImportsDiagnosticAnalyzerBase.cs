// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Shared.Extensions;
using Microsoft.CodeAnalysis.Text;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Diagnostics.RemoveUnnecessaryImports
{
    internal abstract class RemoveUnnecessaryImportsDiagnosticAnalyzerBase : DiagnosticAnalyzer, IBuiltInAnalyzer
    {
        // NOTE: This is a trigger diagnostic, which doesn't show up in the ruleset editor and hence doesn't need a conventional IDE Diagnostic ID string.
        internal const string DiagnosticFixableId = "RemoveUnnecessaryImportsFixable";

        private static LocalizableString localizableMessageAndTitle = new LocalizableResourceString(nameof(WorkspacesResources.RemoveUnnecessaryImportsOrUsings), WorkspacesResources.ResourceManager, typeof(WorkspacesResources));

        private static readonly DiagnosticDescriptor classificationIdDescriptor = new DiagnosticDescriptor(IDEDiagnosticIds.RemoveUnnecessaryImportsDiagnosticId,
                                                                                            localizableMessageAndTitle,
                                                                                            localizableMessageAndTitle,
                                                                                            DiagnosticCategory.Style,
                                                                                            DiagnosticSeverity.Hidden,
                                                                                            isEnabledByDefault: true,
                                                                                            customTags: DiagnosticCustomTags.Unnecessary);

        private static readonly DiagnosticDescriptor fixableIdDescriptor = new TriggerDiagnosticDescriptor(DiagnosticFixableId);

        private static readonly ImmutableArray<DiagnosticDescriptor> descriptors = ImmutableArray.Create(fixableIdDescriptor, classificationIdDescriptor);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
        {
            get
            {
                return descriptors;
            }
        }

        public override void Initialize(AnalysisContext context)
        {
            context.RegisterSemanticModelAction(this.AnalyzeSemanticModel);
        }

        private void AnalyzeSemanticModel(SemanticModelAnalysisContext context)
        {
            var tree = context.SemanticModel.SyntaxTree;
            var root = tree.GetRoot();
            var unncessaryImports = GetUnnecessaryImports(context.SemanticModel, root);
            if (unncessaryImports != null && unncessaryImports.Any())
            {
                Func<SyntaxNode, SyntaxToken> getLastTokenFunc = GetLastTokenDelegateForContiguousSpans();
                var contiguousSpans = unncessaryImports.GetContiguousSpans(getLastTokenFunc);
                var diagnostics = CreateClassificationDiagnostics(contiguousSpans, tree).Concat(
                        CreateFixableDiagnostics(unncessaryImports, tree));

                foreach (var diagnostic in diagnostics)
                {
                    context.ReportDiagnostic(diagnostic);
                }
            }
        }

        protected abstract IEnumerable<SyntaxNode> GetUnnecessaryImports(SemanticModel semanticModel, SyntaxNode root, CancellationToken cancellationToken = default(CancellationToken));
        protected virtual Func<SyntaxNode, SyntaxToken> GetLastTokenDelegateForContiguousSpans()
        {
            return null;
        }

        // Create one diagnostic for each unnecessary span that will be classified as Unnecessary
        private IEnumerable<Diagnostic> CreateClassificationDiagnostics(IEnumerable<TextSpan> contiguousSpans, SyntaxTree tree, CancellationToken cancellationToken = default(CancellationToken))
        {
            foreach (var span in contiguousSpans)
            {
                if (tree.OverlapsHiddenPosition(span, cancellationToken))
                {
                    continue;
                }

                yield return Diagnostic.Create(classificationIdDescriptor, tree.GetLocation(span));
            }
        }

        protected abstract IEnumerable<TextSpan> GetFixableDiagnosticSpans(IEnumerable<SyntaxNode> nodes, SyntaxTree tree, CancellationToken cancellationToken = default(CancellationToken));

        private IEnumerable<Diagnostic> CreateFixableDiagnostics(IEnumerable<SyntaxNode> nodes, SyntaxTree tree, CancellationToken cancellationToken = default(CancellationToken))
        {
            var spans = GetFixableDiagnosticSpans(nodes, tree, cancellationToken);

            foreach (var span in spans)
            {
                yield return Diagnostic.Create(fixableIdDescriptor, tree.GetLocation(span));
            }
        }
    }
}
