using OurPlanCore;
using OurPlanCore.Controls;

internal static class PdfSheetMetadataWorkflowTests
{
    public static void PrecisePolicyPreservesExistingCompoundSuffix()
    {
        SheetMetadataConfig config = SheetMetadataConfig.BuildPreciseV2();
        var metadata = new PdfSheetMetadata
        {
            SheetKey = "s504.00",
            Suffix = "",
            SuffixConfidence = "low",
            RenameCandidate = "s504.00",
            Confidence = "high",
        };

        string proposed = PdfSheetMetadataPolicy.BuildSafeProposedPageName(
            "s504.00 rf d",
            metadata,
            config);

        AssertEqual("s504.00 rf d", proposed, "compound suffix must survive a blank detector result");
    }

    public static void ExactScaleGateRejectsUnsafeSourcesAndExistingScale()
    {
        SheetMetadataConfig config = SheetMetadataConfig.BuildPreciseV2();
        var page = new PageInfo { Name = "a101", ScaleMetersPerPt = 0 };
        var metadata = new PdfSheetMetadata
        {
            SheetKey = "a101",
            Suffix = "1st",
            SelectedScaleMetersPerPt = ViewportConstants.PdfPointMeters * 96,
            ScaleSource = "title_block",
            ScaleConfidence = "high",
            ScaleEvidence = "Title-block scale: 1/8 = 1-0",
        };

        AssertTrue(
            PdfSheetMetadataPolicy.IsExactScaleAutoApplyCandidate(page, metadata, config),
            "high-confidence title-block scale should be eligible");

        metadata.ScaleConfidence = "medium";
        AssertFalse(
            PdfSheetMetadataPolicy.IsExactScaleAutoApplyCandidate(page, metadata, config),
            "Precise v2 defaults must keep medium-confidence scale candidates review-only");
        config.MinimumScaleConfidence = SheetMetadataConfidence.Medium;
        AssertTrue(
            PdfSheetMetadataPolicy.IsExactScaleAutoApplyCandidate(page, metadata, config),
            "Preview preselection must honor an editable Medium minimum");
        AssertFalse(
            PdfSheetMetadataPolicy.IsTrustedHighConfidenceScale(metadata),
            "AutoApplyHighConfidence must still reject a medium-confidence scale");
        metadata.ScaleConfidence = "high";
        AssertTrue(
            PdfSheetMetadataPolicy.IsTrustedHighConfidenceScale(metadata),
            "AutoApplyHighConfidence should accept a high-confidence exact scale");

        metadata.ScaleSource = "inferred";
        AssertFalse(
            PdfSheetMetadataPolicy.IsExactScaleAutoApplyCandidate(page, metadata, config),
            "inferred scale must require explicit review");

        metadata.ScaleSource = "sheet_index";
        metadata.ScaleEvidence = "Drawing list: AS NOTED";
        AssertFalse(
            PdfSheetMetadataPolicy.IsExactScaleAutoApplyCandidate(page, metadata, config),
            "AS NOTED must require explicit review");

        metadata.ScaleEvidence = "Drawing list: 1/8 = 1-0";
        page.ScaleMetersPerPt = ViewportConstants.PdfPointMeters * 48;
        AssertFalse(
            PdfSheetMetadataPolicy.IsExactScaleAutoApplyCandidate(page, metadata, config),
            "an existing scale must be preserved by the precise policy");
    }

    public static void ScaleActionDefaultsToKeepAndClearIsExplicit()
    {
        var row = new PdfMetadataPreviewRow();
        AssertEqual(PdfMetadataScaleAction.Keep, row.ScaleAction, "scale action default");
        AssertFalse(row.ApplyScale, "Keep must not apply scale");

        row.ScaleAction = PdfMetadataScaleAction.Clear;
        AssertTrue(row.ApplyScale, "Clear must be an explicit apply action");
    }

