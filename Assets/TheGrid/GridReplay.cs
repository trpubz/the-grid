using System;
using System.Collections.Generic;
using UnityEngine;

namespace TheGrid
{
    // Offline fixture viewer. Enable it by adding this component to an empty scene.
    public sealed class GridReplay : MonoBehaviour
    {
        [Serializable] public class Actor { public string id, owner, harness, provider; }
        [Serializable] public class Entry { public int seq; public string id, type, actor, target, messageId, replyTo, value; }
        [Serializable] public class Recording { public int version; public string roomId; public Actor[] actors; public Entry[] events; }
        private Recording recording;
        private readonly Dictionary<string, Transform> bots = new Dictionary<string, Transform>();
        private readonly Dictionary<string, string> activity = new Dictionary<string, string>();
        private readonly HashSet<string> offline = new HashSet<string>();
        private Camera view;
        private int index;
        private string detail = "Select Next event to step through the recording.";
        private Transform pulse;
        private Vector3 start, end;
        private float sentAt;

        void Start()
        {
            recording = JsonUtility.FromJson<Recording>(Resources.Load<TextAsset>("grid-demo").text);
            view = Camera.main;
            if (view == null) view = new GameObject("Grid Camera").AddComponent<Camera>();
            view.transform.position = new Vector3(0, 6, -9);
            view.transform.LookAt(Vector3.zero);
            view.clearFlags = CameraClearFlags.SolidColor;
            view.backgroundColor = new Color(.035f, .055f, .085f);
            view.orthographic = true;
            view.orthographicSize = 4;
            for (int i = 0; i < recording.actors.Length; i++)
            {
                Actor actor = recording.actors[i];
                Transform bot = GameObject.CreatePrimitive(PrimitiveType.Capsule).transform;
                bot.name = actor.owner + " / " + actor.harness;
                bot.position = new Vector3(i * 4 - 2, 0, 0);
                bots.Add(actor.id, bot);
                activity.Add(actor.id, "unknown");
            }
            pulse = GameObject.CreatePrimitive(PrimitiveType.Sphere).transform;
            pulse.localScale = Vector3.one * .22f;
            pulse.gameObject.SetActive(false);
        }

        void Update()
        {
            if (pulse == null || !pulse.gameObject.activeSelf) return;
            float t = (Time.unscaledTime - sentAt) / .8f;
            pulse.position = Vector3.Lerp(start, end, t);
            if (t >= 1) pulse.gameObject.SetActive(false);
        }

        void Step()
        {
            Entry e = recording.events[index++];
            detail = e.seq + "   " + e.type + "   " + (e.value ?? e.messageId);
            switch (e.type)
            {
                case "activity.changed": activity[e.actor] = e.value; break;
                case "presence.changed":
                    if (e.value == "offline") offline.Add(e.actor);
                    else offline.Remove(e.actor);
                    break;
                case "message.delivered":
                    // For an ACK, actor is the recipient and target is the original sender.
                    start = bots[e.target].position;
                    end = bots[e.actor].position;
                    sentAt = Time.unscaledTime;
                    pulse.position = start;
                    pulse.gameObject.SetActive(true);
                    break;
            }
        }

        void OnGUI()
        {
            if (recording == null) return;
            GUILayout.BeginArea(new Rect(16, 16, Screen.width - 32, 135), GUI.skin.box);
            GUILayout.Label("The Grid    /    " + recording.roomId + "    /    Offline replay");
            GUILayout.Label(detail);
            if (index < recording.events.Length)
            {
                if (GUILayout.Button("Next event", GUILayout.Width(125))) Step();
            }
            else if (GUILayout.Button("Replay", GUILayout.Width(125)))
            {
                index = 0;
                offline.Clear();
                foreach (Actor a in recording.actors) activity[a.id] = "unknown";
                detail = "Recording reset.";
                pulse.gameObject.SetActive(false);
            }
            GUILayout.EndArea();
            foreach (Actor a in recording.actors)
            {
                Vector3 p = view.WorldToScreenPoint(bots[a.id].position - Vector3.up);
                GUI.Box(new Rect(p.x - 90, Screen.height - p.y, 180, 50),
                    a.owner + " / " + a.harness + "\n" + (offline.Contains(a.id) ? "offline" : activity[a.id]));
            }
        }
    }
}
