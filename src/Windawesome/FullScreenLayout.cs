using System;
using System.Collections.Generic;

namespace Windawesome
{
	public class FullScreenLayout : ILayout
	{
		private static void MaximizeWindow(Window window, System.Drawing.Rectangle workingArea)
		{
			var ws = NativeMethods.GetWindowStyleLongPtr(window.hWnd);
			if (ws.HasFlag(NativeMethods.WS.WS_CAPTION) && ws.HasFlag(NativeMethods.WS.WS_MAXIMIZEBOX))
			{
				if (!ws.HasFlag(NativeMethods.WS.WS_MAXIMIZE))
				{
					// if there is a caption, we can make the window maximized
					NativeMethods.ShowWindowAsync(window.hWnd, NativeMethods.SW.SW_SHOWMAXIMIZED);
				}
			}
			else
			{
				// otherwise, Windows would make the window "truly" full-screen, i.e. on top of all shell
				// windows, which doesn't work for us. So we just set window to take the maximum possible area
				NativeMethods.ShowWindowAsync(window.hWnd, NativeMethods.SW.SW_RESTORE);
				NativeMethods.SetWindowPos(window.hWnd, IntPtr.Zero,
					workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height,
					NativeMethods.SWP.SWP_ASYNCWINDOWPOS | NativeMethods.SWP.SWP_NOACTIVATE |
					NativeMethods.SWP.SWP_NOZORDER | NativeMethods.SWP.SWP_NOOWNERZORDER |
					NativeMethods.SWP.SWP_FRAMECHANGED | NativeMethods.SWP.SWP_NOCOPYBITS);
			}
		}

		#region Layout Members

		string ILayout.LayoutSymbol(int windowsCount)
		{
			return windowsCount == 0 ? "[M]" : "[" + windowsCount + "]";
		}

		string ILayout.LayoutName()
		{
			return "Full Screen";
		}

		bool ILayout.ShouldRestoreSharedWindowsPosition()
		{
			return false;
		}

		void ILayout.Reposition(IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
			windows.ForEach(w => MaximizeWindow(w, workingArea));
		}

		void ILayout.WindowTitlebarToggled(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
			MaximizeWindow(window, workingArea);
		}

		void ILayout.WindowBorderToggled(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
			MaximizeWindow(window, workingArea);
		}

		void ILayout.WindowMinimized(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		void ILayout.WindowRestored(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
			MaximizeWindow(window, workingArea);
		}

		void ILayout.WindowCreated(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea, bool reLayout)
		{
			if (reLayout)
			{
				MaximizeWindow(window, workingArea);
			}
		}

		void ILayout.WindowDestroyed(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea, bool reLayout)
		{
		}

		#endregion
	}
}
