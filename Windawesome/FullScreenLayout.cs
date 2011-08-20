using System;
using System.Collections.Generic;
using System.Drawing;

namespace Windawesome
{
	public class FullScreenLayout : ILayout
	{
		private Workspace workspace;

		private void MaximizeWindow(Window window)
		{
			var bounds = workspace.Monitor.screen.Bounds;
			if (NativeMethods.IsZoomed(window.hWnd) && System.Windows.Forms.Screen.FromHandle(window.hWnd).Bounds != bounds)
			{
				// restore if program is maximized and should be on a different monitor
				NativeMethods.ShowWindowAsync(window.hWnd, NativeMethods.SW.SW_SHOWNOACTIVATE); // should not use SW_RESTORE as it activates the window
				System.Threading.Thread.Sleep(Workspace.minimizeRestoreDelay);
			}

			var workingArea = workspace.Monitor.screen.WorkingArea;
			var winPlacement = NativeMethods.WINDOWPLACEMENT.Default;

			winPlacement.Flags = NativeMethods.WPF.WPF_ASYNCWINDOWPLACEMENT;
			winPlacement.NormalPosition.left = bounds.Left; // these are in working area coordinates
			winPlacement.NormalPosition.right = bounds.Left + workingArea.Width;
			winPlacement.NormalPosition.top = bounds.Top;
			winPlacement.NormalPosition.bottom = bounds.Top + workingArea.Height;

			winPlacement.MaxPosition.X = bounds.X;
			winPlacement.MaxPosition.Y = bounds.Y;

			var ws = NativeMethods.GetWindowStyleLongPtr(window.hWnd);
			winPlacement.ShowCmd = ws.HasFlag(NativeMethods.WS.WS_CAPTION | NativeMethods.WS.WS_MAXIMIZEBOX) ?
				NativeMethods.SW.SW_SHOWMAXIMIZED :
				NativeMethods.SW.SW_SHOWNOACTIVATE;

			NativeMethods.SetWindowPlacement(window.hWnd, ref winPlacement);
		}

		#region ILayout Members

		string ILayout.LayoutSymbol()
		{
			return workspace.GetWindowsCount() == 0 ? "[M]" : "[" + workspace.GetWindowsCount() + "]";
		}

		public string LayoutName()
		{
			return "Full Screen";
		}

		void ILayout.Initialize(Workspace workspace)
		{
			this.workspace = workspace;

			workspace.WindowTitlebarToggled += MaximizeWindow;
			workspace.WindowBorderToggled += MaximizeWindow;
		}

		bool ILayout.ShouldSaveAndRestoreSharedWindowsPosition()
		{
			return false;
		}

		void ILayout.Reposition()
		{
			workspace.GetWindows().ForEach(MaximizeWindow);
			Windawesome.DoLayoutUpdated();
		}

		void ILayout.WindowMinimized(Window window)
		{
		}

		void ILayout.WindowRestored(Window window)
		{
			MaximizeWindow(window);
		}

		void ILayout.WindowCreated(Window window)
		{
			if (workspace.IsWorkspaceVisible)
			{
				MaximizeWindow(window);
				Windawesome.DoLayoutUpdated();
			}
		}

		void ILayout.WindowDestroyed(Window window)
		{
			if (workspace.IsWorkspaceVisible)
			{
				Windawesome.DoLayoutUpdated();
			}
		}

		#endregion
	}
}
