using System;
using System.Runtime.InteropServices;
using AOT;
using TheGrid.Core;
using UnityEngine;

namespace TheGrid
{
    // The program: a small bot standing on the macOS desktop, in a clear panel that never takes the keyboard, with a card
    // of sessions over it. Lit, it is the standing bot with a question badge over its head; dark, the bot switched off. What
    // the card says, where everything sits, what a click hits and when the card shows are ProgramCard's: this only draws
    // them and hands clicks back. Only the macOS player makes one.
    public sealed class DesktopProgram : MonoBehaviour
    {
        const float Times = 3, TextSize = 12;   // the figure's art pixel in points; the card's text
        static readonly Color Back = Hex(0x0c1024, .92f), Ink = Hex(0xe4ecff), Faint = Hex(0x8791c4), Clear = new Color(0, 0, 0, 0);

        delegate void MouseDownFn(IntPtr self, IntPtr sel, IntPtr evt);
        delegate byte AcceptsFirstMouseFn(IntPtr self, IntPtr sel, IntPtr evt);
        // AppKit holds only pointers to these, so the fields keep the delegates from being collected.
        static readonly MouseDownFn mouseDown = MouseDown;
        static readonly AcceptsFirstMouseFn acceptsFirstMouse = AcceptsFirstMouse;
        static Vector2? clickAt;   // where a click landed in the panel: set inside AppKit's callback, acted on in Update

        public event Action<ProgramRow> RowClicked;
        public event Action GridWanted, PinClicked;
        public string Family = "";   // the family of The Grid's own text; "" for the system's fixed-width font. Read in Start.

        IntPtr panel, figure, badge, back, pin, more, standing, off;
        readonly IntPtr[] badges = new IntPtr[ArtNames.Badges.Length];
        readonly IntPtr[] icons = new IntPtr[ProgramCard.MaxRows], dots = new IntPtr[ProgramCard.MaxRows],
            names = new IntPtr[ProgramCard.MaxRows], words = new IntPtr[ProgramCard.MaxRows];
        ProgramView view = new ProgramView(), drawnView;
        ProgramLayout layout;
        Rect screen;
        float screenDue;
        long nearAt;   // when the pointer was last near: once it has gone, when it left
        bool pinned, shown, dirty = true;

        public bool Pinned
        {
            get => pinned;
            set { if (value == pinned) return; pinned = value; dirty = true; }
        }

        public void Show(ProgramView next)
        {
            view = next;
            dirty = true;
        }

        // Unity's main thread is AppKit's, so the panel is made here.
        void Start()
        {
            screen = Cocoa.MainScreenVisible();
            screenDue = Time.unscaledTime + 1;
            layout = ProgramCard.Layout(screen, 0, false, false);
            var cls = Cocoa.ViewClass("TheGridProgramView", Marshal.GetFunctionPointerForDelegate(mouseDown), Marshal.GetFunctionPointerForDelegate(acceptsFirstMouse));
            panel = Cocoa.MakePanel(layout.frame, cls);
            standing = Picture("bot-stand-sw", Times);
            off = Picture("bot-off-sw", Times);
            for (int i = 1; i < badges.Length; i++) badges[i] = Picture(ArtNames.Badges[i], 1);
            figure = Cocoa.AddLayer(panel, layout.figure);
            badge = Cocoa.AddLayer(panel, layout.badge);
            Cocoa.SetImage(badge, Picture("badge-question", Times));
            back = Cocoa.AddBox(panel, Back, 8);
            pin = Cocoa.AddBox(panel, Clear, 0);
            Cocoa.SetBorder(pin, Ink, 1.5f);
            var font = Cocoa.Font(Family, TextSize);
            for (int i = 0; i < ProgramCard.MaxRows; i++)
            {
                icons[i] = Cocoa.AddLayer(panel, default);
                dots[i] = Cocoa.AddBox(panel, Faint, 3);
                names[i] = Cocoa.AddText(panel, font, TextSize, Ink, false);
                words[i] = Cocoa.AddText(panel, font, TextSize, Faint, true);
            }
            more = Cocoa.AddText(panel, font, TextSize, Faint, false);
            Draw();
            Cocoa.Show(panel);
        }