    public static void LearningRecordKeepsImmutableDetectedDecision()
    {
        var page = new PageInfo
        {
            Name = "s504.00 rf d",
            PdfPath = @"C:\missing\plans.pdf",
            PdfPage = 7,
        };
        var metadata = new PdfSheetMetadata
        {
            DetectorVersion = "precise_v2",
            PdfPath = page.PdfPath,
            PageIndex = page.PdfPage,
            SheetKey = "s504.00",
            SheetTitle = "WOOD FRAMING SECTIONS",
            Suffix = "sec",
            RenameCandidate = "s504.00 sec",
        };
        SmartSheetLearningDecision detected = PdfSheetMetadataService.DetectedDecision(metadata);

        metadata.Suffix = "rf d";
        metadata.RenameCandidate = page.Name;
        SmartSheetLearningDecision final = PdfSheetMetadataService.FinalDecision(page, metadata, page.Name, 0);
        SmartSheetLearningRecord record = PdfSheetMetadataService.BuildLearningRecord(
            page,
            metadata,
            "corrected",
            "test",
            final,
            detected,
            "corrected",
            "corrected",
            "not_reviewed");

        AssertEqual("sec", record.Detection.Suffix, "detected suffix must remain immutable");
        AssertEqual("rf d", record.Final.Suffix, "reviewed suffix must be parsed from final page name");
        AssertTrue(record.ObservationKey.Contains("precise_v2", StringComparison.Ordinal), "detector version must be in dedupe key");
        AssertTrue(
            !string.IsNullOrWhiteSpace(record.DetectorConfigFingerprint),
            "detector config fingerprint must be persisted with learning feedback");
        AssertFalse(
            string.Equals(
                PdfSheetMetadataPolicy.BuildObservationKey("pdf", 7, "precise_v2", "config-a"),
                PdfSheetMetadataPolicy.BuildObservationKey("pdf", 7, "precise_v2", "config-b"),
                StringComparison.Ordinal),
            "different detector configs must not dedupe into one observation");
    }

    public static void LearningSummaryDeduplicatesPdfPageDetectorObservation()
    {
        WithTempJob("metadata-learning-dedupe", job =>
        {
            string key = PdfSheetMetadataPolicy.BuildObservationKey("pdf-fingerprint", 3, "precise_v2");
            for (int index = 0; index < 2; index++)
            {
                SmartLearningStore.AppendSheetFeedback(job, null, new SmartSheetLearningRecord
                {
                    UserOutcome = index == 0 ? "accepted" : "corrected",
                    Reviewed = true,
                    SuffixOutcome = index == 0 ? "accepted" : "corrected",
                    ObservationKey = key,
                    PdfFingerprint = "pdf-fingerprint",
                    PdfPage = 3,
                    DetectorVersion = "precise_v2",
                    Final = new SmartSheetLearningDecision
                    {
                        SheetTitle = "WOOD FRAMING SECTIONS",
                        Suffix = index == 0 ? "sec" : "rf d",
                    },
                });
            }

            SmartSheetLearningSummary summary = SmartLearningStore.SaveProjectSummary(job);
            AssertEqual(1, summary.RecordCount, "only latest PDF/page/detector observation should train");
            AssertEqual(1, summary.CorrectedCount, "latest correction should win the dedupe group");
        });
    }

    public static void ProjectLearningReplacesOnlyLowConfidenceEvidence()
    {
        WithTempJob("metadata-learning-precedence", job =>
        {
            SheetMetadataConfig previous = SheetMetadataRulesService.Active.Clone();
            try
            {
                SheetMetadataRulesService.Install(SheetMetadataConfig.BuildPreciseV2());
                SmartLearningStore.SaveProjectLearnedRules(job, new SmartLearnedRuleSet
                {
                    Rules =
                    [
                        new SmartLearnedRule
                        {
                            Enabled = true,
                            Id = "rule_wood_rf_d",
                            TitleToken = "wood",
                            Suffix = "rf d",
                            Support = 8,
                            Confidence = "high",
                        },
                    ],
                });

                var low = new PdfSheetMetadata
                {
                    SheetTitle = "WOOD FRAMING",
                    Suffix = "sec",
                    SuffixSource = "body",
                    SuffixConfidence = "low",
                };
                SmartLearningStore.ApplyProjectLearnedRules(job, low);
                AssertEqual("rf d", low.Suffix, "project learning should correct low-confidence body suffix");

                var exact = new PdfSheetMetadata
                {
                    SheetTitle = "WOOD FRAMING",
                    Suffix = "sec",
                    SuffixSource = "sheet_index",
                    SuffixConfidence = "high",
                };
                SmartLearningStore.ApplyProjectLearnedRules(job, exact);
                AssertEqual("sec", exact.Suffix, "learning must not replace exact sheet-index evidence");

                var intentionalBlank = new PdfSheetMetadata
                {
                    SheetTitle = "WOOD FRAMING",
                    Suffix = "",
                    SuffixSource = "sheet_index",
                    SuffixConfidence = "high",
                };
                SmartLearningStore.ApplyProjectLearnedRules(job, intentionalBlank);
                AssertEqual("", intentionalBlank.Suffix, "learning must preserve an exact intentional blank suffix");
            }
            finally
            {
                SheetMetadataRulesService.Install(previous);
            }
        });
    }

