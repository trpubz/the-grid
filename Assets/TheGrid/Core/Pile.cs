using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace TheGrid.Core
{
    // One message between two sessions, as kept in pile.json. Field names are the JSON keys.
    [Serializable]
    public sealed class PileMessage
    {
        public string key = "";      // The Grid's own name for the row; never changes
        public string id = "";       // the agent app's message id, shared by the sent and the received record; empty when it gives none
        public string sendRef = "";  // tool call id of the sent record; empty until that record is found
        public string recvRef = "";  // uuid of the received record; empty until that record is found
        public string bodyKey = "";  // account and a hash of the text: pairs the two sides when there is no id
        public string account = "", from = "", fromRepo = "", to = "", toRepo = "", summary = "", firstLine = "";
        public string srcPath = "";  // where the full text can be read: the sent record, else the received one
        public long srcOffset;
        public long sentAt, receivedAt;
        public bool ticked, archived;
    }

    // One row of the review pile.
    [Serializable]
    public sealed class PileRow
    {
        public string key = "", from = "", fromRepoName = "", to = "", toRepoName = "", account = "", summary = "", firstLine = "", fromWorkspace = "", toWorkspace = "";
        public long at;              // when it was sent, or when it was received if the sent record was not found
        public bool ticked, crossWorkspace;
    }

    // What the viewer draws. A snapshot is never changed after it is published.
    [Serializable]
    public sealed class PileSnapshot
    {
        public long generatedAt;
        public List<PileRow> rows = new List<PileRow>(); // not archived, newest first
        public int archived;
        public bool reading;         // the first pass over the history has not finished
        public List<string> errors = new List<string>();
    }

    public static class PileFormat
    {
        // "4m", "3h", "2d". Never negative.
        public static string Age(long sinceMs, long nowMs)
        {
            long minutes = Math.Max(0, nowMs - sinceMs) / 60000;
            return minutes < 60 ? minutes + "m" : minutes < 2880 ? minutes / 60 + "h" : minutes / 1440 + "d";
        }

        // The first line that is not blank, trimmed, cut to max characters.
        public static string FirstLine(string body, int max = 140)
        {
            foreach (var raw in (body ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length > 0) return line.Length > max ? line.Substring(0, max) + "…" : line;
            }
            return "";
        }
    }

    // The review pile: every message between Claude Code sessions, once each, with the owner's ticks.
    // It only reads transcripts. Its own state is one file, which must not sit under a Claude config directory.
    public sealed class Pile : IDisposable
    {
        const long SaveOffsetsEveryMs = 60000;

        [Serializable] sealed class FileState
        {
            public string path = "", name = "";
            public long offset;                     // every complete line before this byte has been read
            public bool titled;                     // the name is a generated title, replaced by any real name
            [NonSerialized] public long length;     // size when last read; not saved, so a file that failed to open is tried again at the next launch
        }
        [Serializable] sealed class State
        {
            public int nextKey = 1;
            public bool complete;    // a first pass over every transcript has finished
            public List<FileState> files = new List<FileState>();
            public List<PileMessage> messages = new List<PileMessage>();
        }

        readonly Settings settings;
        readonly string home, statePath;
        readonly object gate = new object();
        readonly ManualResetEventSlim stop = new ManualResetEventSlim(false);
        readonly Dictionary<string, FileState> files = new Dictionary<string, FileState>();
        readonly Dictionary<string, PileMessage> byId = new Dictionary<string, PileMessage>(), bySend = new Dictionary<string, PileMessage>(), byRecv = new Dictionary<string, PileMessage>();
        State state;
        volatile PileSnapshot latest;
        long lastSaveAt;
        bool unsaved;
        Thread thread;

        public Pile(Settings settings, string home, string statePath)
        {
            this.settings = settings;
            this.home = home;
            this.statePath = statePath;
            state = Load(statePath);
            foreach (var f in state.files) files[f.path] = f;
            Reindex();
            Publish(0, null);
        }

        public PileSnapshot Latest => latest;

        static State Load(string path)
        {
            State s = null;
            if (File.Exists(path))
            {
                try { s = JsonUtility.FromJson<State>(File.ReadAllText(path)); }
                catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException) { }
                // Unreadable: keep the file, because it holds the owner's ticks, and rebuild the list from the transcripts.
                if (s == null) try { File.Copy(path, path + ".bad", true); } catch (IOException) { }
            }
            s = s ?? new State();
            s.files = s.files ?? new List<FileState>();
            s.messages = s.messages ?? new List<PileMessage>();
            return s;
        }

        void Save(long nowMs)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(statePath));
                var tmp = statePath + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(state));
                if (File.Exists(statePath)) File.Replace(tmp, statePath, null); else File.Move(tmp, statePath);
                lastSaveAt = nowMs;
                unsaved = false;
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { unsaved = true; }
        }

        void Reindex()
        {
            byId.Clear(); bySend.Clear(); byRecv.Clear();
            foreach (var m in state.messages) Index(m);
        }

        void Index(PileMessage m)
        {
            if (m.id != "") byId[m.id] = m;
            if (m.sendRef != "") bySend[m.sendRef] = m;
            if (m.recvRef != "") byRecv[m.recvRef] = m;
        }

        PileMessage Add(string configDir)
        {
            var m = new PileMessage { key = (state.nextKey++).ToString(), account = RosterBuilder.AccountLabel(configDir) };
            state.messages.Add(m);
            return m;
        }

        // A socket path or a desktop-app session id, as opposed to a session name.
        static bool IsAddress(string to) => to.StartsWith("uds:", StringComparison.Ordinal) || to.StartsWith("local_", StringComparison.Ordinal);

        static string BodyKey(string account, string body)
        {
            using (var sha = SHA1.Create())
                return account + ":" + BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes((body ?? "").Trim())), 0, 8).Replace("-", "");
        }

        // Applies one fact read from a transcript. Returns whether the list changed. Poll calls this; it is public for tests.
        public bool Apply(MessageEvent e, string transcriptPath, string configDir)
        {
            lock (gate)
            {
                if (!files.TryGetValue(transcriptPath, out var file))
                {
                    files[transcriptPath] = file = new FileState { path = transcriptPath };
                    state.files.Add(file);
                }
                PileMessage m;
                switch (e.Kind)
                {
                    case MessageEventKind.SessionName:
                        // A generated title only names a session that has no name of its own.
                        if (e.IsTitle && file.name != "" && !file.titled) return false;
                        file.name = e.Name;
                        file.titled = e.IsTitle;
                        return false;

                    case MessageEventKind.Sent:
                        if (e.OwnSubagent || bySend.ContainsKey(e.Ref)) return false; // a copied transcript repeats its records
                        m = Add(configDir);
                        m.sendRef = e.Ref;
                        m.from = file.name;
                        m.fromRepo = RepoResolver.Resolve(e.Cwd).Root;
                        m.to = e.Peer;
                        m.summary = e.Summary;
                        m.firstLine = PileFormat.FirstLine(e.Body);
                        m.bodyKey = BodyKey(m.account, e.Body);
                        m.sentAt = e.At;
                        m.srcPath = transcriptPath;
                        m.srcOffset = e.Offset;
                        Index(m);
                        return true;

                    case MessageEventKind.SendResult:
                        if (!bySend.TryGetValue(e.Ref, out m)) return false;
                        if (e.OwnSubagent) // the recipient turned out to be the sender's own subagent
                        {
                            state.messages.Remove(m);
                            Reindex();
                            return true;
                        }
                        if (e.MsgId == "" || m.id == e.MsgId) return false;
                        if (byId.TryGetValue(e.MsgId, out var received) && received != m) Merge(received, m); // its receipt was read first
                        else { m.id = e.MsgId; Index(m); }
                        return true;

                    case MessageEventKind.Received:
                        if (e.MsgId != "")
                        {
                            if (!byId.TryGetValue(e.MsgId, out m)) { m = Add(configDir); m.id = e.MsgId; }
                            else if (m.recvRef != "") return false; // already paired
                        }
                        else
                        {
                            if (byRecv.ContainsKey(e.Ref)) return false;
                            m = Add(configDir);
                            m.bodyKey = BodyKey(m.account, e.Body);
                        }
                        m.recvRef = e.Ref;
                        // Names as the messaging layer gave them win: the name the sender addressed, and the sender's name on the receipt.
                        // A transcript's own label is often a generated title, so it only stands in for an address or a missing record.
                        if (file.name != "" && (m.to == "" || IsAddress(m.to))) m.to = file.name;
                        m.toRepo = RepoResolver.Resolve(e.Cwd).Root;
                        m.receivedAt = e.At;
                        if (e.PeerName != "") m.from = e.PeerName;
                        else if (m.from == "") m.from = e.Peer;
                        if (m.firstLine == "") m.firstLine = PileFormat.FirstLine(e.Body);
                        if (m.srcPath == "") { m.srcPath = transcriptPath; m.srcOffset = e.Offset; }
                        Index(m);
                        return true;
                }
                return false;
            }
        }

        // Folds `other` into `keep`: the two were one message all along.
        void Merge(PileMessage keep, PileMessage other)
        {
            if (other.sendRef != "") { keep.srcPath = other.srcPath; keep.srcOffset = other.srcOffset; } // the sent record has the text as written
            if (keep.id == "") keep.id = other.id;
            if (keep.sendRef == "") keep.sendRef = other.sendRef;
            if (keep.recvRef == "") keep.recvRef = other.recvRef;
            if (keep.from == "") keep.from = other.from;
            if (keep.fromRepo == "") keep.fromRepo = other.fromRepo;
            if (keep.to == "" || (other.sendRef != "" && other.to != "" && !IsAddress(other.to))) keep.to = other.to; // the name the sender wrote
            if (keep.toRepo == "") keep.toRepo = other.toRepo;
            if (keep.summary == "") keep.summary = other.summary;
            if (keep.firstLine == "") keep.firstLine = other.firstLine;
            if (keep.sentAt == 0) keep.sentAt = other.sentAt;
            if (keep.receivedAt == 0) keep.receivedAt = other.receivedAt;
            keep.ticked |= other.ticked;
            keep.archived |= other.archived;
            state.messages.Remove(other);
            Reindex();
        }

        // Messages to a desktop-app session carry no shared id: pair a sent one with the oldest unpaired received one of the same text.
        bool PairByText()
        {
            bool any = false;
            var waiting = new Dictionary<string, List<PileMessage>>();
            foreach (var m in state.messages)
                if (m.id == "" && m.sendRef == "" && m.recvRef != "")
                {
                    if (!waiting.TryGetValue(m.bodyKey, out var list)) waiting[m.bodyKey] = list = new List<PileMessage>();
                    list.Add(m);
                }
            if (waiting.Count == 0) return false;
            foreach (var sent in state.messages.ToArray())
            {
                if (sent.id != "" || sent.sendRef == "" || sent.recvRef != "" || !waiting.TryGetValue(sent.bodyKey, out var list) || list.Count == 0) continue;
                var received = list[0];
                list.RemoveAt(0);
                // Here `sent` is kept: it has been on screen longer. It was addressed by id, so the names come from the receipt.
                if (received.to != "" && (sent.to == "" || IsAddress(sent.to))) sent.to = received.to;
                if (received.from != "") sent.from = received.from;
                Merge(sent, received);
                any = true;
            }
            return any;
        }

        // Reads what was appended to every transcript since the last pass. Returns Latest, which is replaced only when something changed.
        // Call it from one thread only. The lock is not held while a file is read, so a tick never waits on a gigabyte of transcript.
        public PileSnapshot Poll(long nowMs)
        {
            var found = new List<KeyValuePair<string, FileInfo>>(); // config directory, transcript
            foreach (var dir in settings.EffectiveConfigDirs(home))
                foreach (var path in SessionFiles.Transcripts(dir))
                    found.Add(new KeyValuePair<string, FileInfo>(dir, new FileInfo(path)));
            // Most recently written first, so on the first pass the newest messages show up first.
            found.Sort((a, b) => SafeTicks(b.Value).CompareTo(SafeTicks(a.Value)));

            bool changed = false, moved = false;
            var events = new List<MessageEvent>();
            foreach (var kv in found)
            {
                string path = kv.Value.FullName;
                long length, from;
                try { length = kv.Value.Length; }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { continue; }
                lock (gate)
                {
                    files.TryGetValue(path, out var known);
                    if (known != null && known.length == length) continue; // nothing appended
                    from = known != null ? known.offset : 0;
                }
                events.Clear();
                long offset = SessionFiles.ReadNewLines(path, from, Messages.Markers, (line, at) =>
                {
                    int first = events.Count;
                    Messages.Parse(line, events);
                    for (int i = first; i < events.Count; i++) events[i].Offset = at;
                });
                lock (gate)
                {
                    bool fileChanged = false;
                    foreach (var e in events) fileChanged |= Apply(e, path, kv.Key);
                    if (!files.TryGetValue(path, out var file))
                    {
                        files[path] = file = new FileState { path = path };
                        state.files.Add(file);
                    }
                    file.offset = offset;
                    file.length = length;
                    moved = true;
                    changed |= fileChanged;
                    if (fileChanged && !state.complete) Publish(nowMs, null); // the first pass is long: show what has been found so far
                }
            }
            lock (gate)
            {
                changed |= PairByText();
                if (!state.complete) { state.complete = true; changed = true; }
                if (changed) Publish(nowMs, null);
                // Offsets move every time any session writes a line; saving them once a minute is enough, since reading a stretch twice adds nothing.
                unsaved |= moved;
                if (changed || (unsaved && nowMs - lastSaveAt >= SaveOffsetsEveryMs)) Save(nowMs);
                return latest;
            }
        }

        static long SafeTicks(FileInfo f)
        {
            try { return f.LastWriteTimeUtc.Ticks; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { return 0; }
        }

        void Publish(long nowMs, string error)
        {
            var snap = new PileSnapshot { generatedAt = nowMs, reading = !state.complete };
            foreach (var m in state.messages)
            {
                if (m.archived) { snap.archived++; continue; }
                snap.rows.Add(new PileRow
                {
                    key = m.key, from = m.from, to = m.to, account = m.account, summary = m.summary, firstLine = m.firstLine, ticked = m.ticked,
                    fromRepoName = RepoName(m.fromRepo), toRepoName = RepoName(m.toRepo),
                    fromWorkspace = settings.WorkspaceFor(m.fromRepo), toWorkspace = settings.WorkspaceFor(m.toRepo),
                    at = m.sentAt > 0 ? m.sentAt : m.receivedAt,
                });
                var row = snap.rows[snap.rows.Count - 1];
                // Flagged only when both records were found: until then one of the two repos is a guess.
                row.crossWorkspace = m.sendRef != "" && m.recvRef != "" && row.fromWorkspace != row.toWorkspace;
            }
            snap.rows.Sort((a, b) => a.at != b.at ? b.at.CompareTo(a.at) : string.CompareOrdinal(a.key, b.key));
            if (error != null) snap.errors.Add(error);
            latest = snap;
        }

        static string RepoName(string root) => string.IsNullOrEmpty(root) ? "" : Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));

        // Publishes again from the same messages, for example after a repo moved to another workspace.
        public void Refresh()
        {
            lock (gate) Publish(latest.generatedAt, null);
        }

        public void SetTicked(string key, bool ticked) => SetTicked(new[] { key }, ticked);

        public void SetTicked(IEnumerable<string> keys, bool ticked)
        {
            lock (gate)
            {
                var wanted = new HashSet<string>(keys);
                foreach (var m in state.messages) if (wanted.Contains(m.key)) m.ticked = ticked;
                Save(lastSaveAt);
                Publish(latest.generatedAt, null);
            }
        }

        // Archives the ticked rows of this list. The agents' own messages are not touched: this class never writes outside its state file.
        public int Clear()
        {
            lock (gate)
            {
                int n = 0;
                foreach (var m in state.messages)
                    if (m.ticked && !m.archived) { m.archived = true; n++; }
                if (n == 0) return 0;
                Save(lastSaveAt);
                Publish(latest.generatedAt, null);
                return n;
            }
        }

        // The whole text of a message, read from the transcript now, or null when that record can no longer be read.
        public string ReadBody(string key)
        {
            string path, sendRef, recvRef;
            long offset;
            lock (gate)
            {
                var m = state.messages.Find(x => x.key == key);
                if (m == null || m.srcPath == "") return null;
                path = m.srcPath; offset = m.srcOffset; sendRef = m.sendRef; recvRef = m.recvRef;
            }
            var line = SessionFiles.ReadLineAt(path, offset);
            if (line == null) return null;
            var events = new List<MessageEvent>();
            Messages.Parse(line, events);
            foreach (var e in events)
                if ((e.Kind == MessageEventKind.Sent && e.Ref == sendRef) || (e.Kind == MessageEventKind.Received && e.Ref == recvRef)) return e.Body;
            return null; // the file was replaced and something else is at that place now
        }

        public void Start(int intervalMs = 2000)
        {
            if (thread != null) return;
            thread = new Thread(() =>
            {
                while (!stop.IsSet)
                {
                    long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    try { Poll(now); }
                    catch (Exception e)
                    {
                        // Never let the loop die: keep the rows, show the problem.
                        lock (gate) Publish(now, "messages: " + e.Message);
                    }
                    stop.Wait(intervalMs);
                }
            }) { IsBackground = true, Name = "TheGrid.Pile" };
            thread.Start();
        }

        public void Dispose()
        {
            stop.Set();
            if (thread != null && thread != Thread.CurrentThread) thread.Join(6000);
            thread = null;
            lock (gate) if (unsaved) Save(lastSaveAt);
        }
    }
}
