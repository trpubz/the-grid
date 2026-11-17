using System;
using System.Runtime.InteropServices;
using AOT;
using TheGrid.Core;
using UnityEngine;

namespace TheGrid
{
    // The program: a small bot standing on the macOS desktop, in a clear panel that never takes the keyboard. Lit, it is the
    // standing bot with a question badge over its head; dark, the bot switched off. Only the macOS player makes one.
    public sealed class DesktopProgram : MonoBehaviour
    {
        const float Wide = 120, High = 189, Margin = 16, Times = 3;
        static readonly Rect FigureAt = new Rect(0, 0, 120, 144), BadgeAt = new Rect(36, 129, 48, 60);   // points, from the bottom left

        delegate void MouseDownFn(IntPtr self, IntPtr sel, IntPtr evt);
        delegate byte AcceptsFirstMouseFn(IntPtr self, IntPtr sel, IntPtr evt);
        // AppKit holds only pointers to these, so the fields keep the delegates from being collected.
        static readonly MouseDownFn mouseDown = MouseDown;
        static readonly AcceptsFirstMouseFn acceptsFirstMouse = AcceptsFirstMouse;
        static bool clicked;   // set inside AppKit's callback, acted on in Update

        public event Action FigureClicked;

        IntPtr panel, figure, badge, standing, off, question;
        bool lit;

        public bool Lit
        {
            get => lit;
            set { if (value == lit) return; lit = value; Draw(); }
        }

        // Unity's main thread is AppKit's, so the panel is made here.
        void Start()
        {
            var screen = Cocoa.MainScreenVisible();
            var view = Cocoa.ViewClass("TheGridProgramView", Marshal.GetFunctionPointerForDelegate(mouseDown), Marshal.GetFunctionPointerForDelegate(acceptsFirstMouse));
            panel = Cocoa.MakePanel(new Rect(screen.xMax - Margin - Wide, screen.y + Margin, Wide, High), view);
            int times = Mathf.RoundToInt(Times * Cocoa.BackingScale());   // whole screen pixels for each pixel of the art
            standing = Picture("bot-stand-sw", times);
            off = Picture("bot-off-sw", times);
            question = Picture("badge-question", times);
            figure = Cocoa.AddLayer(panel, FigureAt);
            badge = Cocoa.AddLayer(panel, BadgeAt);
            Draw();
            Cocoa.Show(panel);
        }

        static IntPtr Picture(string name, int times)
        {
            var t = Resources.Load<Sprite>("Art/" + name).texture;
            return Cocoa.ImageFrom(ProgramCard.Scaled(t.GetPixels32(), t.width, t.height, times), t.width * times, t.height * times,
                new Vector2(t.width * Times, t.height * Times));
        }

        void Draw()
        {
            if (panel == IntPtr.Zero) return;
            Cocoa.SetImage(figure, lit ? standing : off);
            Cocoa.SetImage(badge, lit ? question : IntPtr.Zero);
        }

        // A click is passed on here, on Unity's own time, never from inside AppKit's callback.
        void Update()
        {
            if (!clicked) return;
            clicked = false;
            FigureClicked?.Invoke();
        }

        void OnDestroy()
        {
            if (panel != IntPtr.Zero) Cocoa.Close(panel);
        }

        [MonoPInvokeCallback(typeof(MouseDownFn))]
        static void MouseDown(IntPtr self, IntPtr sel, IntPtr evt) => clicked = true;

        // Yes: the first click on the panel is a click, though The Grid is not the active app.
        [MonoPInvokeCallback(typeof(AcceptsFirstMouseFn))]
        static byte AcceptsFirstMouse(IntPtr self, IntPtr sel, IntPtr evt) => 1;
    }
}
