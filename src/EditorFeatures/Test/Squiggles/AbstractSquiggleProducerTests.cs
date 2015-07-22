// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Editor.Implementation.Diagnostics;
using Microsoft.CodeAnalysis.Editor.Shared.Tagging;
using Microsoft.CodeAnalysis.Editor.UnitTests.Workspaces;
using Microsoft.CodeAnalysis.Options;
using Microsoft.CodeAnalysis.Shared.TestHooks;
using Microsoft.CodeAnalysis.SolutionCrawler;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Roslyn.Test.Utilities;
using Roslyn.Utilities;

namespace Microsoft.CodeAnalysis.Editor.UnitTests.Squiggles
{
    public abstract class AbstractSquiggleProducerTests
    {
        protected static IEnumerable<ITagSpan<IErrorTag>> GetErrorSpans(TestWorkspace workspace, ImmutableDictionary<string, ImmutableArray<DiagnosticAnalyzer>> analyzerMap = null)
        {
            var registrationService = workspace.Services.GetService<ISolutionCrawlerRegistrationService>();
            registrationService.Register(workspace);

            var diagnosticWaiter = new DiagnosticServiceWaiter();
            var listeners = new[] { new Lazy<IAsynchronousOperationListener, FeatureMetadata>(
                () => diagnosticWaiter, new FeatureMetadata(new Dictionary<string, object>() { { "FeatureName", FeatureAttribute.DiagnosticService } })),
                new Lazy<IAsynchronousOperationListener, FeatureMetadata>(
                () => diagnosticWaiter, new FeatureMetadata(new Dictionary<string, object>() { { "FeatureName", FeatureAttribute.ErrorSquiggles } })) };

            var optionsService = workspace.Services.GetService<IOptionService>();

            DiagnosticAnalyzerService analyzerService = null;
            if (analyzerMap == null || analyzerMap.Count == 0)
            {
                var compilerAnalyzersMap = DiagnosticExtensions.GetCompilerDiagnosticAnalyzersMap();
                analyzerService = new TestDiagnosticAnalyzerService(compilerAnalyzersMap);
            }
            else
            {
                analyzerService = new TestDiagnosticAnalyzerService(analyzerMap);
            }

            var diagnosticService = new DiagnosticService(SpecializedCollections.SingletonEnumerable<IDiagnosticUpdateSource>(analyzerService), listeners);

            var document = workspace.Documents.First();
            var buffer = document.GetTextBuffer();

            var foregroundService = workspace.GetService<IForegroundNotificationService>();
            var taggerProvider = new DiagnosticsSquiggleTaggerProvider(optionsService, diagnosticService, foregroundService, listeners);
            var tagger = (AsynchronousTagger<IErrorTag>)taggerProvider.CreateTagger<IErrorTag>(buffer);
            var taggerSource = tagger.TagSource;

            var service = workspace.Services.GetService<ISolutionCrawlerRegistrationService>() as SolutionCrawlerRegistrationService;
            service.WaitUntilCompletion_ForTestingPurposesOnly(workspace, ImmutableArray.Create(analyzerService.CreateIncrementalAnalyzer(workspace)));

            diagnosticWaiter.CreateWaitTask().PumpingWait();

            var snapshot = buffer.CurrentSnapshot;
            var intervalTree = taggerSource.GetTagIntervalTreeForBuffer(buffer);
            var spans = intervalTree.GetIntersectingSpans(new SnapshotSpan(snapshot, 0, snapshot.Length)).ToImmutableArray();

            taggerSource.TestOnly_Dispose();

            registrationService.Unregister(workspace);

            return spans;
        }

        internal static IList<ITagSpan<IErrorTag>> GetErrorsFromUpdateSource(TestWorkspace workspace, TestHostDocument document, DiagnosticsUpdatedArgs updateArgs)
        {
            var source = new TestDiagnosticUpdateSource();

            var diagnosticWaiter = new DiagnosticServiceWaiter();
            var listeners = new[] {
                new Lazy<IAsynchronousOperationListener, FeatureMetadata>(
                    () => diagnosticWaiter,
                    new FeatureMetadata(new Dictionary<string, object>() { { "FeatureName", FeatureAttribute.DiagnosticService } })),
                new Lazy<IAsynchronousOperationListener, FeatureMetadata>(
                    () => diagnosticWaiter,
                    new FeatureMetadata(new Dictionary<string, object>() { { "FeatureName", FeatureAttribute.ErrorSquiggles } })) };

            var optionsService = workspace.Services.GetService<IOptionService>();
            var diagnosticService = new DiagnosticService(SpecializedCollections.SingletonEnumerable<IDiagnosticUpdateSource>(source), listeners);

            var foregroundService = workspace.GetService<IForegroundNotificationService>();  //new TestForegroundNotificationService();

            var buffer = document.GetTextBuffer();
            var provider = new DiagnosticsSquiggleTaggerProvider(optionsService, diagnosticService, foregroundService, listeners);
            var tagger = (AsynchronousTagger<IErrorTag>)provider.CreateTagger<IErrorTag>(buffer);
            var taggerSource = tagger.TagSource;

            source.RaiseDiagnosticsUpdated(updateArgs);

            diagnosticWaiter.CreateWaitTask().PumpingWait();

            var snapshot = buffer.CurrentSnapshot;
            var intervalTree = taggerSource.GetTagIntervalTreeForBuffer(buffer);
            var spans = intervalTree.GetIntersectingSpans(new SnapshotSpan(snapshot, 0, snapshot.Length));

            taggerSource.TestOnly_Dispose();

            return spans;
        }

        internal static DiagnosticData CreateDiagnosticData(TestWorkspace workspace, TestHostDocument document, TextSpan span)
        {
            return new DiagnosticData("test", "test", "test", "test", DiagnosticSeverity.Error, true, 0, workspace, document.Project.Id, document.Id, span);
        }

        private class TestDiagnosticUpdateSource : IDiagnosticUpdateSource
        {
            private ImmutableArray<DiagnosticData> diagnostics = ImmutableArray<DiagnosticData>.Empty;

            public void RaiseDiagnosticsUpdated(DiagnosticsUpdatedArgs args)
            {
                this.diagnostics = args.Diagnostics;
                DiagnosticsUpdated?.Invoke(this, args);
            }

            public event EventHandler<DiagnosticsUpdatedArgs> DiagnosticsUpdated;

            public bool SupportGetDiagnostics => false;

            public ImmutableArray<DiagnosticData> GetDiagnostics(Workspace workspace, ProjectId projectId, DocumentId documentId, object id, CancellationToken cancellationToken)
            {
                return diagnostics;
            }
        }

        private class DiagnosticServiceWaiter : AsynchronousOperationListener { }
    }
}
