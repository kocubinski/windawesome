using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Windawesome
{
	public class TileLayout : ILayout
	{
		public enum LayoutAxis
		{
			LeftToRight,
			RightToLeft,
			TopToBottom,
			BottomToTop,
			Monocle
		}

		private Workspace workspace;
		private IEnumerable<Window> windows;
		private LayoutAxis layoutAxis;
		private LayoutAxis masterAreaAxis;
		private LayoutAxis stackAreaAxis;
		private double masterAreaFactor;
		private int masterAreaWindowsCount;

		public TileLayout(LayoutAxis layoutAxis = LayoutAxis.LeftToRight, LayoutAxis masterAreaAxis = LayoutAxis.Monocle,
			LayoutAxis stackAreaAxis = LayoutAxis.TopToBottom, double masterAreaFactor = 0.6, int masterAreaWindowsCount = 1)
		{
			this.layoutAxis = layoutAxis;
			this.masterAreaAxis = masterAreaAxis;
			this.stackAreaAxis = stackAreaAxis;
			if (masterAreaFactor > 1)
			{
				masterAreaFactor = 1;
			}
			else if (masterAreaFactor < 0)
			{
				masterAreaFactor = 0;
			}
			this.masterAreaFactor = masterAreaFactor;
			if (masterAreaWindowsCount < 0)
			{
				masterAreaWindowsCount = 0;
			}
			this.masterAreaWindowsCount = masterAreaWindowsCount;
		}

		#region API

		public void AddToMasterAreaWindowsCount(int count = 1)
		{
			masterAreaWindowsCount += count;
			if (masterAreaWindowsCount < 0)
			{
				masterAreaWindowsCount = 0;
			}
			this.Reposition();
		}

		public void ToggleLayoutAxis()
		{
			SetLayoutAxis((LayoutAxis) (((int) layoutAxis + 1) % Enum.GetValues(typeof(LayoutAxis)).Length));
		}

		public void ToggleMasterAreaAxis()
		{
			SetMasterAreaAxis((LayoutAxis) (((int) masterAreaAxis + 1) % Enum.GetValues(typeof(LayoutAxis)).Length));
		}

		public void ToggleStackAreaAxis()
		{
			SetStackAreaAxis((LayoutAxis) (((int) stackAreaAxis + 1) % Enum.GetValues(typeof(LayoutAxis)).Length));
		}

		public void SetLayoutAxis(LayoutAxis layoutAxis)
		{
			if (this.layoutAxis != layoutAxis)
			{
				this.layoutAxis = layoutAxis;

				this.Reposition();
			}
		}

		public void SetMasterAreaAxis(LayoutAxis masterAreaAxis)
		{
			if (this.masterAreaAxis != masterAreaAxis)
			{
				this.masterAreaAxis = masterAreaAxis;

				this.Reposition();
			}
		}

		public void SetStackAreaAxis(LayoutAxis stackAreaAxis)
		{
			if (this.stackAreaAxis != stackAreaAxis)
			{
				this.stackAreaAxis = stackAreaAxis;

				this.Reposition();
			}
		}

		public void AddToMasterAreaFactor(double masterAreaFactorChange = 0.05)
		{
			masterAreaFactor += masterAreaFactorChange;
			if (masterAreaFactor > 1)
			{
				masterAreaFactor = 1;
			}
			else if (masterAreaFactor < 0)
			{
				masterAreaFactor = 0;
			}

			this.Reposition();
		}

		#endregion

		private IntPtr PositionAreaWindows(IntPtr winPosInfo, Rectangle workingArea, bool master)
		{
			var count = master ? masterAreaWindowsCount : windows.Count() - masterAreaWindowsCount;
			if (count > 0)
			{
				var otherWindowsCount = master ? windows.Count() - masterAreaWindowsCount : masterAreaWindowsCount;
				var factor = otherWindowsCount == 0 ? 1 : (master ? masterAreaFactor : 1 - masterAreaFactor);
				var axis = master ? masterAreaAxis : stackAreaAxis;

				int eachWidth = workingArea.Width, eachHight = workingArea.Height;
				int x = workingArea.X, y = workingArea.Y;

				switch (layoutAxis)
				{
					case LayoutAxis.LeftToRight:
						eachWidth = (int) (eachWidth * factor);
						x = master ? workingArea.X : workingArea.Right - eachWidth;
						break;
					case LayoutAxis.RightToLeft:
						eachWidth = (int) (eachWidth * factor);
						x = master ? workingArea.Right - eachWidth : workingArea.X;
						break;
					case LayoutAxis.TopToBottom:
						eachHight = (int) (eachHight * factor);
						y = master ? workingArea.Y : workingArea.Bottom - eachHight;
						break;
					case LayoutAxis.BottomToTop:
						eachHight = (int) (eachHight * factor);
						y = master ? workingArea.Bottom - eachHight : workingArea.Y;
						break;
				}
				switch (axis)
				{
					case LayoutAxis.RightToLeft:
						x += eachWidth;
						break;
					case LayoutAxis.BottomToTop:
						y += eachHight;
						break;
				}
				switch (axis)
				{
					case LayoutAxis.LeftToRight:
					case LayoutAxis.RightToLeft:
						eachWidth /= count;
						break;
					case LayoutAxis.TopToBottom:
					case LayoutAxis.BottomToTop:
						eachHight /= count;
						break;
				}
				switch (axis)
				{
					case LayoutAxis.RightToLeft:
						x -= eachWidth;
						break;
					case LayoutAxis.BottomToTop:
						y -= eachHight;
						break;
				}

				var masterOrStackWindows = master ? this.windows.Take(masterAreaWindowsCount) : this.windows.Skip(masterAreaWindowsCount);
				foreach (var window in masterOrStackWindows.Where(Windawesome.WindowIsNotHung))
				{
					// TODO: this doesn't work for ICQ 7.5's windows. MoveWindow works in Debug mode, but not in Release
					winPosInfo = NativeMethods.DeferWindowPos(winPosInfo, window.hWnd, IntPtr.Zero,
						x, y, eachWidth, eachHight,
						NativeMethods.SWP.SWP_FRAMECHANGED | NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOCOPYBITS |
						NativeMethods.SWP.SWP_NOZORDER | NativeMethods.SWP.SWP_NOOWNERZORDER);

					switch (axis)
					{
						case LayoutAxis.LeftToRight:
							x += eachWidth;
							break;
						case LayoutAxis.RightToLeft:
							x -= eachWidth;
							break;
						case LayoutAxis.TopToBottom:
							y += eachHight;
							break;
						case LayoutAxis.BottomToTop:
							y -= eachHight;
							break;
					}
				}
			}

			return winPosInfo;
		}

		private static string GetAreaSymbol(int count, LayoutAxis axis)
		{
			switch (axis)
			{
				case LayoutAxis.LeftToRight:
				case LayoutAxis.RightToLeft:
					return "|";
				case LayoutAxis.TopToBottom:
				case LayoutAxis.BottomToTop:
					return "=";
				default: // LayoutAxis.Monocle
					return count.ToString();
			}
		}

		private void Reposition()
		{
			if (this.windows.Count() > 0)
			{
				var workingArea = workspace.Monitor.WorkingArea;
				var winPosInfo = NativeMethods.BeginDeferWindowPos(this.windows.Count());
				winPosInfo = PositionAreaWindows(winPosInfo, workingArea, true);
				winPosInfo = PositionAreaWindows(winPosInfo, workingArea, false);
				NativeMethods.EndDeferWindowPos(winPosInfo);
			}

			Workspace.DoLayoutUpdated();
		}

		#region ILayout Members

		string ILayout.LayoutSymbol()
		{
			if (layoutAxis == LayoutAxis.Monocle)
			{
				return "[" + workspace.GetWindowsCount() + "]";
			}

			var master = "[]";
			var masterCount = windows.Take(masterAreaWindowsCount).Count(w => w.ShowInTabs);
			var stackCount = workspace.GetWindowsCount() - masterCount;

			if (masterAreaWindowsCount > 1)
			{
				master = GetAreaSymbol(masterCount, masterAreaAxis);
			}
			var stack = GetAreaSymbol(stackCount, stackAreaAxis);

			switch (layoutAxis)
			{
				case LayoutAxis.LeftToRight:
				case LayoutAxis.TopToBottom:
					return master + (master == "[]" ? "" : "]") + stack;
				case LayoutAxis.RightToLeft:
				case LayoutAxis.BottomToTop:
					return stack + (master == "[]" ? "" : "[") + master;
			}

			throw new Exception("Unreachable code... reached!");
		}

		public string LayoutName()
		{
			return "Tile";
		}

		bool ILayout.ShouldSaveAndRestoreSharedWindowsPosition()
		{
			return false;
		}

		void ILayout.Initialize(Workspace workspace)
		{
			this.workspace = workspace;
		}

		void ILayout.Dispose()
		{
		}

		void ILayout.Reposition()
		{
			// restore any maximized windows - should not use SW_RESTORE as it activates the window
			var hasMaximized = false;
			foreach (var window in workspace.GetManagedWindows().Where(w => NativeMethods.IsZoomed(w.hWnd)))
			{
				hasMaximized = true;
				NativeMethods.ShowWindowAsync(window.hWnd, NativeMethods.SW.SW_SHOWNOACTIVATE);
			}
			if (hasMaximized)
			{
				System.Threading.Thread.Sleep(NativeMethods.minimizeRestoreDelay);
			}

			this.windows = workspace.GetManagedWindows();
			Reposition();
		}

		void ILayout.WindowMinimized(Window window)
		{
			(this as ILayout).WindowDestroyed(window);
		}

		void ILayout.WindowRestored(Window window)
		{
			(this as ILayout).WindowCreated(window);
		}

		void ILayout.WindowCreated(Window window)
		{
			if (workspace.IsWorkspaceVisible || window.WorkspacesCount == 1)
			{
				if (NativeMethods.IsZoomed(window.hWnd))
				{
					// restore if maximized - should not use SW_RESTORE as it activates the window
					NativeMethods.ShowWindowAsync(window.hWnd, NativeMethods.SW.SW_SHOWNOACTIVATE);
					System.Threading.Thread.Sleep(NativeMethods.minimizeRestoreDelay);
				}
				if (workspace.IsWorkspaceVisible)
				{
					Reposition();
				}
			}
		}

		void ILayout.WindowDestroyed(Window window)
		{
			if (workspace.IsWorkspaceVisible)
			{
				Reposition();
			}
		}

		#endregion
	}
}
