using System;

namespace TheGrid.Core
{
    // How often a frame is drawn. Display sync stays on (see RosterApp.Start), so fewer frames are had by drawing only
    // every so many screen refreshes.
    public static class Pace
    {
        // Every how many refreshes of a screen that fast a frame is drawn, to draw about that many a second. Never less than every one.
        public static int Interval(double refreshHz, int framesPerSecond)
        {
            if (!(refreshHz >= 1 && refreshHz <= 1000)) refreshHz = 60;   // a screen that does not say how fast it is, or says nonsense
            return framesPerSecond <= 0 ? 1 : Math.Max(1, (int)Math.Round(refreshHz / framesPerSecond));
        }
    }
}
