using System.Collections.Generic;

namespace Windawesome
{
	public class FloatingLayout : ILayout
	{
		#region ILayout Members

		string ILayout.LayoutSymbol(int windowsCount)
		{
			return "><>";
		}

		public string LayoutName()
		{
			return "Floating";
		}

		bool ILayout.ShouldRestoreSharedWindowsPosition()
		{
			return true;
		}

		void ILayout.Reposition(IEnumerable<Window> windows)
		{
		}

		void ILayout.WindowTitlebarToggled(Window window, IEnumerable<Window> windows)
		{
		}

		void ILayout.WindowBorderToggled(Window window, IEnumerable<Window> windows)
		{
		}

		void ILayout.WindowMinimized(Window window, IEnumerable<Window> windows)
		{
		}

		void ILayout.WindowRestored(Window window, IEnumerable<Window> windows)
		{
		}

		void ILayout.WindowCreated(Window window, IEnumerable<Window> windows, bool reLayout)
		{
		}

		void ILayout.WindowDestroyed(Window window, IEnumerable<Window> windows, bool reLayout)
		{
		}

		#endregion
	}
}
