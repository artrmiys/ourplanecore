using Docnet.Core;
using Docnet.Core.Models;
using OurPlaneCore;
using OurPlaneCore.Controls;
using SkiaSharp;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Xml.Linq;

if (Environment.GetEnvironmentVariable("OPC_BENCH") == "1")
    return RenderPerfBenchmark.Run();

string testGlobalRoot = Path.Combine(Path.GetTempPath(), "opc_tests_global", Guid.NewGuid().ToString("N"));
Environment.SetEnvironmentVariable(SmartContextStore.GlobalRootEnvironmentVariable, testGlobalRoot);

var tests = new List<(string Name, Action Run)>
{
    ("measurement count value and label", MeasurementCountValueAndLabel),
    ("measurement line uses own scale first", MeasurementLineUsesOwnScaleFirst),
    ("measurement area uses fallback scale", MeasurementAreaUsesFallbackScale),
    ("measurement area subtracts holes", MeasurementAreaSubtractsHoles),
    ("area cut inside keeps hole behavior", AreaCutInsideKeepsHoleBehavior),
    ("area cut box clips at area edge", AreaCutBoxClipsAtAreaEdge),
    ("area cut through area splits into segments", AreaCutThroughAreaSplitsIntoSegments),
    ("pdf export area path cuts holes", PdfExportAreaPathCutsHoles),
    ("pdf export always uses white paper", PdfExportAlwaysUsesWhitePaper),
    ("output settings default export appearance", OutputSettingsDefaultExportAppearance),
    ("pdf export writes selected sheets", PdfExportWritesSelectedSheets),
    ("pdf export writes measurement lines", PdfExportWritesMeasurementLines),
    ("pdf export skips invalid area point artifacts", PdfExportSkipsInvalidAreaPointArtifacts),
    ("pdf export defaults measurements on for measured sheets", TakeoffsTreeRegressionTests.PdfExportDefaultsMeasurementsOnForMeasuredSheets),
    ("job store persists measurement holes", JobStorePersistsMeasurementHoles),
    ("measurement area joist without direction is blocked", MeasurementJoistWithoutDirectionIsBlocked),
    ("takeoff item normalizes count type totals", TakeoffItemNormalizesCountTotals),
    ("measurement merge moves segment into target takeoff", MeasurementMergeMovesSegmentIntoTargetTakeoff),
    ("measurement merge rejects mixed target type", MeasurementMergeRejectsMixedTargetType),
    ("measurement merge coalesces touching line sections", MeasurementMergeCoalescesTouchingLineSections),
    ("measurement merge keeps separated line sections", MeasurementMergeKeepsSeparatedLineSections),
    ("measurement merge splices overlapping area sections", MeasurementMergeSplicesOverlappingAreaSections),
    ("measurement merge keeps separated area sections", MeasurementMergeKeepsSeparatedAreaSections),
    ("beam length rounds up below and above eight feet", BeamLengthRoundsUpBelowAndAboveEightFeet),
    ("beam default name keeps size suffix outside selection", BeamDefaultNameKeepsSizeSuffixOutsideSelection),
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
    ("page open defers heavy ui work", TakeoffsTreeRegressionTests.PageOpenDefersHeavyUiWork),
    ("page tabs support drag reorder and detach", TakeoffsTreeRegressionTests.PageTabsSupportDragReorderAndDetach),
    ("programmatic page selection opens viewport directly", TakeoffsTreeRegressionTests.ProgrammaticPageSelectionOpensViewportDirectly),
    ("page tree click opens viewport directly", TakeoffsTreeRegressionTests.PageTreeClickOpensViewportDirectly),
    ("page reload invalidates preview prefetch cache", TakeoffsTreeRegressionTests.PageReloadInvalidatesPreviewPrefetchCache),
    ("takeoff tree regression section key handles legacy unfiled item", TakeoffsTreeRegressionTests.SectionSelectionKeyHandlesLegacyUnfiledItem),
    ("takeoff tree regression job load builds before clearing tree", TakeoffsTreeRegressionTests.JobLoadBuildsTakeoffsBeforeClearingTree),
    ("takeoff tree regression page clear keeps loaded takeoffs", TakeoffsTreeRegressionTests.PageClearDoesNotClearLoadedTakeoffs),
    ("takeoff tree regression section menus build lazily", TakeoffsTreeRegressionTests.TakeoffSectionMenusAreBuiltLazily),
    ("takeoff tree regression joist direction resets from section menu", TakeoffsTreeRegressionTests.JoistDirectionCanBeResetFromSectionMenu),
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
    ("takeoff tree regression pages drop batches and refreshes silently", TakeoffsTreeRegressionTests.PagesDropUsesBatchMoveAndSilentRefresh),
    ("takeoff tree regression moved active sheet rebinds viewport", TakeoffsTreeRegressionTests.PagesMovedActiveSheetRebindsViewportWithoutReload),
    ("pdf sheet metadata layer discovery restores states", TakeoffsTreeRegressionTests.PdfSheetMetadataLayerDiscoveryRestoresLayerStates),
    ("sheet manager name edits stay checked", TakeoffsTreeRegressionTests.SheetManagerNameEditsStayCheckedAndDoNotSelectAllOnFocus),
    ("takeoff tree regression page repair moved job suffix", TakeoffsTreeRegressionTests.PageRepairUsesMovedJobSuffixForNonEmptyReferences),
    ("takeoff tree regression drag uses mouse down anchor", TakeoffsTreeRegressionTests.TreeDragUsesMouseDownAnchor),
    ("takeoff tree regression nested rows resolve drop target", TakeoffsTreeRegressionTests.NestedTreeRowsResolveToOwningDropTargets),
    ("takeoff tree regression measurement paste keeps source name", TakeoffsTreeRegressionTests.MeasurementPasteNewTakeoffKeepsSourceName),
    ("takeoff tree refresh button is wired", TakeoffsTreeRegressionTests.TakeoffsTreeRefreshButtonIsWired),
    ("bookmarks dock panel and shortcut are wired", TakeoffsTreeRegressionTests.BookmarksDockPanelAndShortcutAreWired),
    ("takeoff template presets and collapsed depth are wired", TakeoffsTreeRegressionTests.TakeoffTemplatePresetsAndCollapsedDepthAreWired),
    ("takeoff tree search bulk visibility and markup selection are wired", TakeoffsTreeRegressionTests.TreeSearchBulkVisibilityAndViewportMarkupSelectionAreWired),
    ("takeoff folder random colors are wired", TakeoffsTreeRegressionTests.TakeoffFolderRandomColorsAreWired),
    ("page takeoff layers and alt vertex mode are wired", TakeoffsTreeRegressionTests.PageTakeoffLayersAndAltVertexModeAreWired),
    ("dense viewport labels keep joist and selected labels", TakeoffsTreeRegressionTests.DenseViewportLabelsKeepJoistAndSelectedLabels),
    ("page takeoff selection syncs takeoffs tree", TakeoffsTreeRegressionTests.PageTakeoffSelectionSyncsTakeoffsTree),
    ("viewport count hot grips and tight hit test are wired", TakeoffsTreeRegressionTests.ViewportCountHotGripsAndTightHitTestAreWired),
    ("pdf snap duplicate load guard is wired", TakeoffsTreeRegressionTests.PdfSnapDuplicateLoadGuardIsWired),
    ("raster snap strict black lines only is wired", TakeoffsTreeRegressionTests.RasterSnapStrictBlackLinesOnlyIsWired),
    ("raster sheet render skips delayed pdf zoom refresh", TakeoffsTreeRegressionTests.RasterSheetRenderSkipsDelayedPdfZoomRefresh),
    ("pdf sheet metadata parses dotted sheet numbers for suffix rules", TakeoffsTreeRegressionTests.PdfSheetMetadataParsesDottedSheetNumbersForSuffixRules),
    ("pdf raster edge snap preview is wired", TakeoffsTreeRegressionTests.PdfRasterEdgeSnapPreviewIsWired),
    ("pages tree selected sheet scale menu is wired", TakeoffsTreeRegressionTests.PagesTreeSelectedSheetScaleMenuIsWired),
    ("raster sheet cache skips stale page paths", RasterSheetCacheTests.StalePageSnapshotDoesNotCreateRasterFolder),
    ("takeoff auto routing sends sqft areas to sqfts", TakeoffAutoRoutingSendsSqftAreasToSqfts),
    ("takeoff auto routing sends wall lines to sheet floor walls", TakeoffAutoRoutingSendsWallLinesToSheetFloorWalls),
    ("takeoff auto routing sorts page legend labels", TakeoffAutoRoutingSortsPageLegendLabels),
    ("takeoff detail refs sort by sheet then detail", TakeoffDetailRefsSortBySheetThenDetail),
    ("sample guide project creates guide pages and screenshots", SampleJobGuideTests.CreatesGuidePagesScreenshotsAndTakeoffs),
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
    ("bundled python runtime resolves packaged python", MaterialExtractionServiceTests.BundledPythonRuntimeResolvesPackagedPython),
    ("page copy and move preserve source overlay and layers", StorageTests.PageCopyAndMovePreserveSourceOverlayAndLayers),
    ("page corrupt source json is quarantined", StorageTests.PageCorruptSourceJsonIsQuarantined),
    ("page source json repairs from sheet metadata", StorageTests.PageSourceJsonRepairsFromSheetMetadata),
    ("page annotations save load normalize defaults", StorageTests.PageAnnotationsSaveLoadNormalizeDefaults),
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
    ("pdf metadata crop template save load round trips", PdfSheetMetadataCropServiceTests.CropTemplateSaveLoadRoundTrips),
    ("pdf metadata crop template usable when either region exists", PdfSheetMetadataCropServiceTests.CropTemplateUsableWhenEitherRegionExists),
    ("joist rounding aliases normalize", JoistRoundingAliasesNormalize),
    ("joist pitch normalizes common input", JoistPitchNormalizesCommonInput),
    ("joist pitch flat input normalizes empty", JoistPitchFlatInputNormalizesEmpty),
    ("joist pitch rejects invalid input", JoistPitchRejectsInvalidInput),
    ("joist pitch factor matches rise run", JoistPitchFactorMatchesRiseRun),
    ("joist pitch accepts single rise over twelve", JoistPitchAcceptsSingleRiseOverTwelve),
    ("joist layout subtracts area cut holes", JoistLayoutSubtractsAreaCutHoles),
    ("joist layout can skip end joist", JoistLayoutCanSkipEndJoist),
    ("joist pitch length applies slope factor", JoistPitchLengthAppliesSlopeFactor),
    ("joist pitch rounding applies per segment", JoistPitchRoundingAppliesPerSegment),
    ("joist pitch label shows indicator", JoistPitchLabelShowsIndicator),
    ("joist length label shows order and raw lengths", JoistLengthLabelShowsOrderAndRawLengths),
    ("joist length label can use standard format", JoistLengthLabelCanUseStandardFormat),
    ("joist pitch label explains flat slope and order lengths", JoistPitchLabelExplainsFlatSlopeAndOrderLengths),
    ("joist export uses visible label lines", JoistExportUsesVisibleLabelLines),
    ("joist area defaults use compact labels and foot rounding", JoistAreaDefaultsUseCompactLabelsAndFootRounding),
    ("legacy joist item without label flag shows labels", LegacyJoistItemWithoutLabelFlagShowsLabels),
    ("legacy joist item old false label flag migrates to labels", LegacyJoistItemOldFalseLabelFlagMigratesToLabels),
    ("legacy joist item old explicit false label flag migrates to labels", LegacyJoistItemOldExplicitFalseLabelFlagMigratesToLabels),
    ("joist item explicit false label flag stays hidden", JoistItemExplicitFalseLabelFlagStaysHidden),
    ("folder template openings have numbered children", FolderTemplateOpeningsHaveNumberedChildren),
    ("settings manager folder template edits auto persist", TakeoffsTreeRegressionTests.SettingsManagerFolderTemplateEditsAutoPersist),
    ("report template loads synthetic detailed frame list", ReportTemplateServiceTests.LoadsSyntheticDetailedFrameList),
    ("report template loads local template if present", ReportTemplateServiceTests.LoadsLocalTemplateIfPresent),
    ("report builder applies A3 wall block like macro", ReportTemplateServiceTests.AppliesA3WallBlockLikeMacro),
    ("planswift import creates job pages and measurements", PlanSwiftImportTests.ImportCreatesJobPagesAndMeasurements),
    ("planswift import normalizes oversized raster pages", PlanSwiftImportTests.ImportNormalizesOversizedRasterPageWithoutChangingMeasurements),
    ("planswift import skips pages without takeoffs", PlanSwiftImportTests.ImportSkipsPlanSwiftPagesWithoutTakeoffs),
    ("planswift import all option keeps pages without takeoffs", PlanSwiftImportTests.ImportAllOptionKeepsPlanSwiftPagesWithoutTakeoffs),
    ("planswift import preserves holes box and containers", PlanSwiftImportTests.ImportPreservesPlanSwiftHolesBoxAndContainers),
    ("planswift import preserves segments and source metadata", PlanSwiftImportTests.ImportPreservesSegmentsAndSourceMetadata),
    ("planswift import joist segments use linked area section directions", PlanSwiftImportTests.ImportJoistSegmentsUseLinkedAreaSectionDirections),
    ("planswift import into current job uses planswift buckets", PlanSwiftImportTests.ImportIntoCurrentJobUsesPlanSwiftBuckets),
    ("planswift import copies existing ourplanecore job takeoffs", PlanSwiftImportTests.ImportCopiesExistingOurPlaneCoreJobTakeoffs),
    ("planswift txt export writes every root item", PlanSwiftTxtExportWritesEveryRootItem),
    ("planswift export hides generated import notes", PlanSwiftExportHidesGeneratedImportNotes),
    ("pdf import source finder finds nested pdf files", PdfImportSourceFinderFindsNestedPdfFiles),
    ("raster sheet cache builds working image and strict snap manifest", RasterSheetCacheTests.BuildsWorkingImageAndStrictSnapManifest),
    ("active excel export matrix keeps numbers", ActiveExcelExportMatrixKeepsNumbers),
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
    ("app settings count symbol persists", AppSettingsCountSymbolPersists),
    ("atomic write ignores stale fixed temp path", AtomicWriteIgnoresStaleFixedTempPath),
    ("app settings recent job preserves pin and thumbnail", AppSettingsRecentPreservesPinAndThumbnail),
    ("app settings removes recent job by path", AppSettingsRemovesRecentJobByPath),
    ("openai response parser extracts output text", OpenAiResponseParserExtractsOutputText),
    ("openai response parser reports incomplete max tokens", OpenAiResponseParserReportsIncompleteMaxTokens),
    ("keyboard shortcut keys use english display text", KeyboardShortcutKeysUseEnglishDisplayText),
    ("transform rotation snap uses fifteen degree steps", TransformRotationSnapUsesFifteenDegreeSteps),
    ("pdf metadata needs fallback when scale is unresolved", PdfMetadataNeedsFallbackWhenScaleUnresolved),
    ("pdf metadata skip scale avoids fallback", PdfMetadataSkipScaleAvoidsFallback),
    ("pdf preview render cache round trips", PdfPreviewRenderCacheRoundTrips),
    ("pdf preview render cache is wired before layer render", TakeoffsTreeRegressionTests.PdfPreviewRenderCacheIsWiredBeforeLayerRender),
    ("pdf page open uses docnet preview on cache miss", TakeoffsTreeRegressionTests.PdfPageOpenUsesDocnetPreviewOnCacheMiss),
    ("viewport raster page open applies hot bitmap cache", ViewportRasterPageOpenAppliesHotBitmapCache),
    ("viewport raster page open queues warmup without docnet fallback", ViewportRasterPageOpenQueuesWarmupWithoutDocnetFallback),
    ("viewport oversized raster page open queues responsive dpi without docnet", ViewportOversizedRasterPageOpenQueuesResponsiveDpiWithoutDocnetFallback),
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
    ("pdf snap index finds nearest point", PdfSnapIndexFindsNearestPoint),
    ("pdf snap index prefers corner ties", PdfSnapIndexPrefersCornerTies),
    ("pdf snap index snaps to line", PdfSnapIndexSnapsToLine),
    ("pdf snap index finds nearest segment", PdfSnapIndexFindsNearestSegment),
    ("pdf raster edge snap bridges small endpoint gaps", PdfRasterEdgeSnapBridgesSmallEndpointGaps),
};

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
            Environment.GetEnvironmentVariable("OURPLANECORE_TEST_VERBOSE_FAILURES"),
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
    AssertClose(2.0, settings.ViewportZoomWheelFactor, "mouse-wheel zoom step should default to 2x per notch");
    AssertFalse(settings.PdfExportIncludeAnnotations, "PDF annotations should default to the current export profile");
    AssertFalse(settings.PdfExportShowLineLabels, "PDF line labels should default to the current export profile");
    AssertFalse(settings.PdfExportShowAreaLabels, "PDF area labels should default to the current export profile");
    AssertClose(3.5, settings.PdfExportMeasurementStrokeScale, "PDF stroke should default to the current export profile");
    AssertClose(3.5, settings.PdfExportPointSizeScale, "PDF point size should default to the current export profile");
    AssertClose(0.25, settings.PdfExportAreaEdgeScale, "PDF area edge should default to the current export profile");
    AssertClose(0.1826, settings.PdfExportAreaFillOpacity, "PDF area fill should default to the current export profile");
    AssertClose(1.2, settings.PdfExportMeasurementLabelScale, "PDF label should default to the current export profile");
    AssertClose(2.0, settings.PdfExportSheetLegendScale, "PDF legend should default to the current export profile");
    AssertClose(1.2, settings.PdfExportSheetHeaderScale, "PDF header should default to the current export profile");
    AssertClose(248.0, settings.LeftPanelWidth, "left panel should default to the current width");
    AssertClose(269.0, settings.RightPanelWidth, "right panel should default to the current width");

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