    public static void LearnedRuleDistillationRejectsConflictingToken()
    {
        WithTempJob("metadata-learning-conflict", job =>
        {
            for (int index = 0; index < 5; index++)
            {
                SmartLearningStore.AppendSheetFeedback(job, null, new SmartSheetLearningRecord
                {
                    UserOutcome = "corrected",
                    Reviewed = true,
                    SuffixOutcome = "corrected",
                    ObservationKey = $"conflict-{index}",
                    Final = new SmartSheetLearningDecision
                    {
                        SheetTitle = "EXTERIOR DETAILS",
                        Suffix = index < 3 ? "d" : "el",
                    },
                });
            }

            SmartLearningStore.SaveProjectSummary(job);
            SmartLearnedRuleSet rules = SmartLearningStore.LoadProjectLearnedRules(job);
            AssertFalse(
                rules.Rules.Any(rule => string.Equals(rule.TitleToken, "exterior", StringComparison.OrdinalIgnoreCase)),
                "a 60/40 suffix conflict must not become a learned rule");
        });
    }

    public static void ExplicitSuffixScaleAllowOverridesTerminalNoScaleToken()
    {
        SheetMetadataConfig previous = SheetMetadataRulesService.Active.Clone();
        try
        {
            SheetMetadataRulesService.Install(SheetMetadataConfig.BuildPreciseV2());
            var page = new PageInfo
            {
                Name = "a501 d",
                FolderPath = @"C:\missing\a501 d",
                PdfPath = @"C:\missing\plans.pdf",
                PdfPage = 4,
            };
            var response = new SmartAiResponse
            {
                OutputText =
                    """
                    {
                      "sheet_label": "A-501",
                      "sheet_key": "a501",
                      "sheet_title": "SCALED DETAILS",
                      "suffix": "d",
                      "suffix_source": "configured_rule",
                      "suffix_confidence": "high",
                      "suffix_scale_policy": "allow",
                      "selected_scale_text": "1/4\" = 1'0\"",
                      "scale_source": "sheet_override",
                      "scale_override_action": "Set",
                      "scale_confidence": "high"
                    }
                    """,
            };

            bool ok = PdfSheetMetadataService.TryBuildMetadataFromFallbackResponse(
                page,
                new SmartAiRequest { Id = "suffix-scale-allow-test" },
                response,
                out PdfSheetMetadata metadata,
                out string error);

            AssertTrue(ok, $"fallback metadata should parse: {error}");
            AssertFalse(metadata.SkipScale, "explicit suffix allow must beat terminal d no-scale fallback");
            AssertTrue(metadata.CanApplyScale(), "explicitly allowed detail scale should remain usable");
            AssertTrue(PdfSheetMetadataPolicy.IsExactScaleOverrideSet(metadata), "exact scale Set must survive fallback parsing and C# normalization");
        }
        finally
        {
            SheetMetadataRulesService.Install(previous);
        }
    }

