using System.Collections.Generic;

namespace Windawesome
{
	public class FloatingLayout : ILayout
	{
		#region Layout Members

		public string LayoutSymbol(int windowsCount)
		{
			return "><>";
		}

		public string LayoutName()
		{
			return "Floating";
		}

		public void Reposition(LinkedList<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		public bool NeedsToSaveAndRestoreZOrder()
		{
			return true;
		}

		public void WindowTitlebarToggled(Window window, LinkedList<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		public void WindowBorderToggled(Window window, LinkedList<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		public void WindowMinimized(Window window, LinkedList<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		public void WindowRestored(Window window, LinkedList<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		public void WindowCreated(Window window, LinkedList<Window> windows, System.Drawing.Rectangle workingArea, bool reLayout)
		{
		}

		public void WindowDestroyed(Window window, LinkedList<Window> windows, System.Drawing.Rectangle workingArea, bool reLayout)
		{
		}

		#endregion
	}
}