static void PdfExportWritesSelectedSheets()
{
    string dir = Path.Combine(Path.GetTempPath(), "opc_pdf_export_smoke", Guid.NewGuid().ToString("N"));
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
    string dir = Path.Combine(Path.GetTempPath(), "opc_pdf_export_measurements", Guid.NewGuid().ToString("N"));
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
    string dir = Path.Combine(Path.GetTempPath(), "opc_pdf_export_invalid_area", Guid.NewGuid().ToString("N"));
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
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(
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
        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        List<Measurement> loaded = OurPlaneCoreJobStore.LoadMeasurements(item.FolderPath);

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
    var job = new OurPlaneCoreJob
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

        AssertTrue(OurPlaneCoreJobStore.MoveSibling(items[1].FolderPath, -1), "move up should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,A,C", "sibling up");
    });
}

static void TakeoffTreeOrderMovesSiblingDown()
{
    WithTempJob("tree_order_down", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C");

        AssertTrue(OurPlaneCoreJobStore.MoveSibling(items[0].FolderPath, 1), "move down should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,A,C", "sibling down");
    });
}

static void TakeoffTreeOrderBlocksTopUp()
{
    WithTempJob("tree_order_top", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C");

        AssertFalse(OurPlaneCoreJobStore.MoveSibling(items[0].FolderPath, -1), "top move up should be blocked");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "A,B,C", "top up blocked");
    });
}

