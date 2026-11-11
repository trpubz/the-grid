using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace TheGrid.Core
{
    public enum MessageEventKind { Sent, SendResult, Received, SessionName }

    // One fact about a message between sessions, read from one transcript line.
    public sealed class MessageEvent
    {
        public MessageEventKind Kind;
        public string Ref = "";      // Sent and SendResult: the tool call id. Received: the record uuid
        public string MsgId = "";    // SendResult and Received; empty when the records carry none
        public string Peer = "";     // Sent: `to` as written. Received: `from`
        public string PeerName = ""; // Received: the sender's name
        public string Body = "", Summary = "", Cwd = "", Name = ""; // Name: SessionName only
        public long At;              // record timestamp, ms since 1970 UTC; 0 when unreadable
        public bool OwnSubagent;     // Sent with recipient_kind "agent", or SendResult with a pin
        public bool IsTitle;         // SessionName from ai-title: used only when no other name is known
        public long Offset;          // byte offset of the record's line; set by the reader, not by Parse
    }

    // How Claude Code records messages between sessions; see docs/spike-idle-detection.md, "Messages between sessions".
    public static class Messages
    {
        // A line holding none of these cannot be a record Parse reads. Text inside a message is JSON-escaped (\"peer\"), so it never matches.
        public static readonly string[] Markers = { "\"SendMessage\"", "\"peer\"", "\"msg_id\"", "\"pin\"", "\"agent-name\"", "\"custom-title\"", "\"ai-title\"" };

        // JsonUtility matches fields by exact name and skips a field whose JSON type differs, which these records rely on:
        // `message.content` is a list on most records and a string on others, `toolUseResult` an object or a string.
        [Serializable] sealed class Rec { public string type, uuid, timestamp, cwd, agentName, customTitle, aiTitle; public Msg message; public Origin origin; public Attachment attachment; public Result toolUseResult; }
        [Serializable] sealed class Msg { public List<Block> content; }
        [Serializable] sealed class Block { public string type, id, name, tool_use_id; public Input input; }
        [Serializable] sealed class Input { public string to, message, summary, recipient_kind; }
        [Serializable] sealed class Origin { public string kind, from, name, msg_id, body; public bool handback, hostInjected; }
        [Serializable] sealed class Attachment { public string type; public Origin origin; }
        [Serializable] sealed class Result { public string msg_id; public Pin pin; }
        [Serializable] sealed class Pin { public string id; }
        [Serializable] sealed class TextRec { public TextMsg message; }
        [Serializable] sealed class TextMsg { public string content; }

        // The desktop app delivers a message as text: <cross-session-message from="…" name="…">, the body, a closing tag, then advice to the reader.
        static readonly Regex Wrapper = new Regex("<cross-session-message\\b[^>]*?\\bname=\"([^\"]*)\"[^>]*>\\n?(.*?)\\n?</cross-session-message>", RegexOptions.Singleline);

        // Adds nothing for a line that is not valid JSON or not a message record.
        public static void Parse(string line, List<MessageEvent> into)
        {
            Rec r;
            try { r = JsonUtility.FromJson<Rec>(line); }
            catch (ArgumentException) { return; }
            if (r == null) return;
            switch (r.type)
            {
                case "agent-name": AddName(into, r.agentName, false); break;
                case "custom-title": AddName(into, r.customTitle, false); break;
                case "ai-title": AddName(into, r.aiTitle, true); break;
                case "assistant":
                    foreach (var b in Blocks(r))
                        if (b.type == "tool_use" && b.name == "SendMessage" && !string.IsNullOrEmpty(b.id) && b.input != null)
                            into.Add(new MessageEvent
                            {
                                Kind = MessageEventKind.Sent, Ref = b.id, Peer = b.input.to ?? "", Body = b.input.message ?? "", Summary = b.input.summary ?? "",
                                Cwd = r.cwd ?? "", At = Millis(r.timestamp), OwnSubagent = b.input.recipient_kind == "agent",
                            });
                    break;
                case "user":
                    // A send to the sender's own subagent is answered with a pin; a send to another session with a message id.
                    bool pinned = r.toolUseResult != null && r.toolUseResult.pin != null && !string.IsNullOrEmpty(r.toolUseResult.pin.id);
                    string msgId = r.toolUseResult != null ? r.toolUseResult.msg_id ?? "" : "";
                    if (pinned || msgId != "")
                        foreach (var b in Blocks(r))
                            if (b.type == "tool_result" && !string.IsNullOrEmpty(b.tool_use_id))
                                into.Add(new MessageEvent { Kind = MessageEventKind.SendResult, Ref = b.tool_use_id, MsgId = msgId, OwnSubagent = pinned, At = Millis(r.timestamp) });
                    AddReceived(into, r, r.origin, line);
                    break;
                case "attachment": // a message that arrived while the recipient was mid-turn
                    if (r.attachment != null && r.attachment.type == "queued_command") AddReceived(into, r, r.attachment.origin, line);
                    break;
            }
        }

        static List<Block> Blocks(Rec r) => r.message != null && r.message.content != null ? r.message.content : new List<Block>();

        static void AddName(List<MessageEvent> into, string name, bool isTitle)
        {
            if (!string.IsNullOrEmpty(name)) into.Add(new MessageEvent { Kind = MessageEventKind.SessionName, Name = name, IsTitle = isTitle });
        }

        static void AddReceived(List<MessageEvent> into, Rec r, Origin o, string line)
        {
            // handback: a subagent reporting to the session that started it, which is not a message between sessions.
            if (o == null || o.kind != "peer" || o.handback || string.IsNullOrEmpty(r.uuid)) return;
            var e = new MessageEvent
            {
                Kind = MessageEventKind.Received, Ref = r.uuid, MsgId = o.msg_id ?? "", Peer = o.from ?? "", PeerName = o.name ?? "", Body = o.body ?? "",
                Cwd = r.cwd ?? "", At = Millis(r.timestamp),
            };
            if (o.hostInjected)
            {
                TextRec t;
                try { t = JsonUtility.FromJson<TextRec>(line); }
                catch (ArgumentException) { return; }
                var m = Wrapper.Match(t != null && t.message != null ? t.message.content ?? "" : "");
                if (!m.Success) return;
                e.PeerName = m.Groups[1].Value;
                e.Body = m.Groups[2].Value;
            }
            into.Add(e);
        }

        static long Millis(string timestamp) =>
            DateTimeOffset.TryParse(timestamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var t) ? t.ToUnixTimeMilliseconds() : 0;
    }
}
