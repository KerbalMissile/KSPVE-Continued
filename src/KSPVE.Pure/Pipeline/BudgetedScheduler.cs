using System;
using System.Collections.Generic;

namespace KSPVE.Pure.Pipeline;

/// <summary>
/// Spreads jobs over frames within a per-frame millisecond budget using priority credit.
/// The highest-credit job always runs, so oversized or low-priority jobs are never starved.
/// </summary>
public sealed class BudgetedScheduler
{
    private sealed class Job
    {
        public string Id = string.Empty;
        public double Cost;
        public int Priority;
        public long Credit;
    }

    private readonly List<Job> _jobs = new();
    private readonly List<Job> _sorted = new();
    private readonly List<string> _result = new();

    public BudgetedScheduler(double budgetMsPerFrame)
    {
        BudgetMsPerFrame = budgetMsPerFrame;
    }

    public double BudgetMsPerFrame { get; set; }
    public int Count => _jobs.Count;

    /// <summary>Returns false (without registering) for invalid arguments or a duplicate id.</summary>
    public bool Register(string jobId, double costMs, int priority)
    {
        if (jobId == null || priority < 1 || double.IsNaN(costMs) || double.IsInfinity(costMs) || costMs < 0) return false;
        if (Find(jobId) >= 0) return false;
        _jobs.Add(new Job { Id = jobId, Cost = costMs, Priority = priority });
        return true;
    }

    public bool Unregister(string jobId)
    {
        int i = jobId == null ? -1 : Find(jobId);
        if (i < 0) return false;
        _jobs.RemoveAt(i);
        return true;
    }

    /// <summary>The returned list is reused on the next call; copy it if it must outlive the frame.</summary>
    public IReadOnlyList<string> NextFrame()
    {
        _result.Clear();
        if (_jobs.Count == 0) return _result;

        _sorted.Clear();
        foreach (var j in _jobs)
        {
            j.Credit += j.Priority;
            _sorted.Add(j);
        }
        _sorted.Sort(CompareByCredit);

        double total = 0;
        for (int i = 0; i < _sorted.Count; i++)
        {
            var j = _sorted[i];
            if (i > 0 && total + j.Cost > BudgetMsPerFrame) break;
            total += j.Cost;
            j.Credit = 0;
            _result.Add(j.Id);
        }
        return _result;
    }

    private int Find(string id)
    {
        for (int i = 0; i < _jobs.Count; i++)
            if (string.Equals(_jobs[i].Id, id, StringComparison.Ordinal)) return i;
        return -1;
    }

    private static int CompareByCredit(Job a, Job b)
    {
        int c = b.Credit.CompareTo(a.Credit);
        return c != 0 ? c : string.CompareOrdinal(a.Id, b.Id);
    }
}
