using System.Collections.Generic;
using System.Drawing;

namespace Windawesome
{
	public interface ILayout
	{
		string LayoutSymbol(int windowsCount);

		string LayoutName();

		/*
		 * Should return whether Windawesome should restore a shared window's position if there
		 * are no changes to the workspace. If there are, Reposition will be called anyway
		 */
		bool ShouldRestoreSharedWindowsPosition();

		void Reposition(IEnumerable<Window> windows);

		void WindowTitlebarToggled(Window window, IEnumerable<Window> windows);

		void WindowBorderToggled(Window window, IEnumerable<Window> windows);

		void WindowMinimized(Window window, IEnumerable<Window> windows);

		void WindowRestored(Window window, IEnumerable<Window> windows);

		void WindowCreated(Window window, IEnumerable<Window> windows, bool reLayout);

		void WindowDestroyed(Window window, IEnumerable<Window> windows, bool reLayout);
	}
}
