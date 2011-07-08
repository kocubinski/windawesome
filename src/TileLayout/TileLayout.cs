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

		private LayoutAxis layoutAxis;
		private LayoutAxis masterAreaAxis;
		private LayoutAxis stackAreaAxis;
		private double masterAreaFactor;
		private LinkedList<Window> windows;
		private int masterAreaWindowsCount;
		private Rectangle workingArea;

		public TileLayout(LayoutAxis layoutAxis = LayoutAxis.LeftToRight, LayoutAxis masterAreaAxis = LayoutAxis.Monocle,
			LayoutAxis stackAreaAxis = LayoutAxis.TopToBottom, double masterAreaFactor = 0.6, int masterAreaWindowsCount = 1)
		{
			this.layoutAxis = layoutAxis;
			this.masterAreaAxis = masterAreaAxis;
			this.stackAreaAxis = stackAreaAxis;
			this.masterAreaFactor = masterAreaFactor;
			this.masterAreaWindowsCount = masterAreaWindowsCount;

			windows = new LinkedList<Window>();
		}

		#region API

		public void AddToMasterAreaWindowsCount()
		{
			masterAreaWindowsCount++;
			this.Reposition(windows, workingArea);
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

				this.Reposition(windows, workingArea);

				Windawesome.DoLayoutUpdated();
			}
		}

		public void SetMasterAreaAxis(LayoutAxis masterAreaAxis)
		{
			if (this.masterAreaAxis != masterAreaAxis)
			{
				this.masterAreaAxis = masterAreaAxis;

				this.Reposition(windows, workingArea);

				Windawesome.DoLayoutUpdated();
			}
		}

		public void SetStackAreaAxis(LayoutAxis stackAreaAxis)
		{
			if (this.stackAreaAxis != stackAreaAxis)
			{
				this.stackAreaAxis = stackAreaAxis;

				this.Reposition(windows, workingArea);

				Windawesome.DoLayoutUpdated();
			}
		}

		public void ShiftWindowToNextPosition(Window window)
		{
			if (windows.Count > 1 && windows.Last.Value != window)
			{
				var node = windows.Find(window);
				windows.AddAfter(node.Next, window);
				windows.Remove(node);

				this.Reposition(windows, workingArea);
			}
		}

		public void ShiftWindowToPreviousPosition(Window window)
		{
			if (windows.Count > 1 && windows.First.Value != window)
			{
				var node = windows.Find(window);
				windows.AddBefore(node.Previous, window);
				windows.Remove(node);

				this.Reposition(windows, workingArea);
			}
		}

		public void ShiftWindowToMainPosition(Window window)
		{
			if (windows.Count > 1 && windows.First.Value != window)
			{
				var node = windows.Find(window);
				windows.Remove(node);
				windows.AddFirst(node);

				this.Reposition(windows, workingArea);
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

			this.Reposition(windows, workingArea);
		}

		#endregion

		private IntPtr PositionAreaWindows(IntPtr winPosInfo, Rectangle workingArea, bool master)
		{
			var masterOrStackWindows = master ? this.windows.Take(masterAreaWindowsCount) : this.windows.Skip(masterAreaWindowsCount);
			var count = masterOrStackWindows.Count();
			if (count > 0)
			{
				var otherWindowsCount = master ? windows.Count - count : -1;
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

				foreach (var window in masterOrStackWindows)
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

		#region Layout Members

		public string LayoutSymbol(int windowsCount)
		{
			if (layoutAxis == LayoutAxis.Monocle)
			{
				return "[" + windowsCount + "]";
			}

			var master = "[]";
			var masterCount = windows.Take(masterAreaWindowsCount).Count(w => w.ShowInTabs);
			var stackCount = windowsCount - masterCount;

			if (masterAreaWindowsCount > 1)
			{
				master = GetAreaSymbol(masterCount, masterAreaAxis);
			}
			var stack = GetAreaSymbol(stackCount, this.stackAreaAxis);

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

		public void Reposition(IEnumerable<Window> windows, Rectangle workingArea)
		{
			this.workingArea = workingArea;

			//windows.ForEach(window => NativeMethods.ShowWindowAsync(window.hWnd, NativeMethods.SW.SW_SHOWNA)); // TODO: remove if unnecessary

			this.windows = new LinkedList<Window>(windows);

			var winPosInfo = NativeMethods.BeginDeferWindowPos(this.windows.Count);
			winPosInfo = PositionAreaWindows(winPosInfo, workingArea, true);
			winPosInfo = PositionAreaWindows(winPosInfo, workingArea, false);
			NativeMethods.EndDeferWindowPos(winPosInfo);
		}

		public void WindowTitlebarToggled(Window window, IEnumerable<Window> windows, Rectangle workingArea)
		{
		}

		public void WindowBorderToggled(Window window, IEnumerable<Window> windows, Rectangle workingArea)
		{
		}

		public void WindowMinimized(Window window, IEnumerable<Window> windows, Rectangle workingArea)
		{
			this.Reposition(windows, workingArea);
		}

		public void WindowRestored(Window window, IEnumerable<Window> windows, Rectangle workingArea)
		{
			this.Reposition(windows, workingArea);
		}

		public void WindowCreated(Window window, IEnumerable<Window> windows, Rectangle workingArea, bool reLayout)
		{
			if (reLayout)
			{
				this.Reposition(windows, workingArea);
			}
		}

		public void WindowDestroyed(Window window, IEnumerable<Window> windows, Rectangle workingArea, bool reLayout)
		{
			if (reLayout)
			{
				this.Reposition(windows, workingArea);
			}
		}

		#endregion
	}
}
