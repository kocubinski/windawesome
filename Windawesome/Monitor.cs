using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public class Monitor
	{
		public readonly int monitorIndex;
		public readonly Screen screen;
		public Workspace CurrentVisibleWorkspace { get; private set; }
		public IEnumerable<Workspace> Workspaces { get { return workspaces.Keys; } }

		public static readonly IntPtr taskbarHandle;
		public static readonly IntPtr startButtonHandle;

		internal readonly HashSet<IntPtr> temporarilyShownWindows;
		private readonly Dictionary<Workspace, Tuple<int, AppBarNativeWindow, AppBarNativeWindow>> workspaces;

		private static bool isWindowsTaskbarShown;

		private static readonly NativeMethods.WinEventDelegate taskbarShownWinEventDelegate = TaskbarShownWinEventDelegate;
		private static readonly IntPtr taskbarShownWinEventHook;

		private sealed class AppBarNativeWindow : NativeWindow
		{
			public readonly int Height;

			private Screen screen;
			private NativeMethods.RECT rect;
			private bool visible;
			private IEnumerable<IBar> bars;
			private readonly uint callbackMessageNum;
			private readonly NativeMethods.ABE edge;
			private bool isTopMost;

			private static uint count;

			public AppBarNativeWindow(int barHeight, bool topBar)
			{
				this.Height = barHeight;
				visible = false;
				isTopMost = false;
				edge = topBar ? NativeMethods.ABE.ABE_TOP : NativeMethods.ABE.ABE_BOTTOM;

				this.CreateHandle(new CreateParams { Parent = NativeMethods.HWND_MESSAGE });

				callbackMessageNum = NativeMethods.WM_USER + ++count;

				// register as AppBar
				var appBarData = new NativeMethods.APPBARDATA(this.Handle, callbackMessageNum);

				NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_NEW, ref appBarData);
			}

			public void Destroy()
			{
				// unregister as AppBar
				var appBarData = new NativeMethods.APPBARDATA(this.Handle);

				NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_REMOVE, ref appBarData);

				DestroyHandle();
			}

			public bool SetPosition(Screen screen)
			{
				this.screen = screen;

				var appBarData = new NativeMethods.APPBARDATA(this.Handle, uEdge: edge, rc: new NativeMethods.RECT { left = screen.Bounds.Left, right = screen.Bounds.Right });

				if (edge == NativeMethods.ABE.ABE_TOP)
				{
					appBarData.rc.top = screen.Bounds.Top;
					appBarData.rc.bottom = appBarData.rc.top + Height;
				}
				else
				{
					appBarData.rc.bottom = screen.Bounds.Bottom;
					appBarData.rc.top = appBarData.rc.bottom - Height;
				}

				NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_QUERYPOS, ref appBarData);

				if (edge == NativeMethods.ABE.ABE_TOP)
				{
					appBarData.rc.bottom = appBarData.rc.top + Height;
				}
				else
				{
					appBarData.rc.top = appBarData.rc.bottom - Height;
				}

				NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_SETPOS, ref appBarData);

				var changedPosition = appBarData.rc.bottom != rect.bottom || appBarData.rc.top != rect.top ||
					appBarData.rc.left != rect.left || appBarData.rc.right != rect.right;

				this.rect = appBarData.rc;

				this.visible = true;

				return changedPosition;
			}

			public void Hide()
			{
				var appBarData = new NativeMethods.APPBARDATA(this.Handle, uEdge: NativeMethods.ABE.ABE_TOP);

				NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_QUERYPOS, ref appBarData);
				NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_SETPOS, ref appBarData);

				this.visible = false;
			}

			// move the bars to their respective positions
			public IntPtr PositionBars(IntPtr winPosInfo, IEnumerable<IBar> bars)
			{
				this.bars = bars;

				var topBar = edge == NativeMethods.ABE.ABE_TOP;
				var currentY = topBar ? rect.top : rect.bottom;
				foreach (var bar in bars)
				{
					if (!topBar)
					{
						currentY -= bar.GetBarHeight();
					}
					var barRect = new NativeMethods.RECT
						{
							left = rect.left,
							top = currentY,
							right = rect.right,
							bottom = currentY + bar.GetBarHeight()
						};
					if (topBar)
					{
						currentY += bar.GetBarHeight();
					}

					NativeMethods.AdjustWindowRectEx(ref barRect, NativeMethods.GetWindowStyleLongPtr(bar.Handle),
						NativeMethods.GetMenu(bar.Handle) != IntPtr.Zero, NativeMethods.GetWindowExStyleLongPtr(bar.Handle));

					var newSize = new Size(barRect.right - barRect.left, barRect.bottom - barRect.top);
					winPosInfo = NativeMethods.DeferWindowPos(winPosInfo, bar.Handle, NativeMethods.HWND_TOPMOST, barRect.left, barRect.top,
						newSize.Width, newSize.Height, NativeMethods.SWP.SWP_NOACTIVATE);

					bar.OnSizeChanging(newSize);
				}

				isTopMost = true;

				return winPosInfo;
			}

			protected override void WndProc(ref Message m)
			{
				if (m.Msg == callbackMessageNum)
				{
					if (visible)
					{
						switch ((NativeMethods.ABN) m.WParam)
						{
							case NativeMethods.ABN.ABN_FULLSCREENAPP:
								if (m.LParam == IntPtr.Zero)
								{
									// full-screen app is closing
									if (!isTopMost)
									{
										var winPosInfo = NativeMethods.BeginDeferWindowPos(bars.Count());
										winPosInfo = this.bars.Aggregate(winPosInfo, (current, bar) =>
											NativeMethods.DeferWindowPos(current, bar.Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
												NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE));
										NativeMethods.EndDeferWindowPos(winPosInfo);

										isTopMost = true;
									}
								}
								else
								{
									// full-screen app is opening - check if that is the desktop window
									var foregroundWindow = NativeMethods.GetForegroundWindow();
									if (isTopMost && NativeMethods.GetWindowClassName(foregroundWindow) != "WorkerW")
									{
										int processId;
										NativeMethods.GetWindowThreadProcessId(foregroundWindow, out processId);
										var processName = System.Diagnostics.Process.GetProcessById(processId).ProcessName;
										if (processName != "explorer")
										{
											var winPosInfo = NativeMethods.BeginDeferWindowPos(bars.Count());
											winPosInfo = this.bars.Aggregate(winPosInfo, (current, bar) =>
												NativeMethods.DeferWindowPos(current, bar.Handle, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
													NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE));
											NativeMethods.EndDeferWindowPos(winPosInfo);

											isTopMost = false;
										}
									}
								}
								break;
							case NativeMethods.ABN.ABN_POSCHANGED:
								if (SetPosition(screen))
								{
									var winPosInfo = NativeMethods.BeginDeferWindowPos(bars.Count());
									NativeMethods.EndDeferWindowPos(PositionBars(winPosInfo, bars));
								}
								break;
						}
					}
				}
				else
				{
					base.WndProc(ref m);
				}
			}
		}

		static Monitor()
		{
			taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
			if (Windawesome.isAtLeastVista)
			{
				startButtonHandle = NativeMethods.FindWindow("Button", "Start");
			}

			// this is because Windows shows the taskbar at random points when it is made to autohide
			taskbarShownWinEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT.EVENT_OBJECT_SHOW, NativeMethods.EVENT.EVENT_OBJECT_SHOW,
				IntPtr.Zero, taskbarShownWinEventDelegate, 0,
				NativeMethods.GetWindowThreadProcessId(taskbarHandle, IntPtr.Zero),
				NativeMethods.WINEVENT.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT.WINEVENT_SKIPOWNTHREAD);
		}

		public Monitor(int monitorIndex)
		{
			this.workspaces = new Dictionary<Workspace, Tuple<int, AppBarNativeWindow, AppBarNativeWindow>>(2);
			this.temporarilyShownWindows = new HashSet<IntPtr>();

			this.monitorIndex = monitorIndex;
			this.screen = Screen.AllScreens[monitorIndex];
		}

		internal void Dispose()
		{
			// this statement uses the laziness of Where
			workspaces.Values.Select(t => t.Item2).Concat(workspaces.Values.Select(t => t.Item3)).
				Where(ab => ab != null && ab.Handle != IntPtr.Zero).ForEach(ab => ab.Destroy());
		}

		internal static void StaticDispose()
		{
			NativeMethods.UnhookWinEvent(taskbarShownWinEventHook);

			if (!isWindowsTaskbarShown)
			{
				ShowHideWindowsTaskbar(true);
			}
		}

		internal void SetStartingWorkspace(Workspace startingWorkspace)
		{
			CurrentVisibleWorkspace = startingWorkspace;
		}

		internal void Initialize()
		{
			var workspaceTuple = workspaces[CurrentVisibleWorkspace];
			ShowHideBars(null, null, workspaceTuple.Item2, workspaceTuple.Item3, CurrentVisibleWorkspace, CurrentVisibleWorkspace);

			CurrentVisibleWorkspace.SwitchTo();
		}

		public override bool Equals(object obj)
		{
			var other = obj as Monitor;
			return other != null && other.monitorIndex == this.monitorIndex;
		}

		public override int GetHashCode()
		{
			return this.monitorIndex;
		}

		private static void TaskbarShownWinEventDelegate(IntPtr hWinEventHook, uint eventType,
			IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (NativeMethods.IsWindowVisible(taskbarHandle) != isWindowsTaskbarShown)
			{
				ShowHideWindowsTaskbar(isWindowsTaskbarShown);
			}
		}

		internal void SwitchToWorkspace(Workspace workspace)
		{
			CurrentVisibleWorkspace.Unswitch();

			// hides or shows the Windows taskbar
			if (screen.Primary && workspace.ShowWindowsTaskbar != isWindowsTaskbarShown)
			{
				ShowHideWindowsTaskbar(workspace.ShowWindowsTaskbar);
			}

			var oldWorkspace = workspaces[CurrentVisibleWorkspace];
			var newWorkspace = workspaces[workspace];

			// hides the Bars for the old workspace and shows the new ones
			if (newWorkspace.Item1 != oldWorkspace.Item1)
			{
				ShowHideBars(oldWorkspace.Item2, oldWorkspace.Item3,
					newWorkspace.Item2, newWorkspace.Item3,
					CurrentVisibleWorkspace, workspace);
			}

			CurrentVisibleWorkspace = workspace;

			workspace.SwitchTo();
		}

		internal static void ShowHideWindowsTaskbar(bool showWindowsTaskbar)
		{
			// TODO: the first time the Taskbar is toggled, the working area doesn't change?

			var appBarData = new NativeMethods.APPBARDATA(taskbarHandle);
			var state = (NativeMethods.ABS) (uint) NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_GETSTATE, ref appBarData);

			appBarData.lParam = (IntPtr) (showWindowsTaskbar ? state & ~NativeMethods.ABS.ABS_AUTOHIDE : state | NativeMethods.ABS.ABS_AUTOHIDE);
			NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_SETSTATE, ref appBarData);

			var showHide = showWindowsTaskbar ? NativeMethods.SW.SW_SHOWNA : NativeMethods.SW.SW_HIDE;

			NativeMethods.ShowWindow(taskbarHandle, showHide);
			if (Windawesome.isAtLeastVista)
			{
				NativeMethods.ShowWindow(startButtonHandle, showHide);
			}

			isWindowsTaskbarShown = showWindowsTaskbar;
		}

		internal void AddWorkspace(Workspace workspace)
		{
			var workspaceBarsAtTop = workspace.barsAtTop[monitorIndex];
			var workspaceBarsAtBottom = workspace.barsAtBottom[monitorIndex];

			int workspaceBarsEquivalentClass;
			var matchingBar = workspaces.Keys.FirstOrDefault(ws =>
				workspaceBarsAtTop.SequenceEqual(ws.barsAtTop[monitorIndex]) && workspaceBarsAtBottom.SequenceEqual(ws.barsAtBottom[monitorIndex]));
			if (matchingBar != null)
			{
				var matchingWorkspace = workspaces[matchingBar];
				this.workspaces[workspace] = new Tuple<int, AppBarNativeWindow, AppBarNativeWindow>(matchingWorkspace.Item1, matchingWorkspace.Item2, matchingWorkspace.Item3);

				return ;
			}
			else
			{
				workspaceBarsEquivalentClass = (workspaces.Count == 0 ? 0 : workspaces.Values.Max(t => t.Item1)) + 1;
			}

			AppBarNativeWindow appBarTopWindow;
			var topBarsHeight = workspaceBarsAtTop.Sum(bar => bar.GetBarHeight());
			var matchingAppBar = workspaces.Values.Select(t => t.Item2).FirstOrDefault(ab =>
				(ab == null && topBarsHeight == 0) || (ab != null && topBarsHeight == ab.Height));
			if (matchingAppBar != null || topBarsHeight == 0)
			{
				appBarTopWindow = matchingAppBar;
			}
			else
			{
				appBarTopWindow = new AppBarNativeWindow(topBarsHeight, true);
			}

			AppBarNativeWindow appBarBottomWindow;
			var bottomBarsHeight = workspaceBarsAtBottom.Sum(bar => bar.GetBarHeight());
			matchingAppBar = workspaces.Values.Select(t => t.Item3).FirstOrDefault(uniqueAppBar =>
				(uniqueAppBar == null && bottomBarsHeight == 0) || (uniqueAppBar != null && bottomBarsHeight == uniqueAppBar.Height));
			if (matchingAppBar != null || bottomBarsHeight == 0)
			{
				appBarBottomWindow = matchingAppBar;
			}
			else
			{
				appBarBottomWindow = new AppBarNativeWindow(bottomBarsHeight, false);
			}

			this.workspaces[workspace] = new Tuple<int, AppBarNativeWindow, AppBarNativeWindow>(workspaceBarsEquivalentClass, appBarTopWindow, appBarBottomWindow);
		}

		internal void RemoveWorkspace(Workspace workspace)
		{
			var workspaceTuple = workspaces[workspace];
			if (workspaceTuple.Item2 != null && workspaces.Where(kv => kv.Key != workspace).All(kv => kv.Value.Item2 != workspaceTuple.Item2))
			{
				workspaceTuple.Item2.Destroy();
			}
			if (workspaceTuple.Item3 != null && workspaces.Where(kv => kv.Key != workspace).All(kv => kv.Value.Item3 != workspaceTuple.Item3))
			{
				workspaceTuple.Item3.Destroy();
			}
			workspaces.Remove(workspace);
		}

		private void ShowHideBars(AppBarNativeWindow previousAppBarTopWindow, AppBarNativeWindow previousAppBarBottomWindow,
			AppBarNativeWindow newAppBarTopWindow, AppBarNativeWindow newAppBarBottomWindow,
			Workspace oldWorkspace, Workspace newWorkspace)
		{
			ShowHideAppBarForms(previousAppBarTopWindow, newAppBarTopWindow);
			ShowHideAppBarForms(previousAppBarBottomWindow, newAppBarBottomWindow);

			var oldBarsAtTop = oldWorkspace.barsAtTop[monitorIndex];
			var oldBarsAtBottom = oldWorkspace.barsAtBottom[monitorIndex];
			var newBarsAtTop = newWorkspace.barsAtTop[monitorIndex];
			var newBarsAtBottom = newWorkspace.barsAtBottom[monitorIndex];

			// first position and show new bars
			var winPosInfo = NativeMethods.BeginDeferWindowPos(newBarsAtTop.Count + newBarsAtBottom.Count);
			if (newAppBarTopWindow != null)
			{
				winPosInfo = newAppBarTopWindow.PositionBars(winPosInfo, newBarsAtTop);
			}
			if (newAppBarBottomWindow != null)
			{
				winPosInfo = newAppBarBottomWindow.PositionBars(winPosInfo, newBarsAtBottom);
			}
			NativeMethods.EndDeferWindowPos(winPosInfo);

			newBarsAtTop.Concat(newBarsAtBottom).ForEach(b => b.Show());

			// and only after that hide the old ones to avoid flickering
			oldBarsAtTop.Concat(oldBarsAtBottom).Except(newBarsAtTop.Concat(newBarsAtBottom)).ForEach(b => b.Hide());
		}

		private void ShowHideAppBarForms(AppBarNativeWindow hideForm, AppBarNativeWindow showForm)
		{
			// this whole thing is so complicated as to avoid changing of the working area if the bars in the new workspace
			// take the same space as the one in the previous one

			// set the working area to a new one if needed
			if (hideForm != null)
			{
				if (showForm == null || hideForm != showForm)
				{
					hideForm.Hide();
					if (showForm != null)
					{
						showForm.SetPosition(screen);
					}
				}
			}
			else if (showForm != null)
			{
				showForm.SetPosition(screen);
			}
		}
	}
}
