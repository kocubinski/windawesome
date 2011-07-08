using System.Collections.Generic;
using System.Drawing;

namespace Windawesome
{
	public interface ILayout
	{
		string LayoutSymbol(int windowsCount);

		string LayoutName();

		void Reposition(IEnumerable<Window> windows, Rectangle workingArea);

		void WindowTitlebarToggled(Window window, IEnumerable<Window> windows, Rectangle workingArea);

		void WindowBorderToggled(Window window, IEnumerable<Window> windows, Rectangle workingArea);

		void WindowMinimized(Window window, IEnumerable<Window> windows, Rectangle workingArea);

		void WindowRestored(Window window, IEnumerable<Window> windows, Rectangle workingArea);

		void WindowCreated(Window window, IEnumerable<Window> windows, Rectangle workingArea, bool reLayout);

		void WindowDestroyed(Window window, IEnumerable<Window> windows, Rectangle workingArea, bool reLayout);
	}
}
