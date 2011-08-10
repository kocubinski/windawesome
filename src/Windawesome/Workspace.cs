using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public sealed class Workspace
	{
		public readonly int id;
		public ILayout Layout { get; private set; }
		public readonly LinkedList<IBar> barsAtTop;
		public readonly LinkedList<IBar> barsAtBottom;
		public readonly string name;
		public bool ShowWindowsTaskbar { get; private set; }
		public bool IsCurrentWorkspace { get; private set; }
		public readonly bool repositionOnSwitchedTo;
		public static readonly IntPtr taskbarHandle;
		public static readonly IntPtr startButtonHandle;

		private static readonly HashSet<IBar> registeredBars = new HashSet<IBar>();

		private static int[] workspaceBarsEquivalentClasses;
		private static AppBarNativeWindow[] appBarTopWindows;
		private static AppBarNativeWindow[] appBarBottomWindows;

		private static int count;
		private static bool isWindowsTaskbarShown;
		private static IEnumerable<IBar> shownBars = new IBar[0];

		private int floatingWindowsCount;
		private int windowsShownInTabsCount;

		private readonly LinkedList<Window> windows; // all windows, owner window, sorted in Z-order, topmost window first
		private readonly LinkedList<Window> managedWindows; // windows.Where(w => !w.isFloating && !w.isMinimized), owned windows, not sorted
		private readonly LinkedList<Window> sharedWindows; // windows.Where(w => w.shared), not sorted
		private readonly LinkedList<Window> removedSharedWindows; // windows that need to be Initialized but then removed from shared
		internal bool hasChanges;

		private static readonly NativeMethods.WinEventDelegate taskbarShownWinEventDelegate = TaskbarShownWinEventDelegate;
		private static readonly IntPtr taskbarShownWinEventHook;

		private static void TaskbarShownWinEventDelegate(IntPtr hWinEventHook, uint eventType,
			IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (NativeMethods.IsWindowVisible(taskbarHandle) != isWindowsTaskbarShown)
			{
				ShowHideWindowsTaskbar(isWindowsTaskbarShown);
			}
		}

		private sealed class AppBarNativeWindow : NativeWindow
		{
			public readonly int Height;
			private NativeMethods.RECT rect;
			private bool visible;
			private IEnumerable<IBar> bars;
			private readonly uint callbackMessageNum;
			private readonly NativeMethods.ABE edge;
			private bool isTopMost;

			public AppBarNativeWindow(int barHeight, bool topBar)
			{
				this.Height = barHeight;
				visible = false;
				isTopMost = false;
				edge = topBar ? NativeMethods.ABE.ABE_TOP : NativeMethods.ABE.ABE_BOTTOM;

				this.CreateHandle(new CreateParams { Parent = NativeMethods.HWND_MESSAGE });

				callbackMessageNum = NativeMethods.RegisterWindowMessage("APP_BAR_MESSAGE_" + this.Handle);

				// register as AppBar
				var appBarData = new NativeMethods.APPBARDATA
					{
						hWnd = this.Handle,
						uCallbackMessage = callbackMessageNum
					};

				NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_NEW, ref appBarData);
			}

			public void Destroy()
			{
				// unregister as AppBar
				var appBarData = new NativeMethods.APPBARDATA
					{
						hWnd = this.Handle
					};

				NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_REMOVE, ref appBarData);

				DestroyHandle();
			}

			public bool SetPosition()
			{
				var appBarData = new NativeMethods.APPBARDATA
					{
						hWnd = this.Handle,
						uEdge = edge,
						rc = { left = 0, right = SystemInformation.PrimaryMonitorSize.Width }
					};

				if (edge == NativeMethods.ABE.ABE_TOP)
				{
					appBarData.rc.top = 0;
					appBarData.rc.bottom = Height;
				}
				else
				{
					appBarData.rc.bottom = SystemInformation.PrimaryMonitorSize.Height;
					appBarData.rc.top = appBarData.rc.bottom - Height;
				}

				NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_QUERYPOS, ref appBarData);

				switch (edge)
				{
					case NativeMethods.ABE.ABE_TOP:
						appBarData.rc.bottom = appBarData.rc.top + Height;
						break;
					case NativeMethods.ABE.ABE_BOTTOM:
						appBarData.rc.top = appBarData.rc.bottom - Height;
						break;
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
				var appBarData = new NativeMethods.APPBARDATA
					{
						hWnd = this.Handle,
						uEdge = NativeMethods.ABE.ABE_TOP
					};

				appBarData.rc.left = appBarData.rc.right = appBarData.rc.top = appBarData.rc.bottom = 0;

				NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_QUERYPOS, ref appBarData);
				NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_SETPOS, ref appBarData);

				this.visible = false;
			}

			// show and move the bars to their respective positions
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
					var	barRect = new NativeMethods.RECT
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

					winPosInfo = NativeMethods.DeferWindowPos(winPosInfo, bar.Handle, NativeMethods.HWND_TOPMOST, barRect.left, barRect.top,
						barRect.right - barRect.left, barRect.bottom - barRect.top,	NativeMethods.SWP.SWP_NOACTIVATE);

					bar.OnSizeChanging(new Size(barRect.right - barRect.left, barRect.bottom - barRect.top));

					bar.Show();
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
								if (SetPosition())
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

		#region Events

		public delegate void WorkspaceApplicationAddedEventHandler(Workspace workspace, Window window);
		public static event WorkspaceApplicationAddedEventHandler WorkspaceApplicationAdded;

		public delegate void WorkspaceApplicationRemovedEventHandler(Workspace workspace, Window window);
		public static event WorkspaceApplicationRemovedEventHandler WorkspaceApplicationRemoved;

		public delegate void WorkspaceApplicationMinimizedEventHandler(Workspace workspace, Window window);
		public static event WorkspaceApplicationMinimizedEventHandler WorkspaceApplicationMinimized;

		public delegate void WorkspaceApplicationRestoredEventHandler(Workspace workspace, Window window);
		public static event WorkspaceApplicationRestoredEventHandler WorkspaceApplicationRestored;

		public delegate void WorkspaceChangedFromEventHandler(Workspace workspace);
		public static event WorkspaceChangedFromEventHandler WorkspaceChangedFrom;

		public delegate void WorkspaceChangedToEventHandler(Workspace workspace);
		public static event WorkspaceChangedToEventHandler WorkspaceChangedTo;

		public delegate void WorkspaceLayoutChangedEventHandler(Workspace workspace, ILayout oldLayout);
		public static event WorkspaceLayoutChangedEventHandler WorkspaceLayoutChanged;

		public delegate void WindowActivatedEventHandler(IntPtr hWnd);
		public static event WindowActivatedEventHandler WindowActivatedEvent;

		private static void DoWorkspaceApplicationAdded(Workspace workspace, Window window)
		{
			if (WorkspaceApplicationAdded != null)
			{
				WorkspaceApplicationAdded(workspace, window);
			}
			Windawesome.DoLayoutUpdated();
		}

		private static void DoWorkspaceApplicationRemoved(Workspace workspace, Window window)
		{
			if (WorkspaceApplicationRemoved != null)
			{
				WorkspaceApplicationRemoved(workspace, window);
			}
			Windawesome.DoLayoutUpdated();
		}

		private static void DoWorkspaceApplicationMinimized(Workspace workspace, Window window)
		{
			if (WorkspaceApplicationMinimized != null)
			{
				WorkspaceApplicationMinimized(workspace, window);
			}
		}

		private static void DoWorkspaceApplicationRestored(Workspace workspace, Window window)
		{
			if (WorkspaceApplicationRestored != null)
			{
				WorkspaceApplicationRestored(workspace, window);
			}
		}

		private static void DoWorkspaceChangedFrom(Workspace workspace)
		{
			if (WorkspaceChangedFrom != null)
			{
				WorkspaceChangedFrom(workspace);
			}
		}

		private static void DoWorkspaceChangedTo(Workspace workspace)
		{
			if (WorkspaceChangedTo != null)
			{
				WorkspaceChangedTo(workspace);
			}
			Windawesome.DoLayoutUpdated();
		}

		private static void DoWorkspaceLayoutChanged(Workspace workspace, ILayout oldLayout)
		{
			if (WorkspaceLayoutChanged != null)
			{
				WorkspaceLayoutChanged(workspace, oldLayout);
			}
			Windawesome.DoLayoutUpdated();
		}

		private static void DoWindowActivated(IntPtr hWnd)
		{
			if (WindowActivatedEvent != null)
			{
				WindowActivatedEvent(hWnd);
			}
		}

		#endregion

		public string LayoutSymbol
		{
			get
			{
				return Layout.LayoutSymbol(windowsShownInTabsCount);
			}
		}

		static Workspace()
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

		public Workspace(ILayout layout, IEnumerable<IBar> barsAtTop = null, IEnumerable<IBar> barsAtBottom = null, string name = "", bool showWindowsTaskbar = false,
			bool repositionOnSwitchedTo = false)
		{
			windows = new LinkedList<Window>();
			managedWindows = new LinkedList<Window>();
			sharedWindows = new LinkedList<Window>();
			removedSharedWindows = new LinkedList<Window>();

			this.id = ++count;
			this.Layout = layout;
			this.barsAtTop = barsAtTop != null ? new LinkedList<IBar>(barsAtTop) : new LinkedList<IBar>();
			this.barsAtBottom = barsAtBottom != null ? new LinkedList<IBar>(barsAtBottom) : new LinkedList<IBar>();
			this.name = name;
			this.ShowWindowsTaskbar = showWindowsTaskbar;
			this.repositionOnSwitchedTo = repositionOnSwitchedTo;

			this.barsAtTop.Unless(registeredBars.Contains).ForEach(RegisterBar);
			this.barsAtBottom.Unless(registeredBars.Contains).ForEach(RegisterBar);
		}

		internal static void Dispose()
		{
			// this statement uses the laziness of Where
			appBarTopWindows.Concat(appBarBottomWindows).Where(nw => nw != null && nw.Handle != IntPtr.Zero).ForEach(ab => ab.Destroy());

			registeredBars.ForEach(UnregisterBar);

			NativeMethods.UnhookWinEvent(taskbarShownWinEventHook);

			if (!isWindowsTaskbarShown)
			{
				ShowHideWindowsTaskbar(true);
			}
		}

		public override int GetHashCode()
		{
			return this.id;
		}

		public override bool Equals(object obj)
		{
			var workspace = obj as Workspace;
			return workspace != null && workspace.id == this.id;
		}

		internal static void FindWorkspaceBarsEquivalentClasses(int workspacesCount, IEnumerable<Workspace> workspaces)
		{
			if (appBarTopWindows != null) // if this is not the first time calling this function, i.e. a bar is hidden/shown by the user
			{
				// this statement uses the laziness of Where
				appBarTopWindows.Concat(appBarBottomWindows).Where(nw => nw != null && nw.Handle != IntPtr.Zero).ForEach(ab => ab.Destroy());
			}
			appBarTopWindows = new AppBarNativeWindow[workspacesCount];
			appBarBottomWindows = new AppBarNativeWindow[workspacesCount];
			workspaceBarsEquivalentClasses = new int[workspacesCount];

			var listOfUniqueBars = new LinkedList<Tuple<IEnumerable<IBar>, IEnumerable<IBar>, int>>();
			var listOfUniqueTopAppBars = new LinkedList<AppBarNativeWindow>();
			var listOfUniqueBottomAppBars = new LinkedList<AppBarNativeWindow>();

			int i = 0, last = 0;
			foreach (var workspace in workspaces)
			{
				var matchingBar = listOfUniqueBars.FirstOrDefault(uniqueBar =>
					workspace.barsAtTop.SequenceEqual(uniqueBar.Item1) && workspace.barsAtBottom.SequenceEqual(uniqueBar.Item2));
				if (matchingBar != null)
				{
					workspaceBarsEquivalentClasses[i] = matchingBar.Item3;
				}
				else
				{
					workspaceBarsEquivalentClasses[i] = ++last;
					listOfUniqueBars.AddLast(new Tuple<IEnumerable<IBar>, IEnumerable<IBar>, int>(workspace.barsAtTop, workspace.barsAtBottom, last));
				}

				var topBarsHeight = workspace.barsAtTop.Sum(bar => bar.GetBarHeight());
				var matchingAppBar = listOfUniqueTopAppBars.FirstOrDefault(uniqueAppBar =>
					(uniqueAppBar == null && workspace.barsAtTop.Count == 0) || (uniqueAppBar != null && topBarsHeight == uniqueAppBar.Height));
				if (matchingAppBar != null || workspace.barsAtTop.Count == 0)
				{
					appBarTopWindows[i] = matchingAppBar;
				}
				else
				{
					appBarTopWindows[i] = new AppBarNativeWindow(topBarsHeight, true);
					listOfUniqueTopAppBars.AddLast(appBarTopWindows[i]);
				}

				var bottomBarsHeight = workspace.barsAtBottom.Sum(bar => bar.GetBarHeight());
				matchingAppBar = listOfUniqueBottomAppBars.FirstOrDefault(uniqueAppBar =>
					(uniqueAppBar == null && workspace.barsAtBottom.Count == 0) || (uniqueAppBar != null && bottomBarsHeight == uniqueAppBar.Height));
				if (matchingAppBar != null || workspace.barsAtBottom.Count == 0)
				{
					appBarBottomWindows[i] = matchingAppBar;
				}
				else
				{
					appBarBottomWindows[i] = new AppBarNativeWindow(bottomBarsHeight, false);
					listOfUniqueBottomAppBars.AddLast(appBarBottomWindows[i]);
				}

				i++;
			}
		}

		private static void RegisterBar(IBar bar)
		{
			var appBarData = new NativeMethods.APPBARDATA
				{
					hWnd = bar.Handle
				};

			NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_NEW, ref appBarData);

			registeredBars.Add(bar);
		}

		private static void UnregisterBar(IBar bar)
		{
			var appBarData = new NativeMethods.APPBARDATA
				{
					hWnd = bar.Handle
				};

			NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_REMOVE, ref appBarData);
		}

		internal void SwitchTo()
		{
			// hides or shows the Windows taskbar
			if (this.ShowWindowsTaskbar != isWindowsTaskbarShown)
			{
				ShowHideWindowsTaskbar(ShowWindowsTaskbar);
			}

			// hides the Bars for the old workspace and shows the new ones
			if (workspaceBarsEquivalentClasses[Windawesome.PreviousWorkspace - 1] != workspaceBarsEquivalentClasses[this.id - 1])
			{
				HideShowBars(appBarTopWindows[Windawesome.PreviousWorkspace - 1], appBarBottomWindows[Windawesome.PreviousWorkspace - 1],
					appBarTopWindows[this.id - 1], appBarBottomWindows[this.id - 1]);
			}

			// sets the layout- and workspace-specific changes to the windows
			sharedWindows.ForEach(RestoreSharedWindowState);
			if (removedSharedWindows.Count > 0)
			{
				removedSharedWindows.ForEach(w => sharedWindows.Remove(w));
				removedSharedWindows.Clear();
			}

			if (hasChanges || repositionOnSwitchedTo)
			{
				// Repositions if there is/are new/deleted windows
				Reposition();
				hasChanges = false;
			}

			IsCurrentWorkspace = true;

			DoWorkspaceChangedTo(this);
		}

		internal void Unswitch()
		{
			sharedWindows.Where(w => !repositionOnSwitchedTo || w.IsFloating || Layout.ShouldSaveAndRestoreSharedWindowsPosition()).ForEach(w => w.SavePosition());

			IsCurrentWorkspace = false;

			DoWorkspaceChangedFrom(this);
		}

		private void RestoreSharedWindowState(Window window)
		{
			window.Initialize();
			if ((!hasChanges && !repositionOnSwitchedTo) || window.IsFloating || Layout.ShouldSaveAndRestoreSharedWindowsPosition())
			{
				window.RestorePosition();
			}
		}

		public void Reposition()
		{
			Layout.Reposition(managedWindows);
		}

		public void ChangeLayout(ILayout layout)
		{
			if (layout.LayoutName() != this.Layout.LayoutName())
			{
				var oldLayout = this.Layout;
				this.Layout = layout;
				Reposition();
				DoWorkspaceLayoutChanged(this, oldLayout);
			}
		}

		private static void ShowHideWindowsTaskbar(bool showWindowsTaskbar)
		{
			var appBarData = new NativeMethods.APPBARDATA();
			var state = (NativeMethods.ABS) (uint) NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_GETSTATE, ref appBarData);

			appBarData.hWnd = taskbarHandle;
			appBarData.lParam = (IntPtr) (showWindowsTaskbar ? state & ~NativeMethods.ABS.ABS_AUTOHIDE : state | NativeMethods.ABS.ABS_AUTOHIDE);
			NativeMethods.SHAppBarMessage(NativeMethods.ABM.ABM_SETSTATE, ref appBarData);

			var showHide = showWindowsTaskbar ? NativeMethods.SW.SW_SHOWNA : NativeMethods.SW.SW_HIDE;

			NativeMethods.ShowWindowAsync(taskbarHandle, showHide);
			if (Windawesome.isAtLeastVista)
			{
				NativeMethods.ShowWindowAsync(startButtonHandle, showHide);
			}

			isWindowsTaskbarShown = showWindowsTaskbar;
		}

		private void HideShowBars(AppBarNativeWindow previousAppBarTopWindow, AppBarNativeWindow previousAppBarBottomWindow,
			AppBarNativeWindow newAppBarTopWindow, AppBarNativeWindow newAppBarBottomWindow)
		{
			HideShowAppBarForms(previousAppBarTopWindow, newAppBarTopWindow);
			HideShowAppBarForms(previousAppBarBottomWindow, newAppBarBottomWindow);

			// first show new bars and only after that hide the old ones to avoid flickering
			PositionBars(newAppBarTopWindow, newAppBarBottomWindow);

			shownBars.Except(barsAtTop.Concat(barsAtBottom)).ForEach(bar => bar.Hide());

			shownBars = barsAtTop.Concat(barsAtBottom);
		}

		private static void HideShowAppBarForms(AppBarNativeWindow hideForm, AppBarNativeWindow showForm)
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
						showForm.SetPosition();
					}
				}
			}
			else if (showForm != null)
			{
				showForm.SetPosition();
			}
		}

		private void PositionBars(AppBarNativeWindow newAppBarTopWindow, AppBarNativeWindow newAppBarBottomWindow)
		{
			var winPosInfo = NativeMethods.BeginDeferWindowPos(barsAtTop.Count + barsAtBottom.Count);
			if (newAppBarTopWindow != null)
			{
				winPosInfo = newAppBarTopWindow.PositionBars(winPosInfo, barsAtTop);
			}
			if (newAppBarBottomWindow != null)
			{
				winPosInfo = newAppBarBottomWindow.PositionBars(winPosInfo, barsAtBottom);
			}
			NativeMethods.EndDeferWindowPos(winPosInfo);
		}

		internal void ToggleWindowsTaskbarVisibility()
		{
			ShowWindowsTaskbar = !ShowWindowsTaskbar;
			ShowHideWindowsTaskbar(ShowWindowsTaskbar);
			Reposition();
		}

		internal bool HideBar(int workspacesCount, IEnumerable<Workspace> workspaces, IBar hideBar)
		{
			shownBars = shownBars.ToArray(); // because we need to hide the bar after that so we need to save the currently shown ones
			if (barsAtTop.Remove(hideBar) || barsAtBottom.Remove(hideBar))
			{
				FindWorkspaceBarsEquivalentClasses(workspacesCount, workspaces);
				HideShowBars(null, null, appBarTopWindows[this.id - 1], appBarBottomWindows[this.id - 1]);

				Reposition();

				return true;
			}

			return false;
		}

		internal bool ShowBar(int workspacesCount, IEnumerable<Workspace> workspaces, IBar showBar, bool top, int position)
		{
			if (!barsAtTop.Contains(showBar) && !barsAtBottom.Contains(showBar))
			{
				var bars = top ? barsAtTop : barsAtBottom;
				var bar = bars.First;
				while (bar != null && position-- > 0)
				{
					bar = bar.Next;
				}
				if (bar != null)
				{
					bars.AddBefore(bar, showBar);
				}
				else
				{
					bars.AddFirst(showBar);
				}

				FindWorkspaceBarsEquivalentClasses(workspacesCount, workspaces);
				HideShowBars(null, null, appBarTopWindows[this.id - 1], appBarBottomWindows[this.id - 1]);

				Reposition();

				return true;
			}

			return false;
		}

		internal void SetTopManagedWindowAsForeground()
		{
			var topmost = GetTopmostWindow();
			if (topmost != null)
			{
				Windawesome.ForceForegroundWindow(topmost);
			}
			else
			{
				Windawesome.ForceForegroundWindow(NativeMethods.GetShellWindow());
			}
		}

		internal void WindowMinimized(IntPtr hWnd)
		{
			var window = MoveWindowToBottom(hWnd);
			if (window != null)
			{
				window.DoForSelfOrOwned(w =>
					{
						if (!w.IsMinimized)
						{
							w.IsMinimized = true;
							if (managedWindows.Remove(w))
							{
								Layout.WindowMinimized(w, managedWindows);
							}
						}
					});

				window.IsMinimized = true;

				DoWorkspaceApplicationMinimized(this, window);
			}
		}

		internal void WindowRestored(IntPtr hWnd)
		{
			var window = MoveWindowToTop(hWnd);
			if (window != null)
			{
				window.DoForSelfOrOwned(w =>
					{
						if (w.IsMinimized)
						{
							w.IsMinimized = false;
							if (!w.IsFloating)
							{
								managedWindows.AddFirst(w);
								Layout.WindowRestored(w, managedWindows);
							}
						}
					});

				window.IsMinimized = false;

				DoWorkspaceApplicationRestored(this, window);
			}
		}

		public const int minimizeRestoreDelay = 300;
		internal void WindowActivated(IntPtr hWnd)
		{
			Window window;
			if (hWnd == IntPtr.Zero && windows.Count > 0)
			{
				window = windows.First.Value;
				if (!window.IsMinimized)
				{
					// sometimes Windows doesn't send a HSHELL_GETMINRECT message on minimize
					System.Threading.Thread.Sleep(minimizeRestoreDelay);
					if (NativeMethods.IsIconic(window.hWnd))
					{
						WindowMinimized(window.hWnd);
					}
				}
			}
			else if ((window = MoveWindowToTop(hWnd)) != null)
			{
				if (window.IsMinimized)
				{
					System.Threading.Thread.Sleep(minimizeRestoreDelay);
					if (!NativeMethods.IsIconic(hWnd))
					{
						// sometimes Windows doesn't send a HSHELL_GETMINRECT message on restore
						WindowRestored(hWnd);
						return ;
					}
				}
				else if (windows.Count > 1)
				{
					var secondZOrderWindow = windows.First.Next.Value;
					if (!secondZOrderWindow.IsMinimized)
					{
						// sometimes Windows doesn't send a HSHELL_GETMINRECT message on minimize
						System.Threading.Thread.Sleep(minimizeRestoreDelay);
						if (NativeMethods.IsIconic(secondZOrderWindow.hWnd))
						{
							WindowMinimized(secondZOrderWindow.hWnd);
						}
					}
				}
			}

			DoWindowActivated(hWnd);
		}

		internal void WindowCreated(Window window)
		{
			windows.AddFirst(window);
			if (window.WorkspacesCount > 1)
			{
				window.DoForSelfOrOwned(w => sharedWindows.AddFirst(w));
			}
			if (window.ShowInTabs)
			{
				windowsShownInTabsCount++;
			}
			if (IsCurrentWorkspace || window.WorkspacesCount == 1)
			{
				window.DoForSelfOrOwned(w => w.Initialize());
			}

			window.DoForSelfOrOwned(w =>
				{
					if (w.IsFloating)
					{
						floatingWindowsCount++;
					}
					else if (!w.IsMinimized)
					{
						managedWindows.AddFirst(w);
						Layout.WindowCreated(w, managedWindows, IsCurrentWorkspace);

						hasChanges |= !IsCurrentWorkspace;
					}
				});

			DoWorkspaceApplicationAdded(this, window);
		}

		internal void WindowDestroyed(Window window, bool setForeground = true)
		{
			windows.Remove(window);
			if (window.WorkspacesCount > 1)
			{
				window.DoForSelfOrOwned(w => sharedWindows.Remove(w));
			}
			if (window.ShowInTabs)
			{
				windowsShownInTabsCount--;
			}

			window.DoForSelfOrOwned(w =>
				{
					if (w.IsFloating)
					{
						floatingWindowsCount--;
					}
					else if (!w.IsMinimized)
					{
						managedWindows.Remove(w);
						Layout.WindowDestroyed(w, managedWindows, IsCurrentWorkspace);

						hasChanges |= !IsCurrentWorkspace;
					}
				});

			if (IsCurrentWorkspace && setForeground)
			{
				SetTopManagedWindowAsForeground(); // TODO: perhaps switch to the last window that was foreground?
			}

			DoWorkspaceApplicationRemoved(this, window);
		}

		public bool ContainsWindow(IntPtr hWnd)
		{
			return windows.Any(w => w.hWnd == hWnd);
		}

		public Window GetWindow(IntPtr hWnd)
		{
			return managedWindows.FirstOrDefault(w => w.hWnd == hWnd);
		}

		public int GetWindowsCount()
		{
			return windows.Count;
		}

		internal Window GetOwnermostWindow(IntPtr hWnd)
		{
			return windows.FirstOrDefault(w => w.hWnd == hWnd);
		}

		internal void ToggleWindowFloating(Window window)
		{
			if (window != null)
			{
				window.DoForSelfOrOwned(w =>
					{
						w.IsFloating = !w.IsFloating;
						if (w.IsFloating)
						{
							floatingWindowsCount++;
							managedWindows.Remove(w);
							Layout.WindowDestroyed(w, managedWindows, IsCurrentWorkspace);
						}
						else
						{
							floatingWindowsCount--;
							managedWindows.AddFirst(w);
							Layout.WindowCreated(w, managedWindows, IsCurrentWorkspace);
						}
					});
			}
		}

		internal static void ToggleShowHideWindowInTaskbar(Window window)
		{
			if (window != null)
			{
				window.ToggleShowHideInTaskbar();
			}
		}

		internal void ToggleShowHideWindowTitlebar(IntPtr hWnd)
		{
			var window = GetWindow(hWnd);
			if (window != null)
			{
				window.ToggleShowHideTitlebar();
				Layout.WindowTitlebarToggled(window, managedWindows);
			}
		}

		internal void ToggleShowHideWindowBorder(IntPtr hWnd)
		{
			var window = GetWindow(hWnd);
			if (window != null)
			{
				window.ToggleShowHideWindowBorder();
				Layout.WindowBorderToggled(window, managedWindows);
			}
		}

		internal void ToggleShowHideWindowMenu(IntPtr hWnd)
		{
			var window = GetWindow(hWnd);
			if (window != null)
			{
				window.ToggleShowHideWindowMenu();
			}
		}

		internal void Initialize(bool startingWorkspace)
		{
			// I'm adding to the front of the list in WindowCreated, however EnumWindows enums
			// from the top of the Z-order to the bottom, so I need to reverse the list
			var newWindows = windows.ToArray();
			windows.Clear();
			newWindows.ForEach(w => windows.AddFirst(w));

			if (startingWorkspace)
			{
				ShowHideWindowsTaskbar(ShowWindowsTaskbar);

				HideShowBars(null, null, appBarTopWindows[this.id - 1], appBarBottomWindows[this.id - 1]);
			}
			else
			{
				windows.ForEach(w => w.Hide());
			}
		}

		private LinkedListNode<Window> GetWindowNode(IntPtr hWnd)
		{
			for (var node = windows.First; node != null; node = node.Next)
			{
				if (node.Value.hWnd == hWnd)
				{
					return node;
				}
			}

			return null;
		}

		private Window MoveWindowToTop(IntPtr hWnd)
		{
			var node = GetWindowNode(hWnd);

			if (node != null)
			{
				if (node != windows.First)
				{
					// adds the window to the front of the list, i.e. the top of the Z order
					windows.Remove(node);
					windows.AddFirst(node);
				}

				return node.Value;
			}

			return null;
		}

		private Window MoveWindowToBottom(IntPtr hWnd)
		{
			var node = GetWindowNode(hWnd);

			if (node != null)
			{
				if (node != windows.First)
				{
					// adds the window to the back of the list, i.e. the bottom of the Z order
					windows.Remove(node);
					windows.AddLast(node);
				}

				return node.Value;
			}

			return null;
		}

		public Window GetTopmostWindow()
		{
			var window = windows.FirstOrDefault();
			return (window != null && !window.IsMinimized) ? window : null;
		}

		internal void AddToSharedWindows(Window window)
		{
			window.DoForSelfOrOwned(w => sharedWindows.AddFirst(w));
		}

		internal void AddToRemovedSharedWindows(Window window)
		{
			window.DoForSelfOrOwned(w => removedSharedWindows.AddFirst(w));
		}

		internal LinkedList<Window> GetWindows()
		{
			return windows;
		}
	}

	public class Window
	{
		public readonly IntPtr hWnd;
		public bool IsFloating { get; internal set; }
		public bool ShowInTabs { get; private set; }
		public State Titlebar { get; internal set; }
		public State InTaskbar { get; internal set; }
		public State WindowBorders { get; internal set; }
		public int WorkspacesCount { get; internal set; } // if > 1 window is shared between two or more workspaces
		public bool IsMinimized { get; internal set; }
		public string DisplayName { get; internal set; }
		public readonly string className;
		public readonly string processName;
		public readonly bool is64BitProcess;
		public readonly bool redrawOnShow;
		public readonly bool activateLastActivePopup;
		public readonly bool hideOwnedPopups;
		public bool ShowMenu { get; internal set; }
		public readonly OnWindowShownAction onHiddenWindowShownAction;
		public readonly IntPtr menu;

		private readonly LinkedList<Window> ownedWindows;

		private readonly NativeMethods.WS titlebarStyle;

		private readonly NativeMethods.WS borderStyle;
		private readonly NativeMethods.WS_EX borderExStyle;

		private NativeMethods.WINDOWPLACEMENT windowPlacement;
		private readonly NativeMethods.WINDOWPLACEMENT originalWindowPlacement;

		internal Window(IntPtr hWnd, string className, string displayName, string processName, int workspacesCount, bool is64BitProcess,
			NativeMethods.WS originalStyle, NativeMethods.WS_EX originalExStyle, LinkedList<Window> ownedWindows, ProgramRule.Rule rule, ProgramRule programRule,
			IntPtr menu)
		{
			this.hWnd = hWnd;
			IsFloating = rule.isFloating;
			ShowInTabs = rule.showInTabs;
			Titlebar = rule.titlebar;
			InTaskbar = rule.inTaskbar;
			WindowBorders = rule.windowBorders;
			this.WorkspacesCount = workspacesCount;
			this.IsMinimized = NativeMethods.IsIconic(hWnd);
			this.DisplayName = displayName;
			this.className = className;
			this.processName = processName;
			this.is64BitProcess = is64BitProcess;
			redrawOnShow = rule.redrawOnShow;
			activateLastActivePopup = rule.activateLastActivePopup;
			hideOwnedPopups = programRule.hideOwnedPopups;
			ShowMenu = programRule.showMenu;
			onHiddenWindowShownAction = programRule.onHiddenWindowShownAction;
			this.menu = menu;

			this.ownedWindows = ownedWindows;

			titlebarStyle = 0;
			titlebarStyle |= originalStyle & NativeMethods.WS.WS_CAPTION;
			titlebarStyle |= originalStyle & NativeMethods.WS.WS_MINIMIZEBOX;
			titlebarStyle |= originalStyle & NativeMethods.WS.WS_MAXIMIZEBOX;
			titlebarStyle |= originalStyle & NativeMethods.WS.WS_SYSMENU;

			borderStyle = 0;
			borderStyle |= originalStyle & NativeMethods.WS.WS_SIZEBOX;

			borderExStyle = 0;
			borderExStyle |= originalExStyle & NativeMethods.WS_EX.WS_EX_DLGMODALFRAME;
			borderExStyle |= originalExStyle & NativeMethods.WS_EX.WS_EX_CLIENTEDGE;
			borderExStyle |= originalExStyle & NativeMethods.WS_EX.WS_EX_STATICEDGE;
			borderExStyle |= originalExStyle & NativeMethods.WS_EX.WS_EX_WINDOWEDGE;

			windowPlacement = NativeMethods.WINDOWPLACEMENT.Default;
			SavePosition();
			originalWindowPlacement = windowPlacement;
		}

		internal Window(Window window)
		{
			hWnd = window.hWnd;
			this.IsFloating = window.IsFloating;
			this.ShowInTabs = window.ShowInTabs;
			this.Titlebar = window.Titlebar;
			this.InTaskbar = window.InTaskbar;
			this.WindowBorders = window.WindowBorders;
			this.WorkspacesCount = window.WorkspacesCount;
			IsMinimized = window.IsMinimized;
			this.DisplayName = window.DisplayName;
			className = window.className;
			processName = window.processName;
			is64BitProcess = window.is64BitProcess;
			redrawOnShow = window.redrawOnShow;
			activateLastActivePopup = window.activateLastActivePopup;
			hideOwnedPopups = window.hideOwnedPopups;
			ShowMenu = window.ShowMenu;
			onHiddenWindowShownAction = window.onHiddenWindowShownAction;
			menu = window.menu;

			if (window.ownedWindows != null)
			{
				this.ownedWindows = new LinkedList<Window>(window.ownedWindows.Select(w => new Window(w)));
			}

			titlebarStyle = window.titlebarStyle;

			borderStyle = window.borderStyle;
			borderExStyle = window.borderExStyle;

			windowPlacement = window.windowPlacement;
			originalWindowPlacement = window.originalWindowPlacement;
		}

		public override int GetHashCode()
		{
			return hWnd.GetHashCode();
		}

		public override bool Equals(object obj)
		{
			var window = obj as Window;
			return window != null && window.hWnd == hWnd;
		}

		internal void Initialize()
		{
			var style = NativeMethods.GetWindowStyleLongPtr(hWnd);
			var exStyle = NativeMethods.GetWindowExStyleLongPtr(hWnd);
			var prevStyle = style;
			var prevExStyle = exStyle;

			switch (this.InTaskbar)
			{
				case State.SHOWN:
					exStyle = (exStyle | NativeMethods.WS_EX.WS_EX_APPWINDOW) & ~NativeMethods.WS_EX.WS_EX_TOOLWINDOW;
					break;
				case State.HIDDEN:
					exStyle = (exStyle & ~NativeMethods.WS_EX.WS_EX_APPWINDOW) | NativeMethods.WS_EX.WS_EX_TOOLWINDOW;
					break;
			}
			switch (this.Titlebar)
			{
				case State.SHOWN:
					style |= titlebarStyle;
					break;
				case State.HIDDEN:
					style &= ~titlebarStyle;
					break;
			}
			switch (this.WindowBorders)
			{
				case State.SHOWN:
					style	|= borderStyle;
					exStyle |= borderExStyle;
					break;
				case State.HIDDEN:
					style	&= ~borderStyle;
					exStyle &= ~borderExStyle;
					break;
			}

			if (style != prevStyle)
			{
				NativeMethods.SetWindowStyleLongPtr(hWnd, style);
			}
			if (exStyle != prevExStyle)
			{
				NativeMethods.SetWindowExStyleLongPtr(hWnd, exStyle);
			}

			if (style != prevStyle || exStyle != prevExStyle)
			{
				Redraw();
			}
		}

		internal void ToggleShowHideInTaskbar()
		{
			this.InTaskbar = (State) (((int) this.InTaskbar + 1) % 2);
			Initialize();
		}

		internal void ToggleShowHideTitlebar()
		{
			this.Titlebar = (State) (((int) this.Titlebar + 1) % 2);
			Initialize();
		}

		internal void ToggleShowHideWindowBorder()
		{
			this.WindowBorders = (State) (((int) this.WindowBorders + 1) % 2);
			Initialize();
		}

		internal void ToggleShowHideWindowMenu()
		{
			ShowMenu = !ShowMenu;
			ShowWindowMenu();
		}

		internal void Redraw()
		{
			// this whole thing is a hack but I've found no other way to make it work (and I've tried
			// a zillion things). Resizing seems to do the best job.
			NativeMethods.RECT rect;
			NativeMethods.GetWindowRect(hWnd, out rect);
			NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top - 1,
				NativeMethods.SWP.SWP_ASYNCWINDOWPOS | NativeMethods.SWP.SWP_FRAMECHANGED | NativeMethods.SWP.SWP_NOMOVE |
				NativeMethods.SWP.SWP_NOZORDER | NativeMethods.SWP.SWP_NOACTIVATE |
				NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_NOCOPYBITS);
			NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top,
				NativeMethods.SWP.SWP_ASYNCWINDOWPOS | NativeMethods.SWP.SWP_FRAMECHANGED | NativeMethods.SWP.SWP_NOMOVE |
				NativeMethods.SWP.SWP_NOZORDER | NativeMethods.SWP.SWP_NOACTIVATE |
				NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_NOCOPYBITS);

			NativeMethods.RedrawWindow(hWnd, IntPtr.Zero, IntPtr.Zero,
				NativeMethods.RedrawWindowFlags.RDW_ALLCHILDREN |
				NativeMethods.RedrawWindowFlags.RDW_ERASE |
				NativeMethods.RedrawWindowFlags.RDW_INVALIDATE);
		}

		internal void SavePosition()
		{
			NativeMethods.GetWindowPlacement(hWnd, ref windowPlacement);
		}

		internal void RestorePosition()
		{
			switch (windowPlacement.ShowCmd)
			{
				case NativeMethods.SW.SW_SHOWNORMAL:
					windowPlacement.ShowCmd = NativeMethods.SW.SW_SHOWNOACTIVATE;
					break;
				case NativeMethods.SW.SW_SHOW:
					windowPlacement.ShowCmd = NativeMethods.SW.SW_SHOWNA;
					break;
				case NativeMethods.SW.SW_SHOWMINIMIZED:
					windowPlacement.ShowCmd = NativeMethods.SW.SW_SHOWMINNOACTIVE;
					break;
			}
			windowPlacement.Flags |= NativeMethods.WindowPlacementFlags.WPF_ASYNCWINDOWPLACEMENT;
			NativeMethods.SetWindowPlacement(hWnd, ref windowPlacement);
		}

		internal void ShowPopupsAndRedraw()
		{
			if (hideOwnedPopups)
			{
				NativeMethods.ShowOwnedPopups(hWnd, true);
			}

			if (redrawOnShow)
			{
				Redraw();
			}
		}

		internal void Show()
		{
			ShowPopupsAndRedraw();
			NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW.SW_SHOWNA);
		}

		internal void HidePopups()
		{
			if (hideOwnedPopups)
			{
				NativeMethods.ShowOwnedPopups(hWnd, false);
			}
		}

		internal void Hide()
		{
			HidePopups();
			NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW.SW_HIDE);
		}

		internal void ShowWindowMenu()
		{
			if (menu != IntPtr.Zero)
			{
				if (ShowMenu)
				{
					NativeMethods.SetMenu(hWnd, menu);
				}
				else
				{
					NativeMethods.SetMenu(hWnd, IntPtr.Zero);
				}
			}
		}

		internal void DoForSelfOrOwned(Action<Window> action)
		{
			if (ownedWindows != null)
			{
				ownedWindows.ForEach(action);
			}
			else
			{
				action(this);
			}
		}

		internal void RevertToInitialValues()
		{
			if (this.Titlebar != State.AS_IS)
			{
				this.Titlebar = State.SHOWN;
			}
			if (this.InTaskbar != State.AS_IS)
			{
				this.InTaskbar = State.SHOWN;
			}
			if (this.WindowBorders != State.AS_IS)
			{
				this.WindowBorders = State.SHOWN;
			}
			Initialize();

			if (!ShowMenu)
			{
				ToggleShowHideWindowMenu();
			}

			windowPlacement = originalWindowPlacement;
			RestorePosition();
		}
	}
}
