using System;
using System.Diagnostics;
using System.Text;

namespace OurPlanCore;

public partial class MainWindow
{
    // Page-open stage timing. Off by default (no behavior change, no cost).
    // Set OURPLANCORE_PAGE_OPEN_TRACE=1 to log a per-stage breakdown of every
    // page open to the app log, so the next render optimization is driven by
    // measured stage costs instead of guesswork (see
    // docs/PERFORMANCE_RENDER_AND_LAYERS_HANDOFF_2026_05_16.md, "Next
    // Performance Work" item 5).
    private static readonly bool PageOpenTraceEnabled =
        AppIdentity.GetEnvironmentVariable("OURPLANCORE_PAGE_OPEN_TRACE") == "1";

    private PageOpenTrace? BeginPageOpenTrace(string pageName) =>
        PageOpenTraceEnabled ? new PageOpenTrace(pageName) : null;

    private sealed class PageOpenTrace : IDisposable
    {
        private readonly Stopwatch _total = Stopwatch.StartNew();
        private readonly Stopwatch _stage = Stopwatch.StartNew();
        private readonly StringBuilder _log = new();
        private readonly string _pageName;

        public PageOpenTrace(string pageName) => _pageName = pageName;

        public void Mark(string stage)
        {
            _log.Append(stage).Append('=').Append(_stage.ElapsedMilliseconds).Append("ms ");
            _stage.Restart();
        }

        public void Dispose()
        {
            _total.Stop();
            AppLog.Info($"page-open[{_pageName}] {_log}total={_total.ElapsedMilliseconds}ms");
        }
    }
}