static void TakeoffTreeOrderBlocksBottomDown()
{
    WithTempJob("tree_order_bottom", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C");

        AssertFalse(OurPlaneCoreJobStore.MoveSibling(items[2].FolderPath, 1), "bottom move down should be blocked");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "A,B,C", "bottom down blocked");
    });
}

static void TakeoffTreeOrderMovesSiblingBlockUp()
{
    WithTempJob("tree_order_block_up", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlaneCoreJobStore.MoveSiblings([items[1].FolderPath, items[2].FolderPath], -1), "block up should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "B,C,A,D", "block up");
    });
}

static void TakeoffTreeOrderMovesSiblingBlockDown()
{
    WithTempJob("tree_order_block_down", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlaneCoreJobStore.MoveSiblings([items[1].FolderPath, items[2].FolderPath], 1), "block down should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "A,D,B,C", "block down");
    });
}

static void TakeoffTreeOrderMovesBeforeTarget()
{
    WithTempJob("tree_order_before", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlaneCoreJobStore.MoveSiblingsToPosition([items[2].FolderPath], items[0].FolderPath, after: false), "move before should apply");
        AssertTakeoffChildOrder(job.TakeoffsRoot, "C,A,B,D", "move before target");
    });
}

static void TakeoffTreeOrderMovesAfterTarget()
{
    WithTempJob("tree_order_after", job =>
    {
        var items = CreateTakeoffItems(job, "A", "B", "C", "D");

        AssertTrue(OurPlaneCoreJobStore.MoveSiblingsToPosition([items[0].FolderPath], items[2].FolderPath, after: true), "move after should apply");
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

        string movedPath = OurPlaneCoreJobStore.MoveNode(moving.FolderPath, targetFolder);

        AssertTrue(OurPlaneCoreJobStore.IsSameOrDescendant(targetFolder, movedPath), "node should move into folder");
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

        var moved = OurPlaneCoreJobStore.MoveNodes([child.FolderPath], job.TakeoffsRoot).Single();

        AssertTrue(
            OurPlaneCoreJobStore.MoveSiblingsToPosition([moved.MovedPath], folder, after: true),
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

        AssertTrue(OurPlaneCoreJobStore.MoveSiblingsToEnd(selected, job.TakeoffsRoot), "large selection should move to end");

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

        IReadOnlyList<(string SourcePath, string MovedPath)> moved = OurPlaneCoreJobStore.MoveNodes(selected, targetFolder);

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
        AssertEqual("sqfts", OurPlaneCoreJobStore.DisplayName(baseRoute.ParentFolder), "sqft parent");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, porchRoute.ParentFolder, "porch", "#FF4444", "area");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, firstRoute.ParentFolder, "1st", "#FF4444", "area");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, baseRoute.ParentFolder, "base", "#FF4444", "area");

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
        AssertEqual("2nd floor walls", OurPlaneCoreJobStore.DisplayName(route.ParentFolder), "wall floor parent");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "2x4 walls", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "dem 2x4", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "2x8 walls", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "corners", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "corr 2x6", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "2x6 walls", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, route.ParentFolder, "ext 9.98", "#FF4444", "line");

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
        OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "14/S502", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "13/S101", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "2/S102", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "14/S101", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "13/S5.10", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "2/S5.5", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "1/S5.5", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "4/S5.10", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "3/S5.2", "#FF4444", "line");
        OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "8/S6.1", "#FF4444", "line");

        OurPlaneCoreJobStore.SortTakeoffChildren(job.TakeoffsRoot, descending: false);
        AssertTakeoffChildOrder(job.TakeoffsRoot, expected, "detail refs takeoff tree order");
    });
}

