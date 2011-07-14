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

		public string LayoutSymbol(int windowsCount)
		{
			return windowsCount == 0 ? "[M]" : "[" + windowsCount + "]";
		}

		public string LayoutName()
		{
			return "Full Screen";
		}

		public bool ShouldRestoreSharedWindowsPosition()
		{
			return false;
		}

		public void Reposition(IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
			windows.ForEach(w => MaximizeWindow(w, workingArea));
		}

		public void WindowTitlebarToggled(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
			MaximizeWindow(window, workingArea);
		}

		public void WindowBorderToggled(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
			MaximizeWindow(window, workingArea);
		}

		public void WindowMinimized(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
		}

		public void WindowRestored(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea)
		{
			MaximizeWindow(window, workingArea);
		}

		public void WindowCreated(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea, bool reLayout)
		{
			if (reLayout)
			{
				MaximizeWindow(window, workingArea);
			}
		}

		public void WindowDestroyed(Window window, IEnumerable<Window> windows, System.Drawing.Rectangle workingArea, bool reLayout)
		{
		}

		#endregion
	}
}