        // `points` to each pixel of the art, in whole screen pixels, so nothing is smoothed.
        static IntPtr Picture(string name, float points)
        {
            var t = Resources.Load<Sprite>("Art/" + name).texture;
            int times = Mathf.RoundToInt(points * Cocoa.BackingScale());
            return Cocoa.ImageFrom(ProgramCard.Scaled(t.GetPixels32(), t.width, t.height, times), t.width * times, t.height * times,
                new Vector2(t.width * points, t.height * points));
        }

        // Clicks are passed on here, on Unity's own time, never from inside AppKit's callback. The pointer is asked for once
        // a frame and the screen once a second; nothing is drawn unless the layout or the view changed.
        void Update()
        {
            if (panel == IntPtr.Zero) return;
            if (clickAt is Vector2 at)
            {
                clickAt = null;
                switch (ProgramCard.Hit(layout, at, out int row))   // against what is on the screen
                {
                    case ProgramHit.Row: RowClicked?.Invoke(drawnView.rows[row]); break;
                    case ProgramHit.Figure:
                    case ProgramHit.More: GridWanted?.Invoke(); break;
                    case ProgramHit.Pin: PinClicked?.Invoke(); break;
                }
            }
            if (Time.unscaledTime >= screenDue)
            {
                screenDue = Time.unscaledTime + 1;
                var visible = Cocoa.MainScreenVisible();
                if (visible != screen) { screen = visible; dirty = true; }
            }
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            bool near = ProgramCard.Near(layout, Cocoa.PointerOnScreen());
            if (near) nearAt = now;
            bool show = ProgramCard.Shows(pinned, view.rows.Count, near, now, nearAt);
            if (!dirty && show == shown) return;
            dirty = false;
            shown = show;
            layout = ProgramCard.Layout(screen, view.rows.Count, view.more > 0, shown);
            Draw();
        }

        void Draw()
        {
            drawnView = view;
            Cocoa.NoAnimation();
            Cocoa.Place(figure, layout.figure, true);
            Cocoa.SetImage(figure, view.lit ? standing : off);
            Cocoa.Place(badge, layout.badge, view.lit);
            bool card = layout.card.width > 0;
            Cocoa.Place(back, layout.card, card);
            Cocoa.Place(pin, ProgramCard.PinMark(layout.pin), card);
            Cocoa.SetFill(pin, pinned ? Ink : Clear);
            for (int i = 0; i < ProgramCard.MaxRows; i++)
            {
                bool on = i < layout.rows.Count;
                var r = on ? layout.rows[i] : default;
                var row = on ? view.rows[i] : null;
                bool pictured = on && row.badge != Badge.None;
                if (pictured) Cocoa.SetImage(icons[i], badges[(int)row.badge]);
                if (on)
                {
                    Cocoa.SetText(names[i], row.name);
                    Cocoa.SetText(words[i], row.word);
                }
                var parts = ProgramCard.Parts(r, on ? Cocoa.TextWidth(words[i]) : 0);
                Cocoa.Place(icons[i], parts.badge, pictured);
                Cocoa.Place(dots[i], parts.dot, on && !pictured);
                Cocoa.Place(words[i], parts.word, on);
                Cocoa.Place(names[i], parts.name, on);
            }
            bool hasMore = layout.more.width > 0;
            if (hasMore) Cocoa.SetText(more, "+" + view.more + " more");
            Cocoa.Place(more, ProgramCard.MoreLine(layout.more), hasMore);
            Cocoa.SetFrame(panel, layout.frame);
        }

        static Color Hex(int rgb, float alpha = 1) => new Color((rgb >> 16 & 255) / 255f, (rgb >> 8 & 255) / 255f, (rgb & 255) / 255f, alpha);

        void OnDestroy()
        {
            if (panel != IntPtr.Zero) Cocoa.Close(panel);
        }

        [MonoPInvokeCallback(typeof(MouseDownFn))]
        static void MouseDown(IntPtr self, IntPtr sel, IntPtr evt) => clickAt = Cocoa.InWindow(evt);

        // Yes: the first click on the panel is a click, though The Grid is not the active app.
        [MonoPInvokeCallback(typeof(AcceptsFirstMouseFn))]
        static byte AcceptsFirstMouse(IntPtr self, IntPtr sel, IntPtr evt) => 1;
    }
}