static void SheetLegendLiveAutoIgnoresStoredAutoOrder()
{
    WithTempJob("sheet_legend_live_auto", job =>
    {
        PageInfo page = CreatePageItem(job, job.PagesRoot, "A701");
        string walls = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
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
        string sqfts = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
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
        string walls = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        string fourth = OurPlaneCoreJobStore.CreateTakeoffFolder(job, walls, "4th floor walls");
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
        string sqft = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sft");
        string roof = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "eve rake");
        string gables = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "gables");
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
        string misc = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "misc");
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
        string walls = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        string first = OurPlaneCoreJobStore.CreateTakeoffFolder(job, walls, "1st");
        string second = OurPlaneCoreJobStore.CreateTakeoffFolder(job, walls, "2nd");
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
        string walls = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        string firstWalls = OurPlaneCoreJobStore.CreateTakeoffFolder(job, walls, "1st");
        CreateMeasuredTakeoffItem(job, firstWalls, "ext 10", "line", page.FolderPath, [new SKPoint(0, 0), new SKPoint(10, 0)]);
        string sqfts = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
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
        string walls = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "walls");
        string firstWalls = OurPlaneCoreJobStore.CreateTakeoffFolder(job, walls, "1st");
        CreateMeasuredTakeoffItem(job, firstWalls, "ext 10", "line", page.FolderPath, [new SKPoint(0, 0), new SKPoint(10, 0)]);
        string sqfts = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
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
        string sqfts = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
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
        string sqfts = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
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
        string sqfts = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
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
        string sqfts = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
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
        string sqfts = OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, "sqfts");
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
                    PageFolder = @"C:\job\Pages\A101",
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
        string parent = OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, "Parent");
        string folderA = OurPlaneCoreJobStore.CreateFolder(parent, "Folder A");
        PageInfo sheetB = CreatePageItem(job, parent, "Sheet B");
        OurPlaneCoreJobStore.CreateFolder(parent, "Folder C");

        AssertPageChildOrder(parent, "Folder A,Sheet B,Folder C", "initial page/folder order");
        AssertTrue(OurPlaneCoreJobStore.MoveSiblingsToPosition([sheetB.FolderPath], folderA, after: false), "sheet before folder should apply");
        AssertPageChildOrder(parent, "Sheet B,Folder A,Folder C", "sheet moved before folder");
    });
}

static void PageTreeOrderMovesFolderBeforeFolder()
{
    WithTempJob("page_order_folder_before_folder", job =>
    {
        string parent = OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, "Parent");
        string folderA = OurPlaneCoreJobStore.CreateFolder(parent, "Folder A");
        string folderB = OurPlaneCoreJobStore.CreateFolder(parent, "Folder B");
        CreatePageItem(job, parent, "Sheet C");

        AssertPageChildOrder(parent, "Folder A,Folder B,Sheet C", "initial folder/folder order");
        AssertTrue(OurPlaneCoreJobStore.MoveSiblingsToPosition([folderB], folderA, after: false), "folder before folder should apply");
        AssertPageChildOrder(parent, "Folder B,Folder A,Sheet C", "folder moved before folder");
    });
}

static void PageTreeOrderMovesSelectedItemsToEnd()
{
    WithTempJob("page_order_selected_to_end", job =>
    {
        string parent = OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, "Parent");
        PageInfo sheetA = CreatePageItem(job, parent, "Sheet A");
        PageInfo sheetB = CreatePageItem(job, parent, "Sheet B");
        PageInfo sheetC = CreatePageItem(job, parent, "Sheet C");
        PageInfo sheetD = CreatePageItem(job, parent, "Sheet D");

        AssertPageChildOrder(parent, "Sheet A,Sheet B,Sheet C,Sheet D", "initial page order");
        AssertTrue(
            OurPlaneCoreJobStore.MoveSiblingsToEnd([sheetB.FolderPath, sheetD.FolderPath], parent),
            "selected sheets should move to end");
        AssertPageChildOrder(parent, "Sheet A,Sheet C,Sheet B,Sheet D", "selected sheets moved to end");
    });
}

