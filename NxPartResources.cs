using System;
using System.Collections.Generic;
using System.Diagnostics;
using NXOpen;
using NXOpen.Assemblies;

// One synchronous run at a time, enforced by AssemblyTraverser.Run.
internal sealed class NxPartResources : IDisposable
{
    private static NxPartResources _current;
    private readonly Session _session;
    private readonly PartLifetime<Tag> _lifetime = new PartLifetime<Tag>();
    private readonly Dictionary<Tag, bool> _initialModified = new Dictionary<Tag, bool>();
    private readonly Dictionary<Tag, PartLoadState> _initialLoadState = new Dictionary<Tag, PartLoadState>();
    private bool _needsSweep;

    public NxPartResources(Session session)
    {
        if (_current != null) throw new InvalidOperationException("部件资源管理器已在运行。");
        _session = session;
        foreach (BasePart part in session.Parts.ToArray())
        {
            bool modified = part.IsModified;
            _initialModified.Add(part.Tag, modified);
            _initialLoadState.Add(part.Tag, part.PartLoadState);
            _lifetime.RegisterInitial(part.Tag, modified || part.IsFullyLoaded);
        }
        foreach (BasePart part in session.Parts.GetDisplayedParts()) Protect(part);
        Protect(session.Parts.BaseWork);
        // Preserve the exact original work occurrence, including its ancestors.
        for (Component c = session.Parts.WorkComponent; c != null; c = c.Parent)
        {
            Protect(c.OwningPart);
            Protect(c.Prototype as BasePart);
        }
        _current = this;
        LogMemory("start", 0);
    }

    private void Protect(BasePart part)
    {
        if (part != null) _lifetime.Protect(part.Tag);
    }

    // Call before every operation that can load data, including failed opens.
    public static void BeforeLoad(BasePart part)
    {
        NxPartResources owner = _current;
        if (owner == null) return;
        owner._needsSweep = true;
        if (part == null) return;
        // An initially partial part modified before our first touch is not ours
        // to discard (for example a site callback modified it).
        if (owner._lifetime.WasInitial(part.Tag) && !owner._lifetime.WasTouched(part.Tag) && part.IsModified)
            owner._lifetime.Protect(part.Tag);
        owner._lifetime.Touch(part.Tag);
    }

    public void Pin(Part part) { _lifetime.Pin(part.Tag); }
    public static void KeepCurrentPartsOpen()
    {
        if (_current == null) return;
        foreach (BasePart part in _current._session.Parts.ToArray()) _current.Protect(part);
    }
    public void Unpin(Tag key)
    {
        _lifetime.Unpin(key);
        // A child sweep may have deferred this active ancestor.
        _needsSweep = true;
    }

