using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace TheGrid.Core
{
    [Serializable] public sealed class SubagentRow { public string agentId, description, agentType; }

    // Subagents live beside their parent's transcript: <transcript without .jsonl>/subagents/agent-<id>.meta.json and .jsonl.
    public static class Subagents
    {
        [Serializable] sealed class Meta { public string description, agentType; public bool finished; }

        // Active means not marked finished and its log changed within maxAgeMs.
        // shortcut: only the top level is read; subagents started by workflows sit one folder deeper and are not listed. Walk subfolders if they should be.
        public static List<SubagentRow> ReadActive(string transcriptPath, long nowMs, long maxAgeMs = 600000)
        {
            var rows = new List<SubagentRow>();
            if (string.IsNullOrEmpty(transcriptPath) || !transcriptPath.EndsWith(".jsonl", StringComparison.Ordinal)) return rows;
            var dir = Path.Combine(transcriptPath.Substring(0, transcriptPath.Length - ".jsonl".Length), "subagents");
            try
            {
                if (!Directory.Exists(dir)) return rows;
                var metas = Directory.GetFiles(dir, "agent-*.meta.json");
                Array.Sort(metas, StringComparer.Ordinal);
                foreach (var metaPath in metas)
                {
                    var name = Path.GetFileName(metaPath);
                    var id = name.Substring("agent-".Length, name.Length - "agent-".Length - ".meta.json".Length);
                    var log = Path.Combine(dir, "agent-" + id + ".jsonl");
                    if (!File.Exists(log)) continue;
                    long changed = new DateTimeOffset(File.GetLastWriteTimeUtc(log)).ToUnixTimeMilliseconds();
                    if (nowMs - changed > maxAgeMs) continue;
                    Meta m;
                    try { m = JsonUtility.FromJson<Meta>(File.ReadAllText(metaPath)); }
                    catch (ArgumentException) { continue; }
                    if (m == null || m.finished) continue;
                    rows.Add(new SubagentRow { agentId = id, description = m.description ?? "", agentType = m.agentType ?? "" });
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException) { }
            return rows;
        }
    }
}