static void PageTreeOrderMovesNestedFolderOutBelowParent()
{
    WithTempJob("page_order_nested_folder_out", job =>
    {
        string parent = OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, "Parent");
        string child = OurPlaneCoreJobStore.CreateFolder(parent, "Child");
        OurPlaneCoreJobStore.CreateFolder(job.PagesRoot, "Other");

        string moved = OurPlaneCoreJobStore.MoveNode(child, job.PagesRoot);
        AssertTrue(OurPlaneCoreJobStore.MoveSiblingsToPosition([moved], parent, after: true), "nested folder out should apply");
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

        string renamed = OurPlaneCoreJobStore.RenamePageAllowDuplicateName(second.FolderPath, "S101");
        PageInfo? renamedPage = OurPlaneCoreJobStore.TryReadPage(renamed);

        AssertTrue(Directory.Exists(first.FolderPath), "first duplicate-name page should remain");
        AssertTrue(Directory.Exists(renamed), "renamed duplicate-name page should exist");
        AssertFalse(string.Equals(first.FolderPath, renamed, StringComparison.OrdinalIgnoreCase), "duplicate-name pages need unique folders");
        AssertEqual("S101", OurPlaneCoreJobStore.DisplayName(first.FolderPath), "first display name");
        AssertEqual("S101", OurPlaneCoreJobStore.DisplayName(renamed), "renamed display name");
        AssertEqual("S101", renamedPage?.Name ?? "", "renamed page info name");
    });
}

static void TakeoffCreateAllowsDuplicateDisplayNames()
{
    WithTempJob("takeoff_create_duplicate_names", job =>
    {
        TakeoffItem first = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Walls", "#FF0000", "line");
        TakeoffItem second = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Walls", "#00FF00", "area");

        AssertTrue(Directory.Exists(first.FolderPath), "first duplicate-name takeoff should exist");
        AssertTrue(Directory.Exists(second.FolderPath), "second duplicate-name takeoff should exist");
        AssertFalse(string.Equals(first.FolderPath, second.FolderPath, StringComparison.OrdinalIgnoreCase), "duplicate-name takeoffs need unique folders");
        AssertEqual("Walls", first.Name, "first takeoff display name");
        AssertEqual("Walls", second.Name, "second takeoff display name");
        AssertEqual("Walls", OurPlaneCoreJobStore.DisplayName(first.FolderPath), "first stored display name");
        AssertEqual("Walls", OurPlaneCoreJobStore.DisplayName(second.FolderPath), "second stored display name");
    });
}

static void TakeoffRenameAllowsDuplicateDisplayNames()
{
    WithTempJob("takeoff_rename_duplicate_names", job =>
    {
        TakeoffItem first = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Walls", "#FF0000", "line");
        TakeoffItem second = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof", "#00FF00", "area");

        string renamed = OurPlaneCoreJobStore.RenameNodeAllowDuplicateName(second.FolderPath, "Walls");
        TakeoffItem? renamedItem = OurPlaneCoreJobStore.TryReadTakeoffItem(renamed);

        AssertTrue(Directory.Exists(first.FolderPath), "first duplicate-name takeoff should remain");
        AssertTrue(Directory.Exists(renamed), "renamed duplicate-name takeoff should exist");
        AssertFalse(string.Equals(first.FolderPath, renamed, StringComparison.OrdinalIgnoreCase), "renamed duplicate-name takeoff needs unique folder");
        AssertEqual("Walls", OurPlaneCoreJobStore.DisplayName(first.FolderPath), "first takeoff display name");
        AssertEqual("Walls", OurPlaneCoreJobStore.DisplayName(renamed), "renamed takeoff display name");
        AssertEqual("Walls", renamedItem?.Name ?? "", "renamed takeoff item name");
    });
}

static void TakeoffDisplayNamesPreserveSlash()
{
    WithTempJob("takeoff_slash_names", job =>
    {
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "ext 9.1 10/A502", "#FF0000", "line");
        AssertEqual("ext 9.1 10/A502", item.Name, "created takeoff display name preserves slash");
        AssertTrue(Path.GetFileName(item.FolderPath).Contains("_A502", StringComparison.Ordinal), "folder path still uses safe slash replacement");

        string renamed = OurPlaneCoreJobStore.RenameNodeAllowDuplicateName(item.FolderPath, "corr 2x6 10.1 14/A502");
        TakeoffItem? loaded = OurPlaneCoreJobStore.TryReadTakeoffItem(renamed);
        AssertEqual("corr 2x6 10.1 14/A502", OurPlaneCoreJobStore.DisplayName(renamed), "renamed takeoff display name preserves slash");
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
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "2. 3x6", "#FF0000", "line");
        string sourceGuid = ReadDataGuid(item.FolderPath);
        string copy = OurPlaneCoreJobStore.CopyNode(item.FolderPath, job.TakeoffsRoot);
        string secondCopy = OurPlaneCoreJobStore.CopyNode(item.FolderPath, job.TakeoffsRoot);
        TakeoffItem? copied = OurPlaneCoreJobStore.TryReadTakeoffItem(copy);
        TakeoffItem? secondCopied = OurPlaneCoreJobStore.TryReadTakeoffItem(secondCopy);
        string copyGuid = ReadDataGuid(copy);

        AssertFalse(string.Equals(item.FolderPath, copy, StringComparison.OrdinalIgnoreCase), "copy should use a new folder");
        AssertFalse(string.Equals(copy, secondCopy, StringComparison.OrdinalIgnoreCase), "second copy should use another hidden folder");
        AssertEqual("2. 3x6", OurPlaneCoreJobStore.DisplayName(copy), "copy display name should not include Copy");
        AssertEqual("2. 3x6", copied?.Name ?? "", "copied item name should not include Copy");
        AssertEqual("2. 3x6", OurPlaneCoreJobStore.DisplayName(secondCopy), "second copy display name should not include a number");
        AssertEqual("2. 3x6", secondCopied?.Name ?? "", "second copied item name should not include a number");
        AssertFalse(string.Equals(sourceGuid, copyGuid, StringComparison.OrdinalIgnoreCase), "copied takeoff should get a new hidden guid");
    });
}