    public bool ReleaseUnused(string stage)
    {
        if (!_needsSweep) return true;
        _needsSweep = false;
        int closed = 0;
        bool ok = true;
        try
        {
            var active = new HashSet<Tag>();
            foreach (BasePart part in _session.Parts.GetDisplayedParts()) active.Add(part.Tag);
            if (_session.Parts.BaseWork != null) active.Add(_session.Parts.BaseWork.Tag);
            var required = new Dictionary<Tag, string>();
            List<KeyValuePair<Tag, string>> candidates = FindCandidates(active, required);
            // A close can invalidate other BasePart wrappers too. Retain only
            // primitive keys/names across close calls, and reacquire each part.
            foreach (KeyValuePair<Tag, string> candidate in candidates)
            {
                Tag key = candidate.Key;
                string name = candidate.Value;
                BasePart part = FindLoadedPart(key);
                bool alreadyReleased = part == null;
                Exception closeError = null;
                if (part != null)
                {
                    try
                    {
                        RunLog.Write("[Unload begin] " + name + " Modified=" + part.IsModified);
                        TraversalCancellation.NxCall("Close " + name, () =>
                            part.Close(BasePart.CloseWholeTree.False, BasePart.CloseModified.CloseModified, null), cancelable: false);
                    }
                    catch (Exception ex) { closeError = ex; }
                }

                var remaining = new HashSet<Tag>();
                foreach (BasePart current in _session.Parts.ToArray()) remaining.Add(current.Tag);
                bool gone = !remaining.Contains(key);
                var nxError = closeError as NXException;
                // Ignore an inactive-object error ONLY when a fresh enumeration
                // proves this specific part is gone. Do not swallow other errors.
                if (closeError != null && !(gone && nxError != null && nxError.ErrorCode == 3615013))
                {
                    ok = false;
                    RunLog.Error("Unload " + name, closeError);
                }
                if (gone)
                {
                    ++closed;
                    _lifetime.Released(key);
                    RunLog.Write("[Unload done] " + name +
                        (alreadyReleased ? "（已随前一次关闭释放）" : closeError != null ? "（已确认不在会话中）" : ""));
                }
                else
                {
                    ok = false;
                    RunLog.Write("[ERROR] 关闭后部件仍在会话中: " + name);
                }
                foreach (KeyValuePair<Tag, string> entry in required)
                {
                    if (remaining.Contains(entry.Key)) continue;
                    RunLog.Write("[ERROR] NX 意外卸载了受保护部件: " + entry.Value);
                    ok = false;
                }
                if (!ok) break;
            }
        }
        catch (Exception ex) { ok = false; RunLog.Error("释放部件 " + stage, ex); }
        LogMemory(stage, closed);
        if (!ok)
        {
            AssemblyTraverser.RequestStop();
            RunLog.Write("[STOP] 部件未能释放，停止继续加载以免内存持续增长。");
        }
        return ok;
    }

    private List<KeyValuePair<Tag, string>> FindCandidates(HashSet<Tag> active, Dictionary<Tag, string> required)
    {
        var result = new List<KeyValuePair<Tag, string>>();
        BasePart[] parts = _session.Parts.ToArray();
        for (int i = parts.Length - 1; i >= 0; --i)
        {
            BasePart part = parts[i];
            Tag key = part.Tag;
            PartLoadState initialState;
            PartLoadState state = part.PartLoadState;
            if (_initialLoadState.TryGetValue(key, out initialState) && initialState != state &&
                (state == PartLoadState.FullyLoaded || state == PartLoadState.PartiallyLoaded))
                _lifetime.Touch(key);
            if (_lifetime.MustRemainLoaded(key) || active.Contains(key)) required.Add(key, part.Name);
            else if (_lifetime.CanRelease(key)) result.Add(new KeyValuePair<Tag, string>(key, part.Name));
        }
        return result;
    }

    private BasePart FindLoadedPart(Tag key)
    {
        foreach (BasePart part in _session.Parts.ToArray())
            if (part.Tag == key) return part;
        return null;
    }

    private void LogMemory(string stage, int closed)
    {
        try
        {
            using (Process process = Process.GetCurrentProcess())
                RunLog.Write(string.Format("[Memory] {0} LoadedParts={1} Closed={2} PrivateMB={3:F1}",
                    stage, _session.Parts.ToArray().Length, closed, process.PrivateMemorySize64 / 1048576.0));
        }
        catch (Exception ex) { RunLog.Error("内存统计", ex); }
    }

    public void Dispose()
    {
        try
        {
            _needsSweep = true;
            ReleaseUnused("final");
            foreach (BasePart part in _session.Parts.ToArray())
            {
                bool wasModified;
                if (_initialModified.TryGetValue(part.Tag, out wasModified) && !wasModified && part.IsModified)
                    RunLog.Write("[WARN retained modified] " + part.Name + " ReadOnly=" + part.IsReadOnly +
                        "；保留原有部件及修改标记，NX 返回交互界面时仍可能提示只读。请核查 syslog。");
            }
        }
        finally { _current = null; }
    }
}
