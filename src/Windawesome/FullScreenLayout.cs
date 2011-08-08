using System;
using System.Collections.Generic;

namespace Windawesome
{
	public class FullScreenLayout : ILayout
	{
		private static void MaximizeWindow(Window window)
		{
			var ws = NativeMethods.GetWindowStyleLongPtr(window.hWnd);
			if (ws.HasFlag(NativeMethods.WS.WS_CAPTION | NativeMethods.WS.WS_MAXIMIZEBOX))
			{
				if (!NativeMethods.IsZoomed(window.hWnd))
				{
					// if there is a caption, we can make the window maximized
					// TODO: this activates the window which is not desirable. Is there a way NOT to?
					NativeMethods.ShowWindowAsync(window.hWnd, NativeMethods.SW.SW_SHOWMAXIMIZED);
				}
			}
			else
			{
				// otherwise, Windows would make the window "truly" full-screen, i.e. on top of all shell
				// windows, which doesn't work for us. So we just set the window to take the maximum possible area
				NativeMethods.ShowWindowAsync(window.hWnd, NativeMethods.SW.SW_SHOWNOACTIVATE); // should not use SW_RESTORE as it activates the window
				var workingArea = System.Windows.Forms.SystemInformation.WorkingArea;
				NativeMethods.SetWindowPos(window.hWnd, IntPtr.Zero,
					workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height,
					NativeMethods.SWP.SWP_ASYNCWINDOWPOS | NativeMethods.SWP.SWP_NOACTIVATE |
					NativeMethods.SWP.SWP_NOZORDER | NativeMethods.SWP.SWP_NOOWNERZORDER |
					NativeMethods.SWP.SWP_FRAMECHANGED | NativeMethods.SWP.SWP_NOCOPYBITS);
			}
		}

		#region ILayout Members

		string ILayout.LayoutSymbol(int windowsCount)
		{
			return windowsCount == 0 ? "[M]" : "[" + windowsCount + "]";
		}

		public string LayoutName()
		{
			return "Full Screen";
		}

		bool ILayout.ShouldRestoreSharedWindowsPosition()
		{
			return false;
		}

		void ILayout.Reposition(IEnumerable<Window> windows)
		{
			windows.ForEach(MaximizeWindow);
		}

		void ILayout.WindowTitlebarToggled(Window window, IEnumerable<Window> windows)
		{
			MaximizeWindow(window);
		}

		void ILayout.WindowBorderToggled(Window window, IEnumerable<Window> windows)
		{
			MaximizeWindow(window);
		}

		void ILayout.WindowMinimized(Window window, IEnumerable<Window> windows)
		{
		}

		void ILayout.WindowRestored(Window window, IEnumerable<Window> windows)
		{
			MaximizeWindow(window);
		}

		void ILayout.WindowCreated(Window window, IEnumerable<Window> windows, bool reLayout)
		{
			if (reLayout)
			{
				MaximizeWindow(window);
			}
		}

		void ILayout.WindowDestroyed(Window window, IEnumerable<Window> windows, bool reLayout)
		{
		}

		#endregion
	}
}
