using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace TheGrid
{
    // AppKit, reached through the Objective-C runtime. Calls only: what is shown, and when, is the shell's. Each objc_msgSend
    // is declared with its exact argument and return types, because on Apple silicon a loose or variadic one does not work.
    internal static class Cocoa
    {
        const string Objc = "/usr/lib/libobjc.A.dylib";

        [StructLayout(LayoutKind.Sequential)] struct Rect4 { public double x, y, w, h; }
        [StructLayout(LayoutKind.Sequential)] struct Pair { public double a, b; }   // an NSPoint or an NSSize

        [DllImport(Objc)] static extern IntPtr objc_getClass(string name);
        [DllImport(Objc)] static extern IntPtr sel_registerName(string name);
        [DllImport(Objc)] static extern IntPtr objc_allocateClassPair(IntPtr super, string name, IntPtr extra);
        [DllImport(Objc)] static extern void objc_registerClassPair(IntPtr cls);
        [DllImport(Objc)] [return: MarshalAs(UnmanagedType.I1)] static extern bool class_addMethod(IntPtr cls, IntPtr sel, IntPtr imp, string types);

        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr o, IntPtr s);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr o, IntPtr s, IntPtr a);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr o, IntPtr s, string a);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendLong(IntPtr o, IntPtr s, long a);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendBool(IntPtr o, IntPtr s, [MarshalAs(UnmanagedType.I1)] bool a);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendRect(IntPtr o, IntPtr s, Rect4 r);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendRectBool(IntPtr o, IntPtr s, Rect4 r, [MarshalAs(UnmanagedType.I1)] bool a);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendPair(IntPtr o, IntPtr s, Pair a);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern Rect4 GetRectInRegisters(IntPtr o, IntPtr s);
        [DllImport(Objc, EntryPoint = "objc_msgSend_stret")] static extern void GetRectInMemory(out Rect4 r, IntPtr o, IntPtr s);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern Pair GetPair(IntPtr o, IntPtr s);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern double GetDouble(IntPtr o, IntPtr s);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr InitWindow(IntPtr o, IntPtr s, Rect4 r, ulong style, ulong backing, [MarshalAs(UnmanagedType.I1)] bool defer);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr InitFrame(IntPtr o, IntPtr s, Rect4 r);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr InitSize(IntPtr o, IntPtr s, Pair size);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern void SendDouble(IntPtr o, IntPtr s, double a);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendColor(IntPtr o, IntPtr s, double r, double g, double b, double a);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendFamily(IntPtr o, IntPtr s, IntPtr family, ulong traits, long weight, double size);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendSizeWeight(IntPtr o, IntPtr s, double size, double weight);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr SendIndex(IntPtr o, IntPtr s, ulong i);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern ulong GetULong(IntPtr o, IntPtr s);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool GetBool(IntPtr o, IntPtr s);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] [return: MarshalAs(UnmanagedType.I1)] static extern bool SendOptions(IntPtr o, IntPtr s, ulong options);
        [DllImport(Objc, EntryPoint = "objc_msgSend")] static extern IntPtr InitBitmap(IntPtr o, IntPtr s, IntPtr planes, long wide, long high, long bitsPerSample,
            long samplesPerPixel, [MarshalAs(UnmanagedType.I1)] bool alpha, [MarshalAs(UnmanagedType.I1)] bool planar, IntPtr colorSpaceName, ulong format, long bytesPerRow, long bitsPerPixel);

        public static IntPtr Class(string name) => objc_getClass(name);
        public static IntPtr Sel(string name) => sel_registerName(name);
        public static IntPtr NSString(string text) => Send(Class("NSString"), Sel("stringWithUTF8String:"), text);

        // A struct larger than 16 bytes, such as an NSRect, is returned in memory on Intel, through objc_msgSend_stret; Apple
        // silicon returns it in registers and has no objc_msgSend_stret, so that entry point is looked up only on Intel.
        static Rect4 GetRect(IntPtr o, IntPtr s)
        {
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64) return GetRectInRegisters(o, s);
            GetRectInMemory(out var r, o, s);
            return r;
        }

        static Rect4 Of(Rect r) => new Rect4 { x = r.x, y = r.y, w = r.width, h = r.height };

        // A view class made at run time from NSView, whose mouseDown: and acceptsFirstMouse: are the given C# functions. A class
        // name can be registered only once a process, so a second call gets the first class.
        public static IntPtr ViewClass(string name, IntPtr mouseDown, IntPtr acceptsFirstMouse)
        {
            var cls = objc_getClass(name);
            if (cls != IntPtr.Zero) return cls;
            cls = objc_allocateClassPair(Class("NSView"), name, IntPtr.Zero);
            class_addMethod(cls, Sel("mouseDown:"), mouseDown, "v@:@");
            class_addMethod(cls, Sel("acceptsFirstMouse:"), acceptsFirstMouse, "c@:@");
            objc_registerClassPair(cls);
            return cls;
        }

        // The window recipe that was tested on this Mac: borderless and non-activating, floating, on every Space, stationary, out
        // of the window cycle and allowed over full-screen apps; not hidden when The Grid is not in front; clear, no shadow. Its
        // content is a layer-backed view of `viewClass`. Not shown until Show.
        public static IntPtr MakePanel(Rect frame, IntPtr viewClass)
        {
            var panel = InitWindow(Send(Class("NSPanel"), Sel("alloc")), Sel("initWithContentRect:styleMask:backing:defer:"), Of(frame), 0 | 1 << 7, 2, false);
            SendLong(panel, Sel("setLevel:"), 3);
            SendLong(panel, Sel("setCollectionBehavior:"), 1 | 16 | 64 | 256);
            SendBool(panel, Sel("setHidesOnDeactivate:"), false);
            SendBool(panel, Sel("setCanHide:"), false);   // Cmd-H must not take the bot away
            SendBool(panel, Sel("setFloatingPanel:"), true);
            SendBool(panel, Sel("setBecomesKeyOnlyIfNeeded:"), true);
            SendBool(panel, Sel("setOpaque:"), false);
            Send(panel, Sel("setBackgroundColor:"), Send(Class("NSColor"), Sel("clearColor")));
            SendBool(panel, Sel("setHasShadow:"), false);
            var view = InitFrame(Send(viewClass, Sel("alloc")), Sel("initWithFrame:"), new Rect4 { w = frame.width, h = frame.height });
            SendBool(view, Sel("setWantsLayer:"), true);
            Send(panel, Sel("setContentView:"), view);
            return panel;
        }

        // In front without making The Grid the active app.
        public static void Show(IntPtr panel) => Send(panel, Sel("orderFrontRegardless"));

        public static void Close(IntPtr panel) => Send(panel, Sel("close"));

        public static void SetFrame(IntPtr panel, Rect frame) => SendRectBool(panel, Sel("setFrame:display:"), Of(frame), true);

        // A layer over the panel's view, at `at` in points from its bottom left corner. Layers, not views, so every click is the view's.
        public static IntPtr AddLayer(IntPtr panel, Rect at)
        {
            var layer = Send(Class("CALayer"), Sel("layer"));
            SendRect(layer, Sel("setFrame:"), Of(at));
            Send(layer, Sel("setMagnificationFilter:"), NSString("nearest"));
            return Attach(panel, layer);
        }

        static IntPtr Attach(IntPtr panel, IntPtr layer)
        {
            Send(Send(Send(panel, Sel("contentView")), Sel("layer")), Sel("addSublayer:"), layer);   // the view's layer keeps it
            return layer;
        }

        // A plain layer of one colour, its corners rounded.
        public static IntPtr AddBox(IntPtr panel, Color fill, float radius)
        {
            var layer = Send(Class("CALayer"), Sel("layer"));
            SetFill(layer, fill);
            SendDouble(layer, Sel("setCornerRadius:"), radius);
            return Attach(panel, layer);
        }

        public static void SetFill(IntPtr layer, Color fill) => Send(layer, Sel("setBackgroundColor:"), CGColor(fill));

        public static void SetBorder(IntPtr layer, Color colour, float width)
        {
            Send(layer, Sel("setBorderColor:"), CGColor(colour));
            SendDouble(layer, Sel("setBorderWidth:"), width);
        }

        // One line of text, cut with an ellipsis where it does not fit, drawn at the screen's own pixels so it is sharp.
        public static IntPtr AddText(IntPtr panel, IntPtr font, float size, Color colour, bool right)
        {
            var layer = Send(Class("CATextLayer"), Sel("layer"));
            Send(layer, Sel("setFont:"), font);
            SendDouble(layer, Sel("setFontSize:"), size);
            Send(layer, Sel("setForegroundColor:"), CGColor(colour));
            Send(layer, Sel("setAlignmentMode:"), NSString(right ? "right" : "left"));
            Send(layer, Sel("setTruncationMode:"), NSString("end"));
            SendDouble(layer, Sel("setContentsScale:"), BackingScale());
            return Attach(panel, layer);
        }

        public static void SetText(IntPtr layer, string text) => Send(layer, Sel("setString:"), NSString(text));

        // How wide the text layer's line is, in points.
        public static float TextWidth(IntPtr layer) => (float)GetPair(layer, Sel("preferredFrameSize")).a;

        // The family's regular face; the system's fixed-width font when AppKit has no family by that name. Use it at once:
        // nothing keeps it but the layers given it.
        public static IntPtr Font(string family, float size)
        {
            var font = family == "" ? IntPtr.Zero
                : SendFamily(Send(Class("NSFontManager"), Sel("sharedFontManager")), Sel("fontWithFamily:traits:weight:size:"), NSString(family), 0, 5, size);
            return font != IntPtr.Zero ? font : SendSizeWeight(Class("NSFont"), Sel("monospacedSystemFontOfSize:weight:"), size, 0);
        }

        static IntPtr CGColor(Color c) => Send(SendColor(Class("NSColor"), Sel("colorWithSRGBRed:green:blue:alpha:"), c.r, c.g, c.b, c.a), Sel("CGColor"));

        // Core Animation fades every change to a layer; none, for what is changed until this pass is on the screen.
        public static void NoAnimation() => SendBool(Class("CATransaction"), Sel("setDisableActions:"), true);

        public static void Place(IntPtr layer, Rect at, bool shown)
        {
            SendRect(layer, Sel("setFrame:"), Of(at));
            SendBool(layer, Sel("setHidden:"), !shown);
        }

        // Where a mouse event happened in its window, in points from the bottom left.
        public static Vector2 InWindow(IntPtr evt)
        {
            var p = GetPair(evt, Sel("locationInWindow"));
            return new Vector2((float)p.a, (float)p.b);
        }

        // The running app with this bundle id, in front. False when none runs or macOS turns the request down.
        public static bool BringForward(string bundleId)
        {
            var app = Send(Send(Class("NSRunningApplication"), Sel("runningApplicationsWithBundleIdentifier:"), NSString(bundleId)), Sel("firstObject"));
            return app != IntPtr.Zero && SendOptions(app, Sel("activateWithOptions:"), 2);   // NSApplicationActivateIgnoringOtherApps
        }

        // The Grid in front, its window back from the Dock if it was minimised.
        public static void BringGridForward()
        {
            var app = Send(Class("NSApplication"), Sel("sharedApplication"));
            var windows = Send(app, Sel("windows"));
            for (ulong i = 0, n = GetULong(windows, Sel("count")); i < n; i++)
            {
                var window = SendIndex(windows, Sel("objectAtIndex:"), i);
                if (GetBool(window, Sel("isMiniaturized"))) Send(window, Sel("deminiaturize:"), IntPtr.Zero);
            }
            SendBool(app, Sel("activateIgnoringOtherApps:"), true);
        }

        // Shows the image in the layer at once, without Core Animation's fade; no image shows nothing.
        public static void SetImage(IntPtr layer, IntPtr image)
        {
            SendBool(Class("CATransaction"), Sel("setDisableActions:"), true);
            Send(layer, Sel("setContents:"), image);
        }

        // An NSImage of exactly these pixels, rows from the top, `points` in size, so nothing is smoothed. Every pixel is opaque
        // or clear with no colour, so the bytes are already premultiplied, as AppKit wants them.
        public static IntPtr ImageFrom(Color32[] rgba, int width, int height, Vector2 points)
        {
            var rep = InitBitmap(Send(Class("NSBitmapImageRep"), Sel("alloc")),
                Sel("initWithBitmapDataPlanes:pixelsWide:pixelsHigh:bitsPerSample:samplesPerPixel:hasAlpha:isPlanar:colorSpaceName:bitmapFormat:bytesPerRow:bitsPerPixel:"),
                IntPtr.Zero, width, height, 8, 4, true, false, NSString("NSDeviceRGBColorSpace"), 0, width * 4, 32);
            var bytes = new byte[rgba.Length * 4];
            for (int i = 0; i < rgba.Length; i++)
            {
                bytes[i * 4] = rgba[i].r;
                bytes[i * 4 + 1] = rgba[i].g;
                bytes[i * 4 + 2] = rgba[i].b;
                bytes[i * 4 + 3] = rgba[i].a;
            }
            Marshal.Copy(bytes, 0, Send(rep, Sel("bitmapData")), bytes.Length);
            // The art's colours are sRGB. The first rep is never released: the retagged one may share its bytes.
            rep = Send(rep, Sel("bitmapImageRepByRetaggingWithColorSpace:"), Send(Class("NSColorSpace"), Sel("sRGBColorSpace")));
            var size = new Pair { a = points.x, b = points.y };
            SendPair(rep, Sel("setSize:"), size);
            var image = InitSize(Send(Class("NSImage"), Sel("alloc")), Sel("initWithSize:"), size);
            Send(image, Sel("addRepresentation:"), rep);
            return image;
        }

        public static Vector2 PointerOnScreen()
        {
            var p = GetPair(Class("NSEvent"), Sel("mouseLocation"));
            return new Vector2((float)p.a, (float)p.b);
        }

        // The main screen less the menu bar and the Dock, in points from the bottom left of the screen that has the menu bar.
        public static Rect MainScreenVisible()
        {
            var r = GetRect(Send(Class("NSScreen"), Sel("mainScreen")), Sel("visibleFrame"));
            return new Rect((float)r.x, (float)r.y, (float)r.w, (float)r.h);
        }

        public static float BackingScale() => (float)GetDouble(Send(Class("NSScreen"), Sel("mainScreen")), Sel("backingScaleFactor"));
    }
}
