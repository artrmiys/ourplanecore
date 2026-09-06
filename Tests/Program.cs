using Docnet.Core;
using Docnet.Core.Models;
using OurPlanCore;
using OurPlanCore.Controls;
using SkiaSharp;
using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;

if (Environment.GetEnvironmentVariable("ONC_BENCH") == "1")
    return RenderPerfBenchmark.Run();

if (args.Length > 0 && args[0] == "walltrace")
    return WallTraceHarness.Run(args);

if (args.Length > 0 && args[0] == "feedback-tests")
{
    UserFeedbackTests.BeamOffsetPreservesLengthAndOpposesCount();
    UserFeedbackTests.JoistNotePersistsThroughBothFormats();
    UserFeedbackTests.JoistNoteDragUndoCancelAndReadOnly();
    UserFeedbackTests.PreviewMatchesExportAndReactsToSettings();
    Console.WriteLine("4/4 feedback checks passed");
    return 0;
}

if (args.Length > 0 && args[0] == "feedback-ui-smoke")
    return FeedbackUiSmokeHarness.Run();

if (args.Length > 0 && args[0] == "data-safety-ui-smoke")
    return DataSafetyUiSmokeHarness.Run();

if (args.Length > 0 && args[0] == "clipboard-ui-smoke")
    return MeasurementClipboardUiSmokeHarness.Run(args);

if (args.Length > 0 && args[0] == "zoom-real-sample")
    return ViewportZoomSamplingTests.RunReal(args[1], args[2]);

if (args.Length > 0 && args[0] == "real-work-perf")
    return RealProjectPerformanceHarness.Run(args);

if (args.Length > 0 && args[0] == "shortcut-ui-smoke")
    return CustomShortcutUiSmokeHarness.Run(args);

if (args.Length > 0 && args[0] == "sheetmetadata-golden")
    return SheetMetadataGoldenHarness.Run(args);

if (args.Length > 0 && args[0] == "sheetmetadata-install-global")
    return SheetMetadataConfigHarness.InstallGlobalPrecise();

if (args.Length > 0 && args[0] == "sheetmetadata-install-ideal-global")
    return SheetMetadataConfigHarness.InstallGlobalIdeal();

if (args.Length > 0 && args[0] == "sheetmetadata-disable-import-analysis")
    return SheetMetadataConfigHarness.DisableAutomaticImportAnalysis();

if (args.Length > 0 && args[0] == "sheetmetadata-v3-benchmark")
    return SheetMetadataV3BenchmarkHarness.Run(args);

if (args.Length > 0 && args[0] == "runtime-smoke-job")
    return RuntimeSmokeJobHarness.Create();

if (args.Length > 0 && args[0] == "ourplan-runtime-smoke")
    return OurPlanPackageSmokeHarness.Create();

if (args.Length > 0 && args[0] == "storage-analysis")
    return ProjectStorageHarness.Run(args);

if (args.Length > 0 && args[0] == "storage-compact-smoke")
    return ProjectStorageHarness.RunCompactSmoke(args);

if (args.Length > 0 && args[0] == "excel-macro-smoke")
    return ExcelMacroSmokeHarness.Run(args);

if (args.Length > 0 && args[0] == "excel-walls-existing-smoke")
    return ExcelWallsExistingWorkbookSmokeHarness.Run(args);

if (args.Length > 0 && args[0] == "structural-excel-macro-smoke")
    return StructuralExcelMacroSmokeHarness.Run(args);

string testGlobalRoot = Path.Combine(Path.GetTempPath(), "onc_tests_global", Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable(SmartContextStore.GlobalRootEnvironmentVariable, testGlobalRoot);

var tests = new List<(string Name, Action Run)>
{
    ("repeat Line creates independent scaled segments", RepeatDrawingTests.RepeatLineCreatesIndependentScaledSegments),
    ("repeat stops on cancel tool change read-only and page close", RepeatDrawingTests.RepeatStopsOnCancelToolChangeReadOnlyAndPageClose),
    ("normal Line keeps polyline completion and scale gate", RepeatDrawingTests.NormalLineKeepsPolylineCompletionAndScaleGate),
    ("PDF preview plain wheel zooms around cursor", PdfPreviewInteractionTests.PlainWheelZoomsAroundCursor),
    ("PDF preview zoom anchor and free right drag", PdfPreviewInteractionTests.ZoomPreservesAnchorAndRightDragPansFreely),
    ("PDF preview live refresh preserves manual view", PdfPreviewInteractionTests.LiveFrameRefreshPreservesManualView),
    ("PDF preview fit resets pan and permits small page drag", PdfPreviewInteractionTests.FitResetsPanAndAllowsDraggingSmallPage),
    ("feedback Beam offset opposes Count and preserves length", UserFeedbackTests.BeamOffsetPreservesLengthAndOpposesCount),
    ("feedback Joist note persists in both formats", UserFeedbackTests.JoistNotePersistsThroughBothFormats),
    ("feedback Joist note drag undo cancel and read-only", UserFeedbackTests.JoistNoteDragUndoCancelAndReadOnly),
    ("feedback PDF preview matches export and reacts to settings", UserFeedbackTests.PreviewMatchesExportAndReactsToSettings),
    ("measurement count value and label", MeasurementCountValueAndLabel),
    ("measurement line uses own scale first", MeasurementLineUsesOwnScaleFirst),
    ("measurement area uses fallback scale", MeasurementAreaUsesFallbackScale),
    ("measurement area subtracts holes", MeasurementAreaSubtractsHoles),
    ("area cut inside keeps hole behavior", AreaCutInsideKeepsHoleBehavior),
    ("area cut second separate hole adds another hole", AreaCutSecondSeparateHoleAddsAnotherHole),
    ("area cut overlapping existing hole merges", AreaCutOverlappingExistingHoleMerges),
    ("area cut enclosing existing hole merges", AreaCutEnclosingExistingHoleMerges),
    ("area cut concave interior hole is stored", AreaCutConcaveInteriorHoleIsStored),
    ("area cut concave through cut splits", AreaCutConcaveThroughCutSplits),
    ("area cut box through two holes merges", AreaCutBoxThroughTwoHolesMerges),
    ("area cut concave through two holes", AreaCutConcaveThroughTwoHoles),
    ("area cut box clips at area edge", AreaCutBoxClipsAtAreaEdge),
    ("area cut through area splits into segments", AreaCutThroughAreaSplitsIntoSegments),
    ("area combine union merges overlapping areas", AreaCombineUnionMergesOverlappingAreas),
    ("area combine subtract cuts later areas from first", AreaCombineSubtractCutsLaterAreasFromFirst),
    ("area combine intersect keeps only overlap", AreaCombineIntersectKeepsOnlyOverlap),
    ("area combine intersect rejects disjoint areas", AreaCombineIntersectRejectsDisjointAreas),
    ("area combine remove overlap trims later areas", AreaCombineRemoveOverlapTrimsLaterAreas),
    ("area combine divide splits into exclusive and shared", AreaCombineDivideSplitsIntoExclusiveAndShared),
    ("area combine rejects mixed pages", AreaCombineRejectsMixedPages),
    ("area combine allows differing stored scales on one page", AreaCombineAllowsDifferingStoredScales),
    ("takeoff wall sort orders categories then sizes", TakeoffWallSortOrdersCategoriesThenSizes),
    ("takeoff detail sort groups by sheet", TakeoffDetailSortGroupsBySheet),
    ("pdf export area path cuts holes", PdfExportAreaPathCutsHoles),
    ("pdf export always uses white paper", PdfExportAlwaysUsesWhitePaper),
    ("output settings default export appearance", OutputSettingsDefaultExportAppearance),
    ("pdf export Extra Joist glow honors visibility and intensity", PdfExportExtraJoistGlowHonorsVisibilityAndIntensity),
    ("pdf export label categories work without all", PdfExportLabelCategoriesWorkWithoutAll),
    ("pdf export joist summary ignores area label toggle", PdfExportJoistSummaryIgnoresAreaLabelToggle),
    ("pdf export writes selected sheets", PdfExportWritesSelectedSheets),
    ("pdf export writes measurement lines", PdfExportWritesMeasurementLines),
    ("pdf export skips invalid area point artifacts", PdfExportSkipsInvalidAreaPointArtifacts),
    ("pdf export defaults measurements on for measured sheets", TakeoffsTreeRegressionTests.PdfExportDefaultsMeasurementsOnForMeasuredSheets),
    ("roof rafter gable layout matches textbook numbers", ThreeDRoofRafterServiceTests.GableFaceLayoutMatchesTextbookNumbers),
    ("roof rafter flat face produces none", ThreeDRoofRafterServiceTests.FlatFaceProducesNoRafters),
    ("roof rafter face anchors survive rebuilds", ThreeDRoofRafterServiceTests.FaceAnchorsResolveAcrossRebuilds),
    ("similar symbol matcher finds all plain copies", SimilarSymbolMatcherTests.FindsAllPlainCopies),
    ("similar symbol matcher honors rotation toggle", SimilarSymbolMatcherTests.FindsRotatedCopyOnlyWhenEnabled),
    ("similar symbol matcher finds slightly scaled copy", SimilarSymbolMatcherTests.FindsSlightlyScaledCopy),
    ("similar matcher adaptive ink model separates faint and colored ink", SimilarSymbolMatcherTests.AdaptiveInkModelSeparatesFaintAndColoredInk),
    ("similar symbol matcher finds faint gray copies", SimilarSymbolMatcherTests.FindsFaintGraySymbolCopies),
    ("similar symbol matcher rejects distractors", SimilarSymbolMatcherTests.RejectsDistractorsAndDedupes),
    ("similar symbol matcher tolerates loose edge selection", SimilarSymbolMatcherTests.FindsCopiesWithLooseEdgeSelection),
    ("similar symbol matcher trims peripheral selection noise", SimilarSymbolMatcherTests.TrimsPeripheralSelectionNoiseBeforeMatching),
    ("similar symbol matcher trims long peripheral line selection noise", SimilarSymbolMatcherTests.TrimsLongPeripheralLineSelectionNoise),
    ("similar symbol matcher tightens loose whitespace selection", SimilarSymbolMatcherTests.LooseWhitespaceSelectionKeepsCentersPrecise),
    ("similar matcher warns on multi-symbol template", SimilarSymbolMatcherTests.WarnsOnMultiSymbolTemplate),
    ("similar matcher keeps loose whitespace template full-resolution", SimilarSymbolMatcherTests.KeepsLooseWhitespaceTemplateFullResolution),
    ("similar matcher ignores peripheral noise for template resolution", SimilarSymbolMatcherTests.KeepsPeripheralNoiseFromDownsamplingTemplate),
    ("similar matcher warns on downsampled large template", SimilarSymbolMatcherTests.WarnsOnDownsampledLargeTemplate),
    ("similar symbol matcher can include mirrored copies", SimilarSymbolMatcherTests.FindsMirroredCopyOnlyWhenEnabled),
    ("similar symbol matcher matches another sheet bitmap", SimilarSymbolMatcherTests.FindsMatchesOnAnotherSheetBitmap),
    ("similar matcher text-guided centers use other sheet bitmap", SimilarSymbolMatcherTests.TextGuidedNearCentersUseOtherSheetBitmap),
    ("similar count searches all sheets", SimilarSymbolMatcherTests.SimilarCountSearchesAllSheets),
    ("similar symbol matcher rejects near misses at precision threshold", SimilarSymbolMatcherTests.RejectsNearMissAtPrecisionThreshold),
    ("similar symbol matcher scores stroke orientation", SimilarSymbolMatcherTests.ScoresStrokeOrientationForNearMiss),
    ("similar symbol matcher rejects core-only relaxed near miss", SimilarSymbolMatcherTests.RejectsCoreOnlyRelaxedNearMissAtPrecisionThreshold),
    ("similar symbol matcher rejects extra interior marks", SimilarSymbolMatcherTests.RejectsSymbolWithExtraInteriorMark),
    ("similar symbol matcher rejects dense surrounding ink", SimilarSymbolMatcherTests.RejectsSymbolEmbeddedInDenseSurroundingInk),
    ("similar symbol matcher rejects heavy disconnected window ink", SimilarSymbolMatcherTests.RejectsSymbolWithHeavyDisconnectedWindowInk),
    ("similar count defaults favor precision", SimilarSymbolMatcherTests.DefaultSettingsFavorPrecision),
    ("similar matcher uses fine symbol profile", SimilarSymbolMatcherTests.SimilarMatcherUsesFineSymbolProfile),
    ("similar matcher keeps hits near adjacent plan ink", SimilarSymbolMatcherTests.KeepsHitsNearAdjacentPlanInk),
    ("similar matcher keeps hits with disconnected window ink", SimilarSymbolMatcherTests.KeepsHitsWithDisconnectedWindowInk),
    ("similar count waits for readable bitmap", SimilarSymbolMatcherTests.ViewportRequiresReadableBitmapBeforeSimilarCount),
    ("similar count raster sheets use reachable dpi cap", SimilarSymbolMatcherTests.SimilarCountRasterSheetsUseReachableDpiCap),
    ("similar matcher caps large search raster", SimilarSymbolMatcherTests.SimilarMatcherCapsLargeSearchRaster),
    ("similar matcher finds far copies on downsampled large raster", SimilarSymbolMatcherTests.FindsFarCopiesOnDownsampledLargeRaster),
    ("similar matcher verifies text candidate windows by raster", SimilarSymbolMatcherTests.VerifiesTextCandidateWindowsByRaster),
    ("similar matcher recovers multiple symbols per text label", SimilarSymbolMatcherTests.TextGuidedRasterCanRecoverMultipleSymbolsPerLabel),
    ("similar count exact text radius covers nearby repeated symbols", SimilarSymbolMatcherTests.ExactTextGuidedRadiusCoversNearbyRepeatedSymbols),
    ("similar count nearby text guide stays raster only", SimilarSymbolMatcherTests.NearbyTextGuideDoesNotBecomeTextOnlyMarkers),
    ("similar count all sheets keeps manual text offset", SimilarSymbolMatcherTests.AllSheetsTextGuideKeepsManualTemplateOffset),
    ("viewport status uses ui dispatcher", SimilarSymbolMatcherTests.ViewportStatusUsesUiDispatcher),
    ("similar count preview supports review before add", SimilarSymbolMatcherTests.SimilarCountPreviewSupportsReviewBeforeAdd),
    ("similar count review choices survive rescan", SimilarSymbolMatcherTests.SimilarCountReviewChoicesSurviveRescan),
    ("similar count ignores cancelled stale scans", SimilarSymbolMatcherTests.SimilarCountIgnoresCancelledStaleScans),
    ("similar count preview shows confidence", SimilarSymbolMatcherTests.SimilarCountPreviewShowsConfidence),
    ("similar count review summary explains candidates", SimilarSymbolMatcherTests.SimilarCountReviewSummaryExplainsCandidates),
    ("similar count threshold presets are available", SimilarSymbolMatcherTests.SimilarCountThresholdPresetsAreAvailable),
    ("similar count weak matches start review only", SimilarSymbolMatcherTests.SimilarCountWeakMatchesStartReviewOnly),
    ("similar count skips already counted markers", SimilarSymbolMatcherTests.SimilarCountSkipsAlreadyCountedMarkers),
    ("similar count locks destination takeoff", SimilarSymbolMatcherTests.SimilarCountLocksDestinationTakeoff),
    ("similar count handles switched sheet add status", SimilarSymbolMatcherTests.SimilarCountHandlesSwitchedSheetAddStatus),
    ("similar count beam openings completion can launch review", SimilarSymbolMatcherTests.BeamOpeningsCompletionCanLaunchSimilarReview),
    ("similar text query finds split mark tokens", SimilarSymbolMatcherTests.SimilarTextQueryFindsSplitMarkTokens),
    ("similar count uses exact pdf text when available", SimilarSymbolMatcherTests.SimilarCountUsesExactPdfTextWhenAvailable),
    ("similar count is exposed as context tool", SimilarSymbolMatcherTests.SimilarCountIsExposedAsContextTool),
    ("sheet legend long text expands available width", SheetOverlayRendererTests.LongTextExpandsWhenSpaceExists),
    ("sheet legend auto width clamps to visible area", SheetOverlayRendererTests.LongTextClampsToVisibleWidth),
    ("sheet legend columns measure their own content", SheetOverlayRendererTests.MultipleColumnsMeasureTheirOwnContent),
    ("sheet legend dense labels prefer readable columns", SheetOverlayRendererTests.DenseLongEntriesPreferFewerReadableColumns),
    ("sheet legend dense rows stay clipped to bounds", SheetOverlayRendererTests.DenseEntriesStayClippedToBounds),
    ("sheet legend hit bounds match rendered layout", SheetOverlayRendererTests.HitBoundsMatchRenderedLayout),
    ("sheet overlay auto fit recovers scale and offset", SheetOverlayAutoFitServiceTests.RecoversScaleAndOffsetFromRepeatedPlanGeometry),
    ("sheet overlay auto fit recovers rotation", SheetOverlayAutoFitServiceTests.RecoversRotationFromRepeatedPlanGeometry),
    ("sheet overlay auto fit recovers junction shape points", SheetOverlayAutoFitServiceTests.RecoversRotationFromJunctionShapeWithoutSegments),
    ("sheet overlay auto fit rejects sparse geometry", SheetOverlayAutoFitServiceTests.RejectsSparseGeometry),
    ("sheet overlay auto fit auto-selects matching sheet", SheetOverlayAutoFitServiceTests.AutoSelectsBestCandidateByShapeFit),
    ("sheet overlay auto fit auto-select tie uses sheet rank", SheetOverlayAutoFitServiceTests.AutoSelectPrefersCloserSheetWhenScoresTie),
    ("sheet overlay auto fit auto-select reports alternatives", SheetOverlayAutoFitServiceTests.AutoSelectReportsRankedAlternatives),
    ("sheet overlay auto fit review ranks weak candidates", SheetOverlayAutoFitServiceTests.ReviewCandidatesCanIncludeWeakGeometryWithoutAutoSelecting),
    ("sheet overlay auto fit next candidate cycles alternatives", SheetOverlayAutoFitServiceTests.AutoSelectNextCandidateCyclesRankedAlternatives),
    ("sheet overlay raster features recover scale and offset", SheetOverlayRasterFeatureServiceTests.RasterFeaturesRecoverScaleAndOffset),
    ("sheet overlay raster features extract junction points", SheetOverlayRasterFeatureServiceTests.RasterFeaturesExtractJunctionPoints),
    ("sheet overlay reciprocal transform round trips", SheetOverlayReciprocalServiceTests.InvertsTransformRoundTrip),
    ("sheet overlay reciprocal skips unrelated targets", SheetOverlayReciprocalServiceTests.WritesOnlyEmptyOrExistingReciprocalTargets),
    ("sheet overlay reciprocal writes source json", SheetOverlayReciprocalServiceTests.SyncWritesAndClearsReciprocalSource),
    ("sheet overlay adjustment menus are exposed", TakeoffsTreeRegressionTests.SheetOverlayAdjustmentMenusAreExposed),
    ("sheet overlay properties expose source transform and color", SheetOverlayPropertiesRegressionTests.SheetOverlayPropertiesPanelExposesSourceAlignmentTransformAndAppearance),
    ("sheet overlay context menus stay compact", SheetOverlayPropertiesRegressionTests.SheetOverlayContextMenusStayCompactAndRouteAdvancedActionsToProperties),
    ("sheet overlay transform preview persists once", SheetOverlayPropertiesRegressionTests.SheetOverlayTransformPreviewPersistsOnceOnCommit),
    ("sheet overlay active frame follows rotated corners", SheetOverlayPropertiesRegressionTests.SheetOverlayActiveFrameUsesRotatedOverlayCorners),
    ("sheet overlay move and undo use one validated viewport action", SheetOverlayPropertiesRegressionTests.SheetOverlayMoveAndUndoUseOneValidatedViewportAction),
    ("sheet overlay live transform bypasses static frame cache", SheetOverlayPropertiesRegressionTests.SheetOverlayLiveTransformBypassesStaticFrameCache),
    ("sheet overlay live preview survives bitmap replacement", SheetOverlayPropertiesRegressionTests.SheetOverlayLivePreviewSurvivesQualityBitmapReplacement),
    ("sheet overlay auto fit uses undoable transform gateway", SheetOverlayPropertiesRegressionTests.SheetOverlayAutoFitUsesUndoableTransformGateway),
    ("sheet overlay point fit waits for exact binding", SheetOverlayPropertiesRegressionTests.SheetOverlayPointFitWaitsForExactOverlayBinding),
    ("sheet overlay quality refresh failure keeps current bitmap", SheetOverlayPropertiesRegressionTests.SheetOverlayQualityRefreshFailureKeepsCurrentBitmap),
    ("sheet overlay frame honors read-only page and module lifecycle", SheetOverlayPropertiesRegressionTests.SheetOverlayFrameHonorsReadOnlyPageAndModuleLifecycle),
    ("sheet overlay layers share viewport export tree and detached rendering", SheetOverlayPropertiesRegressionTests.SheetOverlayLayersShareViewportExportTreeAndDetachedRendering),
    ("sheet overlay transform shortcuts are wired", TakeoffsTreeRegressionTests.SheetOverlayTransformShortcutsAreWired),
    ("sheet overlay transform dialog has fine adjustments", TakeoffsTreeRegressionTests.SheetOverlayTransformDialogHasFineAdjustments),
    ("sheet overlay mouse drag is wired", TakeoffsTreeRegressionTests.SheetOverlayMouseDragIsWired),
    ("sheet overlay point edit uses pdf snap", TakeoffsTreeRegressionTests.SheetOverlayPointEditUsesPdfSnap),
    ("sheet overlay async load uses fresh page snapshot", TakeoffsTreeRegressionTests.SheetOverlayAsyncLoadUsesFreshPageSnapshot),
    ("sheet overlay cache and paint policy are bounded", SheetOverlayPerformanceRegressionTests.CacheAndPaintPolicyAreBounded),
    ("project storage analysis classifies references duplicates raster and recovery", ProjectStorageAnalyzerTests.AnalysisClassifiesReferencesDuplicatesRasterAndRecovery),
    ("project storage analysis is read only for malformed metadata and snap json", ProjectStorageAnalyzerTests.AnalysisIsReadOnlyForMalformedMetadataAndSnapJson),
    ("project storage analysis protects every referenced exact duplicate", ProjectStorageAnalyzerTests.AnalysisProtectsEveryReferencedExactDuplicate),
    ("project storage analysis handles valid non object reference metadata", ProjectStorageAnalyzerTests.AnalysisHandlesValidNonObjectReferenceMetadata),
    ("project storage analysis reports external page dependencies", ProjectStorageAnalyzerTests.AnalysisReportsExternalPageDependencies),
    ("project storage compact preview is read only and targets raster snap json", ProjectStorageCompactorTests.PreviewIsReadOnlyAndTargetsOnlyRasterSnapJson),
    ("project storage compact preserves json semantics and reports savings", ProjectStorageCompactorTests.CompactPreservesJsonSemanticsAndReportsSavings),
    ("project storage compact skips invalid or changed json", ProjectStorageCompactorTests.InvalidOrChangedJsonIsSkippedWithoutMutation),
    ("project storage compact guards cancellation path races and reparse points", ProjectStorageCompactorTests.CancellationAndPathRaceGuardsAreWired),
    ("new projects default to ourplan format", OurPlanPackageTests.NewProjectsDefaultToOurPlanFormat),
    ("ourplan package round trip preserves durable data and deduplicates objects", OurPlanPackageTests.RoundTripPreservesDurableDataAndDeduplicatesObjects),
    ("ourplan package compresses snap json without changing bytes", OurPlanPackageTests.PackageCompressionShrinksLargeSnapJsonWithoutChangingIt),
    ("ourplan package rejects traversal paths", OurPlanPackageTests.PackageRejectsTraversalPaths),
    ("ourplan package rejects object hash mismatch", OurPlanPackageTests.PackageRejectsObjectHashMismatch),
    ("ourplan package detects external revision conflict", OurPlanPackageTests.PackageSaveDetectsExternalRevisionConflict),
    ("ourplan package unchanged save does not rewrite file", OurPlanPackageTests.UnchangedSaveDoesNotRewritePackage),
    ("ourplan package same metadata content change", OurPlanPackageTests.SameSizeAndTimestampChangeIsNeverSkipped),
    ("ourplan package failed save leaves previous file byte exact", OurPlanPackageTests.FailedPackageSaveLeavesPreviousFileByteExact),
    ("ourplan package rejects excluded active pdf", OurPlanPackageTests.PackageRejectsActivePdfInsideExcludedData),
    ("ourplan stale clean workspace re-extracts newer revision", OurPlanPackageTests.StaleCleanWorkspaceIsReExtractedAfterExternalRevision),
    ("ourplan dirty closed workspace is advertised without implicit recovery", OurPlanPackageTests.DirtyClosedWorkspaceIsAdvertisedButPackageOpensByDefault),
    ("ourplan copied package uses separate exact-path workspace", OurPlanPackageTests.SameRevisionPackageCopyUsesSeparateWorkspace),
    ("ourplan active workspace claim prevents concurrent reuse", OurPlanPackageTests.ActiveWorkspaceClaimPreventsConcurrentReuse),
    ("ourplan prune cannot race exclusive workspace claim", OurPlanPackageTests.PruneCannotRaceAnExclusiveWorkspaceClaim),
    ("ourplan corrupt or missing package opens preserved recovery", OurPlanPackageTests.CorruptAndMissingPackageCanOpenPreservedRecovery),
    ("ourplan managed legacy copy isolates original", OurPlanPackageTests.ManagedLegacyCopyDoesNotMutateOriginalJob),
    ("ourplan package legacy copy is loadable and excludes ephemeral files", OurPlanPackageTests.LegacyFolderCopyIsLoadableAndExcludesEphemeralFiles),
    ("moved and renamed ourplan package opens edits saves and reopens", OurPlanPackageTests.MovedAndRenamedPackageCanOpenEditSaveAndReopen),
    ("legacy folder copy rebases references after source removal", OurPlanPackageTests.LegacyFolderCopyRebasesReferencesAndSurvivesSourceRemoval),
    ("ourplan replace retry recognizes only safe win32 failures", OurPlanPackageTests.ReplaceRetryRecognizesOnlySafeWin32Failures),
    ("ourplan replace retry waits for a real temporary destination lock", OurPlanPackageTests.ReplaceRetryWaitsForRealTemporaryDestinationLock),
    ("ourplan replace retry stops on conflict and has bounded budget", OurPlanPackageTests.ReplaceRetryStopsOnConflictAndHasBoundedBudget),
    ("ourplan persistent replace failure routes through overwrite fallback", OurPlanPackageTests.PersistentReplaceFailureRoutesThroughOverwriteFallback),
    ("ourplan package read handle allows atomic replacement", OurPlanPackageTests.PackageReadHandleAllowsAtomicReplacement),
    ("ourplan guarded overwrite fallback publishes and preserves rollback", OurPlanPackageTests.GuardedOverwriteFallbackPublishesAndPreservesRollback),
    ("ourplan guarded overwrite fallback keeps target visible on failure", OurPlanPackageTests.GuardedOverwriteFallbackNeverRemovesTargetBeforePublish),
    ("ourplan autosave coalesces events and caps dirty age", OurPlanPackageAutosaveTests.ScheduleCoalescesEventsAndCapsDirtyAge),
    ("ourplan autosave limits checkpoints and retries promptly", OurPlanPackageAutosaveTests.ScheduleLimitsSuccessfulCheckpointFrequencyButRetriesPromptly),
    ("ourplan autosave maximum dirty age overrides checkpoint interval", OurPlanPackageAutosaveTests.MaximumDirtyAgeOverridesRecentCheckpointInterval),
    ("ourplan autosave retries only explicit transient failures", OurPlanPackageAutosaveTests.FailurePolicyRetriesOnlyExplicitTransientErrors),
    ("ourplan recovery session promotes after same-path save", OurPlanPackageAutosaveTests.SuccessfulSamePathSavePromotesRecoverySession),
    ("ourplan autosave sees active package checkpoints", OurPlanPackageAutosaveTests.PackageCheckpointActivityIsVisibleToAutosavePreflight),
    ("ourplan workspace changes wire to silent same-path autosave", OurPlanPackageAutosaveTests.WorkspaceChangesWireToSilentSamePathAutosave),
    ("ourplan prior rollback survives final validation and base commit", OurPlanPackageAutosaveTests.PriorPackageRollbackSurvivesValidationAndBaseCommit),
    ("open import uses one picker for both storage types", UnifiedProjectFormatRegressionTests.OpenImportUsesOneProjectPickerForBothStorageTypes),
    ("all new project entry points create package projects", UnifiedProjectFormatRegressionTests.EveryNewProjectEntryPointAlwaysUsesPackageCreation),
    ("pdf takeoff preview matches exact package creation path", UnifiedProjectFormatRegressionTests.PdfTakeoffPreviewMatchesExactCreatedPackagePath),
    ("project storage settings exposes no format selector", UnifiedProjectFormatRegressionTests.ProjectStorageSettingsShowsOneFormatWithoutSelector),
    ("ourplan workspace watcher ignores only control atomic temps", OurPlanPackageHardeningTests.WorkspaceWatcherIgnoresOnlyControlAtomicTemps),
    ("ourplan portable 3d annotations and pdf metadata round trip", OurPlanPackageHardeningTests.PortableThreeDAnnotationsAndPdfMetadataRoundTrip),
    ("ourplan opaque provider ids remain portable", OurPlanPackageHardeningTests.AiOpaqueProviderIdsRemainPortable),
    ("ourplan portable project open stays clean", OurPlanPackageHardeningTests.OpeningPortableProjectDoesNotDirtyTheProject),
    ("ourplan unsafe ai ids and embedded paths are rejected", OurPlanPackageHardeningTests.UnsafeAiIdsAndEmbeddedPathsAreRejected),
    ("ourplan invalid overwrite publishes successfully", OurPlanPackageHardeningTests.OverwritingInvalidTargetPublishesSuccessfully),
    ("ourplan corrupt object overwrite publishes successfully", OurPlanPackageHardeningTests.OverwritingPackageWithCorruptObjectPublishesSuccessfully),
    ("ourplan local publish staging is target scoped", OurPlanPackageHardeningTests.LocalPublishStagingIsScopedByFullTargetPath),
    ("ourplan explicit recovery keeps partial json", OurPlanPackageHardeningTests.ExplicitRecoveryKeepsWorkspaceWithPartialJson),
    ("ourplan recovery rejects external references", OurPlanPackageHardeningTests.RecoveryStillRejectsParseableExternalReferences),
    ("ourplan crash rollback is retained", OurPlanPackageHardeningTests.CrashRollbackIsNeverAutoDeleted),
    ("ourplan provenance does not leak workspace paths", OurPlanPackageHardeningTests.PortableProvenanceDoesNotLeakWorkspacePaths),
    ("ourplan background writes stay with current project", OurPlanPackageHardeningTests.BackgroundWritesStayBoundToCurrentProject),
    ("ourplan legacy pages stay portable and contained", OurPlanPackageHardeningTests.LegacyPageFoldersStayPortableAndContained),
    ("ourplan opaque json attachments remain byte exact", OurPlanPackageSemanticScopeTests.OpaqueJsonAttachmentsRemainByteExact),
    ("ourplan malformed authoritative stores are rejected", OurPlanPackageSemanticScopeTests.MalformedAuthoritativeStoresAreRejected),
    ("ourplan structured data size limits reject manifest before extraction", OurPlanPackageSemanticScopeTests.StructuredDataSizeLimitsRejectManifestBeforeExtraction),
    ("ourplan archive quotas are high but finite", OurPlanPackageArchiveQuotaTests.LimitsAreHighButFinite),
    ("ourplan archive rejects oversized declared objects and totals", OurPlanPackageArchiveQuotaTests.DeclaredObjectAndTotalSizesAreRejected),
    ("ourplan archive rejects oversized compressed manifest", OurPlanPackageArchiveQuotaTests.OversizedCompressedManifestIsRejectedBeforeJsonRead),
    ("sheet overlay reciprocal cleanup is wired", TakeoffsTreeRegressionTests.SheetOverlayReciprocalCleanupIsWired),
    ("sheet overlay auto fit can auto-select overlay", TakeoffsTreeRegressionTests.SheetOverlayAutoFitCanAutoSelectOverlay),
    ("sheet overlay auto fit raster fallback is wired", TakeoffsTreeRegressionTests.SheetOverlayAutoFitRasterFallbackIsWired),
    ("job store persists measurement holes", JobStorePersistsMeasurementHoles),
    ("marquee selects cutout without enclosing Area", CutRegionSelectionRegressionTests.EnclosingMarqueeFindsCutoutWithoutSelectingParentBoundary),
    ("mixed transform keeps cutout and takeoff geometry together", CutRegionSelectionRegressionTests.MixedTransformKeepsRelativeGeometryAndLeavesParentContourFixed),
    ("mixed rotation uses one pivot without moving parent Area", CutRegionSelectionRegressionTests.MixedRotateUsesOnePivotAndLeavesParentContourFixed),
    ("mixed mirrors use one pivot without moving parent Area", CutRegionSelectionRegressionTests.MixedMirrorsUseOnePivotAndLeaveParentContourFixed),
    ("mixed scale uses one pivot without moving parent Area", CutRegionSelectionRegressionTests.MixedScaleUsesOnePivotAndLeavesParentContourFixed),
    ("cutout paste keeps source parent only while contained", CutRegionSelectionRegressionTests.PasteKeepsSourceParentOnlyWhileCutoutStillFits),
    ("ambiguous cutout paste does not mutate an Area", CutRegionSelectionRegressionTests.AmbiguousPasteDoesNotChooseOrMutateAnArea),
    ("pasted overlay Area is excluded as cutout parent", CutRegionSelectionRegressionTests.PastedOverlayAreaIsExcludedFromBaseAreaResolution),
    ("ambiguous mixed bundle preflight has zero mutations", CutRegionSelectionRegressionTests.AmbiguousBundlePreflightHasZeroMutations),
    ("one failed cutout cancels the whole bundle", CutRegionSelectionRegressionTests.OneCutoutFailureCancelsTheEntireBundle),
    ("explicit Area resolves an ambiguous cutout bundle", CutRegionSelectionRegressionTests.ExplicitSelectedAreaResolvesTheWholeAmbiguousBundle),
    ("concave Area rejects a cutout edge that exits and re-enters", CutRegionSelectionRegressionTests.ConcaveBoundaryRejectsAnEdgeThatLeavesAndReenters),
    ("mixed paste preflight precedes measurement mutation", CutRegionSelectionRegressionTests.MixedPastePreflightRunsBeforeAnyMeasurementMutation),
    ("mixed paste rolls back every uncommitted mutation", CutRegionSelectionRegressionTests.MixedPasteRollsBackEveryUncommittedMutation),
    ("measurement area joist without direction is blocked", MeasurementJoistWithoutDirectionIsBlocked),
    ("area line grid creates horizontal and vertical segments", AreaLineGridServiceTests.RectangleCreatesHorizontalAndVerticalSegments),
    ("area line grid holes split segments", AreaLineGridServiceTests.HolesSplitGridSegments),
    ("wall trace parallel pair yields single centerline", WallCenterlineTracerTests.ParallelPairYieldsSingleCenterline),
    ("wall trace clips faces to the selection area", WallCenterlineTracerTests.FacesOutsideAreaAreIgnored),
    ("wall trace rejects faces outside thickness window", WallCenterlineTracerTests.TooFarOrTooCloseFacesAreRejected),
    ("wall trace ignores perpendicular lines", WallCenterlineTracerTests.PerpendicularLinesDoNotPair),
    ("wall trace merges broken face segments", WallCenterlineTracerTests.BrokenFaceSegmentsMergeIntoOneWall),
    ("wall trace chains L-shaped walls at the corner", WallCenterlineTracerTests.LShapedWallsChainAtTheCorner),
    ("wall trace skips walls inside area holes", WallCenterlineTracerTests.WallsInsideAreaHoleAreSkipped),
    ("wall trace ignores faces inside text zones", WallCenterlineTracerTests.FacesInsideExcludedTextZoneAreIgnored),
    ("wall trace triple-line wall yields one centerline", WallCenterlineTracerTests.TripleFaceWallYieldsOneCenterline),
    ("wall trace corner join lands on line intersection", WallCenterlineTracerTests.CornerJoinLandsOnLineIntersection),
    ("wall trace drops rare-angle short noise", WallCenterlineTracerTests.RareAngleShortNoiseIsDropped),
    ("wall trace fill zones keep only filled walls", WallCenterlineTracerTests.FillZonesKeepOnlyFilledWalls),
    ("wall trace off-center fill strip confirms thick wall", WallCenterlineTracerTests.OffCenterFillStripStillConfirmsThickWall),
    ("wall trace dark-fill-only drops light partitions", WallCenterlineTracerTests.DarkFillOnlyDropsLightPartitions),
    ("wall trace dark cutoff adapts to sheet luminances", WallCenterlineTracerTests.DarkFillCutoffAdaptsToSheetLuminances),
    ("wall trace boundary walls excluded by tolerance", WallCenterlineTracerTests.BoundaryWallsAreExcludedByTolerance),
    ("wall trace face crossing text zone keeps full length", WallCenterlineTracerTests.WallFaceCrossingTextZoneKeepsFullLength),
    ("wall trace raster fallback after empty pdf pairs is wired", TakeoffsTreeRegressionTests.WallTraceRasterFallbackAfterEmptyPdfPairsIsWired),
    ("wall trace raster line features yield centerline", WallCenterlineTracerTests.RasterLineFeaturesYieldCenterline),
    ("point along line creates endpoint and step points", PointAlongLineServiceTests.StraightLineCreatesEndpointAndStepPoints),
    ("point along line carries spacing across vertices", PointAlongLineServiceTests.PolylineCarriesSpacingAcrossVertices),
    ("point along line many lines avoid duplicate shared endpoint", PointAlongLineServiceTests.ManyLinesAvoidDuplicateSharedEndpoint),
    ("point along line rejects missing scale", PointAlongLineServiceTests.MissingScaleIsRejected),
    ("takeoff item normalizes count type totals", TakeoffItemNormalizesCountTotals),
    ("measurement merge moves segment into target takeoff", MeasurementMergeMovesSegmentIntoTargetTakeoff),
    ("measurement merge rejects mixed target type", MeasurementMergeRejectsMixedTargetType),
    ("measurement merge coalesces touching line sections", MeasurementMergeCoalescesTouchingLineSections),
    ("measurement merge keeps separated line sections", MeasurementMergeKeepsSeparatedLineSections),
    ("measurement merge splices overlapping area sections", MeasurementMergeSplicesOverlappingAreaSections),
    ("measurement merge keeps separated area sections", MeasurementMergeKeepsSeparatedAreaSections),
    ("point split whole move preserves identity and metadata", PointMeasurementSplitServiceTests.WholeSinglePointMovePreservesIdentityAndMetadata),
    ("point split whole multi-point section preserves grouping", PointMeasurementSplitServiceTests.WholeMultiPointWithoutVertexMapMovesEntireSection),
    ("point split stale marker map cannot expand to whole move", PointMeasurementSplitServiceTests.InvalidExplicitVertexMapDoesNotExpandToWholeMove),
    ("point split partial partition preserves order and metadata", PointMeasurementSplitServiceTests.PartialThreePointPartitionPreservesOrderTotalAndMetadata),
    ("point split all selected points moves original", PointMeasurementSplitServiceTests.AllSelectedPointsMoveOriginalMeasurement),
    ("point split vertex subset takes precedence", PointMeasurementSplitServiceTests.VertexSubsetTakesPrecedenceOverWholeObjectSelection),
    ("point split multi-source updates every owner", PointMeasurementSplitServiceTests.MultiSourceVertexSplitUpdatesEveryOwner),
    ("point split partial save reload preserves both owners", PointMeasurementSplitServiceTests.PartialSplitPersistsSourceAndTargetRoundTrip),
    ("point split UI captures markers and stops Record", PointMeasurementSplitServiceTests.MainWindowPointSplitWiringCapturesMarkersAndStopsRecordBeforeMutation),
    ("beam length rounds up below and above eight feet", BeamLengthRoundsUpBelowAndAboveEightFeet),
    ("beam default name keeps size suffix outside selection", BeamDefaultNameKeepsSizeSuffixOutsideSelection),
    ("beam annotation defaults stay off and red", BeamAnnotationConfigTests.DefaultsStayOffAndRedUntilEnabled),
    ("beam dialog and Settings wire companion line", BeamAnnotationConfigTests.BeamDialogAndSettingsWireTheCompanionLine),
    ("opening size formats one decimal", OpeningSizeFormatsOneDecimal),
    ("opening default name is size only", OpeningDefaultNameIsSizeOnly),
    ("takeoff creation policy chooses safe parents", TakeoffCreationPolicyChoosesSafeParents),
    ("takeoff section order moves single up", TakeoffSectionOrderMovesSingleUp),
    ("takeoff section order moves single down", TakeoffSectionOrderMovesSingleDown),
    ("takeoff section order blocks top up", TakeoffSectionOrderBlocksTopUp),
    ("takeoff section order blocks bottom down", TakeoffSectionOrderBlocksBottomDown),
    ("takeoff section order blocks all selected", TakeoffSectionOrderBlocksAllSelected),
    ("takeoff section order moves contiguous block up", TakeoffSectionOrderMovesContiguousBlockUp),
    ("takeoff section order moves contiguous block down", TakeoffSectionOrderMovesContiguousBlockDown),
    ("takeoff section order moves disjoint selection up", TakeoffSectionOrderMovesDisjointSelectionUp),
    ("takeoff section order moves disjoint selection down", TakeoffSectionOrderMovesDisjointSelectionDown),
    ("takeoff section order ignores invalid duplicate ids", TakeoffSectionOrderIgnoresInvalidDuplicateIds),
    ("takeoff tree order keeps creation order", TakeoffTreeOrderKeepsCreationOrder),
    ("takeoff tree order moves sibling up", TakeoffTreeOrderMovesSiblingUp),
    ("takeoff tree order moves sibling down", TakeoffTreeOrderMovesSiblingDown),
    ("takeoff tree order blocks top up", TakeoffTreeOrderBlocksTopUp),
    ("takeoff tree order blocks bottom down", TakeoffTreeOrderBlocksBottomDown),
    ("takeoff tree order moves sibling block up", TakeoffTreeOrderMovesSiblingBlockUp),
    ("takeoff tree order moves sibling block down", TakeoffTreeOrderMovesSiblingBlockDown),
    ("takeoff tree order moves before target", TakeoffTreeOrderMovesBeforeTarget),
    ("takeoff tree order moves after target", TakeoffTreeOrderMovesAfterTarget),
    ("takeoff tree order appends moved node into folder", TakeoffTreeOrderAppendsMovedNodeIntoFolder),
    ("takeoff tree order moves direct children out below folder", TakeoffTreeOrderMovesDirectChildrenOutBelowFolder),
    ("takeoff tree order stress moves large selection to end", TakeoffTreeOrderStressMovesLargeSelectionToEnd),
    ("takeoff tree order stress batch moves into folder", TakeoffTreeOrderStressBatchMovesIntoFolder),
    ("takeoff create allows duplicate display names", TakeoffCreateAllowsDuplicateDisplayNames),
    ("takeoff rename allows duplicate display names", TakeoffRenameAllowsDuplicateDisplayNames),
    ("takeoff tree regression job save avoids legacy pdf sidecar", TakeoffsTreeRegressionTests.JobSaveDoesNotWriteLegacyPdfSidecar),
    ("takeoff tree regression job page load gates legacy autoload", TakeoffsTreeRegressionTests.JobPageLoadGatesLegacyAutoLoad),
    ("page annotations use lossless dirty lifecycle", TakeoffsTreeRegressionTests.PageAnnotationsUseLosslessDirtyLifecycle),
    ("page open defers heavy ui work", TakeoffsTreeRegressionTests.PageOpenDefersHeavyUiWork),
    ("page tabs support drag reorder and detach", TakeoffsTreeRegressionTests.PageTabsSupportDragReorderAndDetach),
    ("programmatic page selection opens viewport directly", TakeoffsTreeRegressionTests.ProgrammaticPageSelectionOpensViewportDirectly),
    ("page tree click opens viewport directly", TakeoffsTreeRegressionTests.PageTreeClickOpensViewportDirectly),
    ("page reload invalidates preview prefetch cache", TakeoffsTreeRegressionTests.PageReloadInvalidatesPreviewPrefetchCache),
    ("page delete undo restores payload source and sibling order", PageDeleteUndoServiceTests.RestoresPagePayloadSourceAndSiblingOrder),
    ("page delete undo restores folders with nested pages", PageDeleteUndoServiceTests.RestoresDeletedFolderWithNestedPages),
    ("page delete undo collision keeps data without overwrite", PageDeleteUndoServiceTests.CollisionKeepsUndoDataAndDoesNotOverwrite),
    ("page delete undo missing parent falls back safely", PageDeleteUndoServiceTests.MissingParentFallsBackToPagesRootAndRewritesSource),
    ("page delete undo UI uses one scoped undo slot", PageDeleteUndoServiceTests.PagesUiUsesSingleUndoSlotAndScopedShortcut),
    ("takeoff tree regression section key handles legacy unfiled item", TakeoffsTreeRegressionTests.SectionSelectionKeyHandlesLegacyUnfiledItem),
    ("takeoff tree regression job load builds before clearing tree", TakeoffsTreeRegressionTests.JobLoadBuildsTakeoffsBeforeClearingTree),
    ("takeoff tree regression page clear keeps loaded takeoffs", TakeoffsTreeRegressionTests.PageClearDoesNotClearLoadedTakeoffs),
    ("takeoff tree regression section menus build lazily", TakeoffsTreeRegressionTests.TakeoffSectionMenusAreBuiltLazily),
    ("takeoff tree regression joist direction resets from section menu", TakeoffsTreeRegressionTests.JoistDirectionCanBeResetFromSectionMenu),
    ("module catalog matches distributable defaults", ModuleFeatureTests.CatalogMatchesDistributableDefaults),
    ("module config upgrade preserves edits and fills defaults", ModuleFeatureTests.CloneAndUpgradePreserveEditsAndAddMissingModules),
    ("module global store round trips atomically", ModuleFeatureTests.GlobalStoreRoundTripsAtomically),
    ("module resolve uses whole job then global then default", ModuleFeatureTests.ResolveUsesWholeJobThenGlobalThenDefault),
    ("module malformed config falls back safely", ModuleFeatureTests.MalformedConfigFallsBackSafely),
    ("module gate covers workspaces menus and direct actions", ModuleFeatureTests.RequiredSurfacesAreWiredThroughTheModuleGate),
    ("takeoff template defaults include framing line presets", TakeoffTemplateTests.DefaultsIncludeFramingLinePresets),
    ("takeoff template routing falls back to root when folder missing", TakeoffTemplateTests.RoutingUsesExistingFolderOrRootFallback),
    ("takeoff template upgrade merges wiki presets into old configs", TakeoffTemplateTests.UpgradeMergesWikiPresetsIntoOldConfigs),
    ("takeoff template legacy config migrates to default preset", TakeoffTemplateTests.LegacyTemplateMigratesToDefaultPreset),
    ("takeoff template named presets switch without changing default", TakeoffTemplateTests.NamedTemplatePresetsCanSwitchWithoutChangingDefault),
    ("takeoff tree regression fast refresh disabled for data safety", TakeoffsTreeRegressionTests.FastRefreshDisabledForDataSafety),
    ("takeoff tree regression selection uses targeted ui refresh", TakeoffsTreeRegressionTests.TakeoffSelectionUsesTargetedUiRefresh),
    ("takeoff tree regression copy uses incremental tree refresh", TakeoffsTreeRegressionTests.TakeoffCopyUsesIncrementalTreeRefresh),
    ("takeoff tree regression stale drag row reloads tree", TakeoffsTreeRegressionTests.StaleDragRowReloadsTakeoffsTree),
    ("takeoff tree regression drag state resets on release", TakeoffsTreeRegressionTests.TakeoffDragStateResetsOnRelease),
    ("takeoff tree regression loads nested mixed items", TakeoffsTreeRegressionTests.LoadTakeoffItemsKeepsNestedMixedTreeItems),
    ("takeoff tree regression keeps siblings after corrupt measurements", TakeoffsTreeRegressionTests.LoadTakeoffItemsKeepsSiblingsWhenMeasurementsJsonIsCorrupt),
    ("takeoff tree regression page lookup enabled for large tree refresh", TakeoffsTreeRegressionTests.PageMeasurementLookupEnabledForLargeTreeRefresh),
    ("page tree refresh reclassifies stale folder nodes", TakeoffsTreeRegressionTests.PageTreeRefreshReclassifiesStaleFolderNodes),
    ("pages esc and folder icons are wired", TakeoffsTreeRegressionTests.PagesEscAndFolderIconsAreWired),
    ("takeoff tree regression pages drop batches and refreshes silently", TakeoffsTreeRegressionTests.PagesDropUsesBatchMoveAndSilentRefresh),
    ("takeoff tree regression moved active sheet rebinds viewport", TakeoffsTreeRegressionTests.PagesMovedActiveSheetRebindsViewportWithoutReload),
    ("pdf sheet metadata layer discovery restores states", TakeoffsTreeRegressionTests.PdfSheetMetadataLayerDiscoveryRestoresLayerStates),
    ("pdf metadata precise policy preserves compound suffix", PdfSheetMetadataWorkflowTests.PrecisePolicyPreservesExistingCompoundSuffix),
    ("pdf metadata tree name stays short lowercase without title", PdfSheetMetadataWorkflowTests.TreeNameIsShortLowercaseAndNeverAppendsTitle),
    ("pdf metadata exact scale gate rejects unsafe sources", PdfSheetMetadataWorkflowTests.ExactScaleGateRejectsUnsafeSourcesAndExistingScale),
    ("pdf metadata scale action clear is explicit", PdfSheetMetadataWorkflowTests.ScaleActionDefaultsToKeepAndClearIsExplicit),
    ("pdf metadata learning keeps immutable detection", PdfSheetMetadataWorkflowTests.LearningRecordKeepsImmutableDetectedDecision),
    ("pdf metadata learning deduplicates observations", PdfSheetMetadataWorkflowTests.LearningSummaryDeduplicatesPdfPageDetectorObservation),
    ("pdf metadata project learning protects exact evidence", PdfSheetMetadataWorkflowTests.ProjectLearningReplacesOnlyLowConfidenceEvidence),
    ("pdf metadata learning rejects conflicting token", PdfSheetMetadataWorkflowTests.LearnedRuleDistillationRejectsConflictingToken),
    ("pdf metadata explicit suffix scale allow wins", PdfSheetMetadataWorkflowTests.ExplicitSuffixScaleAllowOverridesTerminalNoScaleToken),
    ("pdf metadata reviewed scale clear survives analysis", PdfSheetMetadataWorkflowTests.ReviewedScaleClearSurvivesLaterAnalysis),
    ("pdf metadata explicit suffix clear beats preservation", PdfSheetMetadataWorkflowTests.ExplicitSuffixClearBeatsPreservation),
    ("pdf metadata exact rename override beats preservation", PdfSheetMetadataWorkflowTests.ExactRenameOverrideBeatsManualPreservationAndLowConfidence),
    ("pdf metadata exact scale override actions survive contract", PdfSheetMetadataWorkflowTests.ExactScaleOverrideActionsSurviveCSharpNormalizationContract),
    ("pdf metadata exact scale set beats reviewed clear", PdfSheetMetadataWorkflowTests.ExactScaleSetBeatsPreviouslyReviewedClear),
    ("pdf metadata fallback build does not write before preview", PdfSheetMetadataWorkflowTests.FallbackBuildDoesNotWriteBeforePreviewApproval),
    ("pdf metadata uncommon exact scale round trips without snap", PdfSheetMetadataWorkflowTests.UncommonExactScaleRoundTripsWithoutPresetSnap),
    ("pdf metadata kept no-scale remains no-scale", PdfSheetMetadataWorkflowTests.KeptNoScaleDecisionRemainsNoScale),
    ("pdf metadata project learned scale keeps text numeric sync", PdfSheetMetadataWorkflowTests.ProjectLearnedScaleUpdatesTextAndNumericValueTogether),
    ("pdf metadata learned suffix leaves protected scale unchanged", PdfSheetMetadataWorkflowTests.LearnedSuffixDoesNotMutateProtectedScaleDecision),
    ("sheet metadata legacy preview keeps legacy detector", SheetMetadataConfigTests.LegacyPreviewDoesNotSwitchDetector),
    ("sheet metadata defaults disable automatic import analysis", SheetMetadataConfigTests.DefaultsDisableAutomaticImportAnalysis),
    ("sheet metadata manual policy reviews explicit analysis", SheetMetadataConfigTests.ManualPolicySkipsImportButReviewsExplicitAnalysis),
    ("sheet metadata batch worker skips slow command replay", SheetMetadataConfigTests.BatchWorkerFailureDoesNotReplaySlowFileCommand),
    ("sheet metadata failed batch stops without per-page retry", PdfSheetMetadataBatchRegressionTests.FailedBatchStopsWithoutPerPageRetry),
    ("sheet metadata batch is bounded and carries profile catalog", PdfSheetMetadataBatchRegressionTests.BatchIsBoundedAndCarriesProfileCatalog),
    ("sheet metadata guidance chooses stable profile midpoints", PdfSheetMetadataGuidancePlannerTests.ChoosesStableMiddlePageForEachProfile),
    ("sheet metadata guidance keeps A and S separate", PdfSheetMetadataGuidancePlannerTests.KeepsArchitecturalAndStructuralProfilesSeparate),
    ("sheet metadata guidance uses Default only without a discipline", PdfSheetMetadataGuidancePlannerTests.UsesDefaultOnlyWhenNoDisciplineGroupExists),
    ("sheet metadata guidance skips dedicated profile layouts", PdfSheetMetadataGuidancePlannerTests.SkipsProfilesWithDedicatedTemplates),
    ("sheet metadata schema one restores precise collections", SheetMetadataConfigTests.SchemaOnePreciseMigrationRestoresCollections),
    ("sheet metadata schema two empty rules stay authoritative", SheetMetadataConfigTests.SchemaTwoEmptyRulesRemainAuthoritative),
    ("sheet metadata null rule rows resolve safely", SheetMetadataConfigTests.NullRuleRowsDoNotCrashUpgradeOrResolve),
    ("sheet metadata resolves job global default precedence", SheetMetadataConfigTests.ResolveUsesJobThenGlobalThenDefault),
    ("sheet metadata terminal policy and clone are editable", SheetMetadataConfigTests.TerminalPolicyAndCloneAreEditableAndDeep),
    ("sheet metadata precise catalog keeps structural cases", SheetMetadataConfigTests.PreciseCatalogKeepsProvenStructuralCases),
    ("sheet metadata override actions and fingerprint persist", SheetMetadataConfigTests.OverrideActionsDefaultToKeepAndFingerprintChanges),
    ("sheet manager name edits stay checked", TakeoffsTreeRegressionTests.SheetManagerNameEditsStayCheckedAndDoNotSelectAllOnFocus),
    ("takeoff tree regression page repair moved job suffix", TakeoffsTreeRegressionTests.PageRepairUsesMovedJobSuffixForNonEmptyReferences),
    ("takeoff tree regression drag uses mouse down anchor", TakeoffsTreeRegressionTests.TreeDragUsesMouseDownAnchor),
    ("takeoff tree regression nested rows resolve drop target", TakeoffsTreeRegressionTests.NestedTreeRowsResolveToOwningDropTargets),
    ("takeoff tree regression measurement paste keeps source name", TakeoffsTreeRegressionTests.MeasurementPasteNewTakeoffKeepsSourceName),
    ("takeoff tree regression measurement paste preserves count symbol", TakeoffsTreeRegressionTests.MeasurementPastePreservesCountSymbol),
    ("takeoff cleanup includes only verified zero-record items", TakeoffCleanupServiceTests.SafeFinderIncludesOnlyVerifiedZeroRecordItems),
    ("takeoff cleanup excludes corrupt and missing measurement data", TakeoffCleanupServiceTests.SafeFinderExcludesCorruptAndPossiblyMissingMeasurementData),
    ("takeoff cleanup rejects ambiguous measurement json", TakeoffCleanupServiceTests.SafeFinderRejectsAmbiguousMeasurementJson),
    ("takeoff cleanup rejects json metadata count conflict", TakeoffCleanupServiceTests.SafeFinderRejectsJsonMetadataCountConflict),
    ("takeoff cleanup protects multiline owner and companion", TakeoffCleanupServiceTests.SafeFinderExcludesEmptyMultiLineOwnerAndCompanion),
    ("takeoff cleanup button uses guarded single undo batch", TakeoffCleanupServiceTests.BottomButtonUsesGuardedSingleUndoTrashBatch),
    ("autosave write failure retains dirty item and schedules retry", TakeoffSaveServiceTests.WriteFailureRetainsDirtyItemAndSchedulesRetry),
    ("autosave scheduled retry saves retained item", TakeoffSaveServiceTests.ScheduledRetrySavesRetainedItem),
    ("autosave partial batch retries only failed item", TakeoffSaveServiceTests.PartialBatchRetriesOnlyFailedItem),
    ("autosave missing folder remains pending without resurrection", TakeoffSaveServiceTests.MissingFolderRemainsPendingWithoutResurrection),
    ("autosave failed attempt preserves successful timestamp", TakeoffSaveServiceTests.FailedAttemptPreservesPreviousSuccessfulTimestamp),
    ("autosave empty flush does not invent timestamps", TakeoffSaveServiceTests.EmptyFlushDoesNotInventTimestamps),
    ("autosave missing current job retains dirty item", TakeoffSaveServiceTests.MissingCurrentJobKeepsPendingItem),
    ("autosave switched job cannot flush previous job item", TakeoffSaveServiceTests.SwitchedJobCannotFlushPreviousJobItem),
    ("autosave path outside takeoffs root cannot be written", TakeoffSaveServiceTests.PathOutsideTakeoffsRootCannotBeWritten),
    ("autosave status never claims saved outside clean", TakeoffSaveServiceTests.StatusTextNeverClaimsSavedOutsideClean),
    ("autosave explicit flush failure stops caller", AutosaveLifecycleRegressionTests.ExplicitFlushFailureStopsCaller),
    ("autosave reload flushes before replacing takeoff instances", AutosaveLifecycleRegressionTests.ReloadFlushesBeforeReplacingTakeoffInstances),
    ("autosave job switch stops when current job cannot flush", AutosaveLifecycleRegressionTests.JobSwitchStopsWhenCurrentJobCannotFlush),
    ("autosave window close cancels while takeoffs remain pending", AutosaveLifecycleRegressionTests.WindowCloseIsCanceledWhileTakeoffsRemainPending),
    ("package window close saves current file without choice", AutosaveLifecycleRegressionTests.PackageWindowCloseSavesCurrentFileWithoutChoice),
    ("save as ui palette and shortcut expose one context action", AutosaveLifecycleRegressionTests.SaveAsUiPaletteAndShortcutExposeOneContextSensitiveAction),
    ("legacy save as keeps folder format and switches destination", AutosaveLifecycleRegressionTests.LegacySaveAsKeepsFolderFormatAndSwitchesToDestination),
    ("job lease schema v2 round trips required fields", JobLeaseServiceTests.SchemaV2RoundTripsAllRequiredFields),
    ("job lease reads legacy v1 lock", JobLeaseServiceTests.LegacyV1LockIsReadCompatible),
    ("job lease active local owner blocks second instance", JobLeaseServiceTests.ActiveLocalLeaseBlocksSecondInstance),
    ("job lease stopped local process is stale", JobLeaseServiceTests.StoppedLocalProcessIsImmediatelyStale),
    ("job lease reused process id is stale", JobLeaseServiceTests.ReusedLocalProcessIdDoesNotKeepLeaseActive),
    ("job lease remote owner remains active until expiry", JobLeaseServiceTests.RemoteLeaseStaysActiveUntilExpiry),
    ("job lease takeover rejects active remote and replaces stale owner", JobLeaseServiceTests.ExplicitTakeoverRejectsActiveRemoteAndReplacesStaleOwner),
    ("job lease takeover compare exchange preserves changed owner", JobLeaseServiceTests.TakeoverCasNeverClobbersChangedLease),
    ("job lease heartbeat renews exact owner generation", JobLeaseServiceTests.HeartbeatRenewsOnlyExactOwnerAndGeneration),
    ("job lease ownership loss stops heartbeat", JobLeaseServiceTests.LostOwnershipStopsHeartbeatAndPreservesNewOwner),
    ("job lease release preserves changed owner", JobLeaseServiceTests.ReleaseNeverDeletesChangedOwner),
    ("job lease unreadable state cannot be replaced", JobLeaseServiceTests.UnreadableLeaseCannotBeSilentlyReplaced),
    ("job lease file guard is exclusive and reusable", JobLeaseServiceTests.FileGuardIsExclusiveAndReusable),
    ("job write gate read-only blocks writes except lease metadata", JobWriteAccessTests.ReadOnlySessionBlocksAtomicWritesButAllowsLeaseMetadata),
    ("job write gate permits writes outside registered job", JobWriteAccessTests.WritesOutsideRegisteredJobRemainAllowed),
    ("job write gate closed session blocks late writes", JobWriteAccessTests.ClosedSessionBlocksLateWritesAndOldTokenCannotReopen),
    ("job write gate read-only load does not repair storage", JobWriteAccessTests.ReadOnlyLoadDoesNotCreateOrRepairJobStorage),
    ("job write gate read-only page load skips source repair", JobWriteAccessTests.ReadOnlyPageLoadSkipsSourceRepair),
    ("job write gate read-only mutators preserve job tree", JobWriteAccessTests.ReadOnlyMutatorsFailBeforeChangingJobTree),
    ("job write gate read-only maintenance and snapshots preserve files", JobWriteAccessTests.ReadOnlyMaintenanceAndSnapshotsPreserveJobFiles),
    ("job write gate read-only raster and page image services preserve files", JobWriteAccessTests.ReadOnlyRasterAndPageImageServicesPreserveJobFiles),
    ("job write gate direct storage mutations demand adjacent access", JobWriteAccessTests.DirectStorageMutationsHaveAdjacentWriteDemand),
    ("job write gate concurrent demands share normalized mode", JobWriteAccessTests.ConcurrentDemandsObserveOneNormalizedMode),
    ("job lease main window does not use legacy recovery lock", JobLeaseIntegrationRegressionTests.LegacyRecoveryLockIsNotUsedByMainWindow),
    ("job lease heartbeat uses durable atomic write", JobLeaseIntegrationRegressionTests.LeaseHeartbeatUsesDurableAtomicWrite),
    ("job lease window close releases current access", JobLeaseIntegrationRegressionTests.WindowCloseReleasesCurrentJobAccess),
    ("job lease read-only window close skips job saves", JobLeaseIntegrationRegressionTests.ReadOnlyWindowCloseSkipsJobSaves),
    ("job lease read-only bookmarks block mutations and keep open", JobLeaseIntegrationRegressionTests.ReadOnlyBookmarksBlockMutationsButKeepOpenAvailable),
    ("job lease read-only 3D blocks edits and keeps viewer", ThreeDReadOnlyRegressionTests.EditingIsBlockedBeforeMutationWhileViewerStaysAvailable),
    ("job lease candidate rechecks after old-job flush", JobLeaseLifecycleRegressionTests.CandidateLeaseIsRecheckedAfterOldJobFlush),
    ("job lease reload and ownership loss fail closed", JobLeaseLifecycleRegressionTests.ReloadAndOwnershipLossFailClosed),
    ("job lease AI continuations stop after access loss", JobLeaseLifecycleRegressionTests.AiContinuationsStopWhenWriteAccessChanges),
    ("job lease long async workflows keep origin identity", JobLeaseLifecycleRegressionTests.LongAsyncWorkflowsStayBoundToTheirOriginJob),
    ("job lease import metadata and similar flows recheck origin", JobLeaseLifecycleRegressionTests.ImportMetadataAndSimilarFlowsRecheckOriginAccess),
    ("takeoff tree refresh button is wired", TakeoffsTreeRegressionTests.TakeoffsTreeRefreshButtonIsWired),
    ("bookmarks dock panel and shortcut are wired", TakeoffsTreeRegressionTests.BookmarksDockPanelAndShortcutAreWired),
    ("bookmark crop includes measurements and row preview is local", TakeoffsTreeRegressionTests.BookmarkCropIncludesVisibleMeasurementsAndRowPreviewIsLocal),
    ("takeoff template presets and collapsed depth are wired", TakeoffsTreeRegressionTests.TakeoffTemplatePresetsAndCollapsedDepthAreWired),
    ("tree marquee multi selection is wired", TakeoffsTreeRegressionTests.TreeMarqueeMultiSelectionIsWired),
    ("project tree collapse and takeoff delete selection are wired", TakeoffsTreeRegressionTests.ProjectTreeCollapseAndTakeoffDeleteSelectionAreWired),
    ("takeoff tree search bulk visibility and markup selection are wired", TakeoffsTreeRegressionTests.TreeSearchBulkVisibilityAndViewportMarkupSelectionAreWired),
    ("takeoff folder random colors are wired", TakeoffsTreeRegressionTests.TakeoffFolderRandomColorsAreWired),
    ("annotation tab highlighter is wired", TakeoffsTreeRegressionTests.AnnotationTabHighlighterIsWired),
    ("annotation ortho uses dominant axis and horizontal tie", AnnotationOrthoRegressionTests.ApplyOrthoUsesDominantAxisAndHorizontalTie),
    ("annotation dimension scale prefers current page", AnnotationOrthoRegressionTests.DimensionScalePrefersPageAndFallsBack),
    ("annotation draw line finalizes connected polyline", AnnotationOrthoRegressionTests.DrawLineFinalizesConnectedPolyline),
    ("annotation clipboard keeps group undo and persistence", AnnotationOrthoRegressionTests.AnnotationClipboardKeepsGroupUndoAndPersistenceWiring),
    ("annotation shift ortho stays active through editing", AnnotationOrthoRegressionTests.ShiftOrthoUsesOrAndCoversAnnotationEditing),
    ("viewport rename and cad box selection are wired", TakeoffsTreeRegressionTests.ViewportRenameAndCadBoxSelectionAreWired),
    ("transform scale slider label is wired", TakeoffsTreeRegressionTests.TransformScaleSliderLabelIsWired),
    ("page takeoff layers and alt vertex mode are wired", TakeoffsTreeRegressionTests.PageTakeoffLayersAndAltVertexModeAreWired),
    ("dense viewport labels keep joist and selected labels", TakeoffsTreeRegressionTests.DenseViewportLabelsKeepJoistAndSelectedLabels),
    ("output label toggles support independent categories", TakeoffsTreeRegressionTests.OutputLabelTogglesSupportIndependentCategories),
    ("viewport takeoff properties are type aware", TakeoffsTreeRegressionTests.ViewportTakeoffPropertiesAreTypeAware),
    ("display label toggles refresh detached sheets", TakeoffsTreeRegressionTests.DisplayLabelTogglesRefreshDetachedSheets),
    ("viewport live input settings are visible and editable", TakeoffsTreeRegressionTests.ViewportLiveInputSettingsAreVisibleAndEditable),
    ("page takeoff selection syncs takeoffs tree", TakeoffsTreeRegressionTests.PageTakeoffSelectionSyncsTakeoffsTree),
    ("takeoff tree section rows default hidden and setting wired", TakeoffsTreeRegressionTests.TakeoffTreeSectionRowsDefaultHiddenAndSettingWired),
    ("page measurement visibility toggle is wired", TakeoffsTreeRegressionTests.PageMeasurementVisibilityToggleIsWired),
    ("point along line tool is wired", TakeoffsTreeRegressionTests.PointAlongLineToolIsWired),
    ("viewport count hot grips and tight hit test are wired", TakeoffsTreeRegressionTests.ViewportCountHotGripsAndTightHitTestAreWired),
    ("pdf snap duplicate load guard is wired", TakeoffsTreeRegressionTests.PdfSnapDuplicateLoadGuardIsWired),
    ("raster snap strict black lines only is wired", TakeoffsTreeRegressionTests.RasterSnapStrictBlackLinesOnlyIsWired),
    ("raster sheet render skips delayed pdf zoom refresh", TakeoffsTreeRegressionTests.RasterSheetRenderSkipsDelayedPdfZoomRefresh),
    ("static raster mode suppresses live re-renders", TakeoffsTreeRegressionTests.StaticRasterModeSuppressesLiveReRenders),
    ("static raster exact DPI migration is bidirectional", StaticRasterPrefetchPolicyTests.ExactPinnedDpiRequiresOneTimeMigrationInEitherDirection),
    ("sheet manager DPI presets keep editable defaults", SheetManagerRasterPresetTests.DefaultsMatchSheetManagerContractAndCloneIndependently),
    ("sheet manager exact DPI pin excludes source images", SheetManagerRasterPresetTests.ExactDpiPinExcludesSourceImagesAndInvalidValues),
    ("sheet manager stale snapshots preserve persisted raster metadata", SheetManagerRasterPresetTests.PersistedRasterMetadataWinsOverStalePageSnapshots),
    ("sheet manager raster and pin transitions stay atomic", SheetManagerRasterPresetTests.AtomicPresetTransitionsKeepActiveRasterAndPinTogether),
    ("sheet manager operation gate and pinned viewport paths are wired", SheetManagerRasterPresetTests.OperationGateAndPinnedViewportPathsAreWired),
    ("sheet manager presets are selected-only and grids scroll", SheetManagerRasterPresetTests.ToolbarUsesStrictSelectionAndManagersScrollHorizontally),
    ("static raster and black vector are retained while digitizing", ViewportStaticFrameCacheRegressionTests.StaticRasterAndBlackVectorAreRetainedDuringDigitizing),
    ("pointer preview uses one trailing sixty FPS cadence", ViewportStaticFrameCacheRegressionTests.PointerPreviewUsesOneTrailingSixtyFpsCadence),
    ("area preview performance probe is transient and env gated", ViewportAreaPreviewPerformanceProbeTests.ProbeIsEnvGatedTransientAndUsesPointerCadence),
    ("viewport recorder captures every active paint timing", ViewportAreaPreviewPerformanceProbeTests.RecorderCapturesEveryPaintTimingField),
    ("static raster prefetch accepts only an exact safe raster", StaticRasterPrefetchPolicyTests.ExactTargetRasterSuppressesOnlySafePrefetch),
    ("static raster prefetch honors per-sheet DPI pin", StaticRasterPrefetchPolicyTests.PerSheetPinOverridesTheGlobalStaticTarget),
    ("pdf sheet metadata parses dotted sheet numbers for suffix rules", TakeoffsTreeRegressionTests.PdfSheetMetadataParsesDottedSheetNumbersForSuffixRules),
    ("pdf raster edge snap preview is wired", TakeoffsTreeRegressionTests.PdfRasterEdgeSnapPreviewIsWired),
    ("pages tree selected sheet scale menu is wired", TakeoffsTreeRegressionTests.PagesTreeSelectedSheetScaleMenuIsWired),
    ("raster sheet cache skips stale page paths", RasterSheetCacheTests.StalePageSnapshotDoesNotCreateRasterFolder),
    ("takeoff auto routing sends sqft areas to sqfts", TakeoffAutoRoutingSendsSqftAreasToSqfts),
    ("takeoff auto routing sends wall lines to sheet floor walls", TakeoffAutoRoutingSendsWallLinesToSheetFloorWalls),
    ("takeoff auto routing sorts page legend labels", TakeoffAutoRoutingSortsPageLegendLabels),
    ("sheet legend hidden measurements keep new measurements visible", SheetLegendHiddenMeasurementsKeepNewMeasurementsVisible),
    ("viewport hidden measurement ids filter active page measurements", ViewportHiddenMeasurementIdsFilterActivePageMeasurements),
    ("takeoff detail refs sort by sheet then detail", TakeoffDetailRefsSortBySheetThenDetail),
    ("sample guide project creates guide pages and screenshots", SampleJobGuideTests.CreatesGuidePagesScreenshotsAndTakeoffs),
    ("guide screenshot capture isolates app settings", GuideScreenshotCaptureRegressionTests.CaptureUsesIsolatedSettingsFile),
    ("sheet legend live auto ignores stored auto order", SheetLegendLiveAutoIgnoresStoredAutoOrder),
    ("massing direct sqfts uses floor labels", MassingDirectSqftsUsesFloorLabels),
    ("massing walls parses upper floor folders", MassingWallsParsesUpperFloorFolders),
    ("massing roof takeoffs link eave rake gable", MassingRoofTakeoffsLinkEaveRakeGable),
    ("massing ai plan classifies ambiguous takeoffs", MassingAiPlanClassifiesAmbiguousTakeoffs),
    ("3d wall parser handles default and 2x sizes", ThreeDWallParserHandlesDefaultAndSizes),
    ("3d wall builder creates segments from scaled lines", ThreeDWallBuilderCreatesSegmentsFromScaledLines),
    ("3d auto builder stacks floors by max wall height", ThreeDAutoBuilderStacksFloorsByMaxWallHeight),
    ("3d auto builder adds sqft slabs at floor levels", ThreeDAutoBuilderAddsSqftSlabsAtFloorLevels),
    ("3d auto builder adds rf area as roof slab", ThreeDAutoBuilderAddsRfAreaAsRoofSlab),
    ("3d roof footprint builder creates rake edges from rf areas", ThreeDRoofFootprintBuilderCreatesRakeEdgesFromRfAreas),
    ("3d auto roof selects opposite eaves", ThreeDAutoRoofSelectsOppositeEaves),
    ("3d auto roof preserves manual eaves", ThreeDAutoRoofPreservesManualEaves),
    ("3d model store persists generated model", ThreeDModelStorePersistsGeneratedModel),
    ("3d model store persists roof guides", ThreeDModelStorePersistsRoofGuides),
    ("3d model store infers legacy defines slope", ThreeDModelStoreInfersLegacyDefinesSlope),
    ("roof pitch text parses and formats", RoofPitchTextParsesAndFormats),
    ("3d roof per edge defines slope controls planes", ThreeDRoofPerEdgeDefinesSlopeControlsPlanes),
    ("3d roof base builder unions adjacent rf areas", ThreeDRoofBaseBuilderUnionsAdjacentRfAreas),
    ("3d roof generation requires eave edges", ThreeDRoofGenerationRequiresEaveEdges),
    ("3d roof eave pitch generates complex footprint mesh", ThreeDRoofEavePitchGeneratesComplexFootprintMesh),
    ("3d roof eave pitch generates rake gable triangles", ThreeDRoofEavePitchGeneratesRakeGableTriangles),
    ("3d roof weighted skeleton convex rectangle tiles", RoofProbeTests.WeightedSkeletonConvexRectangleTiles),
    ("3d roof weighted skeleton l shape tiles", RoofProbeTests.WeightedSkeletonLShapeTiles),
    ("3d roof weighted skeleton u shape tiles", RoofProbeTests.WeightedSkeletonUShapeTiles),
    ("3d roof weighted skeleton complex footprint tiles", RoofProbeTests.WeightedSkeletonComplexFootprintTiles),
    ("3d roof weighted skeleton gable eaves tiles", RoofProbeTests.WeightedSkeletonGableEavesTiles),
    ("3d roof mixed pitch roof has no side hole", RoofProbeTests.MixedPitchRoofHasNoSideHole),
    ("3d roof real slab1 mixed pitch diagnostic", RoofProbeTests.RealSlab1MixedPitchDiagnostic),
    ("3d roof parallel eaves offset ridge by pitch", RoofProbeTests.ParallelEavesOffsetRidgeByPitch),
    ("3d roof l shape mixed eave rake probe", RoofProbeTests.LShapeMixedEaveRakeBuilds),
    ("3d roof u shape multiple valleys probe", RoofProbeTests.UShapeMultipleValleysBuilds),
    ("3d roof envelope tiles u shape footprint", RoofProbeTests.EnvelopeTilesUShapeFootprint),
    ("3d roof stepped footprint faces triangulate", RoofProbeTests.SteppedFootprintFacesTriangulate),
    ("3d roof eagleview stepped footprint has no flat generated seams", RoofProbeTests.EagleviewSteppedFootprintHasNoFlatGeneratedSeams),
    ("3d roof gable geometry probe", RoofProbeTests.GableGeometryProbe),
    ("3d roof stepped zig zag valleys probe", RoofProbeTests.SteppedZigZagValleysBuilds),
    ("3d roof skewed gable diagonal rake probe", RoofProbeTests.SkewedGableDiagonalRakeBuilds),
    ("3d roof separate islands probe", RoofProbeTests.SeparateGableIslandsBuild),
    ("3d roof crossing footprint blocks probe", RoofProbeTests.CrossingFootprintDoesNotBuild),
    ("3d roof noisy clockwise footprint probe", RoofProbeTests.NoisyClockwiseFootprintBuilds),
    ("3d roof surface height fits x and z slopes", RoofProbeTests.SurfaceHeightFitsXAndZSlopes),
    ("3d slab triangulator handles concave areas", ThreeDSlabTriangulatorHandlesConcaveAreas),
    ("3d slab triangulator rejects crossing areas", ThreeDSlabTriangulatorRejectsCrossingAreas),
    ("page tree order moves sheet before folder", PageTreeOrderMovesSheetBeforeFolder),
    ("page tree order moves folder before folder", PageTreeOrderMovesFolderBeforeFolder),
    ("page tree order moves selected items to end", PageTreeOrderMovesSelectedItemsToEnd),
    ("page tree order moves nested folder out below parent", PageTreeOrderMovesNestedFolderOutBelowParent),
    ("page sort defaults include finish and mep others", PageSortDefaultsIncludeFinishAndMepOthers),
    ("page sort upgrade adds finish and mep rules", PageSortUpgradeAddsFinishAndMepRules),
    ("page rename allows duplicate display names", PageRenameAllowsDuplicateDisplayNames),
    ("takeoff display names preserve slash", TakeoffDisplayNamesPreserveSlash),
    ("takeoff copy keeps display name", TakeoffCopyKeepsDisplayName),
    ("takeoff move collision keeps display name", TakeoffMoveCollisionKeepsDisplayName),
    ("job store sanitizes unsafe names", JobStoreSanitizesUnsafeNames),
    ("node sort uses natural page order", StorageTests.NodeSortUsesNaturalPageOrder),
    ("duplicate page clones page and rejects folder", StorageTests.DuplicatePageClonesPageAndRejectsFolder),
    ("tree expansion starts collapsed and tracks user opened paths", TreeExpansionStateTests.StartsCollapsedAndTracksUserOpenedPaths),
    ("tree expansion restores snapshot across reload", TreeExpansionStateTests.RestoresSnapshotAcrossReload),
    ("tree expansion rebases moved descendants", TreeExpansionStateTests.RebasesMovedDescendants),
    ("job layout create and load ensures base folders", StorageTests.JobLayoutCreateAndLoadEnsuresBaseFolders),
    ("blank page creation writes renderable pdf and metadata", StorageTests.BlankPageCreationWritesRenderablePdfAndMetadata),
    ("page import writes layer manifest and metadata", StorageTests.PageImportWritesLayerManifestAndMetadata),
    ("page import keeps multiple pdf sources in one folder", StorageTests.PageImportKeepsMultiplePdfSourcesInOneFolder),
    ("material extraction writes rows and summary csvs", MaterialExtractionServiceTests.WritesRowsAndSummaryCsvs),
    ("material report first page uses page detail format", MaterialExtractionServiceTests.MaterialReportFirstPageUsesPageDetailFormat),
    ("material report builds schedule legends", MaterialExtractionServiceTests.MaterialReportBuildsScheduleLegends),
    ("material report builds copyable note annotation", MaterialExtractionServiceTests.MaterialReportBuildsCopyableNoteAnnotation),
    ("material extraction writes report pdf", MaterialExtractionServiceTests.WritesMaterialReportPdf),
    ("material extraction skips generated report sources", MaterialExtractionServiceTests.UniqueSourcePdfsSkipsGeneratedMaterialReports),
    ("bundled tool resolver finds extracted nested files", MaterialExtractionServiceTests.BundledToolResolverFindsExtractedNestedFiles),
    ("project embeds tools in single file", MaterialExtractionServiceTests.ProjectEmbedsToolsInSingleFile),
    ("bundled tool resolver prefers executable sidecars", MaterialExtractionServiceTests.BundledToolResolverPrefersExecutableSidecars),
    ("bundled python runtime resolves packaged python", MaterialExtractionServiceTests.BundledPythonRuntimeResolvesPackagedPython),
    ("bundled python runtime uses bundled dependencies only", MaterialExtractionServiceTests.BundledPythonRuntimeUsesBundledDependenciesOnly),
    ("page copy and move preserve source overlay and layers", StorageTests.PageCopyAndMovePreserveSourceOverlayAndLayers),
    ("page corrupt source json is quarantined", StorageTests.PageCorruptSourceJsonIsQuarantined),
    ("page source json repairs from sheet metadata", StorageTests.PageSourceJsonRepairsFromSheetMetadata),
    ("page overlay references rebase after page move", StorageTests.PageOverlayReferencesRebaseAfterPageMove),
    ("page multiple overlay layers persist copy reorder and rebase", StorageTests.PageMultipleOverlayLayersPersistCopyReorderAndRebase),
    ("page source json repair restores reciprocal overlay", StorageTests.PageSourceJsonRepairRestoresReciprocalOverlay),
    ("page annotations save load normalize defaults", StorageTests.PageAnnotationsSaveLoadNormalizeDefaults),
    ("page annotations unchanged save is idempotent", StorageTests.PageAnnotationsUnchangedSaveIsIdempotent),
    ("page annotations delete last persists empty state", StorageTests.PageAnnotationsDeleteLastPersistsEmptyState),
    ("page annotations follow moved page folder", StorageTests.PageAnnotationsFollowMovedPageFolder),
    ("page corrupt annotations json is quarantined", StorageTests.PageCorruptAnnotationsJsonIsQuarantined),
    ("page bookmarks save load use job-relative page folders", StorageTests.PageBookmarksSaveLoadUseJobRelativePageFolders),
    ("page corrupt bookmarks json is quarantined", StorageTests.PageCorruptBookmarksJsonIsQuarantined),
    ("takeoff save writes counters and reloads fallback scale", StorageTests.TakeoffSaveWritesCountersAndReloadsFallbackScale),
    ("count display symbol persists on takeoff and measurements", StorageTests.CountDisplaySymbolPersistsOnTakeoffAndMeasurements),
    ("takeoff corrupt measurements json is quarantined", StorageTests.TakeoffCorruptMeasurementsJsonIsQuarantined),
    ("pdf metadata page name and scale gate", PdfMetadataPageNameAndScaleGate),
    ("pdf metadata hides duplicate marker from visible names", PdfMetadataHidesDuplicateMarkerFromVisibleNames),
    ("pdf metadata leaves unknown page names blank", PdfMetadataLeavesUnknownPageNamesBlank),
    ("pdf metadata preserves dotted sheet labels", PdfMetadataPreservesDottedSheetLabels),
    ("pdf scale parser handles architectural scale", PdfScaleParserHandlesArchitecturalScale),
    ("pdf scale parser handles mixed fraction scale", PdfScaleParserHandlesMixedFractionScale),
    ("pdf scale parser handles engineering scale", PdfScaleParserHandlesEngineeringScale),
    ("pdf scale parser handles decimal ratio scale", PdfScaleParserHandlesDecimalRatioScale),
    ("pdf metadata crop template save load round trips", PdfSheetMetadataCropServiceTests.CropTemplateSaveLoadRoundTrips),
    ("pdf metadata crop template usable when either region exists", PdfSheetMetadataCropServiceTests.CropTemplateUsableWhenEitherRegionExists),
    ("pdf metadata crop template resolves job then global", PdfSheetMetadataCropServiceTests.CropTemplateResolvesJobOverrideThenGlobal),
    ("pdf metadata crop profiles round trip independently", PdfSheetMetadataCropServiceTests.CropProfileTemplatesRoundTripIndependently),
    ("pdf metadata crop profiles resolve exact then default", PdfSheetMetadataCropServiceTests.CropProfileResolutionUsesExactThenDefaultFallback),
    ("pdf metadata crop profile resolver uses metadata and page hints", PdfSheetMetadataCropServiceTests.CropProfileResolverPrioritizesMetadataAndUsesPageHeuristics),
    ("joist rounding aliases normalize", JoistRoundingAliasesNormalize),
    ("joist pitch normalizes common input", JoistPitchNormalizesCommonInput),
    ("joist pitch flat input normalizes empty", JoistPitchFlatInputNormalizesEmpty),
    ("joist pitch rejects invalid input", JoistPitchRejectsInvalidInput),
    ("joist pitch factor matches rise run", JoistPitchFactorMatchesRiseRun),
    ("joist pitch accepts single rise over twelve", JoistPitchAcceptsSingleRiseOverTwelve),
    ("joist layout subtracts area cut holes", JoistLayoutSubtractsAreaCutHoles),
    ("joist layout can skip end joist", JoistLayoutCanSkipEndJoist),
    ("extra joist clips to the local filled interval", JoistExtraModelTests.CursorClipSelectsOnlyTheFilledLocalInterval),
    ("area cut hole splits an extra joist", JoistExtraModelTests.AreaCutHoleSplitsExtraJoistIntoFilledPieces),
    ("area cut hole tangent keeps an extra joist", JoistExtraModelTests.AreaCutHoleTangentDoesNotTrimExtraJoist),
    ("area through cut distributes an extra joist", JoistExtraModelTests.AreaThroughCutDistributesExtraJoistAcrossSegments),
    ("area cut trims touched extra and preserves untouched extra", JoistExtraModelTests.AreaCutTrimsTouchedExtraAndPreservesUntouchedExtra),
    ("extra joists join totals and keep label grouping", JoistExtraModelTests.ExtraJoistsJoinTotalsButStayInTheirOwnLabelGroup),
    ("extra joist uses area pitch and rounding", JoistExtraModelTests.ExtraJoistUsesTheAreaPitchAndOrderRounding),
    ("joist export places one extra block after regular blocks", JoistExtraModelTests.PlanSwiftExportPlacesAllRegularBlocksBeforeOneExtraBlock),
    ("end joist applies per area without overwriting directions", JoistExtraModelTests.AddEndJoistAppliesPerAreaWithoutOverwritingDirections),
    ("per-area joist edge overrides survive refresh", JoistExtraModelTests.PerAreaEdgeOverridesSurviveRefreshAndControlBothBoundaries),
    ("skewed area edges still produce boundary joists", JoistExtraModelTests.SlightlySkewedAreaEdgesStillProduceBoundaryJoists),
    ("extra joists persist through current and legacy storage", JoistExtraModelTests.MeasurementsAndLegacyProjectFileRoundTripExtras),
    ("area coalesce preserves and deduplicates extra joists", JoistExtraModelTests.AreaCoalescePreservesAndDeduplicatesExtras),
    ("Extra Joists mode stays active until D or Esc", JoistExtraModelTests.ExtraJoistModeContinuesUntilDOrEscapeAndRegularJoistsStayDistinct),
    ("Extra Joists and area edges edit in viewport", JoistExtraModelTests.ExtraJoistsAndAreaEdgesAreEditableInViewport),
    ("joist pitch length applies slope factor", JoistPitchLengthAppliesSlopeFactor),
    ("joist pitch rounding applies per segment", JoistPitchRoundingAppliesPerSegment),
    ("joist pitch label shows indicator", JoistPitchLabelShowsIndicator),
    ("joist length label shows order and raw lengths", JoistLengthLabelShowsOrderAndRawLengths),
    ("joist length label can use standard format", JoistLengthLabelCanUseStandardFormat),
    ("joist pitch label explains flat slope and order lengths", JoistPitchLabelExplainsFlatSlopeAndOrderLengths),
    ("joist export uses visible label lines", JoistExportUsesVisibleLabelLines),
    ("joist export offsets overlapping segment labels", JoistExportOffsetsOverlappingSegmentLabels),
    ("pdf export offsets overlapping measurement labels", PdfExportOffsetsOverlappingMeasurementLabels),
    ("joist area defaults use compact labels and foot rounding", JoistAreaDefaultsUseCompactLabelsAndFootRounding),
    ("legacy joist item without label flag shows labels", LegacyJoistItemWithoutLabelFlagShowsLabels),
    ("legacy joist item old false label flag migrates to labels", LegacyJoistItemOldFalseLabelFlagMigratesToLabels),
    ("legacy joist item old explicit false label flag migrates to labels", LegacyJoistItemOldExplicitFalseLabelFlagMigratesToLabels),
    ("joist item explicit false label flag stays hidden", JoistItemExplicitFalseLabelFlagStaysHidden),
    ("folder template openings have numbered children", FolderTemplateOpeningsHaveNumberedChildren),
    ("folder template defaults include framing trade folders", FolderTemplateDefaultTests.DefaultsIncludeFramingTradeFolders),
    ("folder template defaults include shear and holdowns per floor", FolderTemplateDefaultTests.DefaultsIncludeShearAndHoldownsPerFloor),
    ("folder template defaults use lowercase names", FolderTemplateDefaultTests.DefaultsUseLowercaseFolderNames),
    ("settings manager folder template edits auto persist", TakeoffsTreeRegressionTests.SettingsManagerFolderTemplateEditsAutoPersist),
    ("report template loads synthetic detailed frame list", ReportTemplateServiceTests.LoadsSyntheticDetailedFrameList),
    ("report template loads local template if present", ReportTemplateServiceTests.LoadsLocalTemplateIfPresent),
    ("report template prefers packaged sidecar", ReportTemplateServiceTests.PrefersTemplateBesidePackagedExecutable),
    ("report template falls back to current development copy", ReportTemplateServiceTests.FallsBackToCurrentDevelopmentTemplate),
    ("report builder applies A3 wall block like macro", ReportTemplateServiceTests.AppliesA3WallBlockLikeMacro),
    ("planswift import creates job pages and measurements", PlanSwiftImportTests.ImportCreatesJobPagesAndMeasurements),
    ("planswift import normalizes oversized raster pages", PlanSwiftImportTests.ImportNormalizesOversizedRasterPageWithoutChangingMeasurements),
    ("planswift import skips pages without takeoffs", PlanSwiftImportTests.ImportSkipsPlanSwiftPagesWithoutTakeoffs),
    ("planswift import all option keeps pages without takeoffs", PlanSwiftImportTests.ImportAllOptionKeepsPlanSwiftPagesWithoutTakeoffs),
    ("planswift import preserves holes box and containers", PlanSwiftImportTests.ImportPreservesPlanSwiftHolesBoxAndContainers),
    ("planswift import skips unusable area sections", PlanSwiftImportTests.ImportSkipsUnusablePlanSwiftAreaSections),
    ("planswift import preserves hidden sections and holes", PlanSwiftImportVisibilityTests.ImportPreservesHiddenPlanSwiftSectionsAndHoles),
    ("planswift import preserves segments and source metadata", PlanSwiftImportTests.ImportPreservesSegmentsAndSourceMetadata),
    ("planswift import joist segments use linked area section directions", PlanSwiftImportTests.ImportJoistSegmentsUseLinkedAreaSectionDirections),
    ("planswift import into current job uses planswift buckets", PlanSwiftImportTests.ImportIntoCurrentJobUsesPlanSwiftBuckets),
    ("planswift import read-only current job stops before mutation", PlanSwiftImportTests.ImportIntoReadOnlyCurrentJobStopsBeforeMutation),
    ("planswift import copies existing ourplancore job takeoffs", PlanSwiftImportTests.ImportCopiesExistingOurPlanCoreJobTakeoffs),
    ("planswift txt export writes every root item", PlanSwiftTxtExportWritesEveryRootItem),
    ("planswift export hides generated import notes", PlanSwiftExportHidesGeneratedImportNotes),
    ("planswift export hides pdf import notes", PlanSwiftExportHidesPdfImportNotes),
    ("planswift export groups selected sibling items", PlanSwiftExportGroupsSelectedSiblingItems),
    ("pdf import source finder finds nested pdf files", PdfImportSourceFinderFindsNestedPdfFiles),
    ("raster sheet cache builds working image and strict snap manifest", RasterSheetCacheTests.BuildsWorkingImageAndStrictSnapManifest),
    ("active excel export matrix keeps numbers", ActiveExcelExportMatrixKeepsNumbers),
    ("active excel values export keeps only item values", ActiveExcelValuesExportKeepsOnlyItemValues),
    ("roof pitch uses rise per twelve without sheet scale", RoofPitchUsesRisePerTwelveWithoutSheetScale),
    ("excel macro defaults match TemplateCom contract", ExcelMacroExportTests.DefaultsMatchTemplateComContract),
    ("excel macro walls build numeric floor groups", ExcelMacroExportTests.WallsBuildNumericFloorGroupsAndImperialValues),
    ("excel macro openings use floors one through five", ExcelMacroExportTests.OpeningsUseConfiguredFloorsOneThroughFive),
    ("excel macro openings strip one-character dot floor prefix", ExcelMacroExportTests.OpeningsStripOnlyOneCharacterDotFloorPrefix),
    ("excel macro export rejects separate buildings", ExcelMacroExportTests.SeparateBuildingFoldersAreRejected),
    ("excel macro preprocess ranges exclude floor headers", ExcelMacroExportTests.PerFloorPreprocessRangesExcludeFloorHeaders),
    ("excel macro additional actions route their folders", ExcelMacroExportTests.AdditionalActionsRouteTheirOwnFolders),
    ("excel macro walls use strict per-floor export order", ExcelMacroExportTests.WallsUseStrictPerFloorExportOrder),
    ("excel macro eves and rakes sort by LF descending", ExcelMacroExportTests.EvesAndRakesSortByLfDescending),
    ("excel macro ALL resolves one building root", ExcelMacroExportTests.AllScopeUsesOneBuildingAndRejectsMixedRoots),
    ("excel macro ALL uses the current tree anchor", ExcelMacroExportTests.AllUsesCurrentTreeAnchor),
    ("excel macro fast cleanup batches equivalent row deletes", ExcelMacroExportTests.FastCleanupPlansBatchEquivalentDeletes),
    ("excel framing toolbar and relative formulas are wired", ExcelMacroExportTests.FramingToolbarAndRelativeFormulaCleanupAreWired),
    ("excel macro workbook resolver accepts renamed active workbook", ExcelMacroExportTests.WorkbookResolverAcceptsRenamedActiveWorkbook),
    ("excel macro workbook resolver rejects unsafe fallback", ExcelMacroExportTests.WorkbookResolverRejectsUnsafeFallback),
    ("excel macro cleanup keeps exact mandatory output labels", ExcelMacroExportTests.CleanupWhitelistUsesExactNormalizedLabels),
    ("excel framing defaults match TemplateCom contract", ExcelFramingExportTests.DefaultsMatchTemplateAndMacroContract),
    ("excel framing planner maps floors headers roof and details", ExcelFramingExportTests.PlannerMapsFloorsHeadersRoofAndDetails),
    ("excel framing planner groups joists without Sum", ExcelFramingExportTests.PlannerBuildsGroupedJoistMacroInputWithoutSum),
    ("excel macro ALL recognizes one framing house", ExcelFramingExportTests.AllScopeRecognizesFramingAsOneHouse),
    ("joist pitch persists on takeoff item", JoistPitchPersistsOnTakeoffItem),
    ("joist pitch applies item properties", JoistPitchAppliesItemProperties),
    ("page overlay persists through source rewrites", PageOverlayPersistsThroughSourceRewrites),
    ("job recovery normalizes snapshot reasons", JobRecoveryNormalizesSnapshotReasons),
    ("job recovery filters metadata files", JobRecoveryFiltersMetadataFiles),
    ("job recovery lock writes reads and clears", JobRecoveryLockWritesReadsAndClears),
    ("job recovery treats live foreign lock as active", JobRecoveryTreatsLiveForeignLockAsActive),
    ("job recovery snapshot copies metadata only", JobRecoverySnapshotCopiesMetadataOnly),
    ("job recovery snapshot pruning keeps newest", JobRecoverySnapshotPruningKeepsNewest),
    ("app settings job roots dedupe", AppSettingsJobRootsDedupe),
    ("app settings removes job root by path", AppSettingsRemovesJobRootByPath),
    ("job picker roots classify local cloud network", JobPickerRootsClassifyLocalCloudNetwork),
    ("app settings path can use env override", AppSettingsPathCanUseEnvOverride),
    ("app identity migration keeps durable data only", AppDataMigrationTests.MigratesDurableDataWithoutOverwritingOrCopyingCaches),
    ("app identity migration protects legacy settings", AppDataMigrationTests.ProtectsLegacySettingsUntilCriticalMigrationCompletes),
    ("app identity environment uses current then legacy", AppDataMigrationTests.EnvironmentVariableUsesCurrentThenLegacyFallback),
    ("app settings count symbol persists", AppSettingsCountSymbolPersists),
    ("app settings pdf import raster default migrates", AppSettingsPdfImportRasterDefaultMigrates),
    ("atomic write ignores stale fixed temp path", AtomicWriteIgnoresStaleFixedTempPath),
    ("app settings recent job preserves pin and thumbnail", AppSettingsRecentPreservesPinAndThumbnail),
    ("app settings removes recent job by path", AppSettingsRemovesRecentJobByPath),
    ("openai response parser extracts output text", OpenAiResponseParserExtractsOutputText),
    ("openai response parser reports incomplete max tokens", OpenAiResponseParserReportsIncompleteMaxTokens),
    ("keyboard shortcut keys use english display text", KeyboardShortcutKeysUseEnglishDisplayText),
    ("F1 shortcut catalog covers reachable contexts", KeyboardShortcutCatalogTests.CoversReachableShortcutContexts),
    ("F1 shortcut help is scrollable and modal", KeyboardShortcutCatalogTests.UsesScrollableModalF1Surface),
    ("transform rotation snap uses fifteen degree steps", TransformRotationSnapUsesFifteenDegreeSteps),
    ("pdf metadata needs fallback when scale is unresolved", PdfMetadataNeedsFallbackWhenScaleUnresolved),
    ("pdf metadata skip scale avoids fallback", PdfMetadataSkipScaleAvoidsFallback),
    ("pdf preview render cache round trips", PdfPreviewRenderCacheRoundTrips),
    ("detail tile disk cache round trips", DetailTileDiskCacheRoundTrips),
    ("pdf preview render cache is wired before layer render", TakeoffsTreeRegressionTests.PdfPreviewRenderCacheIsWiredBeforeLayerRender),
    ("pdf page open uses docnet preview on cache miss", TakeoffsTreeRegressionTests.PdfPageOpenUsesDocnetPreviewOnCacheMiss),
    ("viewport raster page open keeps preview path unless raster first", ViewportRasterPageOpenAppliesHotBitmapCache),
    ("viewport raster first page open avoids docnet fallback", ViewportRasterPageOpenQueuesWarmupWithoutDocnetFallback),
    ("viewport raster first oversized page open paints active bitmap first", ViewportOversizedRasterPageOpenQueuesResponsiveDpiWithPreviewFallback),
    ("pdf full-scale render cache is wired before worker", TakeoffsTreeRegressionTests.PdfFullScaleRenderCacheIsWiredBeforeWorker),
    ("pdf layer render uses portable inline image protocol", TakeoffsTreeRegressionTests.PdfLayerRenderUsesPortableInlineImageProtocol),
    ("pdf sheet metadata handles rotated bottom title block", TakeoffsTreeRegressionTests.PdfSheetMetadataHandlesRotatedBottomTitleBlock),
    ("pdf detail clip render is wired", TakeoffsTreeRegressionTests.PdfDetailClipRenderIsWired),
    ("sheet overlay rendering uses sharper sampling", TakeoffsTreeRegressionTests.SheetOverlayRenderingUsesSharperSampling),
    ("sheet overlay persisted cache is wired", TakeoffsTreeRegressionTests.SheetOverlayPersistedCacheIsWired),
    ("viewport stress smoke can exercise high zoom pan", TakeoffsTreeRegressionTests.ViewportStressSmokeCanExerciseHighZoomPan),
    ("pdf takeoff import command is wired", TakeoffsTreeRegressionTests.PdfTakeoffImportCommandIsWired),
    ("viewport edge snap command is wired", TakeoffsTreeRegressionTests.ViewportEdgeSnapCommandIsWired),
    ("sheet overlay render cache round trips", SheetOverlayRenderCacheRoundTrips),
    ("viewport render scale chooses next quality step", ViewportRenderScaleChoosesNextQualityStep),
    ("viewport raster navigation chooses lighter dpi", ViewportRasterNavigationChoosesLighterDpi),
    ("viewport raster quality restore waits for motion quiet", ViewportRasterQualityRestoreWaitsForMotionQuiet),
    ("viewport pan allows edge overscroll", ViewportPanAllowsEdgeOverscroll),
    ("viewport pan allows sheet past frame at work zooms", ViewportPanAllowsSheetPastFrameAtWorkZooms),
    ("viewport background defaults to opaque white", ViewportBackgroundDefaultsToOpaqueWhite),
    ("viewport background strips transparency", ViewportBackgroundStripsTransparency),
    ("viewport background tints comfort colors", ViewportBackgroundTintsComfortColors),
    ("viewport high zoom uses responsive navigation frame", ViewportHighZoomUsesResponsiveNavigationFrame),
    ("viewport far zoom uses responsive navigation frame", ViewportFarZoomUsesResponsiveNavigationFrame),
    ("viewport dense page uses responsive navigation frame", ViewportDensePageUsesResponsiveNavigationFrame),
    ("viewport editing blocks fast navigation frame", ViewportEditingBlocksFastNavigationFrame),
    ("viewport rendering preserves dpi matrix", TakeoffsTreeRegressionTests.ViewportRenderingPreservesDpiMatrix),
    ("viewport visible geometry padding is screen relative", ViewportVisibleGeometryPaddingIsScreenRelative),
    ("viewport measurement labels survive distant zoom", ViewportMeasurementLabelsSurviveDistantZoom),
    ("viewport measurement LOD limits dense details", ViewportMeasurementLodLimitsDenseDetails),
    ("viewport LOD hides expensive layers during fast frames", ViewportLodHidesExpensiveLayersDuringFastFrames),
    ("viewport measurement spatial index filters by bounds", ViewportMeasurementSpatialIndexFiltersByBounds),
    ("viewport measurement spatial index preserves draw order", ViewportMeasurementSpatialIndexPreservesDrawOrder),
    ("viewport pasted batch undo removes many measurements in one callback", ViewportPastedBatchUndoRemovesManyMeasurementsInOneCallback),
    ("viewport overlay undo restores transform in shared history", ViewportOverlayUndoRestoresTransformInSharedHistory),
    ("viewport stale overlay undo falls through to measurements", ViewportStaleOverlayUndoFallsThroughToMeasurementHistory),
    ("overlay preview cannot cross source binding", SheetOverlayUndoLifecycleTests.PreviewCannotCrossOverlayBinding),
    ("same binding reload keeps overlay undo", SheetOverlayUndoLifecycleTests.SameBindingReloadKeepsOverlayUndo),
    ("host overlay commit creates one undo action", SheetOverlayUndoLifecycleTests.HostCommitCancelsPreviewIntoOneUndoAction),
    ("overlay binding tracks target rebase and rejects source mismatch", SheetOverlayUndoLifecycleTests.OverlayBindingTracksTargetRebaseAndRejectsSourceMismatch),
    ("pdf snap index finds nearest point", PdfSnapIndexFindsNearestPoint),
    ("pdf snap index prefers corner ties", PdfSnapIndexPrefersCornerTies),
    ("pdf snap index snaps to line", PdfSnapIndexSnapsToLine),
    ("pdf snap index finds nearest segment", PdfSnapIndexFindsNearestSegment),
    ("pdf raster edge snap bridges small endpoint gaps", PdfRasterEdgeSnapBridgesSmallEndpointGaps),
};

tests.AddRange(DataSafetyTests.Cases);
tests.Add(("magnified raster preserves interpolated pixels after navigation", ViewportZoomSamplingTests.MagnifiedRasterDoesNotChangePixelsAfterNavigation));
tests.AddRange(new (string Name, Action Run)[]
{
    ("custom shortcuts preserve legacy defaults", CustomKeyboardShortcutTests.DefaultsStaySparseAndPreserveLegacyKeys),
    ("custom shortcuts normalize aliases and reject invalid gestures", CustomKeyboardShortcutTests.NormalizesAliasesAndRejectsInvalidGestures),
    ("custom shortcuts detect focus and sequence conflicts", CustomKeyboardShortcutTests.ConflictsRespectFocusAndSequencePrefixes),
    ("custom shortcuts reset clone and preset retain unbound commands", CustomKeyboardShortcutTests.ResetCloneAndPresetRoundTripKeepExplicitUnbound),
    ("custom mirror uses production Undo and read-only guard", CustomKeyboardShortcutTests.MirrorUsesProductionUndoAndReadOnlyGuard),
    ("custom shortcut settings retain damaged and locked originals during recovery", CustomKeyboardShortcutTests.DamagedOrLockedSettingsRecoverWithOriginalBytesRetained),
});
if (args.Contains("data-safety-tests")) tests = DataSafetyTests.Cases.ToList();
if (args.Contains("zoom-sampling-tests")) tests = tests.Where(t => t.Name.StartsWith("magnified raster", StringComparison.Ordinal)).ToList();
if (args.Contains("custom-shortcut-tests")) tests = tests.Where(t => t.Name.StartsWith("custom ", StringComparison.Ordinal)).ToList();

int passed = 0;
var failures = new List<string>();
foreach ((string name, Action run) in tests)
{
    try
    {
        run();
        passed++;
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        string detail = string.Equals(
            Environment.GetEnvironmentVariable("OURPLANCORE_TEST_VERBOSE_FAILURES"),
            "1",
            StringComparison.Ordinal)
            ? ex.ToString()
            : ex.Message;
        failures.Add($"{name}: {detail}");
        Console.WriteLine($"FAIL {name}: {ex.Message}");
    }
}

Console.WriteLine($"{passed}/{tests.Count} tests passed");
if (failures.Count > 0)
{
    Console.Error.WriteLine("Failures:");
    foreach (string failure in failures)
        Console.Error.WriteLine($"- {failure}");
    CleanupTestGlobalRoot(testGlobalRoot);
    return 1;
}

CleanupTestGlobalRoot(testGlobalRoot);
return 0;

static void CleanupTestGlobalRoot(string path)
{
    TryDeleteDirectory(path);
}

static void TryDeleteDirectory(string path)
{
    try
    {
        if (Directory.Exists(path))
            Directory.Delete(path, recursive: true);
    }
    catch
    {
    }
}

static void MeasurementCountValueAndLabel()
{
    var measurement = new Measurement
    {
        MType = "point",
        Points = [new SKPoint(1, 1), new SKPoint(2, 2), new SKPoint(3, 3)],
    };

    AssertClose(3.0, measurement.Value(0), "count value");
    AssertEqual("3 ea", measurement.Label(0), "count label");
}

static void MeasurementLineUsesOwnScaleFirst()
{
    var measurement = new Measurement
    {
        MType = "line",
        ScaleMetersPerPt = 2,
        Points = [new SKPoint(0, 0), new SKPoint(3, 4)],
    };

    AssertClose(10.0, measurement.Value(100), "line length should use measurement scale");
}

static void MeasurementAreaUsesFallbackScale()
{
    var measurement = new Measurement
    {
        MType = "area",
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(10, 0),
            new SKPoint(10, 10),
            new SKPoint(0, 10),
        ],
    };

    AssertClose(25.0, measurement.AreaValue(0.5), "scaled area");
}

static void MeasurementAreaSubtractsHoles()
{
    var measurement = new Measurement
    {
        MType = "area",
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(10, 0),
            new SKPoint(10, 10),
            new SKPoint(0, 10),
        ],
        Holes =
        [
            [
                new SKPoint(2, 2),
                new SKPoint(5, 2),
                new SKPoint(5, 6),
                new SKPoint(2, 6),
            ],
        ],
    };

    AssertClose(88.0, measurement.AreaValue(1), "area should subtract the 3x4 hole");
}

static void AreaCutInsideKeepsHoleBehavior()
{
    var measurement = SimpleAreaMeasurement(0, 0, 10, 10);
    List<SKPoint> cut =
    [
        new SKPoint(2, 2),
        new SKPoint(5, 2),
        new SKPoint(5, 6),
        new SKPoint(2, 6),
    ];

    AreaBooleanGeometry geometry = BuildAreaCutGeometryForTest(measurement, cut);
    AssertEqual("4", geometry.Points.Count.ToString(), "inside cut should keep the original four-point outer area");
    AssertEqual("1", geometry.Holes.Count.ToString(), "inside cut should be stored as one hole");
    AssertClose(0, geometry.Points[0].X, "inside cut keeps outer start x");

    measurement.Points = geometry.Points;
    measurement.Holes = geometry.Holes;
    AssertClose(88.0, measurement.AreaValue(1), "inside area cut should subtract the hole exactly");
}

static void AreaCutSecondSeparateHoleAddsAnotherHole()
{
    var measurement = SimpleAreaMeasurement(0, 0, 10, 10);
    List<SKPoint> firstCut = [new(2, 2), new(4, 2), new(4, 4), new(2, 4)];
    AreaBooleanGeometry first = BuildAreaCutGeometryForTest(measurement, firstCut);
    measurement.Points = first.Points;
    measurement.Holes = first.Holes;

    List<SKPoint> secondCut = [new(6, 6), new(8, 6), new(8, 8), new(6, 8)];
    AreaBooleanGeometry second = BuildAreaCutGeometryForTest(measurement, secondCut);
    AssertEqual("2", second.Holes.Count.ToString(), "a second separate cut should add a second hole");

    measurement.Points = second.Points;
    measurement.Holes = second.Holes;
    AssertClose(92.0, measurement.AreaValue(1), "two 2x2 holes should subtract 8 from the 100 area");
}

static void AreaCutOverlappingExistingHoleMerges()
{
    // Reproduces the reported bug: a second cut that OVERLAPS the existing
    // square hole used to be rejected outright ("cannot overlap an existing
    // hole") and nothing happened. It must now merge into a single hole.
    var measurement = SimpleAreaMeasurement(0, 0, 10, 10);
    List<SKPoint> firstCut = [new(3, 3), new(6, 3), new(6, 6), new(3, 6)];
    AreaBooleanGeometry first = BuildAreaCutGeometryForTest(measurement, firstCut);
    measurement.Points = first.Points;
    measurement.Holes = first.Holes;
    AssertEqual("1", first.Holes.Count.ToString(), "first cut makes one hole");

    // Second box overlaps the lower-right quadrant of the first hole.
    List<SKPoint> secondCut = [new(5, 5), new(8, 5), new(8, 8), new(5, 8)];
    AreaBooleanGeometry second = BuildAreaCutGeometryForTest(measurement, secondCut);
    AssertEqual("1", second.Holes.Count.ToString(), "overlapping second cut should merge into a single hole");

    measurement.Points = second.Points;
    measurement.Holes = second.Holes;
    // Union of the two 3x3 boxes overlapping on a 1x1 corner = 9 + 9 - 1 = 17.
    AssertClose(83.0, measurement.AreaValue(1), "merged hole area must not double-count the overlap");
}

static void AreaCutEnclosingExistingHoleMerges()
{
    // A second cut that fully ENCLOSES the existing hole must also succeed
    // (the enclosure case tripped the same guard) and yield one larger hole.
    var measurement = SimpleAreaMeasurement(0, 0, 10, 10);
    List<SKPoint> firstCut = [new(4, 4), new(6, 4), new(6, 6), new(4, 6)];
    AreaBooleanGeometry first = BuildAreaCutGeometryForTest(measurement, firstCut);
    measurement.Points = first.Points;
    measurement.Holes = first.Holes;

    List<SKPoint> secondCut = [new(2, 2), new(8, 2), new(8, 8), new(2, 8)];
    AreaBooleanGeometry second = BuildAreaCutGeometryForTest(measurement, secondCut);
    AssertEqual("1", second.Holes.Count.ToString(), "enclosing second cut should leave one hole");

    measurement.Points = second.Points;
    measurement.Holes = second.Holes;
    AssertClose(64.0, measurement.AreaValue(1), "enclosing hole should equal the larger 6x6 cut");
}

static void AreaCutConcaveInteriorHoleIsStored()
{
    // A freehand (non-convex, L-shaped) cut fully inside the area must produce
    // one hole with the L's exact area.
    var measurement = SimpleAreaMeasurement(0, 0, 20, 20);
    List<SKPoint> lShape =
    [
        new(4, 4), new(12, 4), new(12, 8), new(8, 8), new(8, 12), new(4, 12),
    ];

    AreaBooleanGeometry geometry = BuildAreaCutGeometryForTest(measurement, lShape);
    AssertEqual("1", geometry.Holes.Count.ToString(), "concave interior cut should store one hole");

    measurement.Points = geometry.Points;
    measurement.Holes = geometry.Holes;
    // L area = 8x4 + 4x4 = 48; remaining = 400 - 48 = 352.
    AssertClose(352.0, measurement.AreaValue(1), "concave interior cut should subtract the L-shape area");
}

static void AreaCutConcaveThroughCutSplits()
{
    // A freehand concave cut that crosses the outer boundary must still cut
    // (previously rejected with "edge cuts need a box or convex cut shape").
    var measurement = SimpleAreaMeasurement(0, 0, 20, 20);
    List<SKPoint> concaveEdge =
    [
        new(-2, 6), new(12, 6), new(12, 10), new(8, 10),
        new(8, 14), new(-2, 14),
    ];

    bool ok = BuildAreaCutGeometriesForTest(measurement, concaveEdge, out List<AreaBooleanGeometry> geometries, out string error);
    AssertTrue(ok, "concave edge cut should succeed: " + error);
    AssertTrue(geometries.Count >= 1, "concave edge cut should yield geometry");

    double remaining = geometries.Sum(g =>
    {
        double outer = Math.Abs(SignedAreaForTest(g.Points));
        double holes = g.Holes.Sum(h => Math.Abs(SignedAreaForTest(h)));
        return outer - holes;
    });
    AssertTrue(remaining < 400.0 - 1.0, "concave edge cut must remove area, not leave it unchanged");
}

static void AreaCutBoxThroughTwoHolesMerges()
{
    // The reported case: area with TWO box holes, then a box slice through both.
    var measurement = SimpleAreaMeasurement(0, 0, 20, 20);
    foreach (List<SKPoint> box in new[]
    {
        new List<SKPoint> { new(4, 8), new(7, 8), new(7, 12), new(4, 12) },
        new List<SKPoint> { new(13, 8), new(16, 8), new(16, 12), new(13, 12) },
    })
    {
        AreaBooleanGeometry g = BuildAreaCutGeometryForTest(measurement, box);
        measurement.Points = g.Points;
        measurement.Holes = g.Holes;
    }
    AssertEqual("2", measurement.Holes.Count.ToString(), "two separate box cuts make two holes");

    // Interior horizontal slice spanning both holes but not the outer edges.
    List<SKPoint> slice = [new(2, 9), new(18, 9), new(18, 11), new(2, 11)];
    AreaBooleanGeometry merged = BuildAreaCutGeometryForTest(measurement, slice);
    AssertEqual("1", merged.Holes.Count.ToString(), "a slice joining both holes should merge them into one hole");
}

static void AreaCutConcaveThroughTwoHoles()
{
    // Freehand concave cut weaving through two existing box holes.
    var measurement = SimpleAreaMeasurement(0, 0, 20, 20);
    foreach (List<SKPoint> box in new[]
    {
        new List<SKPoint> { new(4, 8), new(7, 8), new(7, 12), new(4, 12) },
        new List<SKPoint> { new(13, 8), new(16, 8), new(16, 12), new(13, 12) },
    })
    {
        AreaBooleanGeometry g = BuildAreaCutGeometryForTest(measurement, box);
        measurement.Points = g.Points;
        measurement.Holes = g.Holes;
    }

    List<SKPoint> concave =
    [
        new(2, 9), new(18, 9), new(18, 11), new(10, 11), new(10, 13), new(2, 13),
    ];
    bool ok = BuildAreaCutGeometriesForTest(measurement, concave, out List<AreaBooleanGeometry> geometries, out string error);
    AssertTrue(ok, "concave cut through two holes should succeed: " + error);
    AssertTrue(geometries.Sum(g => g.Points.Count) >= 3, "concave cut through two holes should yield real geometry");
}

static void AreaCutBoxClipsAtAreaEdge()
{
    var measurement = SimpleAreaMeasurement(0, 0, 10, 10);
    measurement.JoistEnabled = true;
    measurement.JoistDirectionLocked = true;
    measurement.JoistSpacingInches = 24;
    measurement.JoistDirectionDegrees = 0;
    List<SKPoint> cut =
    [
        new SKPoint(8, 2),
        new SKPoint(12, 2),
        new SKPoint(12, 5),
        new SKPoint(8, 5),
    ];

    AreaBooleanGeometry geometry = BuildAreaCutGeometryForTest(measurement, cut);
    AssertEqual("0", geometry.Holes.Count.ToString(), "edge cut should bite the outer contour instead of adding an inner hole");
    AssertTrue(geometry.Points.Count > 4, "edge cut should add vertices to the outer contour");
    AssertTrue(geometry.Points.All(point => point.X <= 10.001f), "edge cut contour must not extend outside the area edge");

    measurement.Points = geometry.Points;
    measurement.Holes = geometry.Holes;
    AssertClose(94.0, measurement.AreaValue(1), "area cut at edge should subtract only the overlap");

    JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(measurement, 1);
    AssertClose(94.0, layout.AreaMetersSquared, "joist area should subtract the same clipped edge cut");
}

static void AreaCutThroughAreaSplitsIntoSegments()
{
    var measurement = SimpleAreaMeasurement(0, 0, 10, 10);
    List<SKPoint> cut =
    [
        new SKPoint(4, -1),
        new SKPoint(6, -1),
        new SKPoint(6, 11),
        new SKPoint(4, 11),
    ];

    bool ok = MeasurementAreaBooleanService.TrySubtractAll(
        measurement,
        cut,
        out List<AreaBooleanGeometry> geometries,
        out string error);

    AssertTrue(ok, error);
    AssertEqual("2", geometries.Count.ToString(), "through cut should split the area into two stored area geometries");
    AssertClose(80.0, geometries.Sum(geometry => Math.Abs(SignedAreaForTest(geometry.Points))), "split segments should preserve the remaining area");
    AssertTrue(geometries.All(geometry => geometry.Holes.Count == 0), "simple through cut should not create holes");
}

static void AreaCombineUnionMergesOverlappingAreas()
{
    // Two 10x10 squares overlapping by a 5x10 strip: union area = 100 + 100 - 50.
    var first = SimpleAreaMeasurement(0, 0, 10, 10);
    var second = SimpleAreaMeasurement(5, 0, 15, 10);

    bool ok = MeasurementAreaBooleanService.TryCombine(
        [first, second], SKPathOp.Union, out List<AreaBooleanGeometry> geometries, out string error);

    AssertTrue(ok, error);
    AssertEqual("1", geometries.Count.ToString(), "union of overlapping areas should produce one geometry");
    AssertClose(150.0, Math.Abs(SignedAreaForTest(geometries[0].Points)), "union should cover both squares once");
}

static void AreaCombineSubtractCutsLaterAreasFromFirst()
{
    var first = SimpleAreaMeasurement(0, 0, 10, 10);
    var second = SimpleAreaMeasurement(5, 0, 15, 10);

    bool ok = MeasurementAreaBooleanService.TryCombine(
        [first, second], SKPathOp.Difference, out List<AreaBooleanGeometry> geometries, out string error);

    AssertTrue(ok, error);
    AssertClose(
        50.0,
        geometries.Sum(geometry => Math.Abs(SignedAreaForTest(geometry.Points))),
        "subtract should leave only the exclusive part of the first area");
}

static void AreaCombineIntersectKeepsOnlyOverlap()
{
    var first = SimpleAreaMeasurement(0, 0, 10, 10);
    var second = SimpleAreaMeasurement(5, 0, 15, 10);

    bool ok = MeasurementAreaBooleanService.TryCombine(
        [first, second], SKPathOp.Intersect, out List<AreaBooleanGeometry> geometries, out string error);

    AssertTrue(ok, error);
    AssertEqual("1", geometries.Count.ToString(), "intersect of two overlapping squares should produce one geometry");
    AssertClose(50.0, Math.Abs(SignedAreaForTest(geometries[0].Points)), "intersect should keep only the shared strip");
}

static void AreaCombineIntersectRejectsDisjointAreas()
{
    var first = SimpleAreaMeasurement(0, 0, 10, 10);
    var second = SimpleAreaMeasurement(20, 0, 30, 10);

    bool ok = MeasurementAreaBooleanService.TryCombine(
        [first, second], SKPathOp.Intersect, out _, out string error);

    AssertTrue(!ok, "intersect of disjoint areas should fail");
    AssertTrue(error.Length > 0, "intersect failure should explain itself");
}

static void AreaCombineRemoveOverlapTrimsLaterAreas()
{
    var first = SimpleAreaMeasurement(0, 0, 10, 10);
    var second = SimpleAreaMeasurement(5, 0, 15, 10);
    var third = SimpleAreaMeasurement(2, 2, 8, 8); // fully inside the first

    bool ok = MeasurementAreaBooleanService.TryRemoveOverlap(
        [first, second, third], out List<List<AreaBooleanGeometry>?> trimmed, out string error);

    AssertTrue(ok, error);
    AssertEqual("3", trimmed.Count.ToString(), "remove overlap should report every input area");
    AssertTrue(trimmed[0] == null, "first-selected area keeps priority and stays unchanged");
    AssertClose(
        50.0,
        trimmed[1]!.Sum(geometry => Math.Abs(SignedAreaForTest(geometry.Points))),
        "second area should lose the strip already covered by the first");
    AssertEqual("0", trimmed[2]!.Count.ToString(), "an area fully covered by earlier areas should be removed");
}

static void AreaCombineDivideSplitsIntoExclusiveAndShared()
{
    var first = SimpleAreaMeasurement(0, 0, 10, 10);
    var second = SimpleAreaMeasurement(5, 0, 15, 10);

    bool ok = MeasurementAreaBooleanService.TryDivide(
        [first, second],
        out List<List<AreaBooleanGeometry>> exclusive,
        out List<AreaBooleanGeometry> shared,
        out string error);

    AssertTrue(ok, error);
    AssertClose(
        50.0,
        exclusive[0].Sum(geometry => Math.Abs(SignedAreaForTest(geometry.Points))),
        "divide should keep the first area's exclusive part");
    AssertClose(
        50.0,
        exclusive[1].Sum(geometry => Math.Abs(SignedAreaForTest(geometry.Points))),
        "divide should keep the second area's exclusive part");
    AssertClose(
        50.0,
        shared.Sum(geometry => Math.Abs(SignedAreaForTest(geometry.Points))),
        "divide should return the overlap as the shared geometry");
}

static void AreaCombineAllowsDifferingStoredScales()
{
    // Same sheet = one calibration; stale per-measurement scale copies must not
    // block the combine.
    var first = SimpleAreaMeasurement(0, 0, 10, 10);
    var second = SimpleAreaMeasurement(5, 0, 15, 10);
    second.ScaleMetersPerPt = 2;

    bool ok = MeasurementAreaBooleanService.TryCombine(
        [first, second], SKPathOp.Union, out List<AreaBooleanGeometry> geometries, out string error);

    AssertTrue(ok, error);
    AssertClose(150.0, Math.Abs(SignedAreaForTest(geometries[0].Points)), "union should ignore stored scale differences on one page");
}

static void AreaCombineRejectsMixedPages()
{
    var first = SimpleAreaMeasurement(0, 0, 10, 10);
    var second = SimpleAreaMeasurement(5, 0, 15, 10);
    second.PageFolder = @"C:\job\Pages\A102";

    bool ok = MeasurementAreaBooleanService.TryCombine(
        [first, second], SKPathOp.Union, out _, out string error);

    AssertTrue(!ok, "combine across different pages should fail");
    AssertTrue(error.Contains("same page", StringComparison.OrdinalIgnoreCase), "mixed-page failure should mention the page requirement");
}

static void TakeoffWallSortOrdersCategoriesThenSizes()
{
    var names = new List<string>
    {
        "2x4 10.3 furring",
        "CH",
        "dem 2x6 11.15 staggered",
        "2x6 10.65",
        "corr 2x6 9.09",
        "ext 11.15",
        "ext 9.09",
    };
    names.Sort(TakeoffWallNameComparer.Instance);
    AssertEqual(
        "ext 9.09|ext 11.15|corr 2x6 9.09|dem 2x6 11.15 staggered|2x6 10.65|2x4 10.3 furring|CH",
        string.Join("|", names),
        "wall sort should order ext, corr, dem, then stud sizes descending, then the rest");
}

static void TakeoffDetailSortGroupsBySheet()
{
    var names = new List<string>
    {
        "3/S501",
        "1/A501",
        "10/A501",
        "notes",
        "5_A501",
        "2/A501",
    };
    names.Sort(TakeoffDetailSheetNameComparer.Instance);
    AssertEqual(
        "1/A501|2/A501|5_A501|10/A501|3/S501|notes",
        string.Join("|", names),
        "detail sort should group by sheet after the separator, then detail number, non-details last");
}

static AreaBooleanGeometry BuildAreaCutGeometryForTest(Measurement measurement, IReadOnlyList<SKPoint> cut)
{
    MethodInfo method = typeof(PdfViewport).GetMethod(
        "TryBuildAreaCutGeometry",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Area cut geometry helper was not found.");

    object?[] args = [measurement, cut, null, ""];
    bool ok = (bool)(method.Invoke(null, args) ?? false);
    if (!ok)
        throw new InvalidOperationException(args[3]?.ToString() ?? "Area cut edge clip failed.");

    return (AreaBooleanGeometry)(args[2]
        ?? throw new InvalidOperationException("Area cut helper returned no geometry."));
}

static bool BuildAreaCutGeometriesForTest(
    Measurement measurement,
    IReadOnlyList<SKPoint> cut,
    out List<AreaBooleanGeometry> geometries,
    out string error)
{
    MethodInfo method = typeof(PdfViewport).GetMethod(
        "TryBuildAreaCutGeometries",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Area cut geometries helper was not found.");

    object?[] args = [measurement, cut, null, ""];
    bool ok = (bool)(method.Invoke(null, args) ?? false);
    geometries = (args[2] as List<AreaBooleanGeometry>) ?? [];
    error = args[3]?.ToString() ?? "";
    return ok;
}

static double SignedAreaForTest(IReadOnlyList<SKPoint> polygon)
{
    double area = 0;
    for (int i = 0; i < polygon.Count; i++)
    {
        SKPoint a = polygon[i];
        SKPoint b = polygon[(i + 1) % polygon.Count];
        area += a.X * b.Y - b.X * a.Y;
    }

    return area / 2.0;
}

static Measurement SimpleAreaMeasurement(float left, float top, float right, float bottom, string takeoffFolder = "") =>
    new()
    {
        MType = "area",
        PageFolder = @"C:\job\Pages\A101",
        TakeoffFolder = takeoffFolder,
        ScaleMetersPerPt = 1,
        Points =
        [
            new SKPoint(left, top),
            new SKPoint(right, top),
            new SKPoint(right, bottom),
            new SKPoint(left, bottom),
        ],
    };

static void PdfExportAreaPathCutsHoles()
{
    var measurement = new Measurement
    {
        MType = "area",
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(10, 0),
            new SKPoint(10, 10),
            new SKPoint(0, 10),
        ],
        Holes =
        [
            [
                new SKPoint(2, 2),
                new SKPoint(5, 2),
                new SKPoint(5, 6),
                new SKPoint(2, 6),
            ],
        ],
    };

    MethodInfo method = typeof(PdfExporter).GetMethod(
        "BuildPdfExportAreaPath",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("PDF export area path helper was not found.");

    using var path = (SKPath)(method.Invoke(null, [measurement])
        ?? throw new InvalidOperationException("PDF export area path helper returned null."));

    AssertEqual(SKPathFillType.EvenOdd.ToString(), path.FillType.ToString(), "export area path fill rule");
    AssertEqual("8", path.PointCount.ToString(), "export area path should include outer and hole contour points");
    AssertTrue(path.Contains(1, 1), "export area path should include outer area");
    AssertFalse(path.Contains(3, 3), "export area path should cut the hole");
    AssertTrue(path.Contains(8, 8), "export area path should include area outside the hole");
}

static void PdfExportAlwaysUsesWhitePaper()
{
    AssertEqual("#FFFFFF", PdfExporter.ExportPaperColorHex, "export paper color must stay white");
}

static void OutputSettingsDefaultExportAppearance()
{
    var settings = new AppSettings();
    AppSettingsStore.NormalizeOutputSettings(settings);

    AssertEqual("Dark", settings.Theme, "theme should default to the current display profile");
    AssertEqual("#2B2B2B", settings.ViewportBackground, "viewport background should default to the current display profile");
    AssertFalse(settings.ShowLineLabels, "line labels should default to the current display profile");
    AssertFalse(settings.ShowAreaLabels, "area labels should default to the current display profile");
    AssertClose(0.9619565217391305, settings.MeasurementLabelScale, "viewport labels should default to the current size");
    AssertClose(3.0, settings.ViewportMeasurementStrokeScale, "viewport stroke should default to the current size");
    AssertClose(1.0, settings.ViewportRulerStrokeWidth, "viewport ruler should default to a 1px screen line");
    AssertClose(36.0, settings.ViewportPdfSnapBridgeTolerancePx, "viewport PDF Snap bridge should default to a larger continuation radius");
    AssertClose(2.0, settings.ViewportPointSizeScale, "viewport point size should default to the current size");
    AssertClose(0.25, settings.ViewportAreaEdgeScale, "viewport area edge should default to the current size");
    AssertClose(0.2826086956521738, settings.ViewportAreaFillOpacity, "viewport area fill should default to the current opacity");
    AssertClose(AppSettingsStore.ExtraJoistGlowIntensityDefault, settings.ExtraJoistGlowIntensity, "Extra Joist glow should default to the current viewport intensity");
    AssertClose(2.0, settings.ViewportZoomWheelFactor, "mouse-wheel zoom step should default to 2x per notch");
    AssertFalse(settings.PdfExportIncludeAnnotations, "PDF annotations should default to the current export profile");
    AssertFalse(settings.PdfExportShowLineLabels, "PDF line labels should default to the current export profile");
    AssertFalse(settings.PdfExportShowAreaLabels, "PDF area labels should default to the current export profile");
    AssertTrue(settings.PdfExportShowExtraJoistGlow, "PDF Extra Joist glow should default on");
    AssertClose(3.5, settings.PdfExportMeasurementStrokeScale, "PDF stroke should default to the current export profile");
    AssertClose(3.5, settings.PdfExportPointSizeScale, "PDF point size should default to the current export profile");
    AssertClose(0.25, settings.PdfExportAreaEdgeScale, "PDF area edge should default to the current export profile");
    AssertClose(0.1826, settings.PdfExportAreaFillOpacity, "PDF area fill should default to the current export profile");
    AssertClose(1.2, settings.PdfExportMeasurementLabelScale, "PDF label should default to the current export profile");
    AssertClose(2.0, settings.PdfExportSheetLegendScale, "PDF legend should default to the current export profile");
    AssertClose(1.2, settings.PdfExportSheetHeaderScale, "PDF header should default to the current export profile");
    AssertClose(248.0, settings.LeftPanelWidth, "left panel should default to the current width");
    AssertClose(269.0, settings.RightPanelWidth, "right panel should default to the current width");
    AssertClose(0.5, settings.ExcelMacroStripTopFraction, "Excel strip should default to half height");

    settings.PdfExportMeasurementStrokeScale = 6.0;
    AppSettingsStore.NormalizeOutputSettings(settings);
    AssertClose(6.0, settings.PdfExportMeasurementStrokeScale, "PDF stroke can be made heavier than the old 4x cap");

    settings.ViewportPdfSnapBridgeTolerancePx = 200.0;
    AppSettingsStore.NormalizeOutputSettings(settings);
    AssertClose(96.0, settings.ViewportPdfSnapBridgeTolerancePx, "PDF Snap bridge should clamp at the Viewport maximum");

    settings.ViewportPdfSnapBridgeTolerancePx = -1.0;
    AppSettingsStore.NormalizeOutputSettings(settings);
    AssertClose(36.0, settings.ViewportPdfSnapBridgeTolerancePx, "PDF Snap bridge should recover invalid values to the default");

    settings.ViewportZoomWheelFactor = 9.0;
    AppSettingsStore.NormalizeOutputSettings(settings);
    AssertClose(2.5, settings.ViewportZoomWheelFactor, "wheel zoom step should clamp at the maximum");

    settings.ViewportZoomWheelFactor = 1.0;
    AppSettingsStore.NormalizeOutputSettings(settings);
    AssertClose(1.05, settings.ViewportZoomWheelFactor, "wheel zoom step should clamp at the minimum");

    settings.ViewportZoomWheelFactor = 0.0;
    AppSettingsStore.NormalizeOutputSettings(settings);
    AssertClose(2.0, settings.ViewportZoomWheelFactor, "wheel zoom step should recover invalid values to the default");

    settings.ExtraJoistGlowIntensity = -1.0;
    AppSettingsStore.NormalizeOutputSettings(settings);
    AssertClose(0.0, settings.ExtraJoistGlowIntensity, "Extra Joist glow should clamp at zero");

    settings.ExtraJoistGlowIntensity = 2.0;
    AppSettingsStore.NormalizeOutputSettings(settings);
    AssertClose(1.0, settings.ExtraJoistGlowIntensity, "Extra Joist glow should clamp at full intensity");

    settings.ExtraJoistGlowIntensity = double.NaN;
    AppSettingsStore.NormalizeOutputSettings(settings);
    AssertClose(AppSettingsStore.ExtraJoistGlowIntensityDefault, settings.ExtraJoistGlowIntensity, "invalid Extra Joist glow should recover the current default");

    settings.PdfExportMeasurementStrokeScale = 12.0;
    settings.PdfExportPointSizeScale = 12.0;
    settings.PdfExportMeasurementLabelScale = 12.0;
    settings.PdfExportSheetLegendScale = 12.0;
    settings.PdfExportSheetHeaderScale = 12.0;
    AppSettingsStore.NormalizeOutputSettings(settings);
    AssertClose(10.0, settings.PdfExportMeasurementStrokeScale, "PDF stroke should clamp at the export maximum");
    AssertClose(10.0, settings.PdfExportPointSizeScale, "PDF point should clamp at the export maximum");
    AssertClose(10.0, settings.PdfExportMeasurementLabelScale, "PDF label should clamp at the export maximum");
    AssertClose(10.0, settings.PdfExportSheetLegendScale, "PDF legend should clamp at the export maximum");
    AssertClose(10.0, settings.PdfExportSheetHeaderScale, "PDF header should clamp at the export maximum");
}

static void PdfExportExtraJoistGlowHonorsVisibilityAndIntensity()
{
    MethodInfo method = typeof(PdfExporter).GetMethod(
        "ExportExtraJoistGlowAlpha",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("PdfExporter.ExportExtraJoistGlowAlpha");

    var options = new PdfExportOptions(
        IncludeMeasurements: true,
        IncludeAnnotations: false,
        IncludeLegend: false,
        UnitMode: UnitMode.Imperial,
        LegendAnchor: "TopLeft",
        LegendScale: 1,
        HeaderScale: 1,
        ShowMeasurementLabels: false,
        ShowLineLabels: false,
        ShowAreaLabels: false,
        ShowCountLabels: false,
        MeasurementStrokeScale: 1,
        PointSizeScale: 1,
        MeasurementLabelScale: 1);

    AssertEqual("145", Alpha(options).ToString(CultureInfo.InvariantCulture), "PDF Extra Joist glow should keep the previous default intensity");
    AssertEqual("0", Alpha(options with { ShowExtraJoistGlow = false }).ToString(CultureInfo.InvariantCulture), "PDF Extra Joist glow visibility should disable the halo");
    AssertEqual("255", Alpha(options with { ExtraJoistGlowIntensity = 1.0 }).ToString(CultureInfo.InvariantCulture), "PDF Extra Joist glow should support full intensity");
    AssertEqual("0", Alpha(options with { ExtraJoistGlowIntensity = 0.0 }).ToString(CultureInfo.InvariantCulture), "PDF Extra Joist glow should support zero intensity");

    byte Alpha(PdfExportOptions value) =>
        (byte)(method.Invoke(null, [value]) ?? throw new InvalidOperationException("PDF Extra Joist glow alpha returned null."));
}

static void PdfExportJoistSummaryIgnoresAreaLabelToggle()
{
    MethodInfo method = typeof(PdfExporter).GetMethod(
        "ShouldExportJoistSummaryLabel",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("PdfExporter.ShouldExportJoistSummaryLabel");

    AssertTrue(AllowsJoistSummaryLabel(showAll: false, showArea: false, showJoist: true), "joist summary should work from the Joist output label toggle without requiring All or Area");
    AssertTrue(AllowsJoistSummaryLabel(showAll: true, showArea: false, showJoist: false), "joist summary should still be included when the All output label toggle is on");
    AssertFalse(AllowsJoistSummaryLabel(showAll: false, showArea: true, showJoist: false), "joist summary should not be included by Area alone");

    bool AllowsJoistSummaryLabel(bool showAll, bool showArea, bool showJoist)
    {
        var options = new PdfExportOptions(
            IncludeMeasurements: true,
            IncludeAnnotations: false,
            IncludeLegend: true,
            UnitMode: UnitMode.Imperial,
            LegendAnchor: "TopLeft",
            LegendScale: 1,
            HeaderScale: 1,
            ShowMeasurementLabels: showAll,
            ShowLineLabels: false,
            ShowAreaLabels: showArea,
            ShowCountLabels: false,
            MeasurementStrokeScale: 1,
            PointSizeScale: 1,
            MeasurementLabelScale: 1,
            ShowJoistLabels: showJoist);

        object? result = method.Invoke(null, [options]);
        return result is bool value && value;
    }
}

static void PdfExportLabelCategoriesWorkWithoutAll()
{
    MethodInfo method = typeof(PdfExporter).GetMethod(
        "ShouldExportMeasurementLabel",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("PdfExporter.ShouldExportMeasurementLabel");

    var lineAreaOnly = new PdfExportOptions(
        IncludeMeasurements: true,
        IncludeAnnotations: false,
        IncludeLegend: false,
        UnitMode: UnitMode.Imperial,
        LegendAnchor: "TopLeft",
        LegendScale: 1,
        HeaderScale: 1,
        ShowMeasurementLabels: false,
        ShowLineLabels: true,
        ShowAreaLabels: true,
        ShowCountLabels: false,
        MeasurementStrokeScale: 1,
        PointSizeScale: 1,
        MeasurementLabelScale: 1,
        ShowJoistLabels: false);
    AssertTrue(Allows("line", lineAreaOnly), "line labels should export when Line is on and All is off");
    AssertTrue(Allows("area", lineAreaOnly), "area labels should export when Area is on and All is off");
    AssertFalse(Allows("point", lineAreaOnly), "count labels should stay hidden when Count and All are off");

    var allOnly = lineAreaOnly with
    {
        ShowMeasurementLabels = true,
        ShowLineLabels = false,
        ShowAreaLabels = false
    };
    AssertTrue(Allows("line", allOnly), "All should include line labels");
    AssertTrue(Allows("area", allOnly), "All should include area labels");
    AssertTrue(Allows("point", allOnly), "All should include count labels");

    bool Allows(string measurementType, PdfExportOptions options) =>
        (bool)(method.Invoke(null, [measurementType, options]) ?? false);
}

static void PdfExportWritesSelectedSheets()
{
    string dir = Path.Combine(Path.GetTempPath(), "onc_pdf_export_smoke", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        string sourcePdf = Path.Combine(dir, "source.pdf");
        using (var stream = File.Create(sourcePdf))
        using (var document = SKDocument.CreatePdf(stream))
        {
            SKCanvas canvas = document.BeginPage(120, 80);
            canvas.Clear(SKColors.White);
            using var paint = new SKPaint
            {
                Color = SKColors.Black,
                IsAntialias = true,
                StrokeWidth = 2,
            };
            canvas.DrawLine(10, 10, 110, 70, paint);
            document.EndPage();
            document.Close();
        }

        string outputPdf = Path.Combine(dir, "export.pdf");
        var page = new PageInfo
        {
            Name = "Smoke",
            FolderPath = dir,
            PdfPath = sourcePdf,
            PdfPage = 0,
            ScaleMetersPerPt = 1,
        };
        var options = new PdfExportOptions(
            IncludeMeasurements: false,
            IncludeAnnotations: false,
            IncludeLegend: false,
            UnitMode: UnitMode.Imperial,
            LegendAnchor: "BottomLeft",
            LegendScale: 1,
            HeaderScale: 1,
            ShowMeasurementLabels: true,
            ShowLineLabels: true,
            ShowAreaLabels: true,
            ShowCountLabels: true,
            MeasurementStrokeScale: 1.5,
            PointSizeScale: 1.0,
            MeasurementLabelScale: 1.0);

        (bool ok, string error) = PdfExporter.TryExport(
            [new PdfExportPageInput(page, [], [])],
            outputPdf,
            options);

        AssertTrue(ok, $"PDF export should succeed: {error}");
        AssertTrue(File.Exists(outputPdf), "PDF export should write the output file");
        AssertTrue(new FileInfo(outputPdf).Length > 16, "PDF export should not be empty");
        byte[] header = File.ReadAllBytes(outputPdf).Take(5).ToArray();
        AssertEqual("%PDF-", System.Text.Encoding.ASCII.GetString(header), "PDF export header");
    }
    finally
    {
        TryDeleteDirectory(dir);
    }
}

static void PdfExportWritesMeasurementLines()
{
    string dir = Path.Combine(Path.GetTempPath(), "onc_pdf_export_measurements", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        string sourcePdf = Path.Combine(dir, "source.pdf");
        using (var stream = File.Create(sourcePdf))
        using (var document = SKDocument.CreatePdf(stream))
        {
            SKCanvas canvas = document.BeginPage(120, 80);
            canvas.Clear(SKColors.White);
            document.EndPage();
            document.Close();
        }

        string outputPdf = Path.Combine(dir, "export.pdf");
        var page = new PageInfo
        {
            Name = "Measured",
            FolderPath = dir,
            PdfPath = sourcePdf,
            PdfPage = 0,
            ScaleMetersPerPt = 1,
        };
        var item = new TakeoffItem
        {
            Name = "Red Line",
            Color = "#FF0000",
            MeasurementType = "line",
        };
        var measurement = new Measurement
        {
            MType = "line",
            Points = [new SKPoint(15, 40), new SKPoint(105, 40)],
        };
        var options = new PdfExportOptions(
            IncludeMeasurements: true,
            IncludeAnnotations: false,
            IncludeLegend: false,
            UnitMode: UnitMode.Imperial,
            LegendAnchor: "BottomLeft",
            LegendScale: 1,
            HeaderScale: 1,
            ShowMeasurementLabels: false,
            ShowLineLabels: false,
            ShowAreaLabels: false,
            ShowCountLabels: false,
            MeasurementStrokeScale: 4.0,
            PointSizeScale: 1.0,
            MeasurementLabelScale: 1.0);

        (bool ok, string error) = PdfExporter.TryExport(
            [new PdfExportPageInput(page, [new PdfExportTakeoffInput(item, [measurement])], [])],
            outputPdf,
            options);

        AssertTrue(ok, $"PDF export with measurements should succeed: {error}");
        using SKBitmap bitmap = RenderPdfPage(outputPdf);
        int redPixels = CountPixels(bitmap, color =>
            color.Red > 180 &&
            color.Green < 90 &&
            color.Blue < 90);
        AssertTrue(redPixels > 20, "PDF export should contain visible red measurement geometry");
    }
    finally
    {
        TryDeleteDirectory(dir);
    }
}

static void PdfExportSkipsInvalidAreaPointArtifacts()
{
    string dir = Path.Combine(Path.GetTempPath(), "onc_pdf_export_invalid_area", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    try
    {
        string sourcePdf = Path.Combine(dir, "source.pdf");
        using (var stream = File.Create(sourcePdf))
        using (var document = SKDocument.CreatePdf(stream))
        {
            SKCanvas canvas = document.BeginPage(120, 80);
            canvas.Clear(SKColors.White);
            document.EndPage();
            document.Close();
        }

        string outputPdf = Path.Combine(dir, "export.pdf");
        var page = new PageInfo
        {
            Name = "Measured",
            FolderPath = dir,
            PdfPath = sourcePdf,
            PdfPage = 0,
            ScaleMetersPerPt = 1,
        };
        var item = new TakeoffItem
        {
            Name = "Invalid Joist Area",
            Color = "#00BCD4",
            MeasurementType = "area",
            IsJoistTakeoff = true,
        };
        var measurement = new Measurement
        {
            MType = "area",
            JoistEnabled = true,
            JoistDirectionLocked = true,
            Points = [new SKPoint(60, 40)],
        };
        var options = new PdfExportOptions(
            IncludeMeasurements: true,
            IncludeAnnotations: false,
            IncludeLegend: false,
            UnitMode: UnitMode.Imperial,
            LegendAnchor: "BottomLeft",
            LegendScale: 1,
            HeaderScale: 1,
            ShowMeasurementLabels: false,
            ShowLineLabels: false,
            ShowAreaLabels: false,
            ShowCountLabels: false,
            MeasurementStrokeScale: 4.0,
            PointSizeScale: 4.0,
            MeasurementLabelScale: 1.0);

        (bool ok, string error) = PdfExporter.TryExport(
            [new PdfExportPageInput(page, [new PdfExportTakeoffInput(item, [measurement])], [])],
            outputPdf,
            options);

        AssertTrue(ok, $"PDF export with invalid area should succeed: {error}");
        using SKBitmap bitmap = RenderPdfPage(outputPdf);
        int cyanPixels = CountPixels(bitmap, color =>
            color.Red < 80 &&
            color.Green > 130 &&
            color.Blue > 150);
        AssertEqual("0", cyanPixels.ToString(), "invalid one-point area should not export as point marker");
    }
    finally
    {
        TryDeleteDirectory(dir);
    }
}

static SKBitmap RenderPdfPage(string path)
{
    const float renderScale = 2.0f;
    using var docReader = DocLib.Instance.GetDocReader(path, new PageDimensions(renderScale));
    using var pageReader = docReader.GetPageReader(0);
    int width = pageReader.GetPageWidth();
    int height = pageReader.GetPageHeight();
    byte[] bytes = pageReader.GetImage();
    var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
    Marshal.Copy(bytes, 0, bitmap.GetPixels(), bytes.Length);
    return bitmap;
}

static int CountPixels(SKBitmap bitmap, Func<SKColor, bool> predicate)
{
    int count = 0;
    for (int y = 0; y < bitmap.Height; y++)
    {
        for (int x = 0; x < bitmap.Width; x++)
        {
            if (predicate(bitmap.GetPixel(x, y)))
                count++;
        }
    }

    return count;
}

static void JobStorePersistsMeasurementHoles()
{
    WithTempJob("Hole Job", job =>
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(
            job,
            job.TakeoffsRoot,
            "Area",
            "#FF4444",
            "area");
        var measurement = new Measurement
        {
            MType = "area",
            PageFolder = job.PagesRoot,
            Points =
            [
                new SKPoint(0, 0),
                new SKPoint(12, 0),
                new SKPoint(12, 10),
                new SKPoint(0, 10),
            ],
            Holes =
            [
                [
                    new SKPoint(3, 3),
                    new SKPoint(7, 3),
                    new SKPoint(7, 6),
                    new SKPoint(3, 6),
                ],
            ],
        };

        item.Measurements.Add(measurement);
        OurPlanCoreJobStore.SaveTakeoffItem(item);
        List<Measurement> loaded = OurPlanCoreJobStore.LoadMeasurements(item.FolderPath);

        AssertEqual("1", loaded.Count.ToString(), "loaded measurement count");
        AssertEqual("1", loaded[0].Holes.Count.ToString(), "loaded hole count");
        AssertEqual("4", loaded[0].Holes[0].Count.ToString(), "loaded hole vertices");
        AssertClose(108.0, loaded[0].AreaValue(1), "loaded area should subtract persisted hole");
    });
}

static void MeasurementJoistWithoutDirectionIsBlocked()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = false,
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(10, 0),
            new SKPoint(10, 10),
            new SKPoint(0, 10),
        ],
    };

    AssertClose(0.0, measurement.Value(0.5), "pending joist value");
    AssertEqual("joists: set direction", measurement.Label(0.5), "pending joist label");
}

static void TakeoffItemNormalizesCountTotals()
{
    var item = new TakeoffItem
    {
        MeasurementType = "count",
    };
    item.Measurements.Add(new Measurement
    {
        MType = "point",
        Points = [new SKPoint(0, 0), new SKPoint(1, 1)],
    });

    AssertEqual("2 ea", item.TotalLabel(0), "count total label");
}

static void MeasurementMergeMovesSegmentIntoTargetTakeoff()
{
    var source = new TakeoffItem
    {
        Name = "Source Area",
        FolderPath = @"C:\job\Takeoffs\Source",
        MeasurementType = "area",
        Color = "#111111",
        IsJoistTakeoff = true,
        JoistSpacingInches = 24,
        JoistDirectionDegrees = 90,
    };
    var target = new TakeoffItem
    {
        Name = "Target Area",
        FolderPath = @"C:\job\Takeoffs\Target",
        MeasurementType = "area",
        Color = "#22AAFF",
    };
    var measurement = new Measurement
    {
        MType = "area",
        Color = source.Color,
        PageFolder = @"C:\job\Pages\A101",
        TakeoffFolder = source.FolderPath,
        ScaleMetersPerPt = 0.25,
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(10, 0),
            new SKPoint(10, 10),
            new SKPoint(0, 10),
        ],
        Holes =
        [
            [
                new SKPoint(2, 2),
                new SKPoint(4, 2),
                new SKPoint(4, 4),
                new SKPoint(2, 4),
            ],
        ],
    };
    source.Measurements.Add(measurement);

    MeasurementMoveResult result = MeasurementMergeSplitService.MoveMeasurementsToTakeoff(
        [source, target],
        [measurement],
        target);

    AssertEqual("0", source.Measurements.Count.ToString(), "source should lose moved measurement");
    AssertEqual("1", target.Measurements.Count.ToString(), "target should receive moved measurement");
    AssertTrue(ReferenceEquals(measurement, target.Measurements[0]), "move should preserve the measurement object");
    AssertEqual(target.FolderPath, measurement.TakeoffFolder, "moved measurement folder");
    AssertEqual(target.Color, measurement.Color, "moved measurement color");
    AssertEqual(@"C:\job\Pages\A101", measurement.PageFolder, "moved measurement page");
    AssertClose(0.25, measurement.ScaleMetersPerPt, "moved measurement scale");
    AssertEqual("1", measurement.Holes.Count.ToString(), "moved measurement holes");
    AssertEqual("2", result.ChangedItems.Count.ToString(), "changed item count");
    AssertEqual("1", result.PageFolders.Count.ToString(), "changed page count");
}

static void MeasurementMergeRejectsMixedTargetType()
{
    var source = new TakeoffItem
    {
        Name = "Source Line",
        FolderPath = @"C:\job\Takeoffs\Source",
        MeasurementType = "line",
    };
    var target = new TakeoffItem
    {
        Name = "Target Area",
        FolderPath = @"C:\job\Takeoffs\Target",
        MeasurementType = "area",
    };
    var measurement = new Measurement
    {
        MType = "line",
        TakeoffFolder = source.FolderPath,
        Points = [new SKPoint(0, 0), new SKPoint(1, 1)],
    };
    source.Measurements.Add(measurement);

    bool rejected = false;
    try
    {
        MeasurementMergeSplitService.MoveMeasurementsToTakeoff([source, target], [measurement], target);
    }
    catch (InvalidOperationException)
    {
        rejected = true;
    }

    AssertTrue(rejected, "line segment should not merge into area target");
}

static void MeasurementMergeCoalescesTouchingLineSections()
{
    var source = new TakeoffItem
    {
        Name = "Source Line",
        FolderPath = @"C:\job\Takeoffs\Source",
        MeasurementType = "line",
        Color = "#111111",
    };
    var target = new TakeoffItem
    {
        Name = "Target Line",
        FolderPath = @"C:\job\Takeoffs\Target",
        MeasurementType = "line",
        Color = "#22AAFF",
    };
    var existing = MergeSplitLine("existing", target.FolderPath, 0, 0, 10, 0);
    var moved = MergeSplitLine("moved", source.FolderPath, 10, 0, 20, 0);
    target.Measurements.Add(existing);
    source.Measurements.Add(moved);

    MeasurementMoveResult result = MeasurementMergeSplitService.MoveMeasurementsToTakeoff(
        [source, target],
        [moved],
        target);

    AssertEqual("0", source.Measurements.Count.ToString(), "source line should move out");
    AssertEqual("1", target.Measurements.Count.ToString(), "touching lines should coalesce into one section");
    AssertTrue(ReferenceEquals(existing, target.Measurements[0]), "existing target line should survive coalesce");
    AssertEqual("2", existing.Points.Count.ToString(), "coalesced line should have two endpoints");
    AssertClose(0, existing.Points[0].X, "coalesced start x");
    AssertClose(20, existing.Points[1].X, "coalesced end x");
    AssertEqual("1", result.SelectedMeasurements.Count.ToString(), "selection should point at survivor");
    AssertTrue(ReferenceEquals(existing, result.SelectedMeasurements[0]), "selection should use surviving line");
    AssertEqual("1", result.CoalescedLineCount.ToString(), "coalesced line count");
}

static void MeasurementMergeKeepsSeparatedLineSections()
{
    var source = new TakeoffItem
    {
        Name = "Source Line",
        FolderPath = @"C:\job\Takeoffs\Source",
        MeasurementType = "line",
        Color = "#111111",
    };
    var target = new TakeoffItem
    {
        Name = "Target Line",
        FolderPath = @"C:\job\Takeoffs\Target",
        MeasurementType = "line",
        Color = "#22AAFF",
    };
    var existing = MergeSplitLine("existing", target.FolderPath, 0, 0, 10, 0);
    var moved = MergeSplitLine("moved", source.FolderPath, 15, 0, 25, 0);
    target.Measurements.Add(existing);
    source.Measurements.Add(moved);

    MeasurementMoveResult result = MeasurementMergeSplitService.MoveMeasurementsToTakeoff(
        [source, target],
        [moved],
        target);

    AssertEqual("0", source.Measurements.Count.ToString(), "source line should move out");
    AssertEqual("2", target.Measurements.Count.ToString(), "separated lines should remain separate sections");
    AssertTrue(target.Measurements.Contains(moved), "moved line should remain in target");
    AssertEqual("1", result.SelectedMeasurements.Count.ToString(), "selection should keep moved line");
    AssertTrue(ReferenceEquals(moved, result.SelectedMeasurements[0]), "selection should use moved line");
    AssertEqual("0", result.CoalescedLineCount.ToString(), "no coalesce count");
}

static Measurement MergeSplitLine(string id, string takeoffFolder, float x1, float y1, float x2, float y2) =>
    new()
    {
        Id = id,
        MType = "line",
        PageFolder = @"C:\job\Pages\A101",
        TakeoffFolder = takeoffFolder,
        ScaleMetersPerPt = 0.25,
        Points = [new SKPoint(x1, y1), new SKPoint(x2, y2)],
    };

static void MeasurementMergeSplicesOverlappingAreaSections()
{
    var source = new TakeoffItem
    {
        Name = "Source Area",
        FolderPath = @"C:\job\Takeoffs\Source",
        MeasurementType = "area",
        Color = "#111111",
    };
    var target = new TakeoffItem
    {
        Name = "Target Area",
        FolderPath = @"C:\job\Takeoffs\Target",
        MeasurementType = "area",
        Color = "#22AAFF",
    };
    var existing = SimpleAreaMeasurement(0, 0, 10, 10, target.FolderPath);
    var moved = SimpleAreaMeasurement(5, 0, 15, 10, source.FolderPath);
    target.Measurements.Add(existing);
    source.Measurements.Add(moved);

    MeasurementMoveResult result = MeasurementMergeSplitService.MoveMeasurementsToTakeoff(
        [source, target],
        [moved],
        target);

    AssertEqual("0", source.Measurements.Count.ToString(), "source area should move out");
    AssertEqual("1", target.Measurements.Count.ToString(), "overlapping areas should splice into one section");
    AssertTrue(ReferenceEquals(existing, target.Measurements[0]), "existing target area should survive splice");
    AssertClose(150.0, existing.AreaValue(1), "spliced area should be seamless union area");
    AssertEqual("0", existing.Holes.Count.ToString(), "simple overlapping area splice should have no holes");
    AssertEqual("1", result.SelectedMeasurements.Count.ToString(), "selection should point at area survivor");
    AssertTrue(ReferenceEquals(existing, result.SelectedMeasurements[0]), "selection should use surviving area");
    AssertEqual("1", result.CoalescedAreaCount.ToString(), "coalesced area count");
}

static void MeasurementMergeKeepsSeparatedAreaSections()
{
    var source = new TakeoffItem
    {
        Name = "Source Area",
        FolderPath = @"C:\job\Takeoffs\Source",
        MeasurementType = "area",
        Color = "#111111",
    };
    var target = new TakeoffItem
    {
        Name = "Target Area",
        FolderPath = @"C:\job\Takeoffs\Target",
        MeasurementType = "area",
        Color = "#22AAFF",
    };
    var existing = SimpleAreaMeasurement(0, 0, 10, 10, target.FolderPath);
    var moved = SimpleAreaMeasurement(15, 0, 25, 10, source.FolderPath);
    target.Measurements.Add(existing);
    source.Measurements.Add(moved);

    MeasurementMoveResult result = MeasurementMergeSplitService.MoveMeasurementsToTakeoff(
        [source, target],
        [moved],
        target);

    AssertEqual("0", source.Measurements.Count.ToString(), "source area should move out");
    AssertEqual("2", target.Measurements.Count.ToString(), "separated areas should remain separate sections");
    AssertTrue(target.Measurements.Contains(moved), "moved area should remain in target");
    AssertEqual("1", result.SelectedMeasurements.Count.ToString(), "selection should keep moved area");
    AssertTrue(ReferenceEquals(moved, result.SelectedMeasurements[0]), "selection should use moved area");
    AssertEqual("0", result.CoalescedAreaCount.ToString(), "no area splice count");
}

static void BeamLengthRoundsUpBelowAndAboveEightFeet()
{
    AssertEqual("8", BeamTakeoffService.FormatOrderLengthFeet(7.01), "below eight rounds to next foot");
    AssertEqual("8", BeamTakeoffService.FormatOrderLengthFeet(8.0), "eight stays eight");
    AssertEqual("10", BeamTakeoffService.FormatOrderLengthFeet(8.01), "above eight rounds to next two feet");
    AssertEqual("12", BeamTakeoffService.FormatOrderLengthFeet(11.3), "larger beam rounds to even foot");
}

static void BeamDefaultNameKeepsSizeSuffixOutsideSelection()
{
    string name = BeamTakeoffService.BuildDefaultCountName("Framing", "10", out int selectionLength);

    AssertEqual("Framing Beam 10", name, "beam default name");
    AssertEqual("Framing Beam", name[..selectionLength], "editable prefix");
    AssertEqual(" 10", name[selectionLength..], "size suffix stays after selected text");
}

static void OpeningSizeFormatsOneDecimal()
{
    AssertEqual("7.2x6.8", OpeningTakeoffService.FormatSizeFeet(7.24, 6.75), "opening one decimal size");
    AssertEqual("3.0x4.2", OpeningTakeoffService.FormatSizeFeet(3.0, 4.24), "opening keeps trailing decimal");
}

static void OpeningDefaultNameIsSizeOnly()
{
    AssertEqual("3.0x4.2", OpeningTakeoffService.BuildDefaultCountName("3.0x4.2"), "opening count name");
}

static void TakeoffCreationPolicyChoosesSafeParents()
{
    var job = new OurPlanCoreJob
    {
        RootPath = @"C:\tmp\job",
    };

    string selected = @"C:\tmp\job\Takeoffs\selected";
    string itemParent = @"C:\tmp\job\Takeoffs\item";
    string active = @"C:\tmp\job\Takeoffs\active";

    AssertEqual(job.TakeoffsRoot, TakeoffCreationPolicy.NewItemParentFolder(job), "new item parent");
    AssertEqual(selected, TakeoffCreationPolicy.NewFolderParentFolder(job, selected, itemParent, active, _ => false), "selected folder wins");
    AssertEqual(itemParent, TakeoffCreationPolicy.NewFolderParentFolder(job, "", itemParent, active, _ => false), "selected item parent wins");
    AssertEqual(active, TakeoffCreationPolicy.NewFolderParentFolder(job, "", "", active, path => path == active), "active existing folder wins");
    AssertEqual(job.TakeoffsRoot, TakeoffCreationPolicy.NewFolderParentFolder(job, "", "", active, _ => false), "root fallback");
}

static void TakeoffSectionOrderMovesSingleUp()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["b"], -1), "move should apply");
    AssertSectionOrder("b,a,c", measurements, "single up");
}

static void TakeoffSectionOrderMovesSingleDown()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["b"], 1), "move should apply");
    AssertSectionOrder("a,c,b", measurements, "single down");
}

static void TakeoffSectionOrderBlocksTopUp()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertFalse(TakeoffSectionOrderService.Move(measurements, ["a"], -1), "top cannot move up");
    AssertSectionOrder("a,b,c", measurements, "top up should not change order");
}

static void TakeoffSectionOrderBlocksBottomDown()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertFalse(TakeoffSectionOrderService.Move(measurements, ["c"], 1), "bottom cannot move down");
    AssertSectionOrder("a,b,c", measurements, "bottom down should not change order");
}

static void TakeoffSectionOrderBlocksAllSelected()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertFalse(TakeoffSectionOrderService.CanMove(measurements, ["a", "b", "c"], -1), "all selected cannot move up");
    AssertFalse(TakeoffSectionOrderService.CanMove(measurements, ["a", "b", "c"], 1), "all selected cannot move down");
    AssertFalse(TakeoffSectionOrderService.Move(measurements, ["a", "b", "c"], 1), "all selected move should be blocked");
    AssertSectionOrder("a,b,c", measurements, "all selected should not change order");
}

static void TakeoffSectionOrderMovesContiguousBlockUp()
{
    var measurements = SectionMeasurements("a", "b", "c", "d");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["b", "c"], -1), "block should move up");
    AssertSectionOrder("b,c,a,d", measurements, "contiguous block up");
}

static void TakeoffSectionOrderMovesContiguousBlockDown()
{
    var measurements = SectionMeasurements("a", "b", "c", "d");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["b", "c"], 1), "block should move down");
    AssertSectionOrder("a,d,b,c", measurements, "contiguous block down");
}

static void TakeoffSectionOrderMovesDisjointSelectionUp()
{
    var measurements = SectionMeasurements("a", "b", "c", "d", "e");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["c", "e"], -1), "disjoint selection should move up");
    AssertSectionOrder("a,c,b,e,d", measurements, "disjoint up");
}

static void TakeoffSectionOrderMovesDisjointSelectionDown()
{
    var measurements = SectionMeasurements("a", "b", "c", "d", "e");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["a", "c"], 1), "disjoint selection should move down");
    AssertSectionOrder("b,a,d,c,e", measurements, "disjoint down");
}

static void TakeoffSectionOrderIgnoresInvalidDuplicateIds()
{
    var measurements = SectionMeasurements("a", "b", "c");

    AssertTrue(TakeoffSectionOrderService.Move(measurements, ["missing", "b", "b", ""], -1), "valid selected id should move once");
    AssertSectionOrder("b,a,c", measurements, "invalid and duplicate ids should not duplicate moves");

    AssertFalse(TakeoffSectionOrderService.Move(measurements, ["missing", ""], 1), "invalid-only selection should not move");
    AssertSectionOrder("b,a,c", measurements, "invalid-only selection should leave order unchanged");
}

static void TakeoffTreeOrderKeepsCreationOrder()
{
    WithTempJob("tree_order_create", job =>
    {
        CreateRootTakeoffItem(job, "B");
        CreateTakeoffFolder(job, "A");
        CreateRootTakeoffItem(job, "C");

        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,A,C", "creation order");
    });
}

static void TakeoffTreeOrderMovesSiblingUp()
{
    WithTempJob("tree_order_up", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C");

        AssertTrue(OurPlanCoreJobStore.MoveSibling(items[1].FolderPath, -1), "move up should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,A,C", "sibling up");
    });
}

static void TakeoffTreeOrderMovesSiblingDown()
{
    WithTempJob("tree_order_down", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C");

        AssertTrue(OurPlanCoreJobStore.MoveSibling(items[0].FolderPath, 1), "move down should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,A,C", "sibling down");
    });
}

static void TakeoffTreeOrderBlocksTopUp()
{
    WithTempJob("tree_order_top", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C");

        AssertFalse(OurPlanCoreJobStore.MoveSibling(items[0].FolderPath, -1), "top move up should be blocked");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "A,B,C", "top up blocked");
    });
}

static void TakeoffTreeOrderBlocksBottomDown()
{
    WithTempJob("tree_order_bottom", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C");

        AssertFalse(OurPlanCoreJobStore.MoveSibling(items[2].FolderPath, 1), "bottom move down should be blocked");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "A,B,C", "bottom down blocked");
    });
}

static void TakeoffTreeOrderMovesSiblingBlockUp()
{
    WithTempJob("tree_order_block_up", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlanCoreJobStore.MoveSiblings([items[1].FolderPath, items[2].FolderPath], -1), "block up should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,C,A,D", "block up");
    });
}

static void TakeoffTreeOrderMovesSiblingBlockDown()
{
    WithTempJob("tree_order_block_down", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlanCoreJobStore.MoveSiblings([items[1].FolderPath, items[2].FolderPath], 1), "block down should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "A,D,B,C", "block down");
    });
}

static void TakeoffTreeOrderMovesBeforeTarget()
{
    WithTempJob("tree_order_before", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlanCoreJobStore.MoveSiblingsToPosition([items[2].FolderPath], items[0].FolderPath, after: false), "move before should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "C,A,B,D", "move before target");
    });
}

static void TakeoffTreeOrderMovesAfterTarget()
{
    WithTempJob("tree_order_after", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlanCoreJobStore.MoveSiblingsToPosition([items[0].FolderPath], items[2].FolderPath, after: true), "move after should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,C,A,D", "move after target");
    });
}

static void TakeoffTreeOrderAppendsMovedNodeIntoFolder()
{
    WithTempJob("tree_order_into_folder", job =>
    {
        string targetFolder = CreateTakeoffFolder(job, "Folder");
        CreateNestedTakeoffItem(job, targetFolder, "X");
        CreateNestedTakeoffItem(job, targetFolder, "Y");
        TakeoffItem moving = CreateRootTakeoffItem(job, "A");

        string movedPath = OurPlanCoreJobStore.MoveNode(moving.FolderPath, targetFolder);

        AssertTrue(OurPlanCoreJobStore.IsSameOrDescendant(targetFolder, movedPath), "node should move into folder");
        AssertTakeoffChildOrder(targetFolder, "X,Y,A", "moved node appended into target folder");
    });
}

static void TakeoffTreeOrderMovesDirectChildrenOutBelowFolder()
{
    WithTempJob("tree_order_out_below_folder", job =>
    {
        string folder = CreateTakeoffFolder(job, "Folder");
        TakeoffItem child = CreateNestedTakeoffItem(job, folder, "X");
        CreateNestedTakeoffItem(job, folder, "Y");
        CreateRootTakeoffItem(job, "B");

        var moved = OurPlanCoreJobStore.MoveNodes([child.FolderPath], job.TakeoffsRoot).Single();

        AssertTrue(
            OurPlanCoreJobStore.MoveSiblingsToPosition([moved.MovedPath], folder, after: true),
            "moved child should reorder after its old parent folder");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "Folder,X,B", "child moved out below folder");
        AssertTakeoffChildOrder(folder, "Y", "old parent keeps remaining child");
    });
}

static void TakeoffTreeOrderStressMovesLargeSelectionToEnd()
{
    WithTempJob("tree_order_stress_end", job =>
    {
        var items = Enumerable.Range(1, 260)
            .Select(index => CreateRootTakeoffItem(job, $"T{index:000}"))
            .ToList();
        var selected = items
            .Skip(40)
            .Take(80)
            .Select(item => item.FolderPath)
            .ToList();

        AssertTrue(OurPlanCoreJobStore.MoveSiblingsToEnd(selected, job.TakeoffsRoot), "large selection should move to end");

        IReadOnlyList<string> names = TakeoffChildNames(job.TakeoffsRoot);
        AssertEqual("260", names.Count.ToString(), "stress node count");
        AssertEqual("T040", names[39], "last untouched before moved block");
        AssertEqual("T121", names[40], "first item after moved block gap");
        AssertEqual("T041", names[180], "moved block first at end");
        AssertEqual("T120", names[^1], "moved block last at end");
    });
}

static void TakeoffTreeOrderStressBatchMovesIntoFolder()
{
    WithTempJob("tree_order_stress_batch_folder", job =>
    {
        var items = Enumerable.Range(1, 180)
            .Select(index => CreateRootTakeoffItem(job, $"T{index:000}"))
            .ToList();
        string targetFolder = CreateTakeoffFolder(job, "Target");
        var selected = items
            .Skip(60)
            .Take(60)
            .Select(item => item.FolderPath)
            .ToList();

        IReadOnlyList<(string SourcePath, string MovedPath)> moved = OurPlanCoreJobStore.MoveNodes(selected, targetFolder);

        IReadOnlyList<string> targetNames = TakeoffChildNames(targetFolder);
        IReadOnlyList<string> rootNames = TakeoffChildNames(job.TakeoffsRoot);
        AssertEqual("60", moved.Count.ToString(), "batch moved count");
        AssertEqual("60", targetNames.Count.ToString(), "target child count");
        AssertEqual("T061", targetNames[0], "batch move preserves first selected order");
        AssertEqual("T120", targetNames[^1], "batch move preserves last selected order");
        AssertEqual("121", rootNames.Count.ToString(), "root count after batch move");
    });
}

static void TakeoffAutoRoutingSendsSqftAreasToSqfts()
{
    WithTempJob("auto_route_sqfts", job =>
    {
        TakeoffAutoRouteResult baseRoute = TakeoffAutoRoutingService.ResolveRoute(
            job,
            job.TakeoffsRoot,
            "base",
            "area",
            "A001",
            "");
        TakeoffAutoRouteResult firstRoute = TakeoffAutoRoutingService.ResolveRoute(
            job,
            job.TakeoffsRoot,
            "1st",
            "area",
            "A101 1st",
            "");
        TakeoffAutoRouteResult porchRoute = TakeoffAutoRoutingService.ResolveRoute(
            job,
            job.TakeoffsRoot,
            "porch",
            "area",
            "A101 1st",
            "");

        AssertTrue(baseRoute.Routed, "base area should route");
        AssertEqual("sqfts", OurPlanCoreJobStore.DisplayName(baseRoute.ParentFolder), "sqft parent");
        OurPlanCoreJobStore.CreateTakeoffItem(job, porchRoute.ParentFolder, "porch", "#FF4444", "area");
        OurPlanCoreJobStore.CreateTakeoffItem(job, firstRoute.ParentFolder, "1st", "#FF4444", "area");
        OurPlanCoreJobStore.CreateTakeoffItem(job, baseRoute.ParentFolder, "base", "#FF4444", "area");

        AssertTrue(TakeoffAutoRoutingService.SortFolder(job, baseRoute.ParentFolder), "sqft folder should sort");
        AssertTakeoffChildOrder(baseRoute.ParentFolder, "base,1st,porch", "sqft takeoff order");
    });
}

static void TakeoffAutoRoutingSendsWallLinesToSheetFloorWalls()
{
    WithTempJob("auto_route_walls", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A201 2nd");
        TakeoffAutoRouteResult route = TakeoffAutoRoutingService.ResolveRoute(
            job,
            job.TakeoffsRoot,
            "ext 9.98",
            "line",
            page.Name,
            page.FolderPath);

        AssertTrue(route.Routed, "wall line should route");
        AssertEqual("2nd floor walls", OurPlanCoreJobStore.DisplayName(route.ParentFolder), "wall floor parent");
        OurPlanCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "2x4 walls", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "dem 2x4", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "2x8 walls", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "corners", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "corr 2x6", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "2x6 walls", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "ext 9.98", "#FF4444", "line");

        AssertTrue(TakeoffAutoRoutingService.SortFolder(job, route.ParentFolder), "wall folder should sort");
        AssertTakeoffChildOrder(
            route.ParentFolder,
            "corners,ext 9.98,corr 2x6,dem 2x4,2x8 walls,2x6 walls,2x4 walls",
            "wall takeoff order");
    });
}

static void TakeoffAutoRoutingSortsPageLegendLabels()
{
    var items = new[]
    {
        new TakeoffItem { Name = "porch", MeasurementType = "area" },
        new TakeoffItem { Name = "2x4 walls", MeasurementType = "line" },
        new TakeoffItem { Name = "base", MeasurementType = "area" },
        new TakeoffItem { Name = "ext 9.98", MeasurementType = "line" },
        new TakeoffItem { Name = "2x8 walls", MeasurementType = "line" },
        new TakeoffItem { Name = "corners", MeasurementType = "line" },
        new TakeoffItem { Name = "14/S502", MeasurementType = "line" },
        new TakeoffItem { Name = "13/S101", MeasurementType = "line" },
        new TakeoffItem { Name = "2/S102", MeasurementType = "line" },
        new TakeoffItem { Name = "14/S101", MeasurementType = "line" },
        new TakeoffItem { Name = "1st", MeasurementType = "area" },
        new TakeoffItem { Name = "count 10", MeasurementType = "point" },
        new TakeoffItem { Name = "count 2", MeasurementType = "point" },
        new TakeoffItem { Name = "count 1", MeasurementType = "point" },
    };

    string order = string.Join(",",
        TakeoffAutoRoutingService.SortPageLegendItems(items)
            .Select(item => item.Name));

    AssertEqual(
        "corners,ext 9.98,2x8 walls,2x4 walls,13/S101,14/S101,2/S102,14/S502,base,1st,porch,count 1,count 2,count 10",
        order,
        "page legend label sort");
}

static void SheetLegendHiddenMeasurementsKeepNewMeasurementsVisible()
{
    WithTempJob("sheet_legend_hidden_measurements", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A701");
        string walls = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        TakeoffItem item = CreateMeasuredTakeoffItem(
            job,
            walls,
            "Walls",
            "line",
            page.FolderPath,
            [new SKPoint(0, 0), new SKPoint(10, 0)]);
        page.HiddenMeasurements = [item.Measurements[0].Id];

        string hidden = string.Join(",",
            SheetLegendBuilder.Build(job, page, [item], UnitMode.Imperial)
                .Select(entry => entry.Name));
        AssertEqual("", hidden, "old hidden measurement should not appear in legend");

        item.Measurements.Add(new Measurement
        {
            Name = "Walls",
            MType = "line",
            Color = item.Color,
            PageFolder = page.FolderPath,
            TakeoffFolder = item.FolderPath,
            Points = [new SKPoint(20, 0), new SKPoint(30, 0)],
        });

        string visible = string.Join(",",
            SheetLegendBuilder.Build(job, page, [item], UnitMode.Imperial)
                .Select(entry => entry.Name));
        AssertEqual("Walls", visible, "new measurement should remain visible under snapshot hide");
    });
}

static void ViewportHiddenMeasurementIdsFilterActivePageMeasurements()
{
    RunOnStaThread(() =>
    {
        const string pageFolder = @"C:\job\Pages\A701";
        const string takeoffFolder = @"C:\job\Takeoffs\walls";
        var oldMeasurement = new Measurement
        {
            Id = "m-old",
            Name = "Walls",
            MType = "line",
            Color = "#FF0000",
            PageFolder = pageFolder,
            TakeoffFolder = takeoffFolder,
            Points = [new SKPoint(0, 0), new SKPoint(10, 0)],
        };
        var newMeasurement = new Measurement
        {
            Id = "m-new",
            Name = "Walls",
            MType = "line",
            Color = "#FF0000",
            PageFolder = pageFolder,
            TakeoffFolder = takeoffFolder,
            Points = [new SKPoint(20, 0), new SKPoint(30, 0)],
        };

        var viewport = new PdfViewport();
        viewport.LoadMeasurements([oldMeasurement, newMeasurement]);
        SetPrivateField(viewport, "_pageFolder", pageFolder);

        AssertEqual(
            "2",
            ActiveViewportMeasurements(viewport).Count.ToString(),
            "viewport starts with both measurements visible");

        viewport.SetHiddenMeasurementIds(["m-old"]);
        AssertEqual(
            "m-new",
            string.Join(",", ActiveViewportMeasurements(viewport).Select(measurement => measurement.Id)),
            "viewport hide snapshot must filter hidden measurement IDs while keeping newer IDs visible");

        viewport.SetHiddenMeasurementIds([]);
        AssertEqual(
            "2",
            ActiveViewportMeasurements(viewport).Count.ToString(),
            "viewport show all must restore hidden measurement IDs");
    });
}

static void TakeoffDetailRefsSortBySheetThenDetail()
{
    var items = new[]
    {
        new TakeoffItem { Name = "14/S502", MeasurementType = "line" },
        new TakeoffItem { Name = "13/S101", MeasurementType = "line" },
        new TakeoffItem { Name = "2/S102", MeasurementType = "line" },
        new TakeoffItem { Name = "14/S101", MeasurementType = "line" },
        new TakeoffItem { Name = "13/S5.10", MeasurementType = "line" },
        new TakeoffItem { Name = "2/S5.5", MeasurementType = "line" },
        new TakeoffItem { Name = "1/S5.5", MeasurementType = "line" },
        new TakeoffItem { Name = "4/S5.10", MeasurementType = "line" },
        new TakeoffItem { Name = "3/S5.2", MeasurementType = "line" },
        new TakeoffItem { Name = "8/S6.1", MeasurementType = "line" },
    };

    string legendOrder = string.Join(",",
        TakeoffAutoRoutingService.SortPageLegendItems(items)
            .Select(item => item.Name));
    const string expected =
        "3/S5.2,1/S5.5,2/S5.5,4/S5.10,13/S5.10,8/S6.1,13/S101,14/S101,2/S102,14/S502";
    AssertEqual(expected, legendOrder, "detail refs legend order");

    WithTempJob("detail_ref_sort", job =>
    {
        OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "14/S502", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "13/S101", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "2/S102", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "14/S101", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "13/S5.10", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "2/S5.5", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "1/S5.5", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "4/S5.10", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "3/S5.2", "#FF4444", "line");
        OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "8/S6.1", "#FF4444", "line");

        OurPlanCoreJobStore.SortTakeoffChildren(job.TakeoffsRoot, descending: false);
        AssertTakeoffChildOrder(job.TakeoffsRoot, expected, "detail refs takeoff tree order");
    });
}

static void SheetLegendLiveAutoIgnoresStoredAutoOrder()
{
    WithTempJob("sheet_legend_live_auto", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A701");
        string walls = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        TakeoffItem twoByFour = CreateMeasuredTakeoffItem(
            job,
            walls,
            "2x4 walls",
            "line",
            page.FolderPath,
            [new SKPoint(0, 0), new SKPoint(10, 0)]);
        TakeoffItem corners = CreateMeasuredTakeoffItem(
            job,
            walls,
            "corners",
            "line",
            page.FolderPath,
            [new SKPoint(0, 0), new SKPoint(0, 10)]);
        page.LegendTakeoffOrder =
        [
            Path.GetRelativePath(job.TakeoffsRoot, twoByFour.FolderPath),
            Path.GetRelativePath(job.TakeoffsRoot, corners.FolderPath),
        ];

        page.LegendTakeoffOrderMode = "auto";
        string autoOrder = string.Join(",",
            SheetLegendBuilder.Build(job, page, [twoByFour, corners], UnitMode.Imperial)
                .Select(entry => entry.Name));
        AssertEqual("corners,2x4 walls", autoOrder, "auto mode should use label rules");

        page.LegendTakeoffOrderMode = "manual";
        string manualOrder = string.Join(",",
            SheetLegendBuilder.Build(job, page, [twoByFour, corners], UnitMode.Imperial)
                .Select(entry => entry.Name));
        AssertEqual("2x4 walls,corners", manualOrder, "manual mode should keep saved order");
    });
}

static void MassingDirectSqftsUsesFloorLabels()
{
    WithTempJob("massing_sqfts", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A101");
        string sqfts = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
        CreateMeasuredTakeoffItem(job, sqfts, "1st", "area", page.FolderPath, RectPoints(0, 0, 10, 10));
        CreateMeasuredTakeoffItem(job, sqfts, "2nd", "area", page.FolderPath, RectPoints(20, 0, 10, 10));

        SmartMassingDraft draft = SmartMassingDraftService.BuildDraftFromWallTakeoffs(job, 10);

        AssertEqual("draft_from_takeoffs", draft.Status, "direct sqfts draft status");
        AssertEqual("2", draft.Footprints.Count.ToString(), "direct sqfts footprint count");
        AssertEqual("1,2", string.Join(",", draft.Footprints.Select(footprint => footprint.Level)), "direct sqfts levels");
        AssertClose(0, draft.Footprints.First(footprint => footprint.Level == 1).BaseElevation, "direct sqfts 1st base");
        AssertClose(10, draft.Footprints.First(footprint => footprint.Level == 2).BaseElevation, "direct sqfts 2nd base");
        AssertTrue(draft.Roof.Guides.Any(guide => guide.Kind == "eave_outline"), "direct sqfts roof should include eave guide");
        AssertTrue(draft.Roof.Guides.Any(guide => guide.Kind == "axis_candidate"), "direct sqfts roof should include axis guide");
        AssertTrue(draft.Roof.Planes.Count >= 2, "direct sqfts roof should build candidate planes");
        AssertTrue(
            draft.Roof.Guides.SelectMany(guide => guide.SourceMarkerIds).Any(id => id.StartsWith("takeoff:", StringComparison.OrdinalIgnoreCase)),
            "direct sqfts roof guides should trace to takeoff measurements");
    });
}

static void MassingWallsParsesUpperFloorFolders()
{
    WithTempJob("massing_walls_4th", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A401");
        string walls = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        string fourth = OurPlanCoreJobStore.CreateTakeoffFolder(job, walls, "4th floor walls");
        CreateMeasuredTakeoffItem(job, fourth, "ext 10", "line", page.FolderPath, RectPoints(0, 0, 10, 10));

        SmartMassingDraft draft = SmartMassingDraftService.BuildDraftFromWallTakeoffs(job, 10);

        AssertEqual("draft_from_takeoffs", draft.Status, "4th floor walls draft status");
        AssertEqual("1", draft.Footprints.Count.ToString(), "4th floor walls footprint count");
        AssertEqual("4", draft.Footprints[0].Level.ToString(), "4th floor walls parsed level");
        AssertClose(30, draft.Footprints[0].BaseElevation, "4th floor walls base");
        AssertTrue(draft.Roof.Guides.Any(guide => guide.Kind == "axis_candidate"), "4th floor walls roof should include axis guide");
        AssertTrue(draft.Roof.Planes.Count >= 2, "4th floor walls roof should build candidate planes");
    });
}

static void MassingRoofTakeoffsLinkEaveRakeGable()
{
    WithTempJob("massing_roof_takeoffs", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A501 Roof");
        string sqft = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sft");
        string roof = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "eve rake");
        string gables = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "gables");
        CreateMeasuredTakeoffItem(job, sqft, "1st", "area", page.FolderPath, RectPoints(10, 10, 30, 20));
        CreateMeasuredTakeoffItem(job, roof, "eve", "line", page.FolderPath, [new SKPoint(8, 8), new SKPoint(42, 8)]);
        CreateMeasuredTakeoffItem(job, roof, "rake", "line", page.FolderPath, [new SKPoint(8, 8), new SKPoint(25, 0), new SKPoint(42, 8)]);
        CreateMeasuredTakeoffItem(job, gables, "gable", "area", page.FolderPath, RectPoints(8, 8, 34, 12));

        SmartMassingDraft draft = SmartMassingDraftService.BuildDraftFromWallTakeoffs(job, 10);

        AssertEqual("draft_from_takeoffs", draft.Status, "roof takeoff draft status");
        AssertEqual("gable", draft.Roof.Type, "roof takeoffs should infer gable roof type");
        AssertTrue(draft.Roof.Guides.Any(guide => guide.Kind == "eave"), "roof takeoffs should include eave guide");
        AssertTrue(draft.Roof.Guides.Any(guide => guide.Kind == "rake"), "roof takeoffs should include rake guide");
        AssertTrue(draft.Roof.Guides.Any(guide => guide.Kind == "gable_area"), "roof takeoffs should include gable guide");
        AssertTrue(draft.Roof.SourceMarkerIds.Any(id => id.StartsWith("takeoff:", StringComparison.OrdinalIgnoreCase)), "roof takeoff sources should be linked");
        AssertTrue(draft.Roof.Planes.Count >= 2, "roof takeoffs should still build candidate planes");
    });
}

static void MassingAiPlanClassifiesAmbiguousTakeoffs()
{
    WithTempJob("massing_ai_plan", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A601 Mixed");
        string misc = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "misc");
        TakeoffItem plate = CreateMeasuredTakeoffItem(job, misc, "poly A", "area", page.FolderPath, RectPoints(10, 10, 30, 20));
        TakeoffItem edge = CreateMeasuredTakeoffItem(job, misc, "edge north", "line", page.FolderPath, [new SKPoint(8, 8), new SKPoint(42, 8)]);
        TakeoffItem slope = CreateMeasuredTakeoffItem(job, misc, "slope side", "line", page.FolderPath, [new SKPoint(8, 8), new SKPoint(25, 0), new SKPoint(42, 8)]);
        TakeoffItem end = CreateMeasuredTakeoffItem(job, misc, "end face", "area", page.FolderPath, RectPoints(8, 8, 34, 12));

        var plan = new SmartMassingTakeoffAiPlan
        {
            Summary = "Ambiguous misc takeoffs classified for 3D.",
            RoofType = "gable",
            Assignments =
            [
                AiAssignment(job, plate, "floor_plate", 1),
                AiAssignment(job, edge, "eave", 1),
                AiAssignment(job, slope, "rake", 1),
                AiAssignment(job, end, "gable", 1),
            ],
        };

        SmartMassingDraft draft = SmartMassingDraftService.BuildDraftFromWallTakeoffs(job, 10, plan);

        AssertEqual("draft_from_takeoffs", draft.Status, "ai plan draft status");
        AssertEqual("1", draft.Footprints.Count.ToString(), "ai plan footprint count");
        AssertEqual("gable", draft.Roof.Type, "ai plan roof type");
        AssertTrue(draft.Roof.Guides.Any(guide => guide.Kind == "eave"), "ai plan should link eave");
        AssertTrue(draft.Roof.Guides.Any(guide => guide.Kind == "rake"), "ai plan should link rake");
        AssertTrue(draft.Roof.Guides.Any(guide => guide.Kind == "gable_area"), "ai plan should link gable area");
    });
}

static void ThreeDWallParserHandlesDefaultAndSizes()
{
    ThreeDWallSpec ext = ThreeDWallTakeoffBuilder.ParseSpec(new TakeoffItem { Name = "ext 9.1" });
    AssertClose(9.1, ext.HeightFeet, "ext height");
    AssertClose(6.0, ext.ThicknessInches, "default wall thickness");

    ThreeDWallSpec ext2x4 = ThreeDWallTakeoffBuilder.ParseSpec(new TakeoffItem { Name = "ext 2x4 9.1" });
    AssertClose(9.1, ext2x4.HeightFeet, "2x4 height");
    AssertClose(4.0, ext2x4.ThicknessInches, "2x4 wall thickness");

    ThreeDWallSpec double2x4 = ThreeDWallTakeoffBuilder.ParseSpec(new TakeoffItem { Name = "dem (2) 2x4 10.8" });
    AssertClose(10.8, double2x4.HeightFeet, "double 2x4 height");
    AssertClose(8.0, double2x4.ThicknessInches, "double 2x4 wall thickness");
    AssertEqual("2", double2x4.PlyCount.ToString(), "double 2x4 ply count");
}

static void ThreeDWallBuilderCreatesSegmentsFromScaledLines()
{
    var item = new TakeoffItem
    {
        Name = "ext 2x6 9.1",
        Color = "#123456",
        FolderPath = @"C:\job\Takeoffs\walls\ext",
        MeasurementType = "line",
    };
    item.Measurements.Add(new Measurement
    {
        Id = "m1",
        MType = "line",
        ScaleMetersPerPt = 0.3048,
        Points = [new SKPoint(0, 0), new SKPoint(10, 0), new SKPoint(10, 5)],
    });

    ThreeDWallBuildResult result = ThreeDWallTakeoffBuilder.BuildWalls([item], null, measurement => measurement.ScaleMetersPerPt);
    AssertEqual("2", result.Walls.Count.ToString(), "polyline should produce wall segment per leg");
    AssertClose(10.0, result.Walls[0].EndXFeet - result.Walls[0].StartXFeet, "first wall length");
    AssertClose(9.1, result.Walls[0].HeightFeet, "wall height");
    AssertClose(6.0, result.Walls[0].ThicknessInches, "wall thickness");
    AssertEqual("ext 2x6 9.1", result.Walls[0].Label, "wall label");
}

static void ThreeDAutoBuilderStacksFloorsByMaxWallHeight()
{
    WithTempJob("3d_auto_wall_levels", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A101");
        string walls = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        string first = OurPlanCoreJobStore.CreateTakeoffFolder(job, walls, "1st");
        string second = OurPlanCoreJobStore.CreateTakeoffFolder(job, walls, "2nd");
        CreateMeasuredTakeoffItem(job, first, "ext 2x6 9.1", "line", page.FolderPath, [new SKPoint(0, 0), new SKPoint(10, 0)]);
        CreateMeasuredTakeoffItem(job, first, "dem (2) 2x4 10.8", "line", page.FolderPath, [new SKPoint(0, 2), new SKPoint(10, 2)]);
        CreateMeasuredTakeoffItem(job, second, "ext 2x4 8.5", "line", page.FolderPath, [new SKPoint(0, 4), new SKPoint(10, 4)]);

        ThreeDWallAutoBuildResult result = ThreeDWallAutoBuilder.Build(job, measurement => measurement.ScaleMetersPerPt);

        AssertEqual("3", result.Model.Walls.Count.ToString(), "auto should build all floor wall segments");
        AssertClose(0, result.Model.Walls.First(wall => wall.LevelKey == "1st").BaseElevationFeet, "first floor base");
        AssertClose(10.8, result.Model.Walls.First(wall => wall.LevelKey == "2nd").BaseElevationFeet, "second floor base should use first floor max height");
        AssertClose(10.8, result.Model.Levels.First(level => level.Label == "1st").HeightFeet, "first level height should be max wall height");
    });
}

static void ThreeDAutoBuilderAddsSqftSlabsAtFloorLevels()
{
    WithTempJob("3d_auto_sqft_slabs", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A101");
        string walls = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        string firstWalls = OurPlanCoreJobStore.CreateTakeoffFolder(job, walls, "1st");
        CreateMeasuredTakeoffItem(job, firstWalls, "ext 10", "line", page.FolderPath, [new SKPoint(0, 0), new SKPoint(10, 0)]);
        string sqfts = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
        CreateMeasuredTakeoffItem(job, sqfts, "1st", "area", page.FolderPath, RectPoints(0, 0, 10, 10));
        CreateMeasuredTakeoffItem(job, sqfts, "2nd", "area", page.FolderPath, RectPoints(20, 0, 10, 10));

        ThreeDWallAutoBuildResult result = ThreeDWallAutoBuilder.Build(job, measurement => measurement.ScaleMetersPerPt);

        AssertEqual("2", result.Model.Slabs.Count.ToString(), "auto should build sqft slabs");
        AssertClose(0, result.Model.Slabs.First(slab => slab.LevelKey == "1st").ElevationFeet, "first slab elevation");
        AssertClose(10, result.Model.Slabs.First(slab => slab.LevelKey == "2nd").ElevationFeet, "second slab elevation");
    });
}

static void ThreeDAutoBuilderAddsRfAreaAsRoofSlab()
{
    WithTempJob("3d_auto_rf_roof_slab", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A101");
        string walls = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        string firstWalls = OurPlanCoreJobStore.CreateTakeoffFolder(job, walls, "1st");
        CreateMeasuredTakeoffItem(job, firstWalls, "ext 10", "line", page.FolderPath, [new SKPoint(0, 0), new SKPoint(10, 0)]);
        string sqfts = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
        CreateMeasuredTakeoffItem(job, sqfts, "1st", "area", page.FolderPath, RectPoints(0, 0, 10, 10));
        CreateMeasuredTakeoffItem(job, sqfts, "rf", "area", page.FolderPath, RectPoints(2, 2, 12, 8));

        ThreeDWallAutoBuildResult result = ThreeDWallAutoBuilder.Build(job, measurement => measurement.ScaleMetersPerPt);

        ThreeDFloorSlab roof = result.Model.Slabs.First(slab => slab.LevelKey == "roof");
        AssertEqual("2", result.Model.Slabs.Count.ToString(), "auto should include floor and roof slabs");
        AssertClose(10, roof.ElevationFeet, "rf area should sit at the top level elevation");
        AssertEqual("rf", roof.Label, "rf slab label");
    });
}

static void ThreeDRoofFootprintBuilderCreatesRakeEdgesFromRfAreas()
{
    WithTempJob("3d_rf_roof_footprint", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A501 Roof");
        string sqfts = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
        TakeoffItem rfA = CreateMeasuredTakeoffItem(job, sqfts, "rf A", "area", page.FolderPath, RectPoints(0, 0, 20, 10));
        TakeoffItem rfB = CreateMeasuredTakeoffItem(job, sqfts, "rf B", "area", page.FolderPath, RectPoints(22, 0, 14, 12));

        ThreeDRoofFootprintBuildResult footprint = ThreeDRoofFootprintBuildService.Build(
            ThreeDRoofFootprintBuildService.SourcesFromItems([rfA, rfB]),
            measurement => measurement.ScaleMetersPerPt,
            11,
            0.5);

        AssertEqual("2", footprint.Slabs.Count.ToString(), "rf roof footprint slab count");
        AssertEqual("8", footprint.Guides.Count.ToString(), "rf roof boundary edge count");
        AssertTrue(footprint.Guides.All(guide => guide.Kind == ThreeDRoofGuideKinds.Rake), "rf roof should default boundary edges to rake");
        AssertTrue(footprint.Guides.All(guide => Math.Abs(guide.PitchRisePerFoot) < 0.0001), "rf roof should not apply pitch until an edge is marked eave");

        foreach (ThreeDRoofGuide guide in footprint.Guides)
        {
            guide.Kind = ThreeDRoofGuideKinds.Eave;
            guide.DefinesSlope = true;
            guide.PitchRisePerFoot = 0.5;
            guide.Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Eave);
        }

        var model = new ThreeDWallModel
        {
            Slabs = footprint.Slabs,
            RoofGuides = footprint.Guides,
        };
        ThreeDRoofBuildResult roof = ThreeDRoofBuildService.Build(model);

        AssertTrue(!roof.PlaneBuildBlocked, "marked rf roof eave edges should build roof surfaces");
        AssertTrue(roof.Planes.Count > 2, "marked rf roof eaves should generate mesh faces");
        AssertTrue(roof.Guides.Any(guide => guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus), "marked eaves should generate automatic roof seams");
        AssertTrue(roof.Guides.Any(guide => guide.Kind == ThreeDRoofGuideKinds.Ridge), "marked opposite eaves should generate ridge seams");
        AssertTrue(
            roof.Guides
                .Where(guide => guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus)
                .SelectMany(guide => guide.Points)
                .Any(point => point.YFeet > 11.1),
            "generated roof seams should carry 3D heights");
    });
}

static void ThreeDAutoRoofSelectsOppositeEaves()
{
    WithTempJob("3d_auto_roof_eaves", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A501 Roof");
        string sqfts = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
        TakeoffItem rf = CreateMeasuredTakeoffItem(job, sqfts, "rf", "area", page.FolderPath, RectPoints(0, 0, 30, 12));

        ThreeDRoofFootprintBuildResult footprint = ThreeDRoofFootprintBuildService.Build(
            ThreeDRoofFootprintBuildService.SourcesFromItems([rf]),
            measurement => measurement.ScaleMetersPerPt,
            10,
            0.5);

        ThreeDRoofAutoGuideResult auto = ThreeDRoofAutoGuideService.ApplyAutoEaves(footprint.Guides, 0.5);
        List<ThreeDRoofGuide> eaves = footprint.Guides
            .Where(guide => ThreeDRoofGuideKinds.Normalize(guide.Kind) == ThreeDRoofGuideKinds.Eave)
            .ToList();

        AssertEqual("1", auto.RoofRegionCount.ToString(), "auto roof should find one roof base region");
        AssertEqual("2", auto.EaveGuideCount.ToString(), "auto roof should select two opposite eaves");
        AssertEqual("2", eaves.Count.ToString(), "auto roof should mark two eave guides");
        AssertTrue(eaves.All(guide => string.Equals(guide.AdjustmentStatus, ThreeDRoofAutoGuideService.AutoAdjustmentStatus, StringComparison.OrdinalIgnoreCase)), "auto eaves should be marked as auto");
        AssertTrue(eaves.All(guide => Math.Abs(guide.PitchRisePerFoot - 0.5) < 0.0001), "auto eaves should receive pitch");

        var model = new ThreeDWallModel
        {
            Slabs = footprint.Slabs,
            RoofGuides = footprint.Guides,
        };
        ThreeDRoofBuildResult roof = ThreeDRoofBuildService.Build(model);
        AssertTrue(!roof.PlaneBuildBlocked, "auto-selected eaves should generate roof surfaces");
        AssertTrue(roof.Planes.Count > 0, "auto roof should create preview mesh faces");
    });
}

static void ThreeDAutoRoofPreservesManualEaves()
{
    WithTempJob("3d_auto_roof_manual_eave", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A501 Roof");
        string sqfts = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
        TakeoffItem rf = CreateMeasuredTakeoffItem(job, sqfts, "rf", "area", page.FolderPath, RectPoints(0, 0, 30, 12));

        ThreeDRoofFootprintBuildResult footprint = ThreeDRoofFootprintBuildService.Build(
            ThreeDRoofFootprintBuildService.SourcesFromItems([rf]),
            measurement => measurement.ScaleMetersPerPt,
            10,
            0.5);

        footprint.Guides[0].Kind = ThreeDRoofGuideKinds.Eave;
        footprint.Guides[0].DefinesSlope = true;
        footprint.Guides[0].Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Eave);
        footprint.Guides[0].PitchRisePerFoot = 0.25;
        footprint.Guides[0].AdjustmentStatus = "manual";

        ThreeDRoofAutoGuideResult auto = ThreeDRoofAutoGuideService.ApplyAutoEaves(footprint.Guides, 0.5);

        AssertEqual("0", auto.EaveGuideCount.ToString(), "auto roof should not override manual eave region");
        AssertEqual("1", auto.SkippedManualRegionCount.ToString(), "manual eave region should be reported");
        AssertEqual("manual", footprint.Guides[0].AdjustmentStatus, "manual eave status should remain");
        AssertClose(0.25, footprint.Guides[0].PitchRisePerFoot, "manual eave pitch should remain");
    });
}

static void ThreeDRoofBaseBuilderUnionsAdjacentRfAreas()
{
    WithTempJob("3d_roof_base_union", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A501 Roof");
        string sqfts = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
        TakeoffItem rfA = CreateMeasuredTakeoffItem(job, sqfts, "rf A", "area", page.FolderPath, RectPoints(0, 0, 20, 10));
        TakeoffItem rfB = CreateMeasuredTakeoffItem(job, sqfts, "rf B", "area", page.FolderPath, RectPoints(20, 0, 10, 20));

        ThreeDRoofFootprintBuildResult footprint = ThreeDRoofFootprintBuildService.Build(
            ThreeDRoofFootprintBuildService.SourcesFromItems([rfA, rfB]),
            measurement => measurement.ScaleMetersPerPt,
            11,
            0.5);

        AssertEqual("1", footprint.Slabs.Count.ToString(), "adjacent roof areas should become one roof base layer");
        AssertEqual("6", footprint.Slabs[0].Points.Count.ToString(), "L-shaped roof base should keep the outer turn");
        AssertEqual("6", footprint.Guides.Count.ToString(), "unified roof base should expose one edge per outer side");
        AssertTrue(footprint.Guides.All(guide => guide.Kind == ThreeDRoofGuideKinds.Rake), "roof base edges default to rake");
    });
}

static void ThreeDRoofGenerationRequiresEaveEdges()
{
    var model = new ThreeDWallModel
    {
        Slabs =
        [
            RoofSlab("roof base", 0, 0, 20, 10),
        ],
        RoofGuides =
        [
            RoofGuide(ThreeDRoofGuideKinds.Rake, 0, 0, 20, 0),
            RoofGuide(ThreeDRoofGuideKinds.Rake, 20, 0, 20, 10),
        ],
    };

    ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);

    AssertTrue(result.PlaneBuildBlocked, "roof generation should wait for selected eave edges");
    AssertEqual("0", result.Planes.Count.ToString(), "rake-only roof base should not generate a fake roof");
    AssertTrue(result.Messages.Any(message => message.Contains("Slope", StringComparison.OrdinalIgnoreCase)), "missing slope-defining selection should be explained");
}

static void ThreeDRoofEavePitchGeneratesComplexFootprintMesh()
{
    WithTempJob("3d_roof_complex_mesh", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A501 Roof");
        string sqfts = OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
        TakeoffItem rfA = CreateMeasuredTakeoffItem(job, sqfts, "rf A", "area", page.FolderPath, RectPoints(0, 0, 20, 10));
        TakeoffItem rfB = CreateMeasuredTakeoffItem(job, sqfts, "rf B", "area", page.FolderPath, RectPoints(20, 0, 10, 20));

        ThreeDRoofFootprintBuildResult footprint = ThreeDRoofFootprintBuildService.Build(
            ThreeDRoofFootprintBuildService.SourcesFromItems([rfA, rfB]),
            measurement => measurement.ScaleMetersPerPt,
            11,
            0.5);
        foreach (ThreeDRoofGuide guide in footprint.Guides)
        {
            guide.Kind = ThreeDRoofGuideKinds.Eave;
            guide.DefinesSlope = true;
            guide.PitchRisePerFoot = 0.5;
            guide.Color = ThreeDRoofGuideKinds.Color(ThreeDRoofGuideKinds.Eave);
        }

        var model = new ThreeDWallModel
        {
            Slabs = footprint.Slabs,
            RoofGuides = footprint.Guides,
        };
        ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);

        AssertTrue(!result.PlaneBuildBlocked, "selected eave edges should generate a roof mesh");
        AssertTrue(result.Planes.Count >= 4, "complex roof base should generate distinct roof faces");
        AssertTrue(result.Planes.Any(plane => plane.Kind == "roof_face_envelope"), "roof should include generated envelope faces");
        AssertTrue(
            result.Planes.All(plane => plane.Kind is "roof_face_envelope" or "roof_rake_triangle" or "roof_rake_face"),
            "roof should be face based, not a gridded height preview");
        AssertTrue(result.Guides.Any(guide => guide.Kind == ThreeDRoofGuideKinds.Valley), "complex roof turns should generate valley seams");
        AssertTrue(
            result.Guides
                .Where(guide => guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus)
                .SelectMany(guide => guide.Points)
                .Any(point => point.YFeet > 11.1),
            "complex generated seams should carry 3D heights");
        AssertTrue(result.Planes.SelectMany(plane => plane.Points).Max(point => point.YFeet) > 12, "pitched roof should rise above base elevation");
        AssertTrue(result.Messages.Any(message => message.Contains("eave", StringComparison.OrdinalIgnoreCase)), "generation should mention eave pitch source");
    });
}

static void ThreeDRoofEavePitchGeneratesRakeGableTriangles()
{
    var model = new ThreeDWallModel
    {
        Slabs =
        [
            RoofSlab("roof base", 0, 0, 20, 10),
        ],
        RoofGuides =
        [
            RoofGuide(ThreeDRoofGuideKinds.Eave, 0, 0, 20, 0, 0.5),
            RoofGuide(ThreeDRoofGuideKinds.Eave, 20, 10, 0, 10, 0.5),
            RoofGuide(ThreeDRoofGuideKinds.Rake, 0, 10, 0, 0),
            RoofGuide(ThreeDRoofGuideKinds.Rake, 20, 0, 20, 10),
        ],
    };

    ThreeDRoofBuildResult result = ThreeDRoofBuildService.Build(model);
    List<ThreeDRoofPlane> rakeTriangles = result.Planes
        .Where(plane => plane.Kind == "roof_rake_triangle")
        .ToList();

    AssertTrue(!result.PlaneBuildBlocked, "opposite eaves should build a gable roof");
    AssertEqual("2", rakeTriangles.Count.ToString(), "rake edges should close as two gable triangles");
    AssertTrue(rakeTriangles.All(plane => plane.Points.Count == 3), "rake closure should be triangular for a simple gable");
    AssertTrue(rakeTriangles.All(plane => plane.Points.Max(point => point.YFeet) > 12), "rake triangle should rise to the ridge");
    AssertTrue(
        result.Guides.Any(guide => guide.Status == ThreeDRoofPreviewBuilder.GeneratedSeamStatus &&
                                   guide.Kind == ThreeDRoofGuideKinds.Ridge),
        "gable roof should still generate the ridge guide");
}

static void ThreeDModelStorePersistsGeneratedModel()
{
    WithTempJob("3d_model_store", job =>
    {
        var model = new ThreeDWallModel
        {
            Source = "test",
            Levels =
            [
                new ThreeDFloorLevel { Label = "1st", Ordinal = 1, BaseElevationFeet = 0, HeightFeet = 9.1 },
            ],
            Walls =
            [
                new ThreeDWallSegment
                {
                    TakeoffName = "ext",
                    StartXFeet = 0,
                    StartZFeet = 0,
                    EndXFeet = 10,
                    EndZFeet = 0,
                    HeightFeet = 9.1,
                    ThicknessInches = 6,
                    LevelKey = "1st",
                    GroupKey = "1st|ext",
                },
            ],
            Slabs =
            [
                new ThreeDFloorSlab
                {
                    Label = "1st",
                    LevelKey = "1st",
                    Points =
                    [
                        new ThreeDPoint { XFeet = 0, ZFeet = 0 },
                        new ThreeDPoint { XFeet = 10, ZFeet = 0 },
                        new ThreeDPoint { XFeet = 10, ZFeet = 8 },
                    ],
                },
            ],
        };

        ThreeDModelStore.Save(job, model);
        ThreeDWallModel? loaded = ThreeDModelStore.Load(job);

        AssertTrue(loaded != null, "saved 3d model should load");
        AssertEqual("1", loaded!.Walls.Count.ToString(), "loaded wall count");
        AssertEqual("1", loaded.Slabs.Count.ToString(), "loaded slab count");
        AssertClose(9.1, loaded.Levels[0].HeightFeet, "loaded level height");
    });
}

static void ThreeDModelStorePersistsRoofGuides()
{
    WithTempJob("3d_model_store_roof", job =>
    {
        var model = new ThreeDWallModel
        {
            Source = "roof_test",
            RoofGuides =
            [
                new ThreeDRoofGuide
                {
                    Kind = ThreeDRoofGuideKinds.Eave,
                    Label = "Eave 1",
                    PageFolder = Path.Combine(job.PagesRoot, "A101"),
                    ElevationFeet = 10,
                    DefinesSlope = true,
                    OverhangFeet = 0.5,
                    PitchRisePerFoot = 0.5,
                    Points =
                    [
                        new ThreeDRoofGuidePoint { PdfX = 1, PdfY = 2, XFeet = 10, ZFeet = 20 },
                        new ThreeDRoofGuidePoint { PdfX = 3, PdfY = 4, XFeet = 30, ZFeet = 20 },
                    ],
                },
            ],
        };

        ThreeDModelStore.Save(job, model);
        ThreeDWallModel? loaded = ThreeDModelStore.Load(job);

        AssertTrue(loaded != null, "saved model should load");
        AssertEqual("1", loaded!.RoofGuides.Count.ToString(), "loaded roof guide count");
        AssertEqual(ThreeDRoofGuideKinds.Eave, loaded.RoofGuides[0].Kind, "loaded roof edge kind");
        AssertClose(30, loaded.RoofGuides[0].Points[1].XFeet, "loaded roof guide x feet");
        AssertTrue(loaded.RoofGuides[0].DefinesSlope, "loaded roof edge keeps DefinesSlope");
        AssertClose(0.5, loaded.RoofGuides[0].OverhangFeet, "loaded roof edge keeps overhang feet");
        AssertClose(0.5, loaded.RoofGuides[0].PitchRisePerFoot, "loaded roof edge keeps pitch");
    });
}

static void ThreeDModelStoreInfersLegacyDefinesSlope()
{
    WithTempJob("3d_model_store_legacy_roof", job =>
    {
        // A pre-DefinesSlope model: slope intent lived only in Kind == eave.
        string path = ThreeDModelStore.ModelPath(job);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            """
            {
              "Source": "legacy",
              "RoofGuides": [
                { "Kind": "eave", "Label": "Eave 1", "PitchRisePerFoot": 0.5,
                  "Points": [ { "XFeet": 0, "ZFeet": 0 }, { "XFeet": 20, "ZFeet": 0 } ] },
                { "Kind": "rake", "Label": "Rake 1",
                  "Points": [ { "XFeet": 20, "ZFeet": 0 }, { "XFeet": 20, "ZFeet": 10 } ] }
              ]
            }
            """);

        ThreeDWallModel? loaded = ThreeDModelStore.Load(job);

        AssertTrue(loaded != null, "legacy model should load");
        AssertTrue(loaded!.RoofGuides[0].DefinesSlope, "legacy eave guide infers DefinesSlope");
        AssertTrue(!loaded.RoofGuides[1].DefinesSlope, "legacy rake guide stays non-slope");
    });
}

static void RoofPitchTextParsesAndFormats()
{
    AssertTrue(RoofPitchText.TryParse("6/12", out double a) && Math.Abs(a - 0.5) < 1e-6, "6/12 -> 0.5");
    AssertTrue(RoofPitchText.TryParse("6:12", out double b) && Math.Abs(b - 0.5) < 1e-6, "6:12 -> 0.5");
    AssertTrue(RoofPitchText.TryParse("6 in 12", out double c) && Math.Abs(c - 0.5) < 1e-6, "6 in 12 -> 0.5");
    AssertTrue(RoofPitchText.TryParse("4", out double d) && Math.Abs(d - 4.0 / 12.0) < 1e-6, "bare 4 -> 4/12");
    AssertTrue(RoofPitchText.TryParse("0.333", out double e) && Math.Abs(e - 0.333) < 1e-6, "0.333 -> rise per foot");
    AssertTrue(!RoofPitchText.TryParse("", out _), "empty pitch rejected");
    AssertTrue(!RoofPitchText.TryParse("bad", out _), "bad pitch rejected");
    AssertEqual("6/12", RoofPitchText.Format(0.5), "0.5 formats to 6/12");
    AssertEqual("4/12", RoofPitchText.Format(4.0 / 12.0), "4/12 round-trips");
}

static void ThreeDRoofPerEdgeDefinesSlopeControlsPlanes()
{
    static ThreeDFloorSlab RoofSquare() => new()
    {
        LevelKey = "roof",
        ElevationFeet = 10,
        Points =
        [
            new ThreeDPoint { XFeet = 0, ZFeet = 0 },
            new ThreeDPoint { XFeet = 40, ZFeet = 0 },
            new ThreeDPoint { XFeet = 40, ZFeet = 30 },
            new ThreeDPoint { XFeet = 0, ZFeet = 30 },
        ],
    };

    static ThreeDRoofGuide Edge(double x1, double z1, double x2, double z2, string label) => new()
    {
        Kind = ThreeDRoofGuideKinds.Rake,
        Label = label,
        LevelKey = "roof",
        ElevationFeet = 10,
        Points =
        [
            new ThreeDRoofGuidePoint { XFeet = x1, ZFeet = z1, PdfX = x1, PdfY = z1 },
            new ThreeDRoofGuidePoint { XFeet = x2, ZFeet = z2, PdfX = x2, PdfY = z2 },
        ],
        RawPoints =
        [
            new ThreeDRoofGuidePoint { XFeet = x1, ZFeet = z1, PdfX = x1, PdfY = z1 },
            new ThreeDRoofGuidePoint { XFeet = x2, ZFeet = z2, PdfX = x2, PdfY = z2 },
        ],
    };

    var south = Edge(0, 0, 40, 0, "South");
    var north = Edge(0, 30, 40, 30, "North");
    var model = new ThreeDWallModel
    {
        Slabs = [RoofSquare()],
        RoofGuides = [south, north],
    };

    ThreeDRoofBuildResult noSlope = ThreeDRoofBuildService.Build(model);
    AssertTrue(noSlope.PlaneBuildBlocked, "no DefinesSlope edge blocks the roof");

    south.DefinesSlope = true;
    south.PitchRisePerFoot = 0.5;
    ThreeDRoofBuildResult oneSlope = ThreeDRoofBuildService.Build(model);
    AssertTrue(!oneSlope.PlaneBuildBlocked && oneSlope.Planes.Count >= 1,
        "one DefinesSlope eave builds a single-slope roof");

    north.DefinesSlope = true;
    north.PitchRisePerFoot = 0.25;
    ThreeDRoofBuildResult twoSlope = ThreeDRoofBuildService.Build(model);
    AssertTrue(twoSlope.Planes.Count >= 2,
        "two opposite DefinesSlope eaves with different pitch build two faces");
    AssertTrue(twoSlope.Guides.Any(guide => guide.Kind == ThreeDRoofGuideKinds.Ridge),
        "opposite slope eaves still generate a ridge seam");
}

static void ThreeDSlabTriangulatorHandlesConcaveAreas()
{
    ThreeDPolygonTriangulation result = ThreeDPolygonTriangulator.Triangulate(
    [
        new ThreeDPoint { XFeet = 0, ZFeet = 0 },
        new ThreeDPoint { XFeet = 6, ZFeet = 0 },
        new ThreeDPoint { XFeet = 6, ZFeet = 2 },
        new ThreeDPoint { XFeet = 3, ZFeet = 2 },
        new ThreeDPoint { XFeet = 3, ZFeet = 5 },
        new ThreeDPoint { XFeet = 0, ZFeet = 5 },
    ]);

    AssertTrue(result.Success, "concave slab should triangulate");
    AssertEqual("12", result.TriangleIndices.Count.ToString(), "concave six-point polygon should produce four triangles");
    AssertEqual("6", result.Points.Count.ToString(), "concave slab should preserve useful points");
}

static void ThreeDSlabTriangulatorRejectsCrossingAreas()
{
    ThreeDPolygonTriangulation result = ThreeDPolygonTriangulator.Triangulate(
    [
        new ThreeDPoint { XFeet = 0, ZFeet = 0 },
        new ThreeDPoint { XFeet = 6, ZFeet = 6 },
        new ThreeDPoint { XFeet = 0, ZFeet = 6 },
        new ThreeDPoint { XFeet = 6, ZFeet = 0 },
    ]);

    AssertTrue(!result.Success, "crossing slab should not render with long artifact diagonals");
}

static void PageTreeOrderMovesSheetBeforeFolder()
{
    WithTempJob("page_order_sheet_before_folder", job =>
    {
        string parent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Parent");
        string folderA = OurPlanCoreJobStore.CreateFolder(parent, "Folder A");
        PageInfo sheetB = CreatePageItem(job, parent, "Sheet B");
        OurPlanCoreJobStore.CreateFolder(parent, "Folder C");

        AssertPageChildOrder(parent, "Folder A,Sheet B,Folder C", "initial page/folder order");
        AssertTrue(OurPlanCoreJobStore.MoveSiblingsToPosition([sheetB.FolderPath], folderA, after: false), "sheet before folder should apply");
        AssertPageChildOrder(parent, "Sheet B,Folder A,Folder C", "sheet moved before folder");
    });
}

static void PageTreeOrderMovesFolderBeforeFolder()
{
    WithTempJob("page_order_folder_before_folder", job =>
    {
        string parent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Parent");
        string folderA = OurPlanCoreJobStore.CreateFolder(parent, "Folder A");
        string folderB = OurPlanCoreJobStore.CreateFolder(parent, "Folder B");
        CreatePageItem(job, parent, "Sheet C");

        AssertPageChildOrder(parent, "Folder A,Folder B,Sheet C", "initial folder/folder order");
        AssertTrue(OurPlanCoreJobStore.MoveSiblingsToPosition([folderB], folderA, after: false), "folder before folder should apply");
        AssertPageChildOrder(parent, "Folder B,Folder A,Sheet C", "folder moved before folder");
    });
}

static void PageTreeOrderMovesSelectedItemsToEnd()
{
    WithTempJob("page_order_selected_to_end", job =>
    {
        string parent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Parent");
        PageInfo sheetA = CreatePageItem(job, parent, "Sheet A");
        PageInfo sheetB = CreatePageItem(job, parent, "Sheet B");
        PageInfo sheetC = CreatePageItem(job, parent, "Sheet C");
        PageInfo sheetD = CreatePageItem(job, parent, "Sheet D");

        AssertPageChildOrder(parent, "Sheet A,Sheet B,Sheet C,Sheet D", "initial page order");
        AssertTrue(
            OurPlanCoreJobStore.MoveSiblingsToEnd([sheetB.FolderPath, sheetD.FolderPath], parent),
            "selected sheets should move to end");
        AssertPageChildOrder(parent, "Sheet A,Sheet C,Sheet B,Sheet D", "selected sheets moved to end");
    });
}

static void PageTreeOrderMovesNestedFolderOutBelowParent()
{
    WithTempJob("page_order_nested_folder_out", job =>
    {
        string parent = OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Parent");
        string child = OurPlanCoreJobStore.CreateFolder(parent, "Child");
        OurPlanCoreJobStore.CreateFolder(job.PagesRoot, "Other");

        string moved = OurPlanCoreJobStore.MoveNode(child, job.PagesRoot);
        AssertTrue(OurPlanCoreJobStore.MoveSiblingsToPosition([moved], parent, after: true), "nested folder out should apply");
        AssertPageChildOrder(job.PagesRoot, "Parent,Child,Other", "nested folder moved out below parent");
    });
}

static void PageSortDefaultsIncludeFinishAndMepOthers()
{
    PageSortConfig cfg = PageSortConfig.BuildDefault();

    AssertTrue(cfg.SuffixDetectionOrder.Contains("f", StringComparer.OrdinalIgnoreCase), "f suffix should be detected");
    AssertTrue(cfg.SuffixDetectionOrder.Contains("shw", StringComparer.OrdinalIgnoreCase), "shw suffix should be detected");
    AssertTrue(
        cfg.SuffixRules.Any(rule =>
            string.Equals(rule.Suffix, "f", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.FirstLetter, "a", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.Target, "finish", StringComparison.OrdinalIgnoreCase)),
        "A/f sheets should route to finish");
    AssertTrue(
        cfg.SuffixRules.Any(rule =>
            string.Equals(rule.Suffix, "shw", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.Target, "shear walls", StringComparison.OrdinalIgnoreCase)),
        "shw sheets should route to shear walls");

    foreach (string letter in new[] { "m", "p", "e", "c" })
    {
        AssertTrue(
            cfg.ArchStructRules.Any(rule =>
                string.Equals(rule.Kind, "FirstLetter", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rule.Match, letter, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(rule.Target, "Others", StringComparison.OrdinalIgnoreCase)),
            $"{letter} sheets should route to Others");
    }
}

static void PageSortUpgradeAddsFinishAndMepRules()
{
    var oldConfig = new PageSortConfig
    {
        SchemaVersion = 0,
        ArchStructRules =
        [
            new() { Kind = "FirstLetter", Match = "a", Target = "Arch" },
            new() { Kind = "FirstLetter", Match = "s", Target = "Struct" },
        ],
        SuffixTopOrder = ["v"],
        SuffixDetectionOrder = ["sec", "d"],
        SuffixRules =
        [
            new() { Suffix = "d", FirstLetter = "s", Target = "details struct" },
        ],
    };

    PageSortConfig upgraded = PageSortConfig.UpgradeForCurrentSchema(oldConfig);

    AssertTrue(
        upgraded.SchemaVersion == PageSortConfig.CurrentSchemaVersion,
        "schema version should upgrade to current");
    AssertTrue(upgraded.SuffixDetectionOrder.Contains("f", StringComparer.OrdinalIgnoreCase), "upgrade should add f detection");
    AssertTrue(upgraded.SuffixDetectionOrder.Contains("shw", StringComparer.OrdinalIgnoreCase), "upgrade should add shw detection");
    AssertTrue(
        upgraded.SuffixRules.Any(rule =>
            string.Equals(rule.Suffix, "f", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.Target, "finish", StringComparison.OrdinalIgnoreCase)),
        "upgrade should add finish rule");
    AssertTrue(
        upgraded.SuffixRules.Any(rule =>
            string.Equals(rule.Suffix, "shw", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.Target, "shear walls", StringComparison.OrdinalIgnoreCase)),
        "upgrade should add shear walls rule");
    AssertTrue(
        upgraded.ArchStructRules.Any(rule =>
            string.Equals(rule.Kind, "FileKeyword", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.Match, "civil", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(rule.Target, "Others", StringComparison.OrdinalIgnoreCase)),
        "upgrade should add civil keyword rule");
}

static void PageRenameAllowsDuplicateDisplayNames()
{
    WithTempJob("page_duplicate_names", job =>
    {
        PageInfo first = CreatePageItem(job, job.PagesRoot, "S101");
        PageInfo second = CreatePageItem(job, job.PagesRoot, "S102");

        string renamed = OurPlanCoreJobStore.RenamePageAllowDuplicateName(second.FolderPath, "S101");
        PageInfo? renamedPage = OurPlanCoreJobStore.TryReadPage(renamed);

        AssertTrue(Directory.Exists(first.FolderPath), "first duplicate-name page should remain");
        AssertTrue(Directory.Exists(renamed), "renamed duplicate-name page should exist");
        AssertFalse(string.Equals(first.FolderPath, renamed, StringComparison.OrdinalIgnoreCase), "duplicate-name pages need unique folders");
        AssertEqual("S101", OurPlanCoreJobStore.DisplayName(first.FolderPath), "first display name");
        AssertEqual("S101", OurPlanCoreJobStore.DisplayName(renamed), "renamed display name");
        AssertEqual("S101", renamedPage?.Name ?? "", "renamed page info name");
    });
}

static void TakeoffCreateAllowsDuplicateDisplayNames()
{
    WithTempJob("takeoff_create_duplicate_names", job =>
    {
        TakeoffItem first = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Walls", "#FF0000", "line");
        TakeoffItem second = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Walls", "#00FF00", "area");

        AssertTrue(Directory.Exists(first.FolderPath), "first duplicate-name takeoff should exist");
        AssertTrue(Directory.Exists(second.FolderPath), "second duplicate-name takeoff should exist");
        AssertFalse(string.Equals(first.FolderPath, second.FolderPath, StringComparison.OrdinalIgnoreCase), "duplicate-name takeoffs need unique folders");
        AssertEqual("Walls", first.Name, "first takeoff display name");
        AssertEqual("Walls", second.Name, "second takeoff display name");
        AssertEqual("Walls", OurPlanCoreJobStore.DisplayName(first.FolderPath), "first stored display name");
        AssertEqual("Walls", OurPlanCoreJobStore.DisplayName(second.FolderPath), "second stored display name");
    });
}

static void TakeoffRenameAllowsDuplicateDisplayNames()
{
    WithTempJob("takeoff_rename_duplicate_names", job =>
    {
        TakeoffItem first = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Walls", "#FF0000", "line");
        TakeoffItem second = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof", "#00FF00", "area");

        string renamed = OurPlanCoreJobStore.RenameNodeAllowDuplicateName(second.FolderPath, "Walls");
        TakeoffItem? renamedItem = OurPlanCoreJobStore.TryReadTakeoffItem(renamed);

        AssertTrue(Directory.Exists(first.FolderPath), "first duplicate-name takeoff should remain");
        AssertTrue(Directory.Exists(renamed), "renamed duplicate-name takeoff should exist");
        AssertFalse(string.Equals(first.FolderPath, renamed, StringComparison.OrdinalIgnoreCase), "renamed duplicate-name takeoff needs unique folder");
        AssertEqual("Walls", OurPlanCoreJobStore.DisplayName(first.FolderPath), "first takeoff display name");
        AssertEqual("Walls", OurPlanCoreJobStore.DisplayName(renamed), "renamed takeoff display name");
        AssertEqual("Walls", renamedItem?.Name ?? "", "renamed takeoff item name");
    });
}

static void TakeoffDisplayNamesPreserveSlash()
{
    WithTempJob("takeoff_slash_names", job =>
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "ext 9.1 10/A502", "#FF0000", "line");
        AssertEqual("ext 9.1 10/A502", item.Name, "created takeoff display name preserves slash");
        AssertTrue(Path.GetFileName(item.FolderPath).Contains("_A502", StringComparison.Ordinal), "folder path still uses safe slash replacement");

        string renamed = OurPlanCoreJobStore.RenameNodeAllowDuplicateName(item.FolderPath, "corr 2x6 10.1 14/A502");
        TakeoffItem? loaded = OurPlanCoreJobStore.TryReadTakeoffItem(renamed);
        AssertEqual("corr 2x6 10.1 14/A502", OurPlanCoreJobStore.DisplayName(renamed), "renamed takeoff display name preserves slash");
        AssertEqual("corr 2x6 10.1 14/A502", loaded?.Name ?? "", "loaded takeoff item preserves slash");

        loaded!.Measurements.Add(new Measurement
        {
            MType = "line",
            ScaleMetersPerPt = 0.3048,
            Points = [new SKPoint(0, 0), new SKPoint(1, 0)],
        });
        IReadOnlyList<PlanSwiftExportRow> rows = PlanSwiftTakeoffExporter.BuildRows(
            job,
            [loaded],
            [job.TakeoffsRoot],
            UnitMode.Imperial);
        AssertTrue(rows.Any(row => row.Kind == PlanSwiftExportRowKind.Item && row.Name == "corr 2x6 10.1 14/A502"), "takeoff export preserves slash");
    });
}

static void TakeoffCopyKeepsDisplayName()
{
    WithTempJob("takeoff_copy_name", job =>
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "2. 3x6", "#FF0000", "line");
        string sourceGuid = ReadDataGuid(item.FolderPath);
        string copy = OurPlanCoreJobStore.CopyNode(item.FolderPath, job.TakeoffsRoot);
        string secondCopy = OurPlanCoreJobStore.CopyNode(item.FolderPath, job.TakeoffsRoot);
        TakeoffItem? copied = OurPlanCoreJobStore.TryReadTakeoffItem(copy);
        TakeoffItem? secondCopied = OurPlanCoreJobStore.TryReadTakeoffItem(secondCopy);
        string copyGuid = ReadDataGuid(copy);

        AssertFalse(string.Equals(item.FolderPath, copy, StringComparison.OrdinalIgnoreCase), "copy should use a new folder");
        AssertFalse(string.Equals(copy, secondCopy, StringComparison.OrdinalIgnoreCase), "second copy should use another hidden folder");
        AssertEqual("2. 3x6", OurPlanCoreJobStore.DisplayName(copy), "copy display name should not include Copy");
        AssertEqual("2. 3x6", copied?.Name ?? "", "copied item name should not include Copy");
        AssertEqual("2. 3x6", OurPlanCoreJobStore.DisplayName(secondCopy), "second copy display name should not include a number");
        AssertEqual("2. 3x6", secondCopied?.Name ?? "", "second copied item name should not include a number");
        AssertFalse(string.Equals(sourceGuid, copyGuid, StringComparison.OrdinalIgnoreCase), "copied takeoff should get a new hidden guid");
    });
}

static void TakeoffMoveCollisionKeepsDisplayName()
{
    WithTempJob("takeoff_move_collision_name", job =>
    {
        string targetFolder = CreateTakeoffFolder(job, "Target");
        TakeoffItem existing = OurPlanCoreJobStore.CreateTakeoffItem(job, targetFolder, "Ext Walls", "#FF0000", "line");
        TakeoffItem moving = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Ext Walls", "#00FF00", "line");

        string moved = OurPlanCoreJobStore.MoveNode(moving.FolderPath, targetFolder);
        TakeoffItem? loaded = OurPlanCoreJobStore.TryReadTakeoffItem(moved);

        AssertFalse(string.Equals(existing.FolderPath, moved, StringComparison.OrdinalIgnoreCase), "move collision should use a unique folder path");
        AssertFalse(OurPlanCoreJobStore.DisplayName(moved).Contains("Copy", StringComparison.OrdinalIgnoreCase), "move collision should not add Copy");
        AssertEqual("Ext Walls", OurPlanCoreJobStore.DisplayName(moved), "move collision preserves display name");
        AssertEqual("Ext Walls", loaded?.Name ?? "", "loaded moved item preserves display name");
    });
}

static void JobStoreSanitizesUnsafeNames()
{
    string clean = OurPlanCoreJobStore.SanitizeName("  bad:name?.  ", 120);
    AssertEqual("bad_name_", clean, "sanitized invalid characters");

    string truncated = OurPlanCoreJobStore.SanitizeName(new string('a', 10), 4);
    AssertEqual("aaaa", truncated, "max length");
}

static void PdfMetadataPageNameAndScaleGate()
{
    var metadata = new PdfSheetMetadata
    {
        SheetKey = "s100",
        Suffix = "f",
        SelectedScaleMetersPerPt = 1,
    };

    AssertEqual("s100 f", metadata.ProposedPageName(), "proposed page name");
    AssertTrue(metadata.CanApplyScale(), "scale can apply before skip");

    metadata.SkipScale = true;
    AssertFalse(metadata.CanApplyScale(), "skip scale blocks scale apply");
}

static void PdfMetadataHidesDuplicateMarkerFromVisibleNames()
{
    var metadata = new PdfSheetMetadata
    {
        SheetKey = "s200",
        Suffix = "f",
        RenameCandidate = "s200 f (2)",
    };

    AssertEqual("s200 f", metadata.ProposedPageName(), "duplicate marker should not be visible in proposed name");
    AssertEqual("s200 f", PdfSheetMetadataService.VisibleSheetDisplayName("s200 f (2)"), "visible sheet name strips duplicate marker");
    AssertEqual("dem (2) 2x4", PdfSheetMetadataService.VisibleSheetDisplayName("dem (2) 2x4"), "embedded takeoff-style marker is preserved");
}

static void PdfMetadataLeavesUnknownPageNamesBlank()
{
    var metadata = new PdfSheetMetadata();

    AssertEqual("", metadata.ProposedPageName(), "unknown sheet should not propose dash page name");
}

static void PdfMetadataPreservesDottedSheetLabels()
{
    var page = new PageInfo
    {
        Name = "A5",
        FolderPath = @"C:\job\Pages\A5",
        PdfPath = @"C:\job\sources\plans.pdf",
        PdfPage = 2,
    };
    var request = new SmartAiRequest { Id = "metadata-test" };
    var response = new SmartAiResponse
    {
        OutputText =
            """
            {
              "sheet_label": "A5.03",
              "sheet_title": "WALL SECTIONS",
              "selected_scale_text": "1/4\" = 1'0\"",
              "confidence": "test"
            }
            """,
    };

    bool ok = PdfSheetMetadataService.TryBuildMetadataFromFallbackResponse(
        page,
        request,
        response,
        out PdfSheetMetadata metadata,
        out string error);

    AssertTrue(ok, $"metadata fallback should parse dotted sheet labels: {error}");
    AssertEqual("A5.03", metadata.SheetLabel, "dotted sheet label");
    AssertEqual("a5.03", metadata.ProposedPageName(), "dotted sheet label preserved in lowercase proposed name");
}

static void PdfScaleParserHandlesArchitecturalScale()
{
    bool parsed = PdfSheetMetadataService.TryParseScaleMetersPerPt("1/8\" = 1'0\"", out double metersPerPt);

    AssertTrue(parsed, "scale should parse");
    AssertTrue(metersPerPt > 0, "scale should be positive");
    AssertEqual("1/8\" = 1'0\"", PdfSheetMetadataService.FormatImperialScale(metersPerPt), "roundtrip label");
}

static void PdfScaleParserHandlesMixedFractionScale()
{
    bool parsed = PdfSheetMetadataService.TryParseScaleMetersPerPt("1 1/2\" = 1'0\"", out double mixedMetersPerPt);
    bool parsedLegacy = PdfSheetMetadataService.TryParseScaleMetersPerPt("1-1/2\" = 1'0\"", out double legacyMetersPerPt);

    AssertTrue(parsed, "space mixed fraction scale should parse");
    AssertTrue(parsedLegacy, "hyphen mixed fraction scale should still parse");
    AssertClose(legacyMetersPerPt, mixedMetersPerPt, "mixed fraction styles should match");
    AssertEqual("1 1/2\" = 1'0\"", PdfSheetMetadataService.FormatImperialScale(mixedMetersPerPt), "mixed fraction roundtrip label");
}

static void PdfScaleParserHandlesEngineeringScale()
{
    bool parsed = PdfSheetMetadataService.TryParseScaleMetersPerPt("1\" = 20'0\"", out double metersPerPt);

    AssertTrue(parsed, "engineering scale should parse");
    AssertTrue(metersPerPt > 0, "engineering scale should be positive");
    AssertEqual("1\" = 20'0\"", PdfSheetMetadataService.FormatImperialScale(metersPerPt), "engineering scale roundtrip label");
}

static void PdfScaleParserHandlesDecimalRatioScale()
{
    bool parsed = PdfSheetMetadataService.TryParseScaleMetersPerPt("0.287:1", out double colonMetersPerPt);
    bool parsedBareDecimal = PdfSheetMetadataService.TryParseScaleMetersPerPt("0.287", out double bareDecimalMetersPerPt);
    bool parsedBareCommaDecimal = PdfSheetMetadataService.TryParseScaleMetersPerPt("0,287", out double bareCommaDecimalMetersPerPt);
    bool parsedUserDecimal = PdfSheetMetadataService.TryParseScaleMetersPerPt("0.289", out double userDecimalMetersPerPt);
    bool parsedEqualsDecimal = PdfSheetMetadataService.TryParseScaleMetersPerPt("0.287 = 1", out double equalsDecimalMetersPerPt);
    bool parsedKeyboardK = PdfSheetMetadataService.TryParseScaleMetersPerPt("0.287 k 1", out double keyboardMetersPerPt);
    bool parsedCyrillicK = PdfSheetMetadataService.TryParseScaleMetersPerPt("0.287 к 1", out double cyrillicMetersPerPt);

    AssertTrue(parsed, "decimal ratio scale should parse");
    AssertTrue(parsedBareDecimal, "bare decimal ratio scale should parse");
    AssertTrue(parsedBareCommaDecimal, "bare comma decimal ratio scale should parse");
    AssertTrue(parsedUserDecimal, "0.289 bare decimal ratio should parse");
    AssertTrue(parsedEqualsDecimal, "decimal equals one scale should parse");
    AssertTrue(parsedKeyboardK, "keyboard decimal ratio scale should parse");
    AssertTrue(parsedCyrillicK, "cyrillic decimal ratio scale should parse");
    AssertClose(bareDecimalMetersPerPt, colonMetersPerPt, "bare decimal ratio should match colon ratio");
    AssertClose(bareCommaDecimalMetersPerPt, colonMetersPerPt, "bare comma decimal ratio should match colon ratio");
    AssertClose(equalsDecimalMetersPerPt, colonMetersPerPt, "decimal equals one scale should match colon ratio");
    AssertClose(
        ViewportConstants.PdfPointMeters * (12.0 / 0.289),
        userDecimalMetersPerPt,
        "0.289 bare decimal ratio should use inches-per-foot scale");
    AssertEqual("0.287\" = 1'0\"", PdfSheetMetadataService.FormatImperialScale(colonMetersPerPt), "decimal ratio should roundtrip as decimal inches per foot label");
    AssertEqual("0.289\" = 1'0\"", PdfSheetMetadataService.FormatImperialScale(userDecimalMetersPerPt), "bare decimal ratio should display as decimal inches per foot");
    AssertClose(keyboardMetersPerPt, colonMetersPerPt, "keyboard k decimal ratio should match colon ratio");
    AssertClose(cyrillicMetersPerPt, colonMetersPerPt, "cyrillic k decimal ratio should match colon ratio");
    AssertClose(
        ViewportConstants.PdfPointMeters * (12.0 / 0.287),
        colonMetersPerPt,
        "decimal ratio should use inches-per-foot scale");
}

static void JoistRoundingAliasesNormalize()
{
    AssertEqual(JoistTakeoffCalculator.RoundingNearestFoot, JoistTakeoffCalculator.NormalizeLengthRounding("foot"), "foot alias");
    AssertEqual(JoistTakeoffCalculator.RoundingNearestEvenFoot, JoistTakeoffCalculator.NormalizeLengthRounding("even foot"), "even alias");
    AssertEqual(JoistTakeoffCalculator.RoundingNearestTwoFeet, JoistTakeoffCalculator.NormalizeLengthRounding("2 feet"), "two feet alias");
    AssertEqual("Round Up 2 Feet", JoistTakeoffCalculator.LengthRoundingTitle("nearesttwofeet"), "rounding title");
}

static void JoistPitchNormalizesCommonInput()
{
    AssertEqual("3:12", JoistTakeoffCalculator.NormalizePitch("3:12"), "colon pitch");
    AssertEqual("3:12", JoistTakeoffCalculator.NormalizePitch("3/12"), "slash pitch");
    AssertEqual("3.5:12", JoistTakeoffCalculator.NormalizePitch("3,5 in 12"), "decimal pitch");
}

static void JoistPitchFlatInputNormalizesEmpty()
{
    AssertEqual("", JoistTakeoffCalculator.NormalizePitch(""), "blank pitch");
    AssertEqual("", JoistTakeoffCalculator.NormalizePitch("0:12"), "flat pitch");
}

static void JoistPitchRejectsInvalidInput()
{
    AssertFalse(JoistTakeoffCalculator.TryNormalizePitch("3:0", out _), "zero run rejected");
    AssertFalse(JoistTakeoffCalculator.TryParsePitchFactor("bad", out _), "bad pitch rejected");
}

static void JoistPitchFactorMatchesRiseRun()
{
    AssertTrue(JoistTakeoffCalculator.TryParsePitchFactor("3:12", out double factor), "factor parsed");
    AssertClose(Math.Sqrt(153) / 12.0, factor, "3:12 factor");
}

static void JoistPitchAcceptsSingleRiseOverTwelve()
{
    AssertEqual("4:12", JoistTakeoffCalculator.NormalizePitch("4"), "single rise default run");
}

static void JoistLayoutSubtractsAreaCutHoles()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = true,
        JoistSpacingInches = 60,
        JoistDirectionDegrees = 0,
        JoistLengthRounding = JoistTakeoffCalculator.RoundingNone,
        ScaleMetersPerPt = 0.3048,
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(10, 0),
            new SKPoint(10, 10),
            new SKPoint(0, 10),
        ],
        Holes =
        [
            [
                new SKPoint(4, 2),
                new SKPoint(6, 2),
                new SKPoint(6, 8),
                new SKPoint(4, 8),
            ],
        ],
    };

    JoistLayoutResult layout = JoistTakeoffCalculator.Calculate(measurement, 0);

    AssertEqual("4", layout.Count.ToString(), "cut hole splits middle joist run into two pieces");
    AssertClose(28.0, layout.TotalRawLengthMeters / 0.3048, "joist length should subtract the 2 ft opening on the middle line");
    AssertClose(88.0, layout.AreaMetersSquared / (0.3048 * 0.3048), "joist area should subtract the hole");
}

static void JoistLayoutCanSkipEndJoist()
{
    JoistLayoutResult withEnd = JoistTakeoffCalculator.Calculate(
        SimpleJoistAreaPolygon(),
        0.3048,
        120,
        0,
        JoistTakeoffCalculator.RoundingNone,
        "",
        addEndJoist: true);
    JoistLayoutResult withoutEnd = JoistTakeoffCalculator.Calculate(
        SimpleJoistAreaPolygon(),
        0.3048,
        120,
        0,
        JoistTakeoffCalculator.RoundingNone,
        "",
        addEndJoist: false);

    AssertEqual("2", withEnd.Count.ToString(), "end joist should be included by default");
    AssertEqual("1", withoutEnd.Count.ToString(), "end joist can be skipped");
}

static void JoistPitchLengthAppliesSlopeFactor()
{
    JoistLayoutResult flat = JoistTakeoffCalculator.Calculate(
        SimpleJoistAreaPolygon(),
        0.3048,
        120,
        0,
        JoistTakeoffCalculator.RoundingNone);
    JoistLayoutResult pitched = JoistTakeoffCalculator.Calculate(
        SimpleJoistAreaPolygon(),
        0.3048,
        120,
        0,
        JoistTakeoffCalculator.RoundingNone,
        "3:12");

    AssertTrue(JoistTakeoffCalculator.TryParsePitchFactor("3:12", out double factor), "pitch factor");
    AssertClose(flat.TotalRawLengthMeters * factor, pitched.TotalRawLengthMeters, "sloped raw length");
    AssertClose(flat.TotalLengthMeters * factor, pitched.TotalLengthMeters, "sloped order length without rounding");
    AssertEqual("3:12", pitched.Pitch, "layout pitch");
}

static void JoistPitchRoundingAppliesPerSegment()
{
    JoistLayoutResult rounded = JoistTakeoffCalculator.Calculate(
        SimpleJoistAreaPolygon(),
        0.3048,
        120,
        0,
        JoistTakeoffCalculator.RoundingNearestEvenFoot,
        "6:12");

    AssertEqual("2", rounded.Count.ToString(), "joist count");
    AssertClose(20.0, rounded.TotalLengthMeters / 0.3048, "order length rounds the sloped 8.94 ft joist length");
}

static void JoistPitchLabelShowsIndicator()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = true,
        JoistSpacingInches = 120,
        JoistPitch = "3:12",
        JoistLengthRounding = JoistTakeoffCalculator.RoundingNone,
        ScaleMetersPerPt = 0.3048,
        Points = SimpleJoistAreaPolygon().ToList(),
    };

    AssertTrue(measurement.Label(0.3048, UnitMode.Imperial).Contains("Pitch 3:12", StringComparison.Ordinal), "label pitch line");
}

static void JoistLengthLabelShowsOrderAndRawLengths()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = true,
        JoistSpacingInches = 12,
        JoistLengthRounding = JoistTakeoffCalculator.RoundingNearestEvenFoot,
        ScaleMetersPerPt = 0.3048,
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(4.93f, 0),
            new SKPoint(4.93f, 1),
            new SKPoint(0, 1),
        ],
    };

    string label = measurement.Label(0, UnitMode.Imperial);

    AssertTrue(label.Contains("2 pcs @ 6.00 FT order (raw 4.93)", StringComparison.Ordinal), "label shows raw and order length");
    AssertFalse(label.Contains("(2 / 6.00", StringComparison.Ordinal), "label avoids count slash fraction format");
}

static void JoistLengthLabelCanUseStandardFormat()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = true,
        JoistSpacingInches = 12,
        JoistLengthRounding = JoistTakeoffCalculator.RoundingNearestEvenFoot,
        JoistDetailedLabels = false,
        ScaleMetersPerPt = 0.3048,
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(4.93f, 0),
            new SKPoint(4.93f, 1),
            new SKPoint(0, 1),
        ],
    };

    string label = measurement.Label(0, UnitMode.Imperial);

    AssertTrue(label.Contains("(2 / 6.00)", StringComparison.Ordinal), "standard label keeps old count / length format");
    AssertFalse(label.Contains("raw 4.93", StringComparison.Ordinal), "standard label hides raw details");
}

static void JoistPitchLabelExplainsFlatSlopeAndOrderLengths()
{
    var measurement = new Measurement
    {
        MType = "area",
        JoistEnabled = true,
        JoistDirectionLocked = true,
        JoistSpacingInches = 12,
        JoistPitch = "1:3",
        JoistLengthRounding = JoistTakeoffCalculator.RoundingNearestFoot,
        ScaleMetersPerPt = 0.3048,
        Points =
        [
            new SKPoint(0, 0),
            new SKPoint(11.94f, 0),
            new SKPoint(11.94f, 1),
            new SKPoint(0, 1),
        ],
    };

    string label = measurement.Label(0, UnitMode.Imperial);

    AssertTrue(label.Contains("2 pcs @ 13.00 FT order (flat 11.94, slope 12.59)", StringComparison.Ordinal), "label explains flat, slope, and order length");
}

static void JoistExportUsesVisibleLabelLines()
{
    WithTempJob("Joist Export", job =>
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
        item.IsJoistTakeoff = true;
        item.Measurements.Add(new Measurement
        {
            MType = "area",
            JoistEnabled = true,
            JoistDirectionLocked = true,
            JoistType = "2x10",
            JoistSpacingInches = 120,
            JoistPitch = "3:12",
            JoistLengthRounding = JoistTakeoffCalculator.RoundingNone,
            ScaleMetersPerPt = 0.3048,
            Points = SimpleJoistAreaPolygon().ToList(),
        });

        string visibleLabel = item.Measurements[0].Label(0, UnitMode.Imperial);
        IReadOnlyList<string> labelLines = PlanSwiftTakeoffExporter.JoistLabelLines(item, 0, UnitMode.Imperial);
        IReadOnlyList<PlanSwiftExportRow> rows = PlanSwiftTakeoffExporter.BuildRows(job, [item], [job.TakeoffsRoot], UnitMode.Imperial);

        AssertEqual(visibleLabel, string.Join("\n", labelLines), "export label helper matches canvas label");
        AssertEqual(labelLines[0], rows.First(row => row.Kind == PlanSwiftExportRowKind.Item).Value, "item row value is first label line");
        AssertTrue(rows.Any(row => row.Kind == PlanSwiftExportRowKind.Note && row.Name == "Pitch 3:12"), "pitch label line exported");
    });
}

static void JoistExportOffsetsOverlappingSegmentLabels()
{
    MethodInfo method = typeof(PdfExporter).GetMethod(
        "PlaceJoistSegmentLabelBox",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("PdfExporter.PlaceJoistSegmentLabelBox");

    const float collisionPad = 2f;
    var labelSize = new SKSize(36f, 10f);
    var occupied = new List<SKRect>();

    SKRect first = Place(method, labelSize, collisionPad, occupied);
    occupied.Add(Inflate(first, collisionPad));
    SKRect second = Place(method, labelSize, collisionPad, occupied);

    AssertFalse(Overlaps(first, second), $"overlapping joist segment labels should be offset: {first} vs {second}");
    AssertTrue(
        Math.Abs(CenterX(first) - CenterX(second)) > 1f || Math.Abs(CenterY(first) - CenterY(second)) > 1f,
        "second joist segment label should move away from an occupied label box");

    static SKRect Place(MethodInfo method, SKSize labelSize, float collisionPad, IReadOnlyList<SKRect> occupied)
    {
        object? result = method.Invoke(
            null,
            new object?[]
            {
                new SKPoint(0f, 0f),
                new SKPoint(120f, 0f),
                new SKPoint(60f, 0f),
                labelSize,
                collisionPad,
                occupied,
            });
        return result is SKRect rect
            ? rect
            : throw new InvalidOperationException("Joist label placement did not return a rectangle.");
    }

    static SKRect Inflate(SKRect rect, float pad) =>
        new(rect.Left - pad, rect.Top - pad, rect.Right + pad, rect.Bottom + pad);

    static bool Overlaps(SKRect a, SKRect b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;

    static float CenterX(SKRect rect) => (rect.Left + rect.Right) / 2f;

    static float CenterY(SKRect rect) => (rect.Top + rect.Bottom) / 2f;
}

static void PdfExportOffsetsOverlappingMeasurementLabels()
{
    MethodInfo method = typeof(PdfExporter).GetMethod(
        "PlaceMeasurementLabelBox",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("PdfExporter.PlaceMeasurementLabelBox");

    const float collisionPad = 2f;
    var pageBounds = new SKRect(0f, 0f, 500f, 500f);
    var baseBox = new SKRect(80f, 80f, 220f, 220f);
    var occupied = new List<SKRect>();

    SKRect first = Place(method, baseBox, collisionPad, occupied, pageBounds);
    occupied.Add(Inflate(first, collisionPad));
    SKRect second = Place(method, baseBox, collisionPad, occupied, pageBounds);

    AssertFalse(Overlaps(first, second), $"overlapping PDF measurement labels should be offset: {first} vs {second}");
    AssertTrue(
        second.Left >= pageBounds.Left &&
        second.Top >= pageBounds.Top &&
        second.Right <= pageBounds.Right &&
        second.Bottom <= pageBounds.Bottom,
        "offset PDF measurement label should stay inside the page bounds");

    static SKRect Place(MethodInfo method, SKRect baseBox, float collisionPad, IReadOnlyList<SKRect> occupied, SKRect pageBounds)
    {
        object? result = method.Invoke(
            null,
            new object?[]
            {
                baseBox,
                new SKPoint((baseBox.Left + baseBox.Right) / 2f, (baseBox.Top + baseBox.Bottom) / 2f),
                collisionPad,
                occupied,
                pageBounds,
            });
        return result is SKRect rect
            ? rect
            : throw new InvalidOperationException("Measurement label placement did not return a rectangle.");
    }

    static SKRect Inflate(SKRect rect, float pad) =>
        new(rect.Left - pad, rect.Top - pad, rect.Right + pad, rect.Bottom + pad);

    static bool Overlaps(SKRect a, SKRect b) =>
        a.Left < b.Right && a.Right > b.Left && a.Top < b.Bottom && a.Bottom > b.Top;
}

static void PlanSwiftTxtExportWritesEveryRootItem()
{
    WithTempJob("TXT Export", job =>
    {
        TakeoffItem first = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "First Item", "#FF0000", "line");
        TakeoffItem second = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Second Item", "#00FF00", "line");

        first.Measurements.Add(new Measurement
        {
            MType = "line",
            ScaleMetersPerPt = 0.3048,
            Points = [new SKPoint(0, 0), new SKPoint(1, 0)],
        });
        second.Measurements.Add(new Measurement
        {
            MType = "line",
            ScaleMetersPerPt = 0.3048,
            Points = [new SKPoint(0, 0), new SKPoint(2, 0)],
        });

        IReadOnlyList<PlanSwiftExportRow> rows = PlanSwiftTakeoffExporter.BuildRows(
            job,
            [first, second],
            [job.TakeoffsRoot],
            UnitMode.Imperial);
        string path = Path.Combine(job.RootPath, "takeoffs.txt");
        PlanSwiftTakeoffExporter.WriteTxt(path, rows);
        string text = File.ReadAllText(path);

        AssertEqual("2", rows.Count(row => row.Kind == PlanSwiftExportRowKind.Item).ToString(), "root export item rows");
        AssertTrue(text.Contains("First Item\t", StringComparison.Ordinal), "txt includes first item");
        AssertTrue(text.Contains("Second Item\t", StringComparison.Ordinal), "txt includes second item");
    });
}

static void PlanSwiftExportHidesGeneratedImportNotes()
{
    WithTempJob("Import Notes Export", job =>
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Imported Area", "#FF0000", "area");
        item.Notes = "Keep item note\nImported generated PlanSwift Segment geometry from Takeoff\\segments\\Deck";
        item.Measurements.Add(new Measurement
        {
            MType = "area",
            Notes = "Imported from PlanSwift: Takeoff\\sqfts\\4th\\Section\nKeep section note\nImported from PlanSwift Segment Section: Takeoff\\segments\\Deck\\Section",
            ScaleMetersPerPt = 0.3048,
            Points =
            [
                new SKPoint(0, 0),
                new SKPoint(1, 0),
                new SKPoint(1, 1),
                new SKPoint(0, 1),
            ],
        });

        IReadOnlyList<PlanSwiftExportRow> rows = PlanSwiftTakeoffExporter.BuildRows(
            job,
            [item],
            [job.TakeoffsRoot],
            UnitMode.Imperial);

        AssertFalse(rows.Any(row => row.Name.Contains("Imported from PlanSwift:", StringComparison.OrdinalIgnoreCase)), "generated import source note hidden");
        AssertFalse(rows.Any(row => row.Name.Contains("Imported generated PlanSwift Segment", StringComparison.OrdinalIgnoreCase)), "generated segment source note hidden");
        AssertTrue(rows.Any(row => row.Kind == PlanSwiftExportRowKind.Note && row.Name == "Keep item note"), "manual item note exported");
        AssertTrue(rows.Any(row => row.Kind == PlanSwiftExportRowKind.Note && row.Name == "Keep section note"), "manual section note exported");
        AssertEqual("Manual note", PlanSwiftTakeoffExporter.CleanExportNotes("Imported from PlanSwift: Takeoff\\sqfts\\4th\\Section\nManual note"), "csv export note cleanup");
    });
}

static void PlanSwiftExportHidesPdfImportNotes()
{
    WithTempJob("PDF Import Notes Export", job =>
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "PDF Imported Area", "#FF0000", "area");
        item.Notes = "Imported from PDF takeoff annotations: source.pdf\nKeep item note";
        item.Measurements.Add(new Measurement
        {
            MType = "area",
            Notes = "Imported from PDF takeoff: source.pdf\nPDF page: 1\nAnnotation: abc\nSubtype: /Polygon\nContent: 12 SF\nKeep section note",
            ScaleMetersPerPt = 0.3048,
            Points =
            [
                new SKPoint(0, 0),
                new SKPoint(1, 0),
                new SKPoint(1, 1),
                new SKPoint(0, 1),
            ],
        });

        IReadOnlyList<PlanSwiftExportRow> rows = PlanSwiftTakeoffExporter.BuildRows(
            job,
            [item],
            [job.TakeoffsRoot],
            UnitMode.Imperial);

        AssertFalse(rows.Any(row => row.Name.Contains("Imported from PDF takeoff", StringComparison.OrdinalIgnoreCase)), "pdf import source note hidden");
        AssertFalse(rows.Any(row => row.Name.Contains("PDF page:", StringComparison.OrdinalIgnoreCase)), "pdf page note hidden");
        AssertFalse(rows.Any(row => row.Name.Contains("Annotation:", StringComparison.OrdinalIgnoreCase)), "pdf annotation id note hidden");
        AssertFalse(rows.Any(row => row.Name.Contains("Subtype:", StringComparison.OrdinalIgnoreCase)), "pdf subtype note hidden");
        AssertFalse(rows.Any(row => row.Name.Contains("Content:", StringComparison.OrdinalIgnoreCase)), "pdf content note hidden");
        AssertTrue(rows.Any(row => row.Kind == PlanSwiftExportRowKind.Note && row.Name == "Keep item note"), "manual pdf item note exported");
        AssertTrue(rows.Any(row => row.Kind == PlanSwiftExportRowKind.Note && row.Name == "Keep section note"), "manual pdf section note exported");
    });
}

static void PlanSwiftExportGroupsSelectedSiblingItems()
{
    WithTempJob("Selected Sibling Export", job =>
    {
        string folder = CreateTakeoffFolder(job, "PDF Import");
        TakeoffItem first = CreateNestedTakeoffItem(job, folder, "First Line");
        TakeoffItem second = CreateNestedTakeoffItem(job, folder, "Second Line");
        first.Measurements.Add(new Measurement
        {
            MType = "line",
            ScaleMetersPerPt = 0.3048,
            Points = [new SKPoint(0, 0), new SKPoint(1, 0)],
        });
        second.Measurements.Add(new Measurement
        {
            MType = "line",
            ScaleMetersPerPt = 0.3048,
            Points = [new SKPoint(0, 0), new SKPoint(2, 0)],
        });

        IReadOnlyList<PlanSwiftExportRow> rows = PlanSwiftTakeoffExporter.BuildRows(
            job,
            [first, second],
            [first.FolderPath, second.FolderPath],
            UnitMode.Imperial);

        AssertEqual("1", rows.Count(row => row.Kind == PlanSwiftExportRowKind.Header && row.Name == "PDF Import").ToString(), "selected sibling folder header count");
        AssertEqual("2", rows.Count(row => row.Kind == PlanSwiftExportRowKind.Item).ToString(), "selected sibling item rows");
        AssertTrue(rows.Any(row => row.Kind == PlanSwiftExportRowKind.Item && row.Name == "First Line"), "selected sibling first item exported");
        AssertTrue(rows.Any(row => row.Kind == PlanSwiftExportRowKind.Item && row.Name == "Second Line"), "selected sibling second item exported");
    });
}

static void ActiveExcelExportMatrixKeepsNumbers()
{
    PlanSwiftExportRow[] rows =
    [
        new(PlanSwiftExportRowKind.Header, "Framing"),
        new(PlanSwiftExportRowKind.Item, "2x10 Joists", "12,5", "FT"),
        new(PlanSwiftExportRowKind.Note, "Note\twith\nbreak"),
        new(PlanSwiftExportRowKind.Blank, ""),
    ];

    object[,] values = ActiveExcelTakeoffExportService.BuildValueMatrix(rows);

    AssertEqual("Framing", values[0, 0]?.ToString() ?? "", "header first cell");
    AssertTrue(values[1, 1] is double value && Math.Abs(value - 12.5) < 0.0001, "numeric value exported as number");
    AssertEqual("FT", values[1, 2]?.ToString() ?? "", "unit first cell");
    AssertEqual("Note with break", values[2, 0]?.ToString() ?? "", "note text is cell-safe");
    AssertEqual("", values[3, 0]?.ToString() ?? "", "blank row first cell");
}

static void JoistAreaDefaultsUseCompactLabelsAndFootRounding()
{
    var item = new TakeoffItem
    {
        MeasurementType = "area",
        JoistLengthRounding = JoistTakeoffCalculator.RoundingNearestEvenFoot,
        JoistShowLabels = false,
        JoistDetailedLabels = true,
    };

    JoistTakeoffDefaults.ApplyToNewJoistArea(item);

    AssertTrue(item.IsJoistTakeoff, "joist default enables item");
    AssertEqual(JoistTakeoffCalculator.RoundingNearestFoot, item.JoistLengthRounding, "joist default rounding");
    AssertFalse(item.JoistShowLabels, "joist default hides per-segment labels");
    AssertFalse(item.JoistDetailedLabels, "joist default area label");
    AssertTrue(item.JoistDirectionFollowsAreaRotation, "joist default rotate direction");
    AssertFalse(item.JoistAddEndJoist, "new joist default keeps only the start edge");
}

static void LegacyJoistItemWithoutLabelFlagShowsLabels()
{
    WithTempJob("Legacy Joist Labels", job =>
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
        SetDataXmlProperty(item.FolderPath, "JoistEnabled", "True");
        OurPlanCoreJobStore.SaveMeasurements(item.FolderPath, new[]
        {
            new Measurement
            {
                MType = "area",
                JoistDirectionLocked = true,
                ScaleMetersPerPt = 0.3048,
                Points = SimpleJoistAreaPolygon().ToList(),
            },
        });

        TakeoffItem loaded = OurPlanCoreJobStore.TryReadTakeoffItem(item.FolderPath)
            ?? throw new InvalidOperationException("legacy joist item not loaded");

        AssertFalse(loaded.JoistShowLabels, "legacy joist item labels default hidden");
        AssertFalse(loaded.Measurements[0].JoistShowLabels, "legacy joist measurement labels default hidden");
    });
}

static void LegacyJoistItemOldFalseLabelFlagMigratesToLabels()
{
    WithTempJob("Legacy Joist False Labels", job =>
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
        SetDataXmlProperty(item.FolderPath, "JoistEnabled", "True");
        SetDataXmlProperty(item.FolderPath, "JoistShowLabels", "False");
        OurPlanCoreJobStore.SaveMeasurements(item.FolderPath, new[]
        {
            new Measurement
            {
                MType = "area",
                JoistDirectionLocked = true,
                ScaleMetersPerPt = 0.3048,
                Points = SimpleJoistAreaPolygon().ToList(),
            },
        });

        TakeoffItem loaded = OurPlanCoreJobStore.TryReadTakeoffItem(item.FolderPath)
            ?? throw new InvalidOperationException("legacy joist item not loaded");

        AssertFalse(loaded.JoistShowLabels, "legacy false joist item labels stay hidden by default");
        AssertFalse(loaded.Measurements[0].JoistShowLabels, "legacy false joist measurement labels stay hidden by default");
    });
}

static void LegacyJoistItemOldExplicitFalseLabelFlagMigratesToLabels()
{
    WithTempJob("Legacy Joist Explicit False Labels", job =>
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
        SetDataXmlProperty(item.FolderPath, "JoistEnabled", "True");
        SetDataXmlProperty(item.FolderPath, "JoistShowLabels", "False");
        SetDataXmlProperty(item.FolderPath, "JoistShowLabelsExplicit", "True");
        OurPlanCoreJobStore.SaveMeasurements(item.FolderPath, new[]
        {
            new Measurement
            {
                MType = "area",
                JoistDirectionLocked = true,
                ScaleMetersPerPt = 0.3048,
                Points = SimpleJoistAreaPolygon().ToList(),
            },
        });

        TakeoffItem loaded = OurPlanCoreJobStore.TryReadTakeoffItem(item.FolderPath)
            ?? throw new InvalidOperationException("legacy explicit joist item not loaded");

        AssertFalse(loaded.JoistShowLabels, "legacy explicit false joist item labels stay hidden");
        AssertFalse(loaded.Measurements[0].JoistShowLabels, "legacy explicit false joist measurement labels stay hidden");
        AssertFalse(loaded.JoistShowLabelsUserSet, "legacy explicit marker should not become a user label choice");
    });
}

static void JoistItemExplicitFalseLabelFlagStaysHidden()
{
    WithTempJob("Explicit Joist Hidden Labels", job =>
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
        JoistTakeoffDefaults.ApplyToNewJoistArea(item);
        item.JoistShowLabels = false;
        item.JoistShowLabelsUserSet = true;
        item.Measurements.Add(new Measurement
        {
            MType = "area",
            JoistDirectionLocked = true,
            ScaleMetersPerPt = 0.3048,
            Points = SimpleJoistAreaPolygon().ToList(),
        });
        OurPlanCoreJobStore.SaveTakeoffItem(item);

        TakeoffItem loaded = OurPlanCoreJobStore.TryReadTakeoffItem(item.FolderPath)
            ?? throw new InvalidOperationException("explicit joist item not loaded");

        AssertFalse(loaded.JoistShowLabels, "explicit joist item labels stay hidden");
        AssertFalse(loaded.Measurements[0].JoistShowLabels, "explicit joist measurement labels stay hidden");
    });
}

static void FolderTemplateOpeningsHaveNumberedChildren()
{
    FolderPlanNode openings = PlanSwiftFolderTemplateService.HardcodedSubTree("COM")
        .FirstOrDefault(node => string.Equals(node.Name, "openings", StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException("openings folder missing");

    AssertEqual("0,1,2,3,4,5", string.Join(",", openings.Children.Select(node => node.Name)), "openings child folders");
}

static void JoistPitchPersistsOnTakeoffItem()
{
    WithTempJob("Joist Pitch", job =>
    {
        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
        item.IsJoistTakeoff = true;
        item.JoistPitch = "3/12";
        item.JoistSpacingInches = 120;
        item.JoistLengthRounding = JoistTakeoffCalculator.RoundingNone;
        item.JoistDetailedLabels = false;
        item.JoistDirectionFollowsAreaRotation = false;
        item.JoistAddEndJoist = false;
        item.Measurements.Add(new Measurement
        {
            MType = "area",
            JoistDirectionLocked = true,
            ScaleMetersPerPt = 0.3048,
            Points = SimpleJoistAreaPolygon().ToList(),
        });

        OurPlanCoreJobStore.SaveTakeoffItem(item);
        TakeoffItem loaded = OurPlanCoreJobStore.TryReadTakeoffItem(item.FolderPath)
            ?? throw new InvalidOperationException("takeoff item not loaded");

        AssertEqual("3:12", loaded.JoistPitch, "loaded item pitch");
        AssertEqual("3:12", loaded.Measurements[0].JoistPitch, "loaded measurement pitch");
        AssertFalse(loaded.JoistDetailedLabels, "loaded item label format");
        AssertFalse(loaded.Measurements[0].JoistDetailedLabels, "loaded measurement label format");
        AssertFalse(loaded.JoistDirectionFollowsAreaRotation, "loaded item rotate direction flag");
        AssertFalse(loaded.Measurements[0].JoistDirectionFollowsAreaRotation, "loaded measurement rotate direction flag");
        AssertFalse(loaded.JoistAddEndJoist, "loaded item end joist flag");
        AssertFalse(loaded.Measurements[0].JoistAddEndJoist, "loaded measurement end joist flag");
    });
}

static void JoistPitchAppliesItemProperties()
{
    var item = new TakeoffItem
    {
        MeasurementType = "area",
        IsJoistTakeoff = true,
        JoistPitch = "4/12",
        JoistDetailedLabels = false,
        JoistDirectionFollowsAreaRotation = false,
        JoistAddEndJoist = false,
    };
    item.Measurements.Add(new Measurement
    {
        MType = "area",
        Points = SimpleJoistAreaPolygon().ToList(),
    });

    OurPlanCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);

    AssertEqual("4:12", item.Measurements[0].JoistPitch, "measurement pitch copied");
    AssertFalse(item.Measurements[0].JoistDetailedLabels, "measurement label format copied");
    AssertFalse(item.Measurements[0].JoistDirectionFollowsAreaRotation, "measurement rotate direction flag copied");
    AssertFalse(item.Measurements[0].JoistAddEndJoist, "measurement end joist flag copied");
}

static void PageOverlayPersistsThroughSourceRewrites()
{
    WithTempJob("Page Overlay", job =>
    {
        PageInfo basePage = CreatePageItem(job, job.PagesRoot, "S101");
        PageInfo overlayPage = CreatePageItem(job, job.PagesRoot, "S102");
        PageInfo replacementOverlayPage = CreatePageItem(job, job.PagesRoot, "S103");

        OurPlanCoreJobStore.SavePageOverlay(basePage.FolderPath, overlayPage.FolderPath, "#1E88E5", 0.42);
        OurPlanCoreJobStore.SavePageOverlayTransform(basePage.FolderPath, 12.5, -7.25, 1.2, 3.75);
        OurPlanCoreJobStore.SavePageOverlayVisibility(basePage.FolderPath, false);
        OurPlanCoreJobStore.SavePageHiddenTakeoffs(basePage.FolderPath, ["Walls"]);
        OurPlanCoreJobStore.SavePageHiddenMeasurements(basePage.FolderPath, ["m-old-1", "m-old-2"]);
        PageInfo loaded = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page missing");
        AssertEqual(overlayPage.FolderPath, loaded.OverlayPageFolder, "overlay page path");
        AssertEqual("#1E88E5", loaded.OverlayColor, "overlay color");
        AssertClose(0.42, loaded.OverlayOpacity, "overlay opacity");
        AssertClose(12.5, loaded.OverlayOffsetXPt, "overlay x offset");
        AssertClose(-7.25, loaded.OverlayOffsetYPt, "overlay y offset");
        AssertClose(1.2, loaded.OverlayScale, "overlay scale");
        AssertClose(3.75, loaded.OverlayRotationDegrees, "overlay rotation");
        AssertFalse(loaded.OverlayVisible, "overlay visibility");
        AssertEqual("Walls", string.Join(",", loaded.HiddenTakeoffs), "hidden takeoffs");
        AssertEqual("m-old-1,m-old-2", string.Join(",", loaded.HiddenMeasurements), "hidden measurements");

        OurPlanCoreJobStore.SavePageOverlayTransform(basePage.FolderPath, 13, -8, 1.25);
        PageInfo afterLegacyTransform = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page after legacy transform missing");
        AssertClose(13, afterLegacyTransform.OverlayOffsetXPt, "overlay x after legacy transform");
        AssertClose(-8, afterLegacyTransform.OverlayOffsetYPt, "overlay y after legacy transform");
        AssertClose(1.25, afterLegacyTransform.OverlayScale, "overlay scale after legacy transform");
        AssertClose(3.75, afterLegacyTransform.OverlayRotationDegrees, "overlay rotation survives legacy transform save");

        OurPlanCoreJobStore.SavePageScale(basePage.FolderPath, 0.3048);
        PageInfo afterScale = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page after scale missing");
        AssertEqual(overlayPage.FolderPath, afterScale.OverlayPageFolder, "overlay survives scale save");
        AssertClose(13, afterScale.OverlayOffsetXPt, "overlay x survives scale save");
        AssertClose(-8, afterScale.OverlayOffsetYPt, "overlay y survives scale save");
        AssertClose(1.25, afterScale.OverlayScale, "overlay scale survives scale save");
        AssertClose(3.75, afterScale.OverlayRotationDegrees, "overlay rotation survives scale save");
        AssertFalse(afterScale.OverlayVisible, "overlay visibility survives scale save");
        AssertEqual("Walls", string.Join(",", afterScale.HiddenTakeoffs), "hidden takeoffs survive scale save");
        AssertEqual("m-old-1,m-old-2", string.Join(",", afterScale.HiddenMeasurements), "hidden measurements survive scale save");

        OurPlanCoreJobStore.SavePageOverlay(basePage.FolderPath, overlayPage.FolderPath, "#43A047", 0.8);
        PageInfo afterColor = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page after overlay color missing");
        AssertEqual(overlayPage.FolderPath, afterColor.OverlayPageFolder, "same overlay survives color save");
        AssertEqual("#43A047", afterColor.OverlayColor, "overlay color changes");
        AssertClose(13, afterColor.OverlayOffsetXPt, "same overlay x survives color save");
        AssertClose(-8, afterColor.OverlayOffsetYPt, "same overlay y survives color save");
        AssertClose(1.25, afterColor.OverlayScale, "same overlay scale survives color save");
        AssertClose(3.75, afterColor.OverlayRotationDegrees, "same overlay rotation survives color save");

        OurPlanCoreJobStore.SavePageOverlay(basePage.FolderPath, replacementOverlayPage.FolderPath, "#E53935", 0.82);
        PageInfo afterReplacement = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page after replacement overlay missing");
        AssertEqual(replacementOverlayPage.FolderPath, afterReplacement.OverlayPageFolder, "replacement overlay page path");
        AssertClose(0, afterReplacement.OverlayOffsetXPt, "replacement overlay x resets");
        AssertClose(0, afterReplacement.OverlayOffsetYPt, "replacement overlay y resets");
        AssertClose(1, afterReplacement.OverlayScale, "replacement overlay scale resets");
        AssertClose(0, afterReplacement.OverlayRotationDegrees, "replacement overlay rotation resets");

        OurPlanCoreJobStore.ClearPageOverlay(basePage.FolderPath);
        PageInfo cleared = OurPlanCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page after clear missing");
        AssertEqual("", cleared.OverlayPageFolder, "overlay clears");
        AssertFalse(cleared.OverlayVisible, "overlay clear hides overlay");
        AssertClose(0, cleared.OverlayOffsetXPt, "cleared overlay x resets");
        AssertClose(0, cleared.OverlayOffsetYPt, "cleared overlay y resets");
        AssertClose(1, cleared.OverlayScale, "cleared overlay scale resets");
        AssertClose(0, cleared.OverlayRotationDegrees, "cleared overlay rotation resets");
    });
}

static void JobRecoveryNormalizesSnapshotReasons()
{
    AssertEqual("manual_save", JobRecoveryService.NormalizeSnapshotReason("Manual Save"), "spaces become underscores");
    AssertEqual("before_switch", JobRecoveryService.NormalizeSnapshotReason(" before-switch "), "symbols become underscores");
    AssertEqual("snapshot", JobRecoveryService.NormalizeSnapshotReason(" / "), "empty fallback");
}

static void JobRecoveryFiltersMetadataFiles()
{
    AssertTrue(JobRecoveryService.ShouldCopySnapshotFile(@"C:\job\Takeoffs\Walls\measurements.json"), "measurements copied");
    AssertTrue(JobRecoveryService.ShouldCopySnapshotFile(@"C:\job\Pages\a100\source_pdf.json"), "source metadata copied");
    AssertFalse(JobRecoveryService.ShouldCopySnapshotFile(@"C:\job\sources\plans.pdf"), "pdf skipped");
    AssertFalse(JobRecoveryService.ShouldCopySnapshotFile(@"C:\job\AI_Context\crops\crop.png"), "image skipped");
}

static void JobRecoveryLockWritesReadsAndClears()
{
    WithTempJob("Recovery Lock", job =>
    {
        JobRecoveryService.WriteLock(job);
        AssertTrue(File.Exists(JobRecoveryService.LockPath(job)), "lock written");
        AssertTrue(JobRecoveryService.TryReadLock(job, out JobRecoveryLockInfo info), "lock read");
        AssertEqual(Environment.ProcessId.ToString(), info.ProcessId.ToString(), "process id");
        AssertFalse(JobRecoveryService.IsStaleLock(info), "current lock should not be stale");

        JobRecoveryService.ClearLock(job);
        AssertFalse(File.Exists(JobRecoveryService.LockPath(job)), "lock cleared");
    });
}

static void JobRecoveryTreatsLiveForeignLockAsActive()
{
    using System.Diagnostics.Process process = StartShortLivedSleepProcess();
    try
    {
        var info = new JobRecoveryLockInfo { ProcessId = process.Id };
        AssertFalse(JobRecoveryService.IsCurrentProcessLock(info), "foreign lock should not be current");
        AssertFalse(JobRecoveryService.IsStaleLock(info), "running foreign lock should not be stale");
        AssertTrue(JobRecoveryService.IsStaleLock(new JobRecoveryLockInfo { ProcessId = -1 }), "invalid lock should be stale");
    }
    finally
    {
        if (!process.HasExited)
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(2000);
        }
    }
}

static System.Diagnostics.Process StartShortLivedSleepProcess()
{
    var startInfo = new System.Diagnostics.ProcessStartInfo
    {
        FileName = "powershell.exe",
        Arguments = "-NoProfile -Command \"Start-Sleep -Seconds 30\"",
        CreateNoWindow = true,
        UseShellExecute = false,
    };
    System.Diagnostics.Process process = System.Diagnostics.Process.Start(startInfo)
        ?? throw new InvalidOperationException("Failed to start sleep process for job recovery test.");
    Thread.Sleep(200);
    if (process.HasExited)
        throw new InvalidOperationException("Sleep process exited before job recovery test could inspect it.");
    return process;
}

static void JobRecoverySnapshotCopiesMetadataOnly()
{
    WithTempJob("Recovery Snapshot", job =>
    {
        string page = OurPlanCoreJobStore.EnsureFolder(job.PagesRoot, "A100");
        File.WriteAllText(Path.Combine(page, "source.json"), "{}");
        File.WriteAllText(Path.Combine(page, "sheet.pdf"), "not a real pdf");

        TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Walls", "#FF0000", "line");
        item.Measurements.Add(new Measurement
        {
            MType = "line",
            Points = [new SKPoint(0, 0), new SKPoint(1, 1)],
        });
        OurPlanCoreJobStore.SaveTakeoffItem(item);

        string snapshot = JobRecoveryService.SaveSnapshot(job, "manual save", maxSnapshots: 5);
        AssertTrue(File.Exists(Path.Combine(snapshot, "snapshot_manifest.json")), "manifest copied");
        AssertTrue(File.Exists(Path.Combine(snapshot, "Pages", "A100", "source.json")), "page source copied");
        AssertTrue(File.Exists(Path.Combine(snapshot, "Takeoffs", "Walls", "measurements.json")), "measurements copied");
        AssertFalse(File.Exists(Path.Combine(snapshot, "Pages", "A100", "sheet.pdf")), "pdf skipped");
    });
}

static void JobRecoverySnapshotPruningKeepsNewest()
{
    WithTempJob("Recovery Prune", job =>
    {
        JobRecoveryService.SaveSnapshot(job, "one", maxSnapshots: 2);
        JobRecoveryService.SaveSnapshot(job, "two", maxSnapshots: 2);
        JobRecoveryService.SaveSnapshot(job, "three", maxSnapshots: 2);

        int count = Directory.EnumerateDirectories(JobRecoveryService.SnapshotRoot(job)).Count();
        AssertEqual("2", count.ToString(), "pruned snapshot count");
    });
}

static void AppSettingsJobRootsDedupe()
{
    var settings = new AppSettings();
    string root = Path.Combine(Path.GetTempPath(), "onc_jobs");
    AppSettingsStore.AddJobsRoot(settings, root);
    AppSettingsStore.AddJobsRoot(settings, root + Path.DirectorySeparatorChar);

    AssertEqual("1", AppSettingsStore.CurrentJobsRootPaths(settings).Count.ToString(), "deduped roots");
}

static void AppSettingsRemovesJobRootByPath()
{
    var settings = new AppSettings();
    string root1 = Path.Combine(Path.GetTempPath(), "onc_jobs_remove_1");
    string root2 = Path.Combine(Path.GetTempPath(), "onc_jobs_remove_2");
    AppSettingsStore.AddJobsRoot(settings, root1);
    AppSettingsStore.AddJobsRoot(settings, root2);

    AppSettingsStore.RemoveJobsRoot(settings, root2 + Path.DirectorySeparatorChar);

    AssertEqual("1", AppSettingsStore.CurrentJobsRootPaths(settings).Count.ToString(), "removed one root");
    AssertEqual(Path.GetFullPath(root1), settings.JobsRootPath, "current root falls back");

    AppSettingsStore.RemoveJobsRoot(settings, root1);

    AssertEqual("0", AppSettingsStore.CurrentJobsRootPaths(settings).Count.ToString(), "removed final root");
    AssertEqual("", settings.JobsRootPath, "current root cleared");
}

static void PdfImportSourceFinderFindsNestedPdfFiles()
{
    string root = Path.Combine(Path.GetTempPath(), "onc_pdf_recursive", Guid.NewGuid().ToString("N"));
    string nested = Path.Combine(root, "Nested");
    Directory.CreateDirectory(nested);
    try
    {
        File.WriteAllText(Path.Combine(root, "A.PDF"), "%PDF-1.4");
        File.WriteAllText(Path.Combine(nested, "B.pdf"), "%PDF-1.4");
        File.WriteAllText(Path.Combine(nested, "ignore.txt"), "not a pdf");

        IReadOnlyList<string> files = PdfImportSourceFinder.FindPdfFilesRecursive(root);
        var relative = files
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .ToList();

        AssertEqual("2", files.Count.ToString(), "recursive pdf count");
        AssertEqual("A.PDF", relative[0], "top-level pdf first");
        AssertEqual("Nested/B.pdf", relative[1], "nested pdf found");
    }
    finally
    {
        TryDeleteDirectory(root);
    }
}

static void JobPickerRootsClassifyLocalCloudNetwork()
{
    string localRoot = Path.Combine(Path.GetTempPath(), "onc_local_jobs", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(localRoot);
    try
    {
        JobRootDescriptor local = JobRootSelectorBar.DescribeJobRoot(localRoot);
        AssertEqual("Local", local.KindLabel, "local root kind");
        AssertEqual("Ready", local.StatusLabel, "existing root status");
        AssertEqual(Path.GetFileName(localRoot), local.DisplayName, "root display name");

        AssertEqual(
            JobRootLocationKind.Cloud.ToString(),
            JobRootSelectorBar.ClassifyJobRootPath(@"C:\Users\User\OneDrive\OurPlanCore Jobs").ToString(),
            "OneDrive root kind");
        AssertEqual(
            JobRootLocationKind.Cloud.ToString(),
            JobRootSelectorBar.ClassifyJobRootPath(@"D:\Dropbox\Shared Takeoffs").ToString(),
            "Dropbox root kind");
        AssertEqual(
            JobRootLocationKind.Network.ToString(),
            JobRootSelectorBar.ClassifyJobRootPath(@"\\server\shared\jobs").ToString(),
            "UNC root kind");
        JobRootDescriptor network = JobRootSelectorBar.DescribeJobRoot(@"\\server\shared\jobs");
        AssertEqual("Open on demand", network.StatusLabel, "UNC root must not be probed on the UI thread");
        AssertTrue(network.Exists, "UNC root remains available through explicit open actions");

        JobRootDescriptor missing = JobRootSelectorBar.DescribeJobRoot(
            Path.Combine(localRoot, "missing child"));
        AssertEqual("Missing", missing.StatusLabel, "missing root status");
    }
    finally
    {
        Directory.Delete(localRoot, recursive: true);
    }
}

static void AppSettingsPathCanUseEnvOverride()
{
    string? previous = Environment.GetEnvironmentVariable(AppSettingsStore.SettingsPathEnvironmentVariable);
    string overridePath = Path.Combine(Path.GetTempPath(), "onc_settings_override", Guid.NewGuid().ToString("N"), "settings.json");
    try
    {
        Environment.SetEnvironmentVariable(AppSettingsStore.SettingsPathEnvironmentVariable, overridePath);
        AssertEqual(Path.GetFullPath(overridePath), AppSettingsStore.SettingsPath, "settings path override");
    }
    finally
    {
        Environment.SetEnvironmentVariable(AppSettingsStore.SettingsPathEnvironmentVariable, previous);
    }
}

static void AppSettingsCountSymbolPersists()
{
    string? previous = Environment.GetEnvironmentVariable(AppSettingsStore.SettingsPathEnvironmentVariable);
    string dir = Path.Combine(Path.GetTempPath(), "onc_settings_count_symbol", Guid.NewGuid().ToString("N"));
    string overridePath = Path.Combine(dir, "settings.json");
    try
    {
        Environment.SetEnvironmentVariable(AppSettingsStore.SettingsPathEnvironmentVariable, overridePath);
        var settings = new AppSettings { DefaultCountSymbol = CountDisplaySymbol.Cross };
        AppSettingsStore.Save(settings);

        AppSettings loaded = AppSettingsStore.Load();
        AssertEqual(CountDisplaySymbol.Cross, loaded.DefaultCountSymbol, "default count symbol persisted");

        loaded.DefaultCountSymbol = "invalid";
        AppSettingsStore.Save(loaded);
        AssertEqual(
            CountDisplaySymbol.Circle,
            AppSettingsStore.Load().DefaultCountSymbol,
            "invalid default count symbol falls back");
    }
    finally
    {
        Environment.SetEnvironmentVariable(AppSettingsStore.SettingsPathEnvironmentVariable, previous);
        TryDeleteDirectory(dir);
    }
}

static void AppSettingsPdfImportRasterDefaultMigrates()
{
    var settings = new AppSettings
    {
        BuildRasterCacheOnPdfImport = false,
        PdfImportRasterDefaultsVersion = 0,
    };

    AppSettingsStore.NormalizePdfImportRasterSettings(settings);

    AssertTrue(settings.BuildRasterCacheOnPdfImport, "PDF import raster cache should default on after migration");
    AssertEqual("1", settings.PdfImportRasterDefaultsVersion.ToString(), "PDF import raster defaults version");

    settings.BuildRasterCacheOnPdfImport = false;
    AppSettingsStore.NormalizePdfImportRasterSettings(settings);

    AssertFalse(settings.BuildRasterCacheOnPdfImport, "explicit PDF import raster opt-out should be preserved after migration");
}

static void AtomicWriteIgnoresStaleFixedTempPath()
{
    string dir = Path.Combine(Path.GetTempPath(), "onc_atomic_write", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(dir);
    string path = Path.Combine(dir, "global_ai_index.jsonl");
    string staleFixedTempPath = path + ".tmp";

    try
    {
        File.WriteAllText(path, "old");
        using var locked = new FileStream(staleFixedTempPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

        IoUtil.WriteAllTextAtomic(path, "new");

        AssertEqual("new", File.ReadAllText(path), "atomic write should use a unique temp path");
    }
    finally
    {
        TryDeleteDirectory(dir);
    }
}

static void AppSettingsRecentPreservesPinAndThumbnail()
{
    var settings = new AppSettings();
    string path = Path.Combine(Path.GetTempPath(), "onc_job");
    settings.RecentJobs.Add(new RecentJobInfo
    {
        Name = "Old",
        Path = path,
        ThumbnailPath = "thumb.png",
        IsPinned = true,
    });

    AppSettingsStore.AddRecentJob(settings, path, "New");

    AssertEqual("1", settings.RecentJobs.Count.ToString(), "deduped recent job");
    AssertEqual("New", settings.RecentJobs[0].Name, "updated name");
    AssertEqual("thumb.png", settings.RecentJobs[0].ThumbnailPath, "thumbnail preserved");
    AssertTrue(settings.RecentJobs[0].IsPinned, "pin preserved");
}

static void AppSettingsRemovesRecentJobByPath()
{
    var settings = new AppSettings();
    string path = Path.Combine(Path.GetTempPath(), "onc_job_remove");
    AppSettingsStore.AddRecentJob(settings, path, "Remove Me");
    AppSettingsStore.RemoveRecentJob(settings, path + Path.DirectorySeparatorChar);

    AssertEqual("0", settings.RecentJobs.Count.ToString(), "recent job removed");
}

static void OpenAiResponseParserExtractsOutputText()
{
    string json = """
    {
      "id": "resp_test",
      "status": "completed",
      "output": [
        {
          "type": "message",
          "content": [
            { "type": "output_text", "text": "Line one" },
            { "type": "output_text", "text": "Line two" }
          ]
        }
      ]
    }
    """;

    AssertEqual("Line one" + Environment.NewLine + "Line two", OpenAiResponseParser.ExtractOutputText(json), "output text");
}

static void OpenAiResponseParserReportsIncompleteMaxTokens()
{
    string json = """
    {
      "id": "resp_test",
      "status": "incomplete",
      "incomplete_details": { "reason": "max_output_tokens" },
      "output": [{ "type": "reasoning" }]
    }
    """;

    AssertEqual("incomplete", OpenAiResponseParser.ExtractString(json, "status"), "status");
    AssertEqual(
        "OpenAI response was incomplete (max_output_tokens). See raw response JSON.",
        OpenAiResponseParser.ExtractIncompleteError(json),
        "incomplete error");
}

static void KeyboardShortcutKeysUseEnglishDisplayText()
{
    AssertEqual("BK", KeyboardShortcutKeys.EnglishLayoutDisplay("bk"), "bookmark shortcut display");
    AssertEqual("\u0438\u043B", KeyboardShortcutKeys.RussianLayoutText("bk"), "bookmark russian layout text");
    AssertEqual("BK / \u0418\u041B", KeyboardShortcutKeys.DualLayoutDisplay("bk"), "bookmark dual layout display");
    AssertEqual("E", KeyboardShortcutKeys.EnglishLayoutDisplay("e"), "select shortcut display");
    AssertEqual("T", KeyboardShortcutKeys.EnglishLayoutDisplay("t"), "new item shortcut display");
}

static void TransformRotationSnapUsesFifteenDegreeSteps()
{
    AssertClose(0, TransformEditConstraints.SnapRotationDegrees(7.4), "rotation snap below half step");
    AssertClose(15, TransformEditConstraints.SnapRotationDegrees(7.5), "rotation snap half step");
    AssertClose(-15, TransformEditConstraints.SnapRotationDegrees(-7.5), "negative rotation snap half step");
    AssertClose(180, TransformEditConstraints.SnapRotationDegrees(179), "rotation snap near maximum");
}

static void PdfMetadataNeedsFallbackWhenScaleUnresolved()
{
    var metadata = new PdfSheetMetadata
    {
        SheetLabel = "A100",
        SheetTitle = "FIRST FLOOR PLAN",
        Suffix = "1st",
        SkipScale = false,
    };

    AssertTrue(PdfSheetMetadataService.NeedsFallback(metadata), "missing needed scale should need fallback");
}

static void PdfMetadataSkipScaleAvoidsFallback()
{
    var metadata = new PdfSheetMetadata
    {
        SheetLabel = "D1",
        SheetTitle = "DETAILS",
        Suffix = "d",
        SkipScale = true,
    };

    AssertFalse(PdfSheetMetadataService.NeedsFallback(metadata), "skip scale detail should not need fallback");
}

static void PdfPreviewRenderCacheRoundTrips()
{
    string oldRoot = Environment.GetEnvironmentVariable(PdfPreviewRenderCache.CacheRootEnvironmentVariable) ?? "";
    string root = Path.Combine(Path.GetTempPath(), "onc_preview_cache_tests", Guid.NewGuid().ToString("N"));
    string pdf = Path.Combine(root, "source.pdf");
    try
    {
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable(PdfPreviewRenderCache.CacheRootEnvironmentVariable, Path.Combine(root, "cache"));
        File.WriteAllText(pdf, "%PDF-1.4 preview cache test");
        File.SetLastWriteTimeUtc(pdf, new DateTime(2026, 5, 28, 10, 0, 0, DateTimeKind.Utc));

        var render = new PdfLayerRenderResult
        {
            ImageBytes = [1, 2, 3, 4],
            WidthPt = 612,
            HeightPt = 792,
            Layers = [new PdfLayer(7, "A-WALL", true)],
            LayersCaptured = true,
        };

        AssertFalse(
            PdfPreviewRenderCache.TryReadCleanPreview(
                pdf,
                0,
                ViewportRenderPolicy.InstantPagePreviewRenderScale,
                out _),
            "empty preview cache should miss");
        AssertTrue(
            PdfPreviewRenderCache.IsCleanPreviewRequest(
                pdf,
                0,
                ViewportRenderPolicy.InstantPagePreviewRenderScale,
                new Dictionary<int, bool>(),
                []),
            "initial clean PyMuPDF preview should be cacheable");
        AssertFalse(
            PdfPreviewRenderCache.IsCleanPreviewRequest(
                pdf,
                0,
                1.0,
                new Dictionary<int, bool>(),
                []),
            "normal quality rerender should not write the first-preview cache");
        AssertTrue(
            PdfPreviewRenderCache.IsCleanRenderRequest(
                pdf,
                0,
                1.0,
                new Dictionary<int, bool>(),
                []),
            "normal clean PyMuPDF render should be cacheable for repeat opens");
        AssertFalse(
            PdfPreviewRenderCache.IsCleanRenderRequest(
                pdf,
                0,
                1.0,
                new Dictionary<int, bool> { [7] = false },
                []),
            "hidden PDF layer states should not use the clean render cache");

        PdfPreviewRenderCache.TryWriteCleanPreview(
            pdf,
            0,
            ViewportRenderPolicy.InstantPagePreviewRenderScale,
            render);

        AssertTrue(
            PdfPreviewRenderCache.TryReadCleanPreview(
                pdf,
                0,
                ViewportRenderPolicy.InstantPagePreviewRenderScale,
                out PdfLayerRenderResult cached),
            "written preview cache should hit");
        AssertEqual("4", cached.ImageBytes.Length.ToString(), "cached preview image bytes length");
        AssertClose(612, cached.WidthPt, "cached preview width");
        AssertClose(792, cached.HeightPt, "cached preview height");
        AssertTrue(cached.LayersCaptured, "cached clean render should preserve layer discovery state");
        AssertEqual("1", cached.Layers.Count.ToString(), "cached clean render layers");

        PdfPreviewRenderCache.TryWriteCleanPreview(
            pdf,
            0,
            ViewportRenderPolicy.FastPageSwitchPreviewRenderScale,
            render);
        AssertTrue(
            PdfPreviewRenderCache.TryReadCleanPreview(
                pdf,
                0,
                ViewportRenderPolicy.FastPageSwitchPreviewRenderScale,
                out PdfLayerRenderResult fastCached),
            "written fast preview cache should hit");
        AssertClose(612, fastCached.WidthPt, "cached fast preview width");

        PdfPreviewRenderCache.TryWriteCleanRender(pdf, 0, 1.0f, render);
        AssertTrue(
            PdfPreviewRenderCache.TryReadCleanRender(pdf, 0, 1.0f, out PdfLayerRenderResult fullCached),
            "written full-scale clean render cache should hit");
        AssertClose(612, fullCached.WidthPt, "cached full render width");
        AssertEqual("A-WALL", fullCached.Layers[0].Name, "cached full render layer name");

        File.SetLastWriteTimeUtc(pdf, new DateTime(2026, 5, 28, 10, 1, 0, DateTimeKind.Utc));
        AssertFalse(
            PdfPreviewRenderCache.TryReadCleanPreview(
                pdf,
                0,
                ViewportRenderPolicy.InstantPagePreviewRenderScale,
                out _),
            "changing the source PDF modified time should invalidate the preview cache");
    }
    finally
    {
        Environment.SetEnvironmentVariable(
            PdfPreviewRenderCache.CacheRootEnvironmentVariable,
            string.IsNullOrWhiteSpace(oldRoot) ? null : oldRoot);
        TryDeleteDirectory(root);
    }
}

static void DetailTileDiskCacheRoundTrips()
{
    string oldRoot = Environment.GetEnvironmentVariable(DetailTileDiskCache.CacheRootEnvironmentVariable) ?? "";
    string root = Path.Combine(Path.GetTempPath(), "onc_detail_tile_cache_tests", Guid.NewGuid().ToString("N"));
    string pdf = Path.Combine(root, "source.pdf");
    try
    {
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable(DetailTileDiskCache.CacheRootEnvironmentVariable, Path.Combine(root, "cache"));
        File.WriteAllText(pdf, "%PDF-1.4 detail tile cache test");
        File.SetLastWriteTimeUtc(pdf, new DateTime(2026, 7, 5, 10, 0, 0, DateTimeKind.Utc));

        AssertTrue(
            DetailTileDiskCache.IsCacheableRequest(new Dictionary<int, bool>(), [], null),
            "clean detail request should be disk-cacheable");
        AssertFalse(
            DetailTileDiskCache.IsCacheableRequest(new Dictionary<int, bool> { [7] = false }, [], null),
            "layer-overridden detail request should not be disk-cacheable");
        AssertFalse(
            DetailTileDiskCache.IsCacheableRequest(new Dictionary<int, bool>(), [7], null),
            "highlighted detail request should not be disk-cacheable");

        var requestedClip = new SKRect(600, 600, 1800, 1500);
        var appliedClip = new SKRect(600, 600, 1799.5f, 1500);
        AssertFalse(
            DetailTileDiskCache.TryRead(pdf, 3, 2.5f, requestedClip, out _, out _),
            "empty detail tile cache should miss");

        var render = new PdfLayerRenderResult
        {
            ImageBytes = [9, 8, 7, 6, 5],
            ClipRect = appliedClip,
        };
        DetailTileDiskCache.QueueWrite(pdf, 3, 2.5f, requestedClip, appliedClip, render);

        byte[] cachedBytes = [];
        SKRect cachedClip = SKRect.Empty;
        bool hit = false;
        for (int attempt = 0; attempt < 100 && !hit; attempt++)
        {
            hit = DetailTileDiskCache.TryRead(pdf, 3, 2.5f, requestedClip, out cachedBytes, out cachedClip);
            if (!hit)
                Thread.Sleep(30);
        }

        AssertTrue(hit, "queued detail tile write should become readable");
        AssertEqual("5", cachedBytes.Length.ToString(), "cached detail tile image bytes length");
        AssertClose(appliedClip.Right, cachedClip.Right, "cached detail tile applied clip right");
        AssertFalse(
            DetailTileDiskCache.TryRead(pdf, 3, 2.5f, new SKRect(0, 0, 1200, 900), out _, out _),
            "different clip should miss the detail tile cache");
        AssertFalse(
            DetailTileDiskCache.TryRead(pdf, 4, 2.5f, requestedClip, out _, out _),
            "different page should miss the detail tile cache");
        AssertFalse(
            DetailTileDiskCache.TryRead(pdf, 3, 3.0f, requestedClip, out _, out _),
            "different render scale should miss the detail tile cache");

        File.SetLastWriteTimeUtc(pdf, new DateTime(2026, 7, 5, 10, 1, 0, DateTimeKind.Utc));
        AssertFalse(
            DetailTileDiskCache.TryRead(pdf, 3, 2.5f, requestedClip, out _, out _),
            "changing the source PDF modified time should invalidate the detail tile cache");
    }
    finally
    {
        Environment.SetEnvironmentVariable(
            DetailTileDiskCache.CacheRootEnvironmentVariable,
            string.IsNullOrWhiteSpace(oldRoot) ? null : oldRoot);
        TryDeleteDirectory(root);
    }
}

static void SheetOverlayRenderCacheRoundTrips()
{
    string oldRoot = Environment.GetEnvironmentVariable(SheetOverlayRenderCache.CacheRootEnvironmentVariable) ?? "";
    string root = Path.Combine(Path.GetTempPath(), "onc_sheet_overlay_cache_tests", Guid.NewGuid().ToString("N"));
    string pdf = Path.Combine(root, "overlay.pdf");
    try
    {
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable(SheetOverlayRenderCache.CacheRootEnvironmentVariable, Path.Combine(root, "cache"));
        File.WriteAllText(pdf, "%PDF-1.4 sheet overlay cache test");
        File.SetLastWriteTimeUtc(pdf, new DateTime(2026, 5, 28, 12, 0, 0, DateTimeKind.Utc));

        var page = new PageInfo
        {
            Name = "Base",
            FolderPath = Path.Combine(root, "Pages", "Base"),
            OverlayColor = "#38E5FF",
            OverlayOpacity = 0.62,
        };
        var overlayPage = new PageInfo
        {
            Name = "Overlay",
            FolderPath = Path.Combine(root, "Pages", "Overlay"),
            PdfPath = pdf,
            PdfPage = 0,
            PdfLayers =
            [
                new PdfLayerInfo { Number = 11, Name = "A-WALL", IsOn = true },
                new PdfLayerInfo { Number = 12, Name = "A-OLD", IsOn = false },
            ],
        };

        using var bitmap = new SKBitmap(3, 2, SKColorType.Rgba8888, SKAlphaType.Premul);
        bitmap.Erase(SKColors.Transparent);
        bitmap.SetPixel(1, 1, new SKColor(0x38, 0xE5, 0xFF, 0xB0));

        AssertFalse(
            SheetOverlayRenderCache.TryRead(page, overlayPage, 1.25f, out _, out _, out _),
            "empty sheet overlay cache should miss");

        SheetOverlayRenderCache.TryWrite(page, overlayPage, 1.25f, bitmap, 612, 792);
        string cacheRoot = Path.Combine(root, "cache");
        string[] rawFiles = Directory.GetFiles(cacheRoot, "*.bgra", SearchOption.AllDirectories);
        string[] pngFiles = Directory.GetFiles(cacheRoot, "*.png", SearchOption.AllDirectories);
        AssertEqual("1", rawFiles.Length.ToString(), "written sheet overlay cache raw sidecar count");
        AssertEqual("24", new FileInfo(rawFiles[0]).Length.ToString(), "written sheet overlay raw sidecar bytes");
        AssertEqual("1", pngFiles.Length.ToString(), "written sheet overlay cache png count");

        File.Delete(rawFiles[0]);
        AssertTrue(
            SheetOverlayRenderCache.TryRead(
                page,
                overlayPage,
                1.25f,
                out SKBitmap? cached,
                out float widthPt,
                out float heightPt),
            "written sheet overlay cache should hit");
        using (cached)
        {
            AssertEqual("3", cached?.Width.ToString() ?? "", "cached overlay bitmap width");
            AssertEqual("2", cached?.Height.ToString() ?? "", "cached overlay bitmap height");
        }
        AssertClose(612, widthPt, "cached overlay width pt");
        AssertClose(792, heightPt, "cached overlay height pt");

        rawFiles = Directory.GetFiles(cacheRoot, "*.bgra", SearchOption.AllDirectories);
        AssertEqual("1", rawFiles.Length.ToString(), "png fallback should rebuild sheet overlay raw sidecar");
        File.Delete(pngFiles[0]);
        AssertTrue(
            SheetOverlayRenderCache.TryRead(
                page,
                overlayPage,
                1.25f,
                out SKBitmap? rawCached,
                out widthPt,
                out heightPt),
            "sheet overlay raw sidecar should read when png is unavailable");
        using (rawCached)
        {
            AssertEqual("3", rawCached?.Width.ToString() ?? "", "raw cached overlay bitmap width");
            AssertEqual("2", rawCached?.Height.ToString() ?? "", "raw cached overlay bitmap height");
        }
        AssertClose(612, widthPt, "raw cached overlay width pt");
        AssertClose(792, heightPt, "raw cached overlay height pt");

        string copiedPdf = Path.Combine(root, "Copied", "overlay.pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(copiedPdf)!);
        File.Copy(pdf, copiedPdf);
        File.SetLastWriteTimeUtc(copiedPdf, File.GetLastWriteTimeUtc(pdf));
        var relocatedOverlayPage = new PageInfo
        {
            Name = "Overlay",
            FolderPath = Path.Combine(root, "Copied", "Pages", "Overlay"),
            PdfPath = copiedPdf,
            PdfPage = overlayPage.PdfPage,
            PdfLayers = overlayPage.PdfLayers,
        };
        AssertTrue(
            SheetOverlayRenderCache.TryRead(page, relocatedOverlayPage, 1.25f, out SKBitmap? relocatedCached, out _, out _),
            "relocated copied overlay PDF should reuse the persisted sheet overlay cache by PDF fingerprint");
        relocatedCached?.Dispose();

        var changedPage = new PageInfo
        {
            Name = "Base",
            FolderPath = page.FolderPath,
            OverlayColor = page.OverlayColor,
            OverlayOpacity = 0.75,
        };
        AssertTrue(
            SheetOverlayRenderCache.TryRead(
                changedPage,
                overlayPage,
                1.25f,
                out SKBitmap? opacityCached,
                out _,
                out _),
            "changing draw-time overlay opacity should reuse the color-tinted bitmap cache");
        opacityCached?.Dispose();
    }
    finally
    {
        Environment.SetEnvironmentVariable(
            SheetOverlayRenderCache.CacheRootEnvironmentVariable,
            string.IsNullOrWhiteSpace(oldRoot) ? null : oldRoot);
        TryDeleteDirectory(root);
    }
}

static void ViewportRenderScaleChoosesNextQualityStep()
{
    float[] steps = [0.75f, 1.0f, 1.5f, 2.25f, 3.0f, 4.0f];

    ViewportRenderPolicy.ApplyQualityMode(ViewportRenderPolicy.HighQualityMode);
    try
    {
        AssertClose(1.0, ViewportRenderPolicy.SelectRenderScale(0.4f, steps), "low zoom uses the responsive clarity floor");
        AssertClose(1.5, ViewportRenderPolicy.SelectRenderScale(1.2f, steps), "zoom chooses next higher render step");
        AssertClose(3.0, ViewportRenderPolicy.SelectRenderScale(8.0f, steps), "high quality mode uses a RAM-backed 3x responsive render cap");
        AssertClose(1.0, ViewportRenderPolicy.SelectSheetOverlayRenderScale(0.4f), "sheet overlay uses a cheap 1x source render at low overview zoom");
        AssertClose(2.0, ViewportRenderPolicy.SelectSheetOverlayRenderScale(1.0f), "sheet overlay keeps a 2x minimum source render at work zoom");
        AssertClose(3.0, ViewportRenderPolicy.SelectSheetOverlayRenderScale(2.6f), "sheet overlay upgrades to the high quality 3x source render at work zoom");
        AssertClose(
            3.0,
            ViewportRenderPolicy.SelectRenderScale(8.0f, steps, pageWidthPt: 2592f, pageHeightPt: 3456f),
            "large sheets can use the high-memory responsive render cap",
            tolerance: 0.001);

        ViewportRenderPolicy.ApplyQualityMode(ViewportRenderPolicy.BalancedQualityMode);
        AssertClose(2.25, ViewportRenderPolicy.SelectRenderScale(8.0f, steps), "balanced mode keeps the old responsive render cap");
        AssertClose(2.25, ViewportRenderPolicy.SelectSheetOverlayRenderScale(4.0f), "balanced mode caps sheet overlay refreshes below high quality");

        ViewportRenderPolicy.ApplyQualityMode(ViewportRenderPolicy.MaxQualityMode);
        AssertClose(4.0, ViewportRenderPolicy.SelectRenderScale(8.0f, steps), "max mode uses the 4x RAM render cap");
        AssertClose(4.0, ViewportRenderPolicy.SelectSheetOverlayRenderScale(3.5f, 612f, 792f), "max mode lets small sheet overlays reach 4x");
        AssertTrue(
            ViewportRenderPolicy.SelectSheetOverlayRenderScale(4.0f, 2592f, 1728f) < 3.5f,
            "large sheet overlays should be capped by the overlay pixel budget instead of rendering an oversized bitmap");

        ViewportRenderPolicy.ApplyQualityMode(ViewportRenderPolicy.HighQualityMode);
        AssertClose(
            0.0,
            ViewportRenderPolicy.SelectDetailRenderScale(0.5f, 2000f, 1200f, 0.15f),
            "fit zoom should not trigger expensive detail rendering");
        AssertFalse(
            ViewportRenderPolicy.ShouldUseZoomRefreshRender(0.21f, 0.35f),
            "fit zoom should keep the instant preview instead of starting a full refresh");
        AssertFalse(
            ViewportRenderPolicy.ShouldUseZoomRefreshRender(0.32f, 0.35f),
            "far zoom should keep the instant preview instead of repainting a heavy full-page bitmap");
        AssertTrue(
            ViewportRenderPolicy.ShouldSkipFullPageSharpUpgradeAtLowZoom(0.32f, 0.35f, 0.75f),
            "far zoom should not live-render a readable preview upgrade when it would compete with interaction");
        AssertTrue(
            ViewportRenderPolicy.ShouldSkipFullPageSharpUpgradeAtLowZoom(0.32f, 0.35f, 1.0f),
            "far zoom should still skip heavier full-page sharp upgrades after the readable preview");
        AssertFalse(
            ViewportRenderPolicy.ShouldUseDetailRender(0.32f, 0.35f),
            "ordinary zoom should use a refresh render, not an expensive clipped detail render");
        AssertTrue(
            ViewportRenderPolicy.ShouldUseDetailRender(0.80f, 0.35f),
            "work zoom above the preview should use clipped detail instead of waiting for a full-sheet refresh");
        AssertClose(
            0.80,
            ViewportRenderPolicy.SelectDetailRenderScale(0.80f, 800f, 600f, 0.35f),
            "preview-backed work zoom should render the visible clip at screen-matched scale");
        float defaultRasterScale = RasterSheetCacheService.DefaultRenderScale;
        AssertTrue(
            Math.Abs(ViewportRenderPolicy.RasterSheetDisplayMinZoom - ViewportRenderPolicy.ZoomRefreshMinZoom) < 0.001f &&
            ViewportRenderPolicy.RasterSheetDisplayExitZoom < ViewportRenderPolicy.RasterSheetDisplayMinZoom,
            "raster sheet display should replace full-page sharp refresh at work zoom with lower-zoom hysteresis back to preview");
        AssertTrue(
            ViewportRenderPolicy.SelectRasterSheetDisplayDpi(0.60f) == 72 &&
            ViewportRenderPolicy.SelectRasterSheetDisplayDpi(1.28f) == 144 &&
            ViewportRenderPolicy.SelectRasterSheetDisplayDpi(2.56f) == 200 &&
            ViewportRenderPolicy.SelectRasterSheetDisplayDpi(5.50f) == 200,
            "raster sheet display should choose the smallest readable DPI tier for the current zoom and avoid painting oversized 300/400dpi full-page bitmaps");
        AssertTrue(
            ViewportRenderPolicy.ShouldPreferLowerRasterSheetDpi(200, 72) &&
            ViewportRenderPolicy.ShouldPreferLowerRasterSheetDpi(200, 144) &&
            !ViewportRenderPolicy.ShouldPreferLowerRasterSheetDpi(144, 144) &&
            !ViewportRenderPolicy.ShouldPreferLowerRasterSheetDpi(100, 100),
            "raster sheet display should downshift oversized 200dpi rasters at 67% and 150% instead of repainting the full 200dpi bitmap");
        AssertFalse(
            ViewportRenderPolicy.ShouldUseDetailRender(2.0f, defaultRasterScale),
            "100-200% work zoom should stay on the 200dpi raster sheet instead of waiting for PDF detail rendering");
        AssertTrue(
            ViewportRenderPolicy.ShouldUseDetailRender(3.0f, defaultRasterScale),
            "raster sheet zoom just above 200dpi should be eligible for clipped sharp detail");
        AssertClose(
            3.0,
            ViewportRenderPolicy.SelectDetailRenderScale(3.0f, 600f, 400f, defaultRasterScale),
            "detail render should sharpen over the 200dpi raster sheet instead of being blocked by a low cap");
        AssertFalse(
            ViewportRenderPolicy.ShouldSkipFullRefreshDuringDetail(0.35f),
            "cheap preview should still be allowed to refresh to a normal base bitmap");
        AssertTrue(
            ViewportRenderPolicy.ShouldSkipFullRefreshDuringDetail(1.0f),
            "deep zoom should rely on clipped detail once a normal base bitmap exists");
        AssertClose(
            4.0,
            ViewportRenderPolicy.SelectDetailRenderScale(4.0f, 300f, 220f, 1.0f),
            "interactive detail render should reach screen-matched scale for normal visible clips");
        AssertTrue(
            ViewportRenderPolicy.SelectDetailRenderScale(16.0f, 3200f, 2200f, 1.0f) < 6.0f,
            "very large visible clips should be capped by the detail render pixel budget");

        ViewportRenderPolicy.ApplyQualityMode(ViewportRenderPolicy.MaxQualityMode);
        AssertClose(
            2.0,
            ViewportRenderPolicy.SelectDetailRenderScale(2.0f, 100f, 100f, 1.5f),
            "max quality should allow clipped detail to sharpen above a 1.5x base bitmap");
    }
    finally
    {
        ViewportRenderPolicy.ApplyQualityMode(ViewportRenderPolicy.HighQualityMode);
    }
}

static void ViewportRasterNavigationChoosesLighterDpi()
{
    AssertEqual(
        "100",
        ViewportRenderPolicy.SelectRasterSheetNavigationDpi(1.25f, currentDpi: 200, targetDpi: 144).ToString(),
        "ordinary work-zoom navigation should step below the idle 144dpi target");
    AssertEqual(
        "144",
        ViewportRenderPolicy.SelectRasterSheetNavigationDpi(2.50f, currentDpi: 200, targetDpi: 200).ToString(),
        "deep work-zoom navigation should cap motion painting at the prepared 144dpi tier");
    AssertEqual(
        "72",
        ViewportRenderPolicy.SelectRasterSheetNavigationDpi(0.60f, currentDpi: 200, targetDpi: 72).ToString(),
        "far zoom navigation should use the smallest readable raster tier");
    AssertEqual(
        "0",
        ViewportRenderPolicy.SelectRasterSheetNavigationDpi(0.0f, currentDpi: 200, targetDpi: 144).ToString(),
        "invalid zoom should not pick a navigation raster tier");
}

static void ViewportPanAllowsEdgeOverscroll()
{
    AssertClose(
        -1000,
        ViewportRenderPolicy.ClampPanWithOverscroll(-1900, pageSizePt: 2000, visibleSizePt: 1000),
        "large sheet can pan left edge past the viewport frame");
    AssertClose(
        1900,
        ViewportRenderPolicy.ClampPanWithOverscroll(1900, pageSizePt: 2000, visibleSizePt: 1000),
        "ordinary near-edge pan remains unchanged before the right overscroll limit");
    AssertClose(
        640,
        ViewportRenderPolicy.ClampPanWithOverscroll(640, pageSizePt: 2000, visibleSizePt: 1000),
        "ordinary in-page pan remains unchanged");
    AssertClose(
        -1000,
        ViewportRenderPolicy.ClampPanWithOverscroll(-1500, pageSizePt: 500, visibleSizePt: 1000),
        "small sheet can pan left edge beyond the viewport frame");
    AssertClose(
        500,
        ViewportRenderPolicy.ClampPanWithOverscroll(900, pageSizePt: 500, visibleSizePt: 1000),
        "small sheet can pan right edge beyond the viewport frame");
}

static void ViewportPanAllowsSheetPastFrameAtWorkZooms()
{
    RunOnStaThread(() =>
    {
        var viewport = new PdfViewport();
        SetPrivateField(viewport, "_pdfW", 1000f);
        SetPrivateField(viewport, "_pdfH", 800f);
        InvokePrivate(viewport, "UpdateCanvasMetrics", 1200, 900);

        foreach (float zoom in new[] { 0.67f, 1.50f })
        {
            SetPrivateField(viewport, "_zoom", zoom);

            SetPrivateField(viewport, "_panX", -9999f);
            SetPrivateField(viewport, "_panY", 9999f);
            InvokePrivate(viewport, "ClampPanToPage");
            PdfViewport.ViewState state = viewport.CaptureViewState();

            AssertClose(
                -1200f / zoom,
                state.PanX,
                $"viewport at {zoom:P0} should allow the sheet left edge to pan past the right frame");
            AssertClose(
                800f,
                state.PanY,
                $"viewport at {zoom:P0} should allow the sheet bottom edge to pan past the top frame");

            SetPrivateField(viewport, "_panX", 9999f);
            SetPrivateField(viewport, "_panY", -9999f);
            InvokePrivate(viewport, "ClampPanToPage");
            state = viewport.CaptureViewState();

            AssertClose(
                1000f,
                state.PanX,
                $"viewport at {zoom:P0} should allow the sheet right edge to pan past the left frame");
            AssertClose(
                -900f / zoom,
                state.PanY,
                $"viewport at {zoom:P0} should allow the sheet top edge to pan past the bottom frame");
        }
    });
}

static void ViewportBackgroundDefaultsToOpaqueWhite()
{
    AssertEqual("#FFFFFF", ViewportBackgroundPolicy.NormalizeColor(null), "null falls back to white");
    AssertEqual("#FFFFFF", ViewportBackgroundPolicy.NormalizeColor("   "), "blank falls back to white");
    AssertEqual("#FFFFFF", ViewportBackgroundPolicy.NormalizeColor("not-a-color"), "invalid falls back to white");
}

static void ViewportBackgroundStripsTransparency()
{
    AssertEqual("#ABCDEF", ViewportBackgroundPolicy.NormalizeColor("#00ABCDEF"), "transparent alpha is removed");
    AssertEqual("#ABCDEF", ViewportBackgroundPolicy.NormalizeColor(" #abcdef "), "valid color is canonicalized");
}

static void ViewportBackgroundTintsComfortColors()
{
    AssertFalse(ViewportBackgroundPolicy.ShouldTintRenderedPage("#FFFFFF"), "default white is not tinted");
    AssertTrue(ViewportBackgroundPolicy.ShouldTintRenderedPage("#FFF8E8"), "warm paper tints rendered page");
    AssertTrue(ViewportBackgroundPolicy.ShouldTintRenderedPage("#EFF7ED"), "soft green tints rendered page");
    AssertTrue(ViewportBackgroundPolicy.ShouldTintRenderedPage("#B8B8B8"), "dark gray paper tints rendered page");
    AssertTrue(ViewportBackgroundPolicy.ShouldTintRenderedPage("#000000"), "black paper tints rendered page");
    AssertTrue(
        ViewportBackgroundPolicy.RenderedPageTintAlpha("#000000") == ViewportBackgroundPolicy.DarkPageTintAlpha,
        "black paper uses dark tint alpha");
}

static void ViewportHighZoomUsesResponsiveNavigationFrame()
{
    AssertTrue(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: false,
            isFastNavigating: true,
            zoom: ViewportRenderPolicy.HighZoomFastFrameThreshold,
            activePageMeasurementCount: 0,
            hasBlockingInteraction: false),
        "high-zoom navigation should stay responsive even when optional visual simplification is off");

    AssertTrue(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: true,
            isFastNavigating: true,
            zoom: ViewportRenderPolicy.HighZoomFastFrameThreshold,
            activePageMeasurementCount: 0,
            hasBlockingInteraction: false),
        "enabled fast navigation should use fast frames at high zoom");
    AssertTrue(
        ViewportConstants.NavigationIdleMs >= 240 &&
        ViewportConstants.NavigationIdleMs > ViewportConstants.ZoomRerenderDelayMs &&
        ViewportRenderPolicy.DetailRenderNavigationQuietMs >= ViewportConstants.NavigationIdleMs &&
        ViewportRenderPolicy.DetailRenderNavigationQuietMs > ViewportRenderPolicy.DetailRenderCoalesceDelayMs &&
        ViewportRenderPolicy.PageOpenDeferredNavigationQuietMs >= ViewportConstants.NavigationIdleMs &&
        ViewportRenderPolicy.RasterSheetMotionQualityRestoreQuietMs > ViewportConstants.NavigationIdleMs,
        "high-zoom pan bursts should stay idle-gated while detail render waits for a real navigation quiet window");

    AssertTrue(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: true,
            isFastNavigating: true,
            zoom: ViewportRenderPolicy.HighZoomFastFrameThreshold - 0.1f,
            activePageMeasurementCount: 0,
            hasBlockingInteraction: false),
        "ordinary pan should also stay responsive instead of waiting for a full-quality frame");
}

static void ViewportRasterQualityRestoreWaitsForMotionQuiet()
{
    AssertTrue(
        ViewportRenderPolicy.ShouldHoldRasterSheetQualityAfterNavigation(
            TimeSpan.FromMilliseconds(ViewportConstants.NavigationIdleMs),
            targetDpi: 200),
        "200dpi raster sheet restore should wait beyond the first navigation idle tick");
    AssertFalse(
        ViewportRenderPolicy.ShouldHoldRasterSheetQualityAfterNavigation(
            TimeSpan.FromMilliseconds(ViewportRenderPolicy.RasterSheetMotionQualityRestoreQuietMs + 1),
            targetDpi: 200),
        "200dpi raster sheet restore should resume after the motion quiet window");
    AssertFalse(
        ViewportRenderPolicy.ShouldHoldRasterSheetQualityAfterNavigation(
            TimeSpan.Zero,
            targetDpi: ViewportRenderPolicy.RasterSheetNavigationMaxDpi),
        "navigation-tier raster sheet DPI should remain available during motion");
}

static void ViewportFarZoomUsesResponsiveNavigationFrame()
{
    AssertTrue(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: false,
            isFastNavigating: true,
            zoom: ViewportRenderPolicy.FarZoomFastFrameThreshold,
            activePageMeasurementCount: 0,
            hasBlockingInteraction: false),
        "far-zoom navigation should stay responsive even when optional visual simplification is off");

    AssertTrue(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: true,
            isFastNavigating: true,
            zoom: ViewportRenderPolicy.FarZoomFastFrameThreshold,
            activePageMeasurementCount: 0,
            hasBlockingInteraction: false),
        "enabled fast navigation should use fast frames at far zoom");
}

static void ViewportDensePageUsesResponsiveNavigationFrame()
{
    AssertTrue(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: false,
            isFastNavigating: true,
            zoom: 1.0f,
            activePageMeasurementCount: ViewportRenderPolicy.DenseNavigationFastFrameThreshold,
            hasBlockingInteraction: false),
        "dense-page navigation should stay responsive even when optional visual simplification is off");

    AssertTrue(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: true,
            isFastNavigating: true,
            zoom: 1.0f,
            activePageMeasurementCount: ViewportRenderPolicy.DenseNavigationFastFrameThreshold,
            hasBlockingInteraction: false),
        "enabled fast navigation should use fast frames for dense pages");
}

static void ViewportEditingBlocksFastNavigationFrame()
{
    AssertFalse(
        ViewportRenderPolicy.ShouldUseFastNavigationFrame(
            simplifyNavigationRendering: true,
            isFastNavigating: true,
            zoom: 6.0f,
            activePageMeasurementCount: ViewportRenderPolicy.DenseNavigationFastFrameThreshold,
            hasBlockingInteraction: true),
        "active edit interaction should keep the full render frame");
}

static void ViewportVisibleGeometryPaddingIsScreenRelative()
{
    AssertClose(
        ViewportRenderPolicy.VisibleGeometryPaddingScreenPx,
        ViewportRenderPolicy.VisibleGeometryPaddingPdf(1.0f),
        "1x zoom uses the configured screen padding in PDF points");
    AssertClose(
        ViewportRenderPolicy.VisibleGeometryPaddingScreenPx / 4.0f,
        ViewportRenderPolicy.VisibleGeometryPaddingPdf(4.0f),
        "high zoom reduces PDF-space padding instead of widening the screen margin");
    AssertClose(
        ViewportRenderPolicy.VisibleGeometryPaddingScreenPx * 2.0f,
        ViewportRenderPolicy.VisibleGeometryPaddingPdf(0.5f),
        "far zoom expands PDF-space padding to keep the same screen margin");
}

static void ViewportMeasurementLabelsSurviveDistantZoom()
{
    AssertTrue(
        ViewportRenderPolicy.ShouldDrawMeasurementLabels(
            zoom: ViewportRenderPolicy.MeasurementLabelMinZoom - 0.01f,
            activePageMeasurementCount: 1,
            fastNavigationFrame: false),
        "distant zoom should keep measurement labels controlled by display toggles");

    AssertTrue(
        ViewportRenderPolicy.ShouldDrawMeasurementLabels(
            zoom: ViewportRenderPolicy.MeasurementLabelMinZoom,
            activePageMeasurementCount: 1,
            fastNavigationFrame: false),
        "near zoom should draw labels for small pages");

    AssertTrue(
        ViewportRenderPolicy.ShouldDrawMeasurementLabels(
            zoom: ViewportRenderPolicy.HighZoomFastFrameThreshold,
            activePageMeasurementCount: 1,
            fastNavigationFrame: true),
        "small-page labels should not blink off during fast navigation frames");

    AssertFalse(
        ViewportRenderPolicy.ShouldDrawMeasurementLabels(
            zoom: 2.0f,
            activePageMeasurementCount: ViewportRenderPolicy.DenseMeasurementLabelThreshold + 1,
            fastNavigationFrame: false),
        "very dense pages can still suppress expensive label drawing");
}

static void ViewportMeasurementLodLimitsDenseDetails()
{
    AssertTrue(
        ViewportRenderPolicy.SlowSnapLogMs > 0 &&
        ViewportRenderPolicy.SlowSnapLogMs < ViewportRenderPolicy.SlowFrameLogMs,
        "snap search logging should catch pointer hitches before a full slow frame");

    AssertFalse(
        ViewportRenderPolicy.ShouldDrawMeasurementDetails(
            zoom: 2.0f,
            activePageMeasurementCount: ViewportRenderPolicy.DenseMeasurementDetailThreshold + 1,
            fastNavigationFrame: false),
        "dense pages should skip expensive measurement detail layer");

    AssertTrue(
        ViewportRenderPolicy.DetailRenderPaddingScreenPxForZoom(1.8f) <= 384f,
        "moderate zoom detail renders should keep clips small enough for responsive panning");
    AssertTrue(
        ViewportRenderPolicy.DetailRenderPaddingScreenPxForZoom(2.5f) <= 512f,
        "work zoom detail renders should avoid rendering a huge offscreen margin");
}

static void ViewportLodHidesExpensiveLayersDuringFastFrames()
{
    AssertTrue(
        ViewportRenderPolicy.ShouldDrawSheetOverlay(
            fastNavigationFrame: true,
            isOverlayEditing: false),
        "sheet overlays should remain visible during fast navigation so page switches do not look like the overlay disappeared");

    AssertTrue(
        ViewportRenderPolicy.ShouldDrawSheetOverlay(
            fastNavigationFrame: true,
            isOverlayEditing: true),
        "overlay point editing should keep the overlay visible");

    AssertTrue(
        ViewportRenderPolicy.ShouldDrawMeasurementGeometry(
            activePageMeasurementCount: ViewportRenderPolicy.DenseMeasurementGeometryThreshold + 1,
            fastNavigationFrame: true),
        "takeoff geometry should stay visible while high-resolution PDF detail catches up");

    AssertTrue(
        ViewportRenderPolicy.ShouldUseSimplifiedAreaPaint(
            zoom: 0.5f,
            activePageMeasurementCount: ViewportRenderPolicy.DenseMeasurementLabelThreshold,
            fastNavigationFrame: false),
        "far zoom dense sheets should skip expensive area fills while keeping outlines visible");
}

static void ViewportMeasurementSpatialIndexFiltersByBounds()
{
    Measurement near = SpatialIndexLine("near", 10, 10, 40, 10);
    Measurement far = SpatialIndexLine("far", 500, 500, 530, 500);
    Measurement hole = new()
    {
        Id = "hole",
        MType = "area",
        Points =
        [
            new SKPoint(200, 200),
            new SKPoint(260, 200),
            new SKPoint(260, 260),
            new SKPoint(200, 260),
        ],
        Holes =
        [
            [
                new SKPoint(215, 215),
                new SKPoint(225, 215),
                new SKPoint(225, 225),
                new SKPoint(215, 225),
            ],
        ],
    };

    var index = new ViewportMeasurementSpatialIndex([near, far, hole]);
    IReadOnlyList<Measurement> hits = index.Query(SKRect.Create(0, 0, 80, 80));
    AssertEqual("near", string.Join(",", hits.Select(hit => hit.Id)), "small query should return only intersecting measurement bounds");

    IReadOnlyList<ViewportMeasurementVertexCandidate> vertices = index.QueryVertices(SKRect.Create(8, 8, 5, 5));
    AssertEqual("near:0", string.Join(",", vertices.Select(hit => $"{hit.Measurement.Id}:{hit.GlobalIndex}")), "vertex query should return only nearby vertices");

    IReadOnlyList<ViewportMeasurementSegmentCandidate> segments = index.QuerySegments(SKRect.Create(20, 8, 5, 5));
    AssertEqual("near", string.Join(",", segments.Select(hit => hit.Measurement.Id)), "segment query should return only nearby segments");

    hits = index.Query(SKRect.Create(218, 218, 2, 2));
    AssertEqual("hole", string.Join(",", hits.Select(hit => hit.Id)), "holes should contribute to indexed measurement bounds");
}

static void ViewportMeasurementSpatialIndexPreservesDrawOrder()
{
    Measurement first = SpatialIndexLine("first", 0, 0, 100, 0);
    Measurement second = SpatialIndexLine("second", 20, 20, 120, 20);
    Measurement third = SpatialIndexLine("third", 40, 40, 140, 40);
    var index = new ViewportMeasurementSpatialIndex([first, second, third]);

    IReadOnlyList<Measurement> hits = index.Query(SKRect.Create(10, -10, 150, 70));
    AssertEqual("first,second,third", string.Join(",", hits.Select(hit => hit.Id)), "spatial query should preserve active-page draw order");

    Measurement broad = SpatialIndexLine("broad", -10000, -10000, 10000, 10000);
    index = new ViewportMeasurementSpatialIndex([first, broad, third]);
    hits = index.Query(SKRect.Create(50, 50, 1, 1));
    AssertEqual("broad", string.Join(",", hits.Select(hit => hit.Id)), "broad measurements should be included once");

    IReadOnlyList<ViewportMeasurementSegmentCandidate> segmentHits = index.QuerySegments(SKRect.Create(50, 50, 1, 1));
    AssertEqual("broad", string.Join(",", segmentHits.Select(hit => hit.Measurement.Id)), "broad segments should be included once");
}

static Measurement SpatialIndexLine(string id, float x1, float y1, float x2, float y2) =>
    new()
    {
        Id = id,
        MType = "line",
        Points =
        [
            new SKPoint(x1, y1),
            new SKPoint(x2, y2),
        ],
    };

static void ViewportRasterPageOpenAppliesHotBitmapCache()
{
    WithTempRasterBackedPage("raster_hot_open", page =>
    {
        AssertFalse(
            page.RasterSheet?.UseAsPageOpenRaster == true,
            "raster-backed viewport test page should default to the normal preview-first open path");
        AssertTrue(
            PdfViewport.WarmRasterSheetBitmapCache(page),
            "raster bitmap cache should warm before hot page open");

        RunOnStaThread(() =>
        {
            var viewport = new PdfViewport();
            viewport.LoadPage(page.PdfPath, page.PdfPage, page.FolderPath, rasterSheet: page.RasterSheet);

            AssertFalse(
                GetPrivateField<bool>(viewport, "_usingRasterSheetRender"),
                "default fit raster-backed page open should keep the normal preview-first path");
        });

        PageInfo rasterFirstPage = EnableRasterFirst(page);
        RunOnStaThread(() =>
        {
            var viewport = new PdfViewport();
            viewport.LoadPage(
                rasterFirstPage.PdfPath,
                rasterFirstPage.PdfPage,
                rasterFirstPage.FolderPath,
                rasterSheet: rasterFirstPage.RasterSheet);

            AssertTrue(
                GetPrivateField<bool>(viewport, "_usingRasterSheetRender"),
                "Raster First fit page open should apply the pre-rendered raster bitmap immediately");
            AssertTrue(
                GetPrivateFieldValue(viewport, "_pendingDocnetRender") == null,
                "Raster First fit page open must not queue docnet PDF render when the pre-rendered raster exists");
        });

        RunOnStaThread(() =>
        {
            var viewport = new PdfViewport();
            viewport.LoadPage(
                page.PdfPath,
                page.PdfPage,
                page.FolderPath,
                restoreView: new PdfViewport.ViewState(0.75f, 0, 0),
                rasterSheet: page.RasterSheet);

            AssertTrue(
                GetPrivateField<bool>(viewport, "_usingRasterSheetRender"),
                "default work-zoom hot raster-backed page open should still apply raster bitmap synchronously");
            AssertFalse(
                GetPrivateField<bool>(viewport, "_showingPreviousPageDuringSwitch"),
                "default work-zoom hot raster-backed page open should not keep a previous sheet placeholder");
            AssertTrue(
                GetPrivateFieldValue(viewport, "_pendingDocnetRender") == null,
                "default work-zoom hot raster-backed page open must not queue docnet PDF render");
            AssertTrue(
                GetPrivateFieldValue(viewport, "_pageBitmap") is SKBitmap { Width: > 0, Height: > 0 },
                "default work-zoom hot raster-backed page open should load a visible bitmap");
        });
    });
}

static void ViewportRasterPageOpenQueuesWarmupWithoutDocnetFallback()
{
    WithTempRasterBackedPage("raster_cold_open", page =>
    {
        RunOnStaThread(() =>
        {
            var viewport = new PdfViewport();
            viewport.LoadPage(page.PdfPath, page.PdfPage, page.FolderPath, rasterSheet: page.RasterSheet);

            bool usingRaster = GetPrivateField<bool>(viewport, "_usingRasterSheetRender");
            AssertFalse(
                usingRaster,
                "ordinary cold raster-backed fit-open should keep the normal preview-first path unless Raster First is enabled");
        });

        PageInfo rasterFirstPage = EnableRasterFirst(page);
        RunOnStaThread(() =>
        {
            var viewport = new PdfViewport();
            viewport.LoadPage(
                rasterFirstPage.PdfPath,
                rasterFirstPage.PdfPage,
                rasterFirstPage.FolderPath,
                rasterSheet: rasterFirstPage.RasterSheet);

            AssertTrue(
                GetPrivateField<bool>(viewport, "_usingRasterSheetRender"),
                "Raster First cold fit-open should synchronously apply the pre-rendered raster image");
            AssertTrue(
                GetPrivateFieldValue(viewport, "_pendingDocnetRender") == null,
                "Raster First cold fit-open must not fall back to docnet while a raster image exists");
        });

        RunOnStaThread(() =>
        {
            var viewport = new PdfViewport();
            viewport.LoadPage(
                page.PdfPath,
                page.PdfPage,
                page.FolderPath,
                rasterSheet: page.RasterSheet,
                hasSheetOverlayConfigured: true);

            bool usingRaster = GetPrivateField<bool>(viewport, "_usingRasterSheetRender");
            AssertTrue(
                usingRaster || HasRasterBitmapWarmupInFlight(viewport),
                "overlay cold raster-backed page open should either apply raster immediately or queue bitmap warmup");
            AssertTrue(
                GetPrivateFieldValue(viewport, "_pendingDocnetRender") == null,
                "overlay cold raster-backed page open must not fall back to docnet while raster bitmap warmup is queued");
        });
    });
}

static void ViewportOversizedRasterPageOpenQueuesResponsiveDpiWithPreviewFallback()
{
    WithTempRasterBackedPage("raster_oversized_open", page =>
    {
        AssertEqual(
            "200",
            RasterSheetCacheService.RenderScaleToDpi(page.RasterSheet?.RenderScale ?? 0).ToString(),
            "oversized raster-backed viewport test should start from active 200dpi metadata");

        RunOnStaThread(() =>
        {
            var viewport = new PdfViewport();
            viewport.LoadPage(
                page.PdfPath,
                page.PdfPage,
                page.FolderPath,
                rasterSheet: page.RasterSheet);

            AssertFalse(
                HasRasterDpiBuildInFlight(viewport, 72),
                "default oversized 200dpi raster fit-open should not build a lower-DPI raster before the first frame");
            AssertFalse(
                GetPrivateField<bool>(viewport, "_usingRasterSheetRender"),
                "default oversized 200dpi raster fit-open should keep the normal preview-first path");
        });

        PageInfo rasterFirstPage = EnableRasterFirst(page);
        RunOnStaThread(() =>
        {
            var viewport = new PdfViewport();
            viewport.LoadPage(
                rasterFirstPage.PdfPath,
                rasterFirstPage.PdfPage,
                rasterFirstPage.FolderPath,
                rasterSheet: rasterFirstPage.RasterSheet);

            AssertFalse(
                HasRasterDpiBuildInFlight(viewport, 72),
                "Raster First oversized 200dpi raster fit-open should not build a lower-DPI raster before the first frame");
            AssertTrue(
                GetPrivateField<bool>(viewport, "_usingRasterSheetRender"),
                "Raster First oversized 200dpi raster fit-open should paint the active pre-rendered bitmap immediately");
            AssertTrue(
                GetPrivateFieldValue(viewport, "_pendingDocnetRender") == null,
                "Raster First oversized 200dpi raster fit-open must not queue docnet PDF render when the raster exists");
        });

        foreach ((float zoom, int expectedDpi) in new[] { (0.67f, 72), (1.50f, 144) })
        {
            RunOnStaThread(() =>
            {
                var viewport = new PdfViewport();
                viewport.LoadPage(
                    rasterFirstPage.PdfPath,
                    rasterFirstPage.PdfPage,
                    rasterFirstPage.FolderPath,
                    restoreView: new PdfViewport.ViewState(zoom, 0, 0),
                    rasterSheet: rasterFirstPage.RasterSheet);

                AssertFalse(
                    HasRasterDpiBuildInFlight(viewport, expectedDpi),
                    $"Raster First oversized 200dpi raster page open at {zoom:P0} should not block first frame on {expectedDpi}dpi build");
                AssertTrue(
                    GetPrivateField<bool>(viewport, "_usingRasterSheetRender"),
                    $"Raster First oversized 200dpi raster page open at {zoom:P0} should paint the active pre-rendered bitmap immediately");
                AssertTrue(
                    GetPrivateFieldValue(viewport, "_pendingDocnetRender") == null,
                    $"Raster First oversized 200dpi raster page open at {zoom:P0} must not queue docnet PDF render when the raster exists");
            });
        }
    }, RasterSheetCacheService.DefaultRenderScale);
}

static void ViewportPastedBatchUndoRemovesManyMeasurementsInOneCallback()
{
    RunOnStaThread(() =>
    {
        const int existingCount = 40;
        const int pastedCount = 500;
        string pageFolder = Path.Combine(Path.GetTempPath(), "onc_batch_undo_page");
        var existing = Enumerable.Range(0, existingCount)
            .Select(index => BatchUndoLineMeasurement(index, pageFolder, @"Takeoffs\Existing"))
            .ToList();
        var pasted = Enumerable.Range(existingCount, pastedCount)
            .Select(index => BatchUndoLineMeasurement(index, pageFolder, @"Takeoffs\Pasted"))
            .ToList();

        var viewport = new PdfViewport();
        viewport.SetMeasurements(existing.Concat(pasted));

        int singleRemoved = 0;
        int batchRemovedCalls = 0;
        int batchRemovedCount = 0;
        viewport.MeasurementRemoved += _ => singleRemoved++;
        viewport.MeasurementsRemoved += measurements =>
        {
            batchRemovedCalls++;
            batchRemovedCount += measurements.Count;
        };

        viewport.RegisterAddedMeasurementsUndo(pasted, "remove pasted measurements");
        viewport.UndoLast();

        AssertEqual("0", singleRemoved.ToString(), "batch undo should not emit one removed event per measurement");
        AssertEqual("1", batchRemovedCalls.ToString(), "batch undo should emit one removed callback");
        AssertEqual(pastedCount.ToString(), batchRemovedCount.ToString(), "batch undo callback count");
        AssertEqual(existingCount.ToString(), LoadedViewportMeasurementCount(viewport).ToString(), "undo should keep pre-existing measurements");
    });
}

static void ActiveExcelValuesExportKeepsOnlyItemValues()
{
    PlanSwiftExportRow[] rows =
    [
        new(PlanSwiftExportRowKind.Header, "Framing"),
        new(PlanSwiftExportRowKind.Item, "First", "12,5", "FT"),
        new(PlanSwiftExportRowKind.Note, "Do not export"),
        new(PlanSwiftExportRowKind.Blank, ""),
        new(PlanSwiftExportRowKind.Item, "Second", "7", "EA"),
    ];

    object[,] values = ActiveExcelTakeoffExportService.BuildValuesMatrix(rows);

    AssertEqual("2", values.GetLength(0).ToString(), "only item values are exported");
    AssertEqual("1", values.GetLength(1).ToString(), "values use one Excel column");
    AssertTrue(values[0, 0] is double first && Math.Abs(first - 12.5) < 0.0001, "first value stays numeric");
    AssertTrue(values[1, 0] is double second && Math.Abs(second - 7) < 0.0001, "second value stays numeric");
}

static void RoofPitchUsesRisePerTwelveWithoutSheetScale()
{
    AssertTrue(
        RoofPitchGeometry.TryMeasure(new SKPoint(0, 0), new SKPoint(24, 12), out RoofPitchResult pitch),
        "pitch line is measurable");
    AssertEqual("6:12", pitch.Label, "pitch label uses rise per twelve");
    AssertTrue(Math.Abs(pitch.AngleDegrees - 26.565) < 0.01, "pitch angle is calculated");
    AssertEqual("0:12", RoofPitchGeometry.Label(new SKPoint(0, 5), new SKPoint(30, 5)), "horizontal pitch");
    AssertEqual("∞:12", RoofPitchGeometry.Label(new SKPoint(5, 0), new SKPoint(5, 30)), "vertical pitch");
    AssertEqual("pitch", OurPlanCoreJobStore.NormalizePageAnnotationKind("roofpitch"), "pitch kind persists");
}

static void ViewportOverlayUndoRestoresTransformInSharedHistory()
{
    RunOnStaThread(() =>
    {
        string targetPageFolder = Path.Combine(Path.GetTempPath(), "onc_overlay_undo_target");
        string overlayPageFolder = Path.Combine(Path.GetTempPath(), "onc_overlay_undo_source");
        var viewport = new PdfViewport();
        SetPrivateField(viewport, "_pageFolder", targetPageFolder);
        viewport.SetSheetOverlay(
            new SKBitmap(20, 10),
            200,
            100,
            "Overlay source",
            overlayPageFolder: overlayPageFolder);

        try
        {
            var changes = new List<SheetOverlayTransformChange>();
            viewport.SheetOverlayTransformChanged += changes.Add;
            AssertTrue(
                viewport.TryCommitSheetOverlayTransform(
                    targetPageFolder,
                    overlayPageFolder,
                    18,
                    -7,
                    1.25f,
                    12,
                    "Overlay moved."),
                "current target/source overlay transform should commit");
            AssertEqual("1", changes.Count.ToString(), "overlay commit event count");

            var measurement = new Measurement
            {
                MType = "line",
                PageFolder = targetPageFolder,
                Points = [new SKPoint(0, 0), new SKPoint(10, 0)],
            };
            viewport.SetMeasurements([measurement], clearUndoStack: false);
            viewport.RegisterAddedMeasurementsUndo([measurement], "remove test measurement");

            viewport.UndoLast();
            AssertEqual("0", LoadedViewportMeasurementCount(viewport).ToString(), "newer measurement undo should run first");
            AssertEqual(
                "18",
                viewport.CurrentSheetOverlayTransform()?.OffsetXPt.ToString(CultureInfo.InvariantCulture) ?? "",
                "measurement undo must leave the older overlay transform intact");

            viewport.UndoLast();
            SheetOverlayTransformSnapshot restored = viewport.CurrentSheetOverlayTransform() ??
                throw new InvalidOperationException("overlay transform disappeared during undo");
            AssertEqual("0", restored.OffsetXPt.ToString(CultureInfo.InvariantCulture), "overlay undo X");
            AssertEqual("0", restored.OffsetYPt.ToString(CultureInfo.InvariantCulture), "overlay undo Y");
            AssertEqual("1", restored.OverlayScale.ToString(CultureInfo.InvariantCulture), "overlay undo scale");
            AssertEqual("0", restored.OverlayRotationDegrees.ToString(CultureInfo.InvariantCulture), "overlay undo rotation");
            AssertEqual("2", changes.Count.ToString(), "overlay undo persistence event count");
            AssertEqual(targetPageFolder, changes[^1].TargetPageFolder, "overlay undo target identity");
            AssertEqual(overlayPageFolder, changes[^1].OverlayPageFolder, "overlay undo source identity");
        }
        finally
        {
            viewport.ClearSheetOverlay();
        }
    });
}

static void ViewportStaleOverlayUndoFallsThroughToMeasurementHistory()
{
    RunOnStaThread(() =>
    {
        string targetPageFolder = Path.Combine(Path.GetTempPath(), "onc_overlay_stale_target");
        string overlayPageFolder = Path.Combine(Path.GetTempPath(), "onc_overlay_stale_source");
        var viewport = new PdfViewport();
        SetPrivateField(viewport, "_pageFolder", targetPageFolder);
        viewport.SetSheetOverlay(
            new SKBitmap(20, 10),
            200,
            100,
            "Overlay source",
            overlayPageFolder: overlayPageFolder);

        try
        {
            var measurement = new Measurement
            {
                MType = "line",
                PageFolder = targetPageFolder,
                Points = [new SKPoint(0, 0), new SKPoint(10, 0)],
            };
            viewport.SetMeasurements([measurement]);
            viewport.RegisterAddedMeasurementsUndo([measurement], "remove fallback measurement");

            int transformEvents = 0;
            viewport.SheetOverlayTransformChanged += _ => transformEvents++;
            AssertTrue(
                viewport.TryCommitSheetOverlayTransform(
                    targetPageFolder,
                    overlayPageFolder,
                    9,
                    4,
                    1.1f,
                    3,
                    "Overlay moved."),
                "overlay setup transform should commit");

            SetPrivateField(
                viewport,
                "_sheetOverlaySourcePageFolder",
                Path.Combine(Path.GetTempPath(), "onc_overlay_replaced_source"));
            viewport.UndoLast();

            AssertEqual("0", LoadedViewportMeasurementCount(viewport).ToString(), "stale overlay undo must fall through");
            AssertEqual("1", transformEvents.ToString(), "stale overlay undo must not emit a persistence event");
            AssertEqual(
                "9",
                viewport.CurrentSheetOverlayTransform()?.OffsetXPt.ToString(CultureInfo.InvariantCulture) ?? "",
                "stale overlay undo must not mutate the replacement overlay");
        }
        finally
        {
            viewport.ClearSheetOverlay();
        }
    });
}

static Measurement BatchUndoLineMeasurement(int index, string pageFolder, string takeoffFolder) =>
    new()
    {
        MType = "line",
        PageFolder = pageFolder,
        TakeoffFolder = takeoffFolder,
        Points =
        [
            new SKPoint(index, 0),
            new SKPoint(index + 1, 1),
        ],
    };

static int LoadedViewportMeasurementCount(PdfViewport viewport)
{
    var field = typeof(PdfViewport).GetField("_measurementSet", BindingFlags.NonPublic | BindingFlags.Instance);
    if (field?.GetValue(viewport) is not ICollection<Measurement> measurements)
        throw new InvalidOperationException("PdfViewport measurement index was not available for test inspection.");
    return measurements.Count;
}

static IReadOnlyList<Measurement> ActiveViewportMeasurements(PdfViewport viewport)
{
    object? active = InvokePrivate(viewport, "ActivePageMeasurements");
    if (active is not IReadOnlyList<Measurement> measurements)
        throw new InvalidOperationException("PdfViewport active measurements were not available for test inspection.");
    return measurements;
}

static T GetPrivateField<T>(object instance, string name)
{
    object? value = GetPrivateFieldValue(instance, name);
    if (value is not T typed)
        throw new InvalidOperationException($"{instance.GetType().Name}.{name} was not a {typeof(T).Name} for test inspection.");
    return typed;
}

static void SetPrivateField<T>(object instance, string name, T value)
{
    FieldInfo field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(instance.GetType().FullName, name);
    field.SetValue(instance, value);
}

static object? InvokePrivate(object instance, string name, params object[] args)
{
    MethodInfo method = instance.GetType()
        .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
        .FirstOrDefault(candidate =>
            string.Equals(candidate.Name, name, StringComparison.Ordinal) &&
            candidate.GetParameters().Length == args.Length)
        ?? throw new MissingMethodException(instance.GetType().FullName, name);
    return method.Invoke(instance, args);
}

static object? GetPrivateFieldValue(object instance, string name)
{
    FieldInfo field = instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)
        ?? throw new MissingFieldException(instance.GetType().FullName, name);
    return field.GetValue(instance);
}

static bool HasRasterBitmapWarmupInFlight(PdfViewport viewport)
{
    object gate = GetPrivateField<object>(viewport, "_rasterSheetRebuildGate");
    lock (gate)
    {
        HashSet<string> inFlight = GetPrivateField<HashSet<string>>(viewport, "_rasterSheetRebuildsInFlight");
        return inFlight.Any(key => key.Contains("|bitmap:full", StringComparison.OrdinalIgnoreCase));
    }
}

static bool HasRasterDpiBuildInFlight(PdfViewport viewport, int dpi)
{
    object gate = GetPrivateField<object>(viewport, "_rasterSheetRebuildGate");
    lock (gate)
    {
        HashSet<string> inFlight = GetPrivateField<HashSet<string>>(viewport, "_rasterSheetRebuildsInFlight");
        string marker = $"|dpi:{dpi}";
        return inFlight.Any(key => key.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }
}

static void RunOnStaThread(Action action)
{
    Exception? failure = null;
    var thread = new Thread(() =>
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            failure = ex;
        }
    });
    thread.SetApartmentState(ApartmentState.STA);
    thread.Start();
    thread.Join();
    if (failure != null)
        throw new InvalidOperationException(failure.Message, failure);
}

static void PdfSnapIndexFindsNearestPoint()
{
    var index = new PdfSnapPointIndex(
    [
        new PdfGeometrySnapPoint(new SKPoint(100, 100), "pdf-point"),
        new PdfGeometrySnapPoint(new SKPoint(130, 100), "pdf-corner"),
    ]);

    AssertTrue(
        index.TryFind(new SKPoint(103, 100), tolerancePt: 8, out PdfGeometrySnapPoint snap),
        "nearby PDF snap point should be found");
    AssertEqual("100,100", $"{snap.Point.X:0},{snap.Point.Y:0}", "nearest snap point");

    AssertFalse(
        index.TryFind(new SKPoint(160, 100), tolerancePt: 8, out _),
        "distant PDF snap point should not be found");
}

static void PdfSnapIndexPrefersCornerTies()
{
    var index = new PdfSnapPointIndex(
    [
        new PdfGeometrySnapPoint(new SKPoint(100, 100), "pdf-point"),
        new PdfGeometrySnapPoint(new SKPoint(100, 100), "pdf-corner"),
    ]);

    AssertTrue(
        index.TryFind(new SKPoint(100, 100), tolerancePt: 4, out PdfGeometrySnapPoint snap),
        "same-location PDF snap candidates should be found");
    AssertEqual("pdf-corner", snap.Kind, "corner snap priority");
}

static void PdfSnapIndexSnapsToLine()
{
    var index = new PdfSnapPointIndex(
        [],
        [
            new PdfGeometrySnapSegment(new SKPoint(10, 10), new SKPoint(110, 10), "pdf-line"),
        ]);

    AssertTrue(
        index.TryFind(new SKPoint(64, 14), tolerancePt: 8, out PdfGeometrySnapPoint snap),
        "nearby PDF segment should be found");
    AssertEqual("64,10", $"{snap.Point.X:0},{snap.Point.Y:0}", "nearest point on line");
    AssertEqual("pdf-line", snap.Kind, "line snap kind");
}

static void PdfSnapIndexFindsNearestSegment()
{
    var index = new PdfSnapPointIndex(
        [],
        [
            new PdfGeometrySnapSegment(new SKPoint(10, 10), new SKPoint(110, 10), "pdf-line"),
            new PdfGeometrySnapSegment(new SKPoint(10, 50), new SKPoint(110, 50), "pdf-line"),
        ]);

    AssertTrue(
        index.TryFindSegment(new SKPoint(64, 14), tolerancePt: 8, out PdfGeometrySnapSegment segment, out float distance),
        "nearby PDF segment should be available for edge snap preview");
    AssertEqual("10,10", $"{segment.Start.X:0},{segment.Start.Y:0}", "nearest segment start");
    AssertEqual("110,10", $"{segment.End.X:0},{segment.End.Y:0}", "nearest segment end");
    AssertTrue(Math.Abs(distance - 4) < 0.001f, "nearest segment distance should be returned");
    var hits = index.FindSegments(new SKPoint(64, 14), tolerancePt: 8);
    AssertEqual("1", hits.Count.ToString(), "nearby PDF segment hits should be enumerable for contour ranking");
    AssertEqual("0", hits[0].Index.ToString(), "nearby PDF segment hit should preserve source segment index");
    AssertTrue(Math.Abs(hits[0].DistancePt - 4) < 0.001f, "nearby PDF segment hit should preserve distance");

    AssertFalse(
        index.TryFindSegment(new SKPoint(64, 34), tolerancePt: 8, out _, out _),
        "distant PDF segment should not be available for edge snap preview");
}

static void PdfRasterEdgeSnapBridgesSmallEndpointGaps()
{
    MethodInfo method = typeof(PdfViewport).GetMethod(
        "BuildPdfSnapContour",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("PdfViewport.BuildPdfSnapContour");
    MethodInfo coreMethod = typeof(PdfViewport).GetMethod(
        "BuildPdfSnapContourCore",
        BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException("PdfViewport.BuildPdfSnapContourCore");
    Type boundaryModeType = typeof(PdfViewport).GetNestedType("PdfSnapBoundaryMode", BindingFlags.NonPublic)
        ?? throw new MissingMemberException("PdfViewport.PdfSnapBoundaryMode");
    object everythingMode = Enum.Parse(boundaryModeType, "Everything");

    var bridgeSegments = new List<PdfGeometrySnapSegment>
    {
        new(new SKPoint(0, 0), new SKPoint(10, 0), "pdf-line"),
        new(new SKPoint(16, 0), new SKPoint(30, 0), "pdf-line"),
        new(new SKPoint(36, 0), new SKPoint(50, 0), "pdf-line"),
    };
    object?[] bridgeArgs = [bridgeSegments, 0, 7f, false, false, 0];
    var bridgePoints = (List<SKPoint>)method.Invoke(null, bridgeArgs)!;
    AssertEqual("0,0|10,0|16,0|30,0|36,0|50,0", FormatPoints(bridgePoints), "small endpoint gaps should bridge along an unambiguous chain");

    var directionalGapSegments = new List<PdfGeometrySnapSegment>
    {
        new(new SKPoint(0, 0), new SKPoint(20, 0), "pdf-line"),
        new(new SKPoint(70, 3), new SKPoint(100, 3), "pdf-line"),
        new(new SKPoint(108, 3), new SKPoint(130, 3), "pdf-line"),
    };
    object?[] directionalGapArgs = [directionalGapSegments, 0, 20f, false, false, 0];
    var directionalGapPoints = (List<SKPoint>)method.Invoke(null, directionalGapArgs)!;
    AssertEqual("0,0|20,0|70,3|100,3|108,3|130,3", FormatPoints(directionalGapPoints), "shifted collinear gaps should continue through window-like breaks");

    var closestBranchSegments = new List<PdfGeometrySnapSegment>
    {
        new(new SKPoint(0, 0), new SKPoint(10, 0), "pdf-line"),
        new(new SKPoint(15, 0), new SKPoint(30, 0), "pdf-line"),
        new(new SKPoint(15, 2), new SKPoint(30, 2), "pdf-line"),
    };
    object?[] closestBranchArgs = [closestBranchSegments, 0, 7f, false, false, 0];
    var closestBranchPoints = (List<SKPoint>)method.Invoke(null, closestBranchArgs)!;
    AssertEqual("0,0|10,0|15,0|30,0", FormatPoints(closestBranchPoints), "near branches should prefer the best same-axis continuation");

    var thickBranchSegments = new List<PdfGeometrySnapSegment>
    {
        new(new SKPoint(0, 0), new SKPoint(10, 0), "pdf-line", "", 6f),
        new(new SKPoint(15, 0), new SKPoint(30, 0), "pdf-line", "", 0.5f),
        new(new SKPoint(15, 2), new SKPoint(30, 2), "pdf-line", "", 6f),
    };
    object?[] thickBranchArgs = [thickBranchSegments, 0, 7f, false, false, 0];
    var thickBranchPoints = (List<SKPoint>)method.Invoke(null, thickBranchArgs)!;
    AssertEqual("0,0|10,0|15,2|30,2", FormatPoints(thickBranchPoints), "thick exterior linework should stay on the matching stroke width");

    var ambiguousBranchSegments = new List<PdfGeometrySnapSegment>
    {
        new(new SKPoint(0, 0), new SKPoint(10, 0), "pdf-line"),
        new(new SKPoint(15, 2), new SKPoint(30, 2), "pdf-line"),
        new(new SKPoint(15, -2), new SKPoint(30, -2), "pdf-line"),
    };
    object?[] ambiguousBranchArgs = [ambiguousBranchSegments, 0, 7f, false, false, 0];
    var ambiguousBranchPoints = (List<SKPoint>)method.Invoke(null, ambiguousBranchArgs)!;
    AssertEqual("0,0|10,0", FormatPoints(ambiguousBranchPoints), "equally likely branches should not be guessed");

    var openRectangleSegments = new List<PdfGeometrySnapSegment>
    {
        new(new SKPoint(0, 0), new SKPoint(80, 0), "pdf-line"),
        new(new SKPoint(80, 0), new SKPoint(80, 50), "pdf-line"),
        new(new SKPoint(80, 50), new SKPoint(0, 50), "pdf-line"),
        new(new SKPoint(0, 50), new SKPoint(0, 32), "pdf-line"),
        new(new SKPoint(0, 18), new SKPoint(0, 0), "pdf-line"),
    };
    object?[] boundaryArgs = [openRectangleSegments, 0, 16f, true, false, 0];
    var boundaryPoints = (List<SKPoint>)method.Invoke(null, boundaryArgs)!;
    AssertTrue(boundaryArgs[4] is true, "Area PDF boundary pass should close a likely exterior contour across bridge-sized door/window gaps");
    double boundaryArea = Math.Abs(SignedAreaForTest(boundaryPoints));
    AssertTrue(boundaryArea > 3000, $"closed PDF boundary contour should preserve the probable exterior footprint area, got {boundaryArea:0.0}: {FormatPoints(boundaryPoints)}");

    var shortJogSegments = new List<PdfGeometrySnapSegment>
    {
        new(new SKPoint(0, 0), new SKPoint(120, 0), "pdf-line"),
        new(new SKPoint(120, 0), new SKPoint(120, 24), "pdf-line"),
        new(new SKPoint(120, 24), new SKPoint(220, 24), "pdf-line"),
        new(new SKPoint(220, 24), new SKPoint(220, 100), "pdf-line"),
        new(new SKPoint(220, 100), new SKPoint(0, 100), "pdf-line"),
        new(new SKPoint(0, 100), new SKPoint(0, 0), "pdf-line"),
    };
    object?[] shortJogArgs = [shortJogSegments, 1, 80f, true, false, 0];
    var shortJogPoints = (List<SKPoint>)method.Invoke(null, shortJogArgs)!;
    AssertTrue(shortJogArgs[4] is true, "large bridge tolerance must not collapse selected 24pt exterior jog segments");
    double shortJogArea = Math.Abs(SignedAreaForTest(shortJogPoints));
    AssertTrue(shortJogArea > 18000, $"short exterior jog contour should stay closed and full size, got {shortJogArea:0.0}: {FormatPoints(shortJogPoints)}");

    var noisyWallCoreSegments = new List<PdfGeometrySnapSegment>();
    int noisySelectedIndex = AddFragmentedHorizontal(noisyWallCoreSegments, 0, 0, 120, 8);
    AddFragmentedVertical(noisyWallCoreSegments, 120, 0, 70, 6);
    AddFragmentedHorizontal(noisyWallCoreSegments, 70, 120, 0, 8);
    AddFragmentedVertical(noisyWallCoreSegments, 0, 70, 0, 6);
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(-24, -40), new SKPoint(144, -40), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(-24, 110), new SKPoint(144, 110), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(0, -40), new SKPoint(0, 0), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(120, -40), new SKPoint(120, 0), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(0, 70), new SKPoint(0, 110), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(120, 70), new SKPoint(120, 110), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(60, 0), new SKPoint(60, 28), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(60, 42), new SKPoint(60, 70), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(60, 28), new SKPoint(78, 28), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(60, 31), new SKPoint(78, 31), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(60, 28), new SKPoint(68, 32), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(68, 32), new SKPoint(74, 38), "pdf-line"));
    noisyWallCoreSegments.Add(new PdfGeometrySnapSegment(new SKPoint(74, 38), new SKPoint(78, 46), "pdf-line"));

    object?[] noisyWallCoreArgs = [noisyWallCoreSegments, noisySelectedIndex, 24f, true, false, 0];
    var noisyWallCorePoints = (List<SKPoint>)method.Invoke(null, noisyWallCoreArgs)!;
    AssertTrue(noisyWallCoreArgs[4] is true, "dense exterior wall core should still close when sparse dimension graphics are connected");
    SKRect noisyWallCoreBounds = BoundsForTest(noisyWallCorePoints);
    AssertTrue(
        noisyWallCoreBounds.Top > -12 &&
        noisyWallCoreBounds.Bottom < 82 &&
        noisyWallCoreBounds.Left > -12 &&
        noisyWallCoreBounds.Right < 132,
        $"sparse dimension graphics should not expand PDF wall contour bounds, got {noisyWallCoreBounds}: {FormatPoints(noisyWallCorePoints)}");

    object?[] noisyEverythingArgs = [noisyWallCoreSegments, noisySelectedIndex, 24f, true, everythingMode, false, 0];
    var noisyEverythingPoints = (List<SKPoint>)coreMethod.Invoke(null, noisyEverythingArgs)!;
    AssertTrue(noisyEverythingArgs[5] is true, "polyline everything should still close the likely exterior wall contour");
    SKRect noisyEverythingBounds = BoundsForTest(noisyEverythingPoints);
    AssertTrue(
        noisyEverythingBounds.Top > -12 &&
        noisyEverythingBounds.Bottom < 82 &&
        noisyEverythingBounds.Left > -12 &&
        noisyEverythingBounds.Right < 132,
        $"polyline everything should ignore interior door arcs and narrow paired door lines, got {noisyEverythingBounds}: {FormatPoints(noisyEverythingPoints)}");

    var doorOnlySegments = new List<PdfGeometrySnapSegment>
    {
        new(new SKPoint(60, 28), new SKPoint(78, 28), "pdf-line"),
        new(new SKPoint(60, 31), new SKPoint(78, 31), "pdf-line"),
        new(new SKPoint(60, 28), new SKPoint(68, 32), "pdf-line"),
        new(new SKPoint(68, 32), new SKPoint(74, 38), "pdf-line"),
        new(new SKPoint(74, 38), new SKPoint(78, 46), "pdf-line"),
    };
    object?[] doorOnlyArgs = [doorOnlySegments, 0, 24f, true, everythingMode, false, 0];
    var doorOnlyPoints = (List<SKPoint>)coreMethod.Invoke(null, doorOnlyArgs)!;
    AssertEqual("0", doorOnlyPoints.Count.ToString(), "interior door swing symbols should not become area PDF contour fallback polylines");

    var realLikeDoorSegments = new List<PdfGeometrySnapSegment>
    {
        new(new SKPoint(1509.72f, 721.44f), new SKPoint(1509.72f, 745.44f), "pdf-line"),
        new(new SKPoint(1510.68f, 721.44f), new SKPoint(1510.68f, 745.44f), "pdf-line"),
        new(new SKPoint(1506.00f, 721.92f), new SKPoint(1510.68f, 721.44f), "pdf-line"),
        new(new SKPoint(1501.56f, 723.24f), new SKPoint(1506.00f, 721.92f), "pdf-line"),
        new(new SKPoint(1497.36f, 725.52f), new SKPoint(1501.56f, 723.24f), "pdf-line"),
        new(new SKPoint(1493.76f, 728.40f), new SKPoint(1497.36f, 725.52f), "pdf-line"),
        new(new SKPoint(1490.76f, 732.12f), new SKPoint(1493.76f, 728.40f), "pdf-line"),
        new(new SKPoint(1488.48f, 736.20f), new SKPoint(1490.76f, 732.12f), "pdf-line"),
        new(new SKPoint(1487.16f, 740.76f), new SKPoint(1488.48f, 736.20f), "pdf-line"),
        new(new SKPoint(1486.68f, 745.44f), new SKPoint(1487.16f, 740.76f), "pdf-line"),
    };
    object?[] realLikeDoorArgs = [realLikeDoorSegments, 0, 24f, true, everythingMode, false, 0];
    var realLikeDoorPoints = (List<SKPoint>)coreMethod.Invoke(null, realLikeDoorArgs)!;
    AssertEqual("0", realLikeDoorPoints.Count.ToString(), "fragmented a203-style interior door swing should be rejected before area contour fallback");

    var exteriorPrioritySegments = new List<PdfGeometrySnapSegment>
    {
        new(new SKPoint(0, 0), new SKPoint(160, 0), "pdf-line"),
        new(new SKPoint(160, 0), new SKPoint(160, 100), "pdf-line"),
        new(new SKPoint(160, 100), new SKPoint(0, 100), "pdf-line"),
        new(new SKPoint(0, 100), new SKPoint(0, 0), "pdf-line"),
        new(new SKPoint(80, 0), new SKPoint(80, 40), "pdf-line"),
        new(new SKPoint(80, 40), new SKPoint(80, 72), "pdf-line"),
        new(new SKPoint(80, 40), new SKPoint(92, 44), "pdf-line"),
        new(new SKPoint(92, 44), new SKPoint(101, 52), "pdf-line"),
        new(new SKPoint(101, 52), new SKPoint(106, 64), "pdf-line"),
    };
    object?[] exteriorPriorityArgs = [exteriorPrioritySegments, 5, 24f, true, everythingMode, false, 0];
    var exteriorPriorityPoints = (List<SKPoint>)coreMethod.Invoke(null, exteriorPriorityArgs)!;
    AssertEqual(
        "0",
        exteriorPriorityPoints.Count.ToString(),
        $"door-like selected hits should not return an interior wall capsule before the exterior candidate, got {FormatPoints(exteriorPriorityPoints)}");

    var largeBridgeSegments = new List<PdfGeometrySnapSegment>
    {
        new(new SKPoint(0, 0), new SKPoint(120, 0), "pdf-line"),
        new(new SKPoint(120, 0), new SKPoint(120, 40), "pdf-line"),
        new(new SKPoint(120, 40), new SKPoint(120, 80), "pdf-line"),
        new(new SKPoint(120, 80), new SKPoint(0, 80), "pdf-line"),
        new(new SKPoint(0, 80), new SKPoint(0, 0), "pdf-line"),
        new(new SKPoint(120, 40), new SKPoint(150, 40), "pdf-line"),
        new(new SKPoint(300, 40), new SKPoint(340, 40), "pdf-line"),
        new(new SKPoint(340, 40), new SKPoint(340, 120), "pdf-line"),
        new(new SKPoint(340, 120), new SKPoint(300, 120), "pdf-line"),
    };
    object?[] largeBridgeArgs = [largeBridgeSegments, 0, 80f, true, everythingMode, false, 0];
    var largeBridgePoints = (List<SKPoint>)coreMethod.Invoke(null, largeBridgeArgs)!;
    AssertTrue(largeBridgeArgs[5] is true, "large bridge contour should still close the selected local footprint");
    SKRect largeBridgeBounds = BoundsForTest(largeBridgePoints);
    AssertTrue(
        largeBridgeBounds.Right < 220 &&
        largeBridgeBounds.Bottom < 120,
        $"large bridge should not glue a distant aligned foreign figure into the selected footprint, got {largeBridgeBounds}: {FormatPoints(largeBridgePoints)}");

    static int AddFragmentedHorizontal(List<PdfGeometrySnapSegment> segments, float y, float x0, float x1, int pieces)
    {
        int first = segments.Count;
        float step = (x1 - x0) / pieces;
        for (int i = 0; i < pieces; i++)
        {
            float start = x0 + (step * i);
            float end = x0 + (step * (i + 1));
            segments.Add(new PdfGeometrySnapSegment(new SKPoint(start, y), new SKPoint(end, y), "pdf-line"));
        }

        return first;
    }

    static void AddFragmentedVertical(List<PdfGeometrySnapSegment> segments, float x, float y0, float y1, int pieces)
    {
        float step = (y1 - y0) / pieces;
        for (int i = 0; i < pieces; i++)
        {
            float start = y0 + (step * i);
            float end = y0 + (step * (i + 1));
            segments.Add(new PdfGeometrySnapSegment(new SKPoint(x, start), new SKPoint(x, end), "pdf-line"));
        }
    }
}

static string FormatPoints(IReadOnlyList<SKPoint> points) =>
    string.Join("|", points.Select(point => $"{point.X:0},{point.Y:0}"));

static SKRect BoundsForTest(IReadOnlyList<SKPoint> points)
{
    if (points.Count == 0)
        return SKRect.Empty;

    float left = points.Min(point => point.X);
    float top = points.Min(point => point.Y);
    float right = points.Max(point => point.X);
    float bottom = points.Max(point => point.Y);
    return new SKRect(left, top, right, bottom);
}

static List<Measurement> SectionMeasurements(params string[] ids) =>
    ids.Select(id => new Measurement { Id = id }).ToList();

static void AssertSectionOrder(string expected, IReadOnlyList<Measurement> measurements, string message) =>
    AssertEqual(expected, string.Join(",", measurements.Select(measurement => measurement.Id)), message);

static TakeoffItem CreateMeasuredTakeoffItem(
    OurPlanCoreJob job,
    string parentFolder,
    string name,
    string measurementType,
    string pageFolder,
    IReadOnlyList<SKPoint> points)
{
    TakeoffItem item = OurPlanCoreJobStore.CreateTakeoffItem(job, parentFolder, name, "#FF4444", measurementType);
    item.Measurements.Add(new Measurement
    {
        Id = Guid.NewGuid().ToString("N"),
        Name = name,
        MType = measurementType,
        Color = "#FF4444",
        PageFolder = pageFolder,
        TakeoffFolder = item.FolderPath,
        ScaleMetersPerPt = 0.3048,
        Points = points.ToList(),
    });
    OurPlanCoreJobStore.SaveTakeoffItem(item);
    return item;
}

static SmartMassingTakeoffAiAssignment AiAssignment(
    OurPlanCoreJob job,
    TakeoffItem item,
    string role,
    int level) =>
    new()
    {
        TakeoffId = Path.GetFileName(item.FolderPath),
        FolderPath = Path.GetRelativePath(job.RootPath, item.FolderPath),
        Role = role,
        Level = level,
        Confidence = 0.92,
        Reason = "test plan",
    };

static IReadOnlyList<SKPoint> RectPoints(float x, float y, float width, float height) =>
[
    new SKPoint(x, y),
    new SKPoint(x + width, y),
    new SKPoint(x + width, y + height),
    new SKPoint(x, y + height),
];

static ThreeDRoofGuide RoofGuide(string kind, double x1, double z1, double x2, double z2, double pitchRisePerFoot = 0)
{
    var guide = new ThreeDRoofGuide
    {
        Kind = kind,
        Label = ThreeDRoofGuideKinds.Title(kind),
        PageFolder = @"C:\job\Pages\A101",
        ElevationFeet = 9.1,
        PitchRisePerFoot = pitchRisePerFoot,
        DefinesSlope = ThreeDRoofGuideKinds.Normalize(kind) == ThreeDRoofGuideKinds.Eave,
        Color = ThreeDRoofGuideKinds.Color(kind),
        Points =
        [
            new ThreeDRoofGuidePoint { PdfX = x1, PdfY = z1, XFeet = x1, ZFeet = z1 },
            new ThreeDRoofGuidePoint { PdfX = x2, PdfY = z2, XFeet = x2, ZFeet = z2 },
        ],
    };
    guide.RawPoints = guide.Points
        .Select(point => new ThreeDRoofGuidePoint
        {
            PdfX = point.PdfX,
            PdfY = point.PdfY,
            XFeet = point.XFeet,
            ZFeet = point.ZFeet,
        })
        .ToList();
    return guide;
}

static ThreeDFloorSlab RoofSlab(string label, double x, double z, double width, double depth) =>
    new()
    {
        Label = label,
        TakeoffName = label,
        LevelKey = "roof",
        ElevationFeet = 10,
        Points =
        [
            new ThreeDPoint { XFeet = x, ZFeet = z },
            new ThreeDPoint { XFeet = x + width, ZFeet = z },
            new ThreeDPoint { XFeet = x + width, ZFeet = z + depth },
            new ThreeDPoint { XFeet = x, ZFeet = z + depth },
        ],
    };

static TakeoffItem CreateRootTakeoffItem(OurPlanCoreJob job, string name) =>
    OurPlanCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, name, "#FF4444", "line");

static TakeoffItem CreateNestedTakeoffItem(OurPlanCoreJob job, string parentFolder, string name) =>
    OurPlanCoreJobStore.CreateTakeoffItem(job, parentFolder, name, "#FF4444", "line");

static string CreateTakeoffFolder(OurPlanCoreJob job, string name) =>
    OurPlanCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, name);

static List<TakeoffItem> CreateTakeoffItems(OurPlanCoreJob job, params string[] names) =>
    names.Select(name => CreateRootTakeoffItem(job, name)).ToList();

static PageInfo CreatePageItem(OurPlanCoreJob job, string parentFolder, string name)
{
    string sourcePdf = Path.Combine(job.RootPath, "source.pdf");
    if (!File.Exists(sourcePdf))
        File.WriteAllText(sourcePdf, "%PDF-1.4 test");

    return OurPlanCoreJobStore.CreatePageFromPdf(job, sourcePdf, name, parentFolder);
}

static void WithTempRasterBackedPage(
    string name,
    Action<PageInfo> action,
    float renderScale = 0.5f)
{
    string tempRoot = Path.Combine(Path.GetTempPath(), "onc_viewport_raster_tests", Guid.NewGuid().ToString("N"));
    try
    {
        string pdfPath = RasterTestPdfFactory.Create(tempRoot);
        OurPlanCoreJob job = OurPlanCoreJobStore.CreateJob(tempRoot, name);
        string importFolder = OurPlanCoreJobStore.DefaultImportFolder(job);
        PageInfo page = OurPlanCoreJobStore.ImportPdf(job, pdfPath, [$"{name}_sheet"], importFolder).Single();
        RasterSheetBuildResult build = RasterSheetCacheService.BuildAndEnable(page, renderScale);
        AssertTrue(build.Ok, build.Error);

        PageInfo refreshed = OurPlanCoreJobStore.TryReadPage(page.FolderPath)
            ?? throw new InvalidOperationException("Raster-backed viewport test page was not readable after build.");
        AssertTrue(
            refreshed.RasterSheet is { Enabled: true },
            "raster-backed viewport test page should have enabled raster metadata");
        action(refreshed);
    }
    finally
    {
        TryDeleteDirectory(tempRoot);
    }
}

static PageInfo EnableRasterFirst(PageInfo page)
{
    AssertTrue(
        RasterSheetCacheService.TrySetUseAsPageOpenRaster(page, true, out string error, out bool changed),
        error);
    AssertTrue(changed, "Raster First should change the persisted raster sheet metadata");

    PageInfo refreshed = OurPlanCoreJobStore.TryReadPage(page.FolderPath)
        ?? throw new InvalidOperationException("Raster-backed viewport test page was not readable after Raster First toggle.");
    AssertTrue(
        RasterSheetCacheService.UseAsPageOpenRaster(refreshed.RasterSheet),
        "Raster First should be persisted on the page raster metadata");
    return refreshed;
}

static void AssertTakeoffChildOrder(string parentFolder, string expected, string message)
{
    string actual = string.Join(",", TakeoffChildNames(parentFolder));
    AssertEqual(expected, actual, message);
}

static IReadOnlyList<string> TakeoffChildNames(string parentFolder) =>
    OurPlanCoreJobStore.GetOrderedChildDirectories(parentFolder)
        .Select(OurPlanCoreJobStore.DisplayName)
        .ToList();

static void AssertPageChildOrder(string parentFolder, string expected, string message)
{
    string actual = string.Join(",",
        OurPlanCoreJobStore.GetOrderedChildDirectories(parentFolder)
            .Select(OurPlanCoreJobStore.DisplayName));
    AssertEqual(expected, actual, message);
}

static string ReadDataGuid(string folder)
{
    XElement root = XDocument.Load(Path.Combine(folder, "Data.xml")).Root
        ?? throw new InvalidOperationException("missing Data.xml root");
    return root.Attribute("GUID")?.Value ?? "";
}

static void SetDataXmlProperty(string folder, string propertyName, string value)
{
    string path = Path.Combine(folder, "Data.xml");
    XDocument doc = XDocument.Load(path);
    XElement root = doc.Root ?? throw new InvalidOperationException("missing Data.xml root");
    XElement properties = root.Element("Properties") ?? new XElement("Properties");
    if (properties.Parent == null)
        root.Add(properties);

    XElement? property = properties
        .Elements("Property")
        .FirstOrDefault(element => string.Equals(
            element.Attribute("Name")?.Value,
            propertyName,
            StringComparison.Ordinal));
    if (property == null)
    {
        properties.Add(new XElement(
            "Property",
            new XAttribute("Name", propertyName),
            new XAttribute("Value", value)));
    }
    else
    {
        property.SetAttributeValue("Value", value);
    }

    doc.Save(path);
    File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(1));
}

static void AssertTrue(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static void AssertFalse(bool condition, string message) =>
    AssertTrue(!condition, message);

static void AssertEqual(string expected, string actual, string message)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
}

static void AssertClose(double expected, double actual, string message, double tolerance = 0.000001)
{
    if (Math.Abs(expected - actual) > tolerance)
        throw new InvalidOperationException($"{message}: expected {expected}, got {actual}");
}

static IReadOnlyList<SKPoint> SimpleJoistAreaPolygon() =>
[
    new SKPoint(0, 0),
    new SKPoint(8, 0),
    new SKPoint(8, 10),
    new SKPoint(0, 10),
];

static void WithTempJob(string name, Action<OurPlanCoreJob> action)
{
    string root = Path.Combine(Path.GetTempPath(), "onc_tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        File.WriteAllText(Path.Combine(root, "Data.xml"), $"<Item Class=\"Folder\" Name=\"{name}\" />");
        OurPlanCoreJobStore.EnsureFolder(root, "Pages");
        OurPlanCoreJobStore.EnsureFolder(root, "Takeoffs");
        var job = new OurPlanCoreJob
        {
            Name = name,
            RootPath = root,
        };
        action(job);
    }
    finally
    {
        TryDeleteDirectory(root);
    }
}