static void TakeoffMoveCollisionKeepsDisplayName()
{
    WithTempJob("takeoff_move_collision_name", job =>
    {
        string targetFolder = CreateTakeoffFolder(job, "Target");
        TakeoffItem existing = OurPlaneCoreJobStore.CreateTakeoffItem(job, targetFolder, "Ext Walls", "#FF0000", "line");
        TakeoffItem moving = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Ext Walls", "#00FF00", "line");

        string moved = OurPlaneCoreJobStore.MoveNode(moving.FolderPath, targetFolder);
        TakeoffItem? loaded = OurPlaneCoreJobStore.TryReadTakeoffItem(moved);

        AssertFalse(string.Equals(existing.FolderPath, moved, StringComparison.OrdinalIgnoreCase), "move collision should use a unique folder path");
        AssertFalse(OurPlaneCoreJobStore.DisplayName(moved).Contains("Copy", StringComparison.OrdinalIgnoreCase), "move collision should not add Copy");
        AssertEqual("Ext Walls", OurPlaneCoreJobStore.DisplayName(moved), "move collision preserves display name");
        AssertEqual("Ext Walls", loaded?.Name ?? "", "loaded moved item preserves display name");
    });
}

static void JobStoreSanitizesUnsafeNames()
{
    string clean = OurPlaneCoreJobStore.SanitizeName("  bad:name?.  ", 120);
    AssertEqual("bad_name_", clean, "sanitized invalid characters");

    string truncated = OurPlaneCoreJobStore.SanitizeName(new string('a', 10), 4);
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
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
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

static void PlanSwiftTxtExportWritesEveryRootItem()
{
    WithTempJob("TXT Export", job =>
    {
        TakeoffItem first = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "First Item", "#FF0000", "line");
        TakeoffItem second = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Second Item", "#00FF00", "line");

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
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Imported Area", "#FF0000", "area");
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
    AssertTrue(item.JoistAddEndJoist, "joist default end joist");
}

static void LegacyJoistItemWithoutLabelFlagShowsLabels()
{
    WithTempJob("Legacy Joist Labels", job =>
    {
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
        SetDataXmlProperty(item.FolderPath, "JoistEnabled", "True");
        OurPlaneCoreJobStore.SaveMeasurements(item.FolderPath, new[]
        {
            new Measurement
            {
                MType = "area",
                JoistDirectionLocked = true,
                ScaleMetersPerPt = 0.3048,
                Points = SimpleJoistAreaPolygon().ToList(),
            },
        });

        TakeoffItem loaded = OurPlaneCoreJobStore.TryReadTakeoffItem(item.FolderPath)
            ?? throw new InvalidOperationException("legacy joist item not loaded");

        AssertFalse(loaded.JoistShowLabels, "legacy joist item labels default hidden");
        AssertFalse(loaded.Measurements[0].JoistShowLabels, "legacy joist measurement labels default hidden");
    });
}

static void LegacyJoistItemOldFalseLabelFlagMigratesToLabels()
{
    WithTempJob("Legacy Joist False Labels", job =>
    {
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
        SetDataXmlProperty(item.FolderPath, "JoistEnabled", "True");
        SetDataXmlProperty(item.FolderPath, "JoistShowLabels", "False");
        OurPlaneCoreJobStore.SaveMeasurements(item.FolderPath, new[]
        {
            new Measurement
            {
                MType = "area",
                JoistDirectionLocked = true,
                ScaleMetersPerPt = 0.3048,
                Points = SimpleJoistAreaPolygon().ToList(),
            },
        });

        TakeoffItem loaded = OurPlaneCoreJobStore.TryReadTakeoffItem(item.FolderPath)
            ?? throw new InvalidOperationException("legacy joist item not loaded");

        AssertFalse(loaded.JoistShowLabels, "legacy false joist item labels stay hidden by default");
        AssertFalse(loaded.Measurements[0].JoistShowLabels, "legacy false joist measurement labels stay hidden by default");
    });
}

static void LegacyJoistItemOldExplicitFalseLabelFlagMigratesToLabels()
{
    WithTempJob("Legacy Joist Explicit False Labels", job =>
    {
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
        SetDataXmlProperty(item.FolderPath, "JoistEnabled", "True");
        SetDataXmlProperty(item.FolderPath, "JoistShowLabels", "False");
        SetDataXmlProperty(item.FolderPath, "JoistShowLabelsExplicit", "True");
        OurPlaneCoreJobStore.SaveMeasurements(item.FolderPath, new[]
        {
            new Measurement
            {
                MType = "area",
                JoistDirectionLocked = true,
                ScaleMetersPerPt = 0.3048,
                Points = SimpleJoistAreaPolygon().ToList(),
            },
        });

        TakeoffItem loaded = OurPlaneCoreJobStore.TryReadTakeoffItem(item.FolderPath)
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
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
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
        OurPlaneCoreJobStore.SaveTakeoffItem(item);

        TakeoffItem loaded = OurPlaneCoreJobStore.TryReadTakeoffItem(item.FolderPath)
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
        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Roof Joists", "#FF0000", "area");
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

        OurPlaneCoreJobStore.SaveTakeoffItem(item);
        TakeoffItem loaded = OurPlaneCoreJobStore.TryReadTakeoffItem(item.FolderPath)
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

    OurPlaneCoreJobStore.ApplyTakeoffPropertiesToMeasurements(item);

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

        OurPlaneCoreJobStore.SavePageOverlay(basePage.FolderPath, overlayPage.FolderPath, "#1E88E5", 0.42);
        OurPlaneCoreJobStore.SavePageOverlayTransform(basePage.FolderPath, 12.5, -7.25, 1.2);
        OurPlaneCoreJobStore.SavePageOverlayVisibility(basePage.FolderPath, false);
        OurPlaneCoreJobStore.SavePageHiddenTakeoffs(basePage.FolderPath, ["Walls"]);
        PageInfo loaded = OurPlaneCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page missing");
        AssertEqual(overlayPage.FolderPath, loaded.OverlayPageFolder, "overlay page path");
        AssertEqual("#1E88E5", loaded.OverlayColor, "overlay color");
        AssertClose(0.42, loaded.OverlayOpacity, "overlay opacity");
        AssertClose(12.5, loaded.OverlayOffsetXPt, "overlay x offset");
        AssertClose(-7.25, loaded.OverlayOffsetYPt, "overlay y offset");
        AssertClose(1.2, loaded.OverlayScale, "overlay scale");
        AssertFalse(loaded.OverlayVisible, "overlay visibility");
        AssertEqual("Walls", string.Join(",", loaded.HiddenTakeoffs), "hidden takeoffs");

        OurPlaneCoreJobStore.SavePageScale(basePage.FolderPath, 0.3048);
        PageInfo afterScale = OurPlaneCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page after scale missing");
        AssertEqual(overlayPage.FolderPath, afterScale.OverlayPageFolder, "overlay survives scale save");
        AssertClose(12.5, afterScale.OverlayOffsetXPt, "overlay x survives scale save");
        AssertClose(-7.25, afterScale.OverlayOffsetYPt, "overlay y survives scale save");
        AssertClose(1.2, afterScale.OverlayScale, "overlay scale survives scale save");
        AssertFalse(afterScale.OverlayVisible, "overlay visibility survives scale save");
        AssertEqual("Walls", string.Join(",", afterScale.HiddenTakeoffs), "hidden takeoffs survive scale save");

        OurPlaneCoreJobStore.ClearPageOverlay(basePage.FolderPath);
        PageInfo cleared = OurPlaneCoreJobStore.TryReadPage(basePage.FolderPath)
            ?? throw new InvalidOperationException("base page after clear missing");
        AssertEqual("", cleared.OverlayPageFolder, "overlay clears");
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
        string page = OurPlaneCoreJobStore.EnsureFolder(job.PagesRoot, "A100");
        File.WriteAllText(Path.Combine(page, "source.json"), "{}");
        File.WriteAllText(Path.Combine(page, "sheet.pdf"), "not a real pdf");

        TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, "Walls", "#FF0000", "line");
        item.Measurements.Add(new Measurement
        {
            MType = "line",
            Points = [new SKPoint(0, 0), new SKPoint(1, 1)],
        });
        OurPlaneCoreJobStore.SaveTakeoffItem(item);

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
    string root = Path.Combine(Path.GetTempPath(), "opc_jobs");
    AppSettingsStore.AddJobsRoot(settings, root);
    AppSettingsStore.AddJobsRoot(settings, root + Path.DirectorySeparatorChar);

    AssertEqual("1", AppSettingsStore.CurrentJobsRootPaths(settings).Count.ToString(), "deduped roots");
}

static void AppSettingsRemovesJobRootByPath()
{
    var settings = new AppSettings();
    string root1 = Path.Combine(Path.GetTempPath(), "opc_jobs_remove_1");
    string root2 = Path.Combine(Path.GetTempPath(), "opc_jobs_remove_2");
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
    string root = Path.Combine(Path.GetTempPath(), "opc_pdf_recursive", Guid.NewGuid().ToString("N"));
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
    string localRoot = Path.Combine(Path.GetTempPath(), "opc_local_jobs", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(localRoot);
    try
    {
        JobRootDescriptor local = JobRootSelectorBar.DescribeJobRoot(localRoot);
        AssertEqual("Local", local.KindLabel, "local root kind");
        AssertEqual("Ready", local.StatusLabel, "existing root status");
        AssertEqual(Path.GetFileName(localRoot), local.DisplayName, "root display name");

        AssertEqual(
            JobRootLocationKind.Cloud.ToString(),
            JobRootSelectorBar.ClassifyJobRootPath(@"C:\Users\User\OneDrive\OurPlaneCore Jobs").ToString(),
            "OneDrive root kind");
        AssertEqual(
            JobRootLocationKind.Cloud.ToString(),
            JobRootSelectorBar.ClassifyJobRootPath(@"D:\Dropbox\Shared Takeoffs").ToString(),
            "Dropbox root kind");
        AssertEqual(
            JobRootLocationKind.Network.ToString(),
            JobRootSelectorBar.ClassifyJobRootPath(@"\\server\shared\jobs").ToString(),
            "UNC root kind");

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
    string overridePath = Path.Combine(Path.GetTempPath(), "opc_settings_override", Guid.NewGuid().ToString("N"), "settings.json");
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
    string dir = Path.Combine(Path.GetTempPath(), "opc_settings_count_symbol", Guid.NewGuid().ToString("N"));
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

static void AtomicWriteIgnoresStaleFixedTempPath()
{
    string dir = Path.Combine(Path.GetTempPath(), "opc_atomic_write", Guid.NewGuid().ToString("N"));
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
    string path = Path.Combine(Path.GetTempPath(), "opc_job");
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
    string path = Path.Combine(Path.GetTempPath(), "opc_job_remove");
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
    string root = Path.Combine(Path.GetTempPath(), "opc_preview_cache_tests", Guid.NewGuid().ToString("N"));
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

static void SheetOverlayRenderCacheRoundTrips()
{
    string oldRoot = Environment.GetEnvironmentVariable(SheetOverlayRenderCache.CacheRootEnvironmentVariable) ?? "";
    string root = Path.Combine(Path.GetTempPath(), "opc_sheet_overlay_cache_tests", Guid.NewGuid().ToString("N"));
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

        var changedPage = new PageInfo
        {
            Name = "Base",
            FolderPath = page.FolderPath,
            OverlayColor = page.OverlayColor,
            OverlayOpacity = 0.75,
        };
        AssertFalse(
            SheetOverlayRenderCache.TryRead(changedPage, overlayPage, 1.25f, out _, out _, out _),
            "changing overlay opacity should invalidate the tinted overlay cache");
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
        AssertClose(2.0, ViewportRenderPolicy.SelectSheetOverlayRenderScale(0.4f), "sheet overlay keeps a 2x minimum source render");
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
    AssertFalse(
        ViewportRenderPolicy.ShouldDrawSheetOverlay(
            fastNavigationFrame: true,
            isOverlayEditing: false),
        "fast navigation should hide sheet overlays until idle unless the overlay is being edited");

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
        AssertTrue(
            PdfViewport.WarmRasterSheetBitmapCache(page),
            "raster bitmap cache should warm before hot page open");

        RunOnStaThread(() =>
        {
            var viewport = new PdfViewport();
            viewport.LoadPage(page.PdfPath, page.PdfPage, page.FolderPath, rasterSheet: page.RasterSheet);

            AssertTrue(
                GetPrivateField<bool>(viewport, "_usingRasterSheetRender"),
                "hot raster-backed page open should apply raster bitmap synchronously");
            AssertFalse(
                GetPrivateField<bool>(viewport, "_showingPreviousPageDuringSwitch"),
                "hot raster-backed page open should not keep a previous sheet placeholder");
            AssertTrue(
                GetPrivateFieldValue(viewport, "_pendingDocnetRender") == null,
                "hot raster-backed page open must not queue docnet PDF render");
            AssertTrue(
                GetPrivateFieldValue(viewport, "_pageBitmap") is SKBitmap { Width: > 0, Height: > 0 },
                "hot raster-backed page open should load a visible bitmap");
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
            AssertTrue(
                usingRaster || HasRasterBitmapWarmupInFlight(viewport),
                "cold raster-backed page open should either apply raster immediately or queue bitmap warmup");
            AssertTrue(
                GetPrivateFieldValue(viewport, "_pendingDocnetRender") == null,
                "cold raster-backed page open must not fall back to docnet while raster bitmap warmup is queued");
        });
    });
}

static void ViewportOversizedRasterPageOpenQueuesResponsiveDpiWithoutDocnetFallback()
{
    WithTempRasterBackedPage("raster_oversized_open", page =>
    {
        AssertEqual(
            "200",
            RasterSheetCacheService.RenderScaleToDpi(page.RasterSheet?.RenderScale ?? 0).ToString(),
            "oversized raster-backed viewport test should start from active 200dpi metadata");

        foreach ((float zoom, int expectedDpi) in new[] { (0.67f, 72), (1.50f, 144) })
        {
            RunOnStaThread(() =>
            {
                var viewport = new PdfViewport();
                viewport.LoadPage(
                    page.PdfPath,
                    page.PdfPage,
                    page.FolderPath,
                    restoreView: new PdfViewport.ViewState(zoom, 0, 0),
                    rasterSheet: page.RasterSheet);

                AssertTrue(
                    HasRasterDpiBuildInFlight(viewport, expectedDpi) || HasReadyRasterDpi(page, expectedDpi),
                    $"oversized 200dpi raster page open at {zoom:P0} should queue or prepare {expectedDpi}dpi raster");
                AssertFalse(
                    GetPrivateField<bool>(viewport, "_usingRasterSheetRender"),
                    $"oversized 200dpi raster page open at {zoom:P0} should not paint the oversized bitmap while lower dpi is missing");
                AssertTrue(
                    GetPrivateFieldValue(viewport, "_pendingDocnetRender") == null,
                    $"oversized 200dpi raster page open at {zoom:P0} must not fall back to docnet while responsive raster dpi is queued");
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
        string pageFolder = Path.Combine(Path.GetTempPath(), "opc_batch_undo_page");
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

static bool HasReadyRasterDpi(PageInfo page, int dpi)
{
    PageInfo refreshed = OurPlaneCoreJobStore.TryReadPage(page.FolderPath) ?? page;
    float renderScale = RasterSheetCacheService.RasterDpiToRenderScale(dpi);
    return RasterSheetCacheService.HasReadyReadableRaster(refreshed, renderScale);
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
    OurPlaneCoreJob job,
    string parentFolder,
    string name,
    string measurementType,
    string pageFolder,
    IReadOnlyList<SKPoint> points)
{
    TakeoffItem item = OurPlaneCoreJobStore.CreateTakeoffItem(job, parentFolder, name, "#FF4444", measurementType);
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
    OurPlaneCoreJobStore.SaveTakeoffItem(item);
    return item;
}

static SmartMassingTakeoffAiAssignment AiAssignment(
    OurPlaneCoreJob job,
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

static TakeoffItem CreateRootTakeoffItem(OurPlaneCoreJob job, string name) =>
    OurPlaneCoreJobStore.CreateTakeoffItem(job, job.TakeoffsRoot, name, "#FF4444", "line");

static TakeoffItem CreateNestedTakeoffItem(OurPlaneCoreJob job, string parentFolder, string name) =>
    OurPlaneCoreJobStore.CreateTakeoffItem(job, parentFolder, name, "#FF4444", "line");

static string CreateTakeoffFolder(OurPlaneCoreJob job, string name) =>
    OurPlaneCoreJobStore.CreateTakeoffFolder(job, job.TakeoffsRoot, name);

static List<TakeoffItem> CreateTakeoffItems(OurPlaneCoreJob job, params string[] names) =>
    names.Select(name => CreateRootTakeoffItem(job, name)).ToList();

static PageInfo CreatePageItem(OurPlaneCoreJob job, string parentFolder, string name)
{
    string sourcePdf = Path.Combine(job.RootPath, "source.pdf");
    if (!File.Exists(sourcePdf))
        File.WriteAllText(sourcePdf, "%PDF-1.4 test");

    return OurPlaneCoreJobStore.CreatePageFromPdf(job, sourcePdf, name, parentFolder);
}

static void WithTempRasterBackedPage(
    string name,
    Action<PageInfo> action,
    float renderScale = 0.5f)
{
    string pdfPath = Path.Combine(
        FindRepoRoot(),
        "reference",
        "window_detector_poc",
        "outputs",
        "wind_window_points_marked.pdf");
    if (!File.Exists(pdfPath))
        throw new FileNotFoundException("Raster-backed viewport test PDF is missing.", pdfPath);

    string tempRoot = Path.Combine(Path.GetTempPath(), "opc_viewport_raster_tests", Guid.NewGuid().ToString("N"));
    try
    {
        OurPlaneCoreJob job = OurPlaneCoreJobStore.CreateJob(tempRoot, name);
        string importFolder = OurPlaneCoreJobStore.DefaultImportFolder(job);
        PageInfo page = OurPlaneCoreJobStore.ImportPdf(job, pdfPath, [$"{name}_sheet"], importFolder).Single();
        RasterSheetBuildResult build = RasterSheetCacheService.BuildAndEnable(page, renderScale);
        AssertTrue(build.Ok, build.Error);

        PageInfo refreshed = OurPlaneCoreJobStore.TryReadPage(page.FolderPath)
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

static string FindRepoRoot()
{
    string dir = Directory.GetCurrentDirectory();
    while (!string.IsNullOrWhiteSpace(dir))
    {
        if (File.Exists(Path.Combine(dir, "ourplanecore.csproj")))
            return dir;

        string? parent = Directory.GetParent(dir)?.FullName;
        if (string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
            break;
        dir = parent ?? "";
    }

    throw new DirectoryNotFoundException("Could not locate ourplanecore repo root.");
}

static void AssertTakeoffChildOrder(string parentFolder, string expected, string message)
{
    string actual = string.Join(",", TakeoffChildNames(parentFolder));
    AssertEqual(expected, actual, message);
}

static IReadOnlyList<string> TakeoffChildNames(string parentFolder) =>
    OurPlaneCoreJobStore.GetOrderedChildDirectories(parentFolder)
        .Select(OurPlaneCoreJobStore.DisplayName)
        .ToList();

static void AssertPageChildOrder(string parentFolder, string expected, string message)
{
    string actual = string.Join(",",
        OurPlaneCoreJobStore.GetOrderedChildDirectories(parentFolder)
            .Select(OurPlaneCoreJobStore.DisplayName));
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

static void WithTempJob(string name, Action<OurPlaneCoreJob> action)
{
    string root = Path.Combine(Path.GetTempPath(), "opc_tests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        File.WriteAllText(Path.Combine(root, "Data.xml"), $"<Item Class=\"Folder\" Name=\"{name}\" />");
        OurPlaneCoreJobStore.EnsureFolder(root, "Pages");
        OurPlaneCoreJobStore.EnsureFolder(root, "Takeoffs");
        var job = new OurPlaneCoreJob
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
