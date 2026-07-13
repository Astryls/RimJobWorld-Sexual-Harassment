using RimWorld;
using Verse;

namespace RJWSexualHarassment
{
    /// <summary>
    /// Main-menu "pets" button worker. Instead of toggling a docked main-tab, this opens/closes the
    /// floating Harem window (draggable, resizable, non-pausing) so the dashboard floats free of the
    /// bottom menu bar. Press the button again (or the window's X / Esc) to close it.
    /// </summary>
    public class MainButtonWorker_Harem : MainButtonWorker
    {
        public override void Activate()
        {
            var stack = Find.WindowStack;
            var existing = stack.WindowOfType<Window_Harem>();
            if (existing != null) existing.Close();
            else stack.Add(new Window_Harem());
        }
    }
}
