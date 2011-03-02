using System.Collections.Generic;
using System.Drawing;

namespace Windawesome
{
	public interface ILayout
	{
		string LayoutSymbol(int windowsCount);

		string LayoutName();

		void Reposition(LinkedList<Window> windows, Rectangle workingArea);

		bool NeedsToSaveAndRestoreZOrder();

		void WindowTitlebarToggled(Window window, LinkedList<Window> windows, Rectangle workingArea);

		void WindowBorderToggled(Window window, LinkedList<Window> windows, Rectangle workingArea);

		void WindowMinimized(Window window, LinkedList<Window> windows, Rectangle workingArea);

		void WindowRestored(Window window, LinkedList<Window> windows, Rectangle workingArea);

		void WindowCreated(Window window, LinkedList<Window> windows, Rectangle workingArea, bool reLayout);

		void WindowDestroyed(Window window, LinkedList<Window> windows, Rectangle workingArea, bool reLayout);
	}
}