    public static void ReviewedScaleClearSurvivesLaterAnalysis()
    {
        WithTempJob("metadata-scale-clear", job =>
        {
            SheetMetadataConfig previous = SheetMetadataRulesService.Active.Clone();
            try
            {
                SheetMetadataRulesService.Install(SheetMetadataConfig.BuildPreciseV2());
                PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "a101", job.PagesRoot);
                OurPlanCoreJobStore.WriteSourcePdfMetadata(page.FolderPath, new PdfSheetMetadata
                {
                    PdfPath = page.PdfPath,
                    PageIndex = page.PdfPage,
                    SheetKey = "a101",
                    Suffix = "1st",
                    SkipScale = true,
                    SkipReason = "manual-review: scale cleared",
                    ScaleSource = "manual-review",
                    ScaleConfidence = "high",
                });

                var response = new SmartAiResponse
                {
                    OutputText =
                        """
                        {
                          "sheet_label": "A-101",
                          "sheet_key": "a101",
                          "sheet_title": "FIRST FLOOR PLAN",
                          "suffix": "1st",
                          "selected_scale_text": "1/8\" = 1'0\"",
                          "scale_source": "title_block",
                          "scale_confidence": "high"
                        }
                        """,
                };

                bool ok = PdfSheetMetadataService.TryBuildMetadataFromFallbackResponse(
                    page,
                    new SmartAiRequest { Id = "preserve-clear-test" },
                    response,
                    out PdfSheetMetadata metadata,
                    out string error,
                    job);

                AssertTrue(ok, $"fallback metadata should parse: {error}");
                AssertTrue(metadata.SkipScale, "reviewed Clear must survive a later detector pass");
                AssertEqual(0d, metadata.SelectedScaleMetersPerPt, "reviewed Clear must keep scale at zero");
                AssertEqual("manual-review", metadata.ScaleSource, "reviewed Clear provenance must survive");
            }
            finally
            {
                SheetMetadataRulesService.Install(previous);
            }
        });
    }

    public static void ExplicitSuffixClearBeatsPreservation()
    {
        SheetMetadataConfig config = SheetMetadataConfig.BuildPreciseV2();
        var metadata = new PdfSheetMetadata
        {
            SheetKey = "a101",
            RenameCandidate = "a101",
            Suffix = "",
            SuffixSource = "sheet_override",
            SuffixConfidence = "high",
            SuffixExplicitClear = true,
        };

        string proposed = PdfSheetMetadataPolicy.BuildSafeProposedPageName(
            "a101 old suffix",
            metadata,
            config,
            existingNameIsManual: true);

        AssertEqual("a101", proposed, "explicit Clear must remove an existing suffix");

        metadata.Suffix = "new suffix";
        metadata.RenameCandidate = "a101 new suffix";
        metadata.SuffixOverrideAction = "Set";
        metadata.SuffixExplicitClear = false;
        string setProposal = PdfSheetMetadataPolicy.BuildSafeProposedPageName(
            "a101 old suffix",
            metadata,
            config,
            existingNameIsManual: true);
        AssertEqual("a101 new suffix", setProposal, "exact Suffix Set must replace an existing manual suffix");
        AssertTrue(PdfSheetMetadataPolicy.IsExactSuffixOverride(metadata), "exact Suffix Set must carry explicit provenance");
    }

    public static void ExactRenameOverrideBeatsManualPreservationAndLowConfidence()
    {
        SheetMetadataConfig config = SheetMetadataConfig.BuildPreciseV2();
        var page = new PageInfo { Name = "a101 old" };
        var metadata = new PdfSheetMetadata
        {
            SheetKey = "a101",
            RenameCandidate = "a101 reviewed",
            RenameOverrideApplied = true,
            TitleConfidence = "low",
            SuffixConfidence = "low",
        };

        string proposed = PdfSheetMetadataPolicy.BuildSafeProposedPageName(
            page.Name,
            metadata,
            config,
            existingNameIsManual: true);

        AssertEqual("a101 reviewed", proposed, "exact page-name override must beat manual preservation");
        AssertTrue(PdfSheetMetadataPolicy.CanAutoRename(page, metadata, config, proposed), "exact page-name override must be applicable despite weak detector evidence");
        AssertTrue(PdfSheetMetadataPolicy.IsTrustedHighConfidenceRename(metadata), "saved exact override must be trusted for high-confidence apply policy");
    }

    public static void ExactScaleOverrideActionsSurviveCSharpNormalizationContract()
    {
        var set = new PdfSheetMetadata
        {
            ScaleOverrideAction = "Set",
            ScaleSource = "sheet_override",
            SelectedScaleMetersPerPt = ViewportConstants.PdfPointMeters * 2,
            Suffix = "d",
            SuffixScalePolicy = "allow",
        };
        AssertTrue(PdfSheetMetadataPolicy.IsExactScaleOverrideSet(set), "exact Set must remain explicit in preview policy");
        AssertFalse(set.SkipScale, "exact Set must beat a no-scale suffix through explicit allow provenance");

        var clear = new PdfSheetMetadata
        {
            ScaleOverrideAction = "Clear",
            ScaleSource = "sheet_override",
            SkipScale = true,
            SkipReason = "configured_clear",
        };
        AssertTrue(PdfSheetMetadataPolicy.IsExactScaleOverrideClear(clear), "exact Clear must map to the preview Clear action");
        AssertFalse(PdfSheetMetadataPolicy.IsExactScaleOverrideSet(clear), "Clear must never masquerade as Set");
    }

    public static void ExactScaleSetBeatsPreviouslyReviewedClear()
    {
        WithTempJob("metadata-exact-set-after-clear", job =>
        {
            SheetMetadataConfig previous = SheetMetadataRulesService.Active.Clone();
            try
            {
                SheetMetadataRulesService.Install(SheetMetadataConfig.BuildPreciseV2());
                PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "a101", job.PagesRoot);
                OurPlanCoreJobStore.WriteSourcePdfMetadata(page.FolderPath, new PdfSheetMetadata
                {
                    SheetKey = "a101",
                    SkipScale = true,
                    SkipReason = "manual-review: scale cleared",
                    ScaleSource = "manual-review",
                    ScaleConfidence = "high",
                });

                var response = new SmartAiResponse
                {
                    OutputText =
                        """
                        {
                          "sheet_label": "A-101",
                          "sheet_key": "a101",
                          "sheet_title": "FIRST FLOOR PLAN",
                          "suffix": "1st",
                          "suffix_scale_policy": "allow",
                          "selected_scale_text": "1/4\" = 1'0\"",
                          "scale_source": "sheet_override",
                          "scale_override_action": "Set",
                          "scale_confidence": "high"
                        }
                        """,
                };

                bool ok = PdfSheetMetadataService.TryBuildMetadataFromFallbackResponse(
                    page,
                    new SmartAiRequest { Id = "exact-set-after-clear" },
                    response,
                    out PdfSheetMetadata metadata,
                    out string error,
                    job);

                AssertTrue(ok, $"exact Set fallback must parse: {error}");
                AssertTrue(metadata.CanApplyScale(), "new exact Set must beat an older reviewed Clear");
                AssertEqual("sheet_override", metadata.ScaleSource, "exact Set provenance must survive");
                AssertTrue(PdfSheetMetadataPolicy.IsExactScaleOverrideSet(metadata), "exact Set must be preselectable after normalization");
            }
            finally
            {
                SheetMetadataRulesService.Install(previous);
            }
        });
    }

    public static void FallbackBuildDoesNotWriteBeforePreviewApproval()
    {
        WithTempJob("metadata-preview-cancel", job =>
        {
            PageInfo page = OurPlanCoreJobStore.CreateBlankPage(job, "a101 original", job.PagesRoot);
            OurPlanCoreJobStore.WriteSourcePdfMetadata(page.FolderPath, new PdfSheetMetadata
            {
                SheetKey = "a101",
                SheetTitle = "ORIGINAL MANUAL TITLE",
                Suffix = "original",
                Source = "manual-review",
            });
            string metadataPath = OurPlanCoreJobStore.SourcePdfMetadataPath(page.FolderPath);
            string before = File.ReadAllText(metadataPath);

            var response = new SmartAiResponse
            {
                OutputText =
                    """
                    {
                      "sheet_label": "A-101",
                      "sheet_key": "a101",
                      "sheet_title": "FIRST FLOOR PLAN",
                      "suffix": "1st",
                      "rename_candidate": "a101 1st",
                      "title_confidence": "high",
                      "suffix_confidence": "high"
                    }
                    """,
            };

            bool ok = PdfSheetMetadataService.TryBuildMetadataFromFallbackResponse(
                page,
                new SmartAiRequest { Id = "preview-cancel-no-write" },
                response,
                out PdfSheetMetadata built,
                out string error,
                job);

            AssertTrue(ok, $"fallback preview candidate should build: {error}");
            AssertEqual("a101 1st", built.RenameCandidate, "preview candidate should contain the proposed rename");
            AssertEqual(before, File.ReadAllText(metadataPath), "building a preview candidate must not mutate source_pdf.json");
        });
    }

    public static void UncommonExactScaleRoundTripsWithoutPresetSnap()
    {
        const string exactScale = "5.5\" = 1'0\"";
        AssertTrue(
            PdfSheetMetadataService.TryParseScaleMetersPerPt(exactScale, out double scaleMetersPerPt),
            "uncommon exact scale should parse");
        var metadata = new PdfSheetMetadata
        {
            SelectedScaleText = exactScale,
            ScaleText = exactScale,
            SelectedScaleMetersPerPt = scaleMetersPerPt,
            ScaleSource = "sheet_override",
            ScaleOverrideAction = "Set",
        };

        string previewText = PdfSheetMetadataPolicy.ReviewScaleText(metadata);
        AssertEqual(exactScale, previewText, "preview must preserve exact uncommon scale text");
        string appliedText = PdfSheetMetadataPolicy.AppliedScaleText(previewText, scaleMetersPerPt);
        AssertEqual(exactScale, appliedText, "apply must preserve reviewed exact scale text");
        AssertTrue(
            PdfSheetMetadataService.TryParseScaleMetersPerPt(appliedText, out double roundTrip),
            "persisted uncommon scale should remain parseable");
        AssertTrue(
            PdfSheetMetadataPolicy.SameScale(scaleMetersPerPt, roundTrip),
            "preview/apply text must round-trip to the identical numeric scale");

        metadata.SelectedScaleText = "1/8\" = 1'0\"";
        metadata.ScaleText = metadata.SelectedScaleText;
        PdfSheetMetadataPolicy.PersistKeptScaleDecision(metadata, scaleMetersPerPt);
        AssertTrue(
            PdfSheetMetadataService.TryParseScaleMetersPerPt(metadata.EffectiveScaleText, out double keptScale),
            "Keep fallback must create parseable exact scale text");
        AssertTrue(
            PdfSheetMetadataPolicy.SameScale(scaleMetersPerPt, keptScale),
            "Keep must preserve uncommon numeric scale even when detector text differs");
        SmartSheetLearningDecision final = PdfSheetMetadataService.FinalDecision(
            new PageInfo { Name = "a101" },
            metadata,
            "a101",
            scaleMetersPerPt);
        AssertTrue(
            PdfSheetMetadataService.TryParseScaleMetersPerPt(final.ScaleText, out double learnedScale),
            "learning must persist a parseable uncommon scale");
        AssertTrue(
            PdfSheetMetadataPolicy.SameScale(scaleMetersPerPt, learnedScale),
            "learning must preserve the uncommon numeric scale");
    }

    public static void KeptNoScaleDecisionRemainsNoScale()
    {
        var metadata = new PdfSheetMetadata
        {
            SkipScale = true,
            SkipReason = "not_to_scale",
            ScaleSource = "title_block",
            ScaleEvidence = "NOT TO SCALE",
        };

        PdfSheetMetadataPolicy.PersistKeptScaleDecision(metadata, 0);

        AssertTrue(metadata.SkipScale, "Keep must preserve an explicit NTS/no-scale decision");
        AssertEqual("not_to_scale", metadata.SkipReason, "Keep must preserve the detected NTS reason");
        AssertEqual(0d, metadata.SelectedScaleMetersPerPt, "kept NTS must remain numerically empty");
    }

    public static void ProjectLearnedScaleUpdatesTextAndNumericValueTogether()
    {
        WithTempJob("metadata-learned-scale-sync", job =>
        {
            SheetMetadataConfig previous = SheetMetadataRulesService.Active.Clone();
            try
            {
                SheetMetadataRulesService.Install(SheetMetadataConfig.BuildPreciseV2());
                const string learnedScale = "5.5\" = 1'0\"";
                SmartLearningStore.SaveProjectLearnedRules(job, new SmartLearnedRuleSet
                {
                    Rules =
                    [
                        new SmartLearnedRule
                        {
                            Enabled = true,
                            TitleToken = "roof",
                            ScaleText = learnedScale,
                            Confidence = "high",
                            Support = 8,
                        },
                    ],
                });
                PdfSheetMetadataService.TryParseScaleMetersPerPt(
                    "1/8\" = 1'0\"",
                    out double oldScaleMetersPerPt);
                var metadata = new PdfSheetMetadata
                {
                    SheetTitle = "ROOF FRAMING PLAN",
                    SelectedScaleText = "1/8\" = 1'0\"",
                    ScaleText = "1/8\" = 1'0\"",
                    SelectedScaleMetersPerPt = oldScaleMetersPerPt,
                    SelectedScaleRatio = oldScaleMetersPerPt / ViewportConstants.PdfPointMeters,
                    ScaleSource = "body",
                    ScaleConfidence = "low",
                    SkipScale = true,
                };

                SmartLearningStore.ApplyProjectLearnedRules(job, metadata);

                AssertEqual(learnedScale, metadata.EffectiveScaleText, "learned scale text should replace weak text");
                AssertTrue(
                    PdfSheetMetadataService.TryParseScaleMetersPerPt(learnedScale, out double expected),
                    "learned test scale should parse");
                AssertTrue(
                    PdfSheetMetadataPolicy.SameScale(expected, metadata.SelectedScaleMetersPerPt),
                    "learned scale must replace the numeric value with the same decision");
                AssertFalse(metadata.SkipScale, "a learned concrete scale must clear an older skip decision");
            }
            finally
            {
                SheetMetadataRulesService.Install(previous);
            }
        });
    }

    public static void LearnedSuffixDoesNotMutateProtectedScaleDecision()
    {
        WithTempJob("metadata-learned-suffix-scale-isolation", job =>
        {
            SheetMetadataConfig previous = SheetMetadataRulesService.Active.Clone();
            try
            {
                SheetMetadataRulesService.Install(SheetMetadataConfig.BuildPreciseV2());
                PdfSheetMetadataService.TryParseScaleMetersPerPt(
                    "1/8\" = 1'0\"",
                    out double exactScale);
                SmartLearningStore.SaveProjectLearnedRules(job, new SmartLearnedRuleSet
                {
                    Rules =
                    [
                        new SmartLearnedRule
                        {
                            Enabled = true,
                            TitleToken = "roof",
                            Suffix = "d",
                            SkipScale = true,
                            Confidence = "high",
                            Support = 8,
                        },
                    ],
                });
                var scaled = new PdfSheetMetadata
                {
                    SheetTitle = "ROOF PLAN",
                    SuffixConfidence = "low",
                    SelectedScaleText = "1/8\" = 1'0\"",
                    ScaleText = "1/8\" = 1'0\"",
                    SelectedScaleMetersPerPt = exactScale,
                    ScaleSource = "title_block",
                    ScaleConfidence = "high",
                };

                SmartLearningStore.ApplyProjectLearnedRules(job, scaled);

                AssertEqual("d", scaled.Suffix, "learned suffix may fill a weak suffix");
                AssertFalse(scaled.SkipScale, "learned suffix must not suppress a protected exact scale");
                AssertTrue(
                    PdfSheetMetadataPolicy.SameScale(exactScale, scaled.SelectedScaleMetersPerPt),
                    "learned suffix must not change protected numeric scale");
                AssertEqual("allow", scaled.SuffixScalePolicy, "protected exact scale must survive later suffix normalization");

                SmartLearningStore.SaveProjectLearnedRules(job, new SmartLearnedRuleSet
                {
                    Rules =
                    [
                        new SmartLearnedRule
                        {
                            Enabled = true,
                            TitleToken = "roof",
                            Suffix = "u",
                            SkipScale = false,
                            Confidence = "high",
                            Support = 8,
                        },
                    ],
                });
                var nts = new PdfSheetMetadata
                {
                    SheetTitle = "ROOF PLAN",
                    SuffixConfidence = "low",
                    SkipScale = true,
                    SkipReason = "not_to_scale",
                    ScaleSource = "title_block",
                    ScaleConfidence = "high",
                };

                SmartLearningStore.ApplyProjectLearnedRules(job, nts);

                AssertEqual("u", nts.Suffix, "learned suffix may still fill the independent suffix field");
                AssertTrue(nts.SkipScale, "learned suffix must not clear protected title-block NTS");
                AssertEqual("not_to_scale", nts.SkipReason, "protected NTS provenance must remain intact");
            }
            finally
            {
                SheetMetadataRulesService.Install(previous);
            }
        });
    }

    private static void WithTempJob(string name, Action<OurPlanCoreJob> action)
    {
        string parent = Path.Combine(Path.GetTempPath(), $"ourplancore-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(parent);
        try
        {
            action(OurPlanCoreJobStore.CreateJob(parent, name));
        }
        finally
        {
            try
            {
                Directory.Delete(parent, recursive: true);
            }
            catch
            {
                // Cleanup must not hide the assertion that failed.
            }
        }
    }

    private static void AssertEqual<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{message}: expected '{expected}', got '{actual}'");
    }

    private static void AssertTrue(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }

    private static void AssertFalse(bool condition, string message) =>
        AssertTrue(!condition, message);
}
