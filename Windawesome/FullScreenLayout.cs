
namespace Windawesome
{
	public class FullScreenLayout : ILayout
	{
		private Workspace workspace;

		private void MaximizeWindow(Window window)
		{
			if (Windawesome.WindowIsNotHung(window))
			{
				var newMonitorBoundsAndWorkingArea = workspace.Monitor.MonitorInfo;
				var newMonitorBounds = newMonitorBoundsAndWorkingArea.rcMonitor;
				var newMonitorWorkingArea = newMonitorBoundsAndWorkingArea.rcWork;

				var hWindowsMonitor = NativeMethods.MonitorFromWindow(window.hWnd, NativeMethods.MFRF.MONITOR_MONITOR_DEFAULTTONULL);
				var windowsMonitorInfo = NativeMethods.MONITORINFO.Default;
				NativeMethods.GetMonitorInfo(hWindowsMonitor, ref windowsMonitorInfo);

				if (NativeMethods.IsZoomed(window.hWnd) && windowsMonitorInfo.rcMonitor != newMonitorBounds)
				{
					// restore if program is maximized and should be on a different monitor
					NativeMethods.ShowWindow(window.hWnd, NativeMethods.SW.SW_SHOWNOACTIVATE); // should not use SW_RESTORE as it activates the window
					System.Threading.Thread.Sleep(NativeMethods.minimizeRestoreDelay);
				}

				var winPlacement = NativeMethods.WINDOWPLACEMENT.Default;
				NativeMethods.GetWindowPlacement(window.hWnd, ref winPlacement);

				winPlacement.MaxPosition.X = newMonitorBounds.left;
				winPlacement.MaxPosition.Y = newMonitorBounds.top;

				var ws = NativeMethods.GetWindowStyleLongPtr(window.hWnd);
				if (ws.HasFlag(NativeMethods.WS.WS_CAPTION | NativeMethods.WS.WS_MAXIMIZEBOX))
				{
					if (windowsMonitorInfo.rcMonitor != newMonitorBounds)
					{
						winPlacement.NormalPosition.left += newMonitorBounds.left - windowsMonitorInfo.rcMonitor.left; // these are in working area coordinates
						winPlacement.NormalPosition.right += newMonitorBounds.right - windowsMonitorInfo.rcMonitor.right;
						winPlacement.NormalPosition.top += newMonitorBounds.top - windowsMonitorInfo.rcMonitor.top;
						winPlacement.NormalPosition.bottom += newMonitorBounds.bottom - windowsMonitorInfo.rcMonitor.bottom;
					}

					winPlacement.ShowCmd = NativeMethods.SW.SW_SHOWMAXIMIZED;
				}
				else
				{
					winPlacement.NormalPosition.left = newMonitorBounds.left; // these are in working area coordinates
					winPlacement.NormalPosition.right = newMonitorBounds.left + newMonitorWorkingArea.right - newMonitorWorkingArea.left;
					winPlacement.NormalPosition.top = newMonitorBounds.top;
					winPlacement.NormalPosition.bottom = newMonitorBounds.top + newMonitorWorkingArea.bottom - newMonitorWorkingArea.top;

					winPlacement.ShowCmd = NativeMethods.SW.SW_SHOWNOACTIVATE;
				}

				NativeMethods.SetWindowPlacement(window.hWnd, ref winPlacement);
			}
		}

		private void OnWorkspaceApplicationAddedOrRemoved(Workspace workspace, Window window)
		{
			if (workspace == this.workspace && workspace.IsWorkspaceVisible)
			{
				Workspace.DoLayoutUpdated();
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

			Workspace.WorkspaceApplicationAdded += OnWorkspaceApplicationAddedOrRemoved;
			Workspace.WorkspaceApplicationRemoved += OnWorkspaceApplicationAddedOrRemoved;
		}

		bool ILayout.ShouldSaveAndRestoreSharedWindowsPosition()
		{
			return false;
		}

		void ILayout.Reposition()
		{
			workspace.GetManagedWindows().ForEach(MaximizeWindow);
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
			}
		}

		void ILayout.WindowDestroyed(Window window)
		{
		}

		#endregion
	}
}
