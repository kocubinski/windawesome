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
			if (Windawesome.WindowIsNotHung(window))
			{
				var newBounds = workspace.Monitor.screen.Bounds;
				var currentBounds = System.Windows.Forms.Screen.FromHandle(window.hWnd).Bounds;
				//if (NativeMethods.IsZoomed(window.hWnd) && currentBounds != newBounds)
				//{
				//    // restore if program is maximized and should be on a different monitor
				//    NativeMethods.ShowWindowAsync(window.hWnd, NativeMethods.SW.SW_SHOWNOACTIVATE); // should not use SW_RESTORE as it activates the window
				//    System.Threading.Thread.Sleep(Workspace.minimizeRestoreDelay);
				//}

				var winPlacement = NativeMethods.WINDOWPLACEMENT.Default;
				NativeMethods.GetWindowPlacement(window.hWnd, ref winPlacement);

				winPlacement.MaxPosition.X = newBounds.X;
				winPlacement.MaxPosition.Y = newBounds.Y;

				var ws = NativeMethods.GetWindowStyleLongPtr(window.hWnd);
				if (ws.HasFlag(NativeMethods.WS.WS_CAPTION | NativeMethods.WS.WS_MAXIMIZEBOX))
				{
					if (currentBounds != newBounds)
					{
						winPlacement.NormalPosition.left += newBounds.Left - currentBounds.Left; // these are in working area coordinates
						winPlacement.NormalPosition.right += newBounds.Right - currentBounds.Right;
						winPlacement.NormalPosition.top += newBounds.Top - currentBounds.Top;
						winPlacement.NormalPosition.bottom += newBounds.Bottom - currentBounds.Bottom;
					}

					winPlacement.ShowCmd = NativeMethods.SW.SW_SHOWMAXIMIZED;
				}
				else
				{
					var workingArea = workspace.Monitor.screen.WorkingArea;

					winPlacement.NormalPosition.left = newBounds.Left; // these are in working area coordinates
					winPlacement.NormalPosition.right = newBounds.Left + workingArea.Width;
					winPlacement.NormalPosition.top = newBounds.Top;
					winPlacement.NormalPosition.bottom = newBounds.Top + workingArea.Height;

					winPlacement.ShowCmd = NativeMethods.SW.SW_SHOWNOACTIVATE;
				}

				NativeMethods.SetWindowPlacement(window.hWnd, ref winPlacement);
			}
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
			Workspace.DoLayoutUpdated();
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
				Workspace.DoLayoutUpdated();
			}
		}

		void ILayout.WindowDestroyed(Window window)
		{
			if (workspace.IsWorkspaceVisible)
			{
				Workspace.DoLayoutUpdated();
			}
		}

		#endregion
	}
}
