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

		public void Reposition(IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		public void WindowTitlebarToggled(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		public void WindowBorderToggled(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		public void WindowMinimized(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		public void WindowRestored(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		public void WindowCreated(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea, bool reLayout)
		{
		}

		public void WindowDestroyed(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea, bool reLayout)
		{
		}

		#endregion
	}
}
