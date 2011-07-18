using System.Collections.Generic;

namespace Windawesome
{
	public class FloatingLayout : ILayout
	{
		#region Layout Members

		string ILayout.LayoutSymbol(int windowsCount)
		{
			return "><>";
		}

		string ILayout.LayoutName()
		{
			return "Floating";
		}

		bool ILayout.ShouldRestoreSharedWindowsPosition()
		{
			return true;
		}

		void ILayout.Reposition(IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		void ILayout.WindowTitlebarToggled(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		void ILayout.WindowBorderToggled(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		void ILayout.WindowMinimized(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		void ILayout.WindowRestored(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		void ILayout.WindowCreated(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea, bool reLayout)
		{
		}

		void ILayout.WindowDestroyed(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea, bool reLayout)
		{
		}

		#endregion
	}
}
