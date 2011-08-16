using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public sealed class Workspace
	{
		public readonly int id;
		public Monitor Monitor { get; private set; }
		public ILayout Layout { get; private set; }
		public readonly LinkedList<IBar>[] barsAtTop;
		public readonly LinkedList<IBar>[] barsAtBottom;
		public readonly string name;
		public bool ShowWindowsTaskbar { get; private set; }
		public bool IsCurrentWorkspace { get; internal set; }
		public bool IsWorkspaceVisible { get; private set; }
		public readonly bool repositionOnSwitchedTo;

		private int floatingWindowsCount;
		private int windowsShownInTabsCount;

		private readonly LinkedList<Window> windows; // all windows, owner window, sorted in Z-order, topmost window first
		private readonly LinkedList<Window> managedWindows; // windows.Where(w => !w.isFloating && !w.isMinimized), owned windows, not sorted
		private readonly LinkedList<Window> sharedWindows; // windows.Where(w => w.shared), not sorted
		private readonly LinkedList<Window> removedSharedWindows; // windows that need to be Initialized but then removed from shared
		internal bool hasChanges;

		private static int count;

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

		public Workspace(Monitor monitor, ILayout layout, IEnumerable<IBar> barsAtTop = null, IEnumerable<IBar> barsAtBottom = null, string name = "", bool showWindowsTaskbar = false,
			bool repositionOnSwitchedTo = false)
		{
			windows = new LinkedList<Window>();
			managedWindows = new LinkedList<Window>();
			sharedWindows = new LinkedList<Window>();
			removedSharedWindows = new LinkedList<Window>();

			this.id = ++count;
			this.Monitor = monitor;
			this.Layout = layout;
			this.barsAtTop = Screen.AllScreens.Select(_ => new LinkedList<IBar>()).ToArray();
			this.barsAtBottom = Screen.AllScreens.Select(_ => new LinkedList<IBar>()).ToArray();
			if (barsAtTop != null)
			{
				barsAtTop.ForEach(bar => this.barsAtTop[bar.Monitor.monitorIndex].AddLast(bar));
			}
			if (barsAtBottom != null)
			{
				barsAtBottom.ForEach(bar => this.barsAtBottom[bar.Monitor.monitorIndex].AddLast(bar));
			}
			this.name = name;
			this.ShowWindowsTaskbar = showWindowsTaskbar;
			this.repositionOnSwitchedTo = repositionOnSwitchedTo;
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

		internal void SwitchTo()
		{
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

			IsCurrentWorkspace = IsWorkspaceVisible = true;

			DoWorkspaceChangedTo(this);
		}

		internal void Unswitch()
		{
			sharedWindows.Where(w => !repositionOnSwitchedTo || w.IsFloating || Layout.ShouldSaveAndRestoreSharedWindowsPosition()).ForEach(w => w.SavePosition());

			IsCurrentWorkspace = IsWorkspaceVisible = false;

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

		internal void ToggleWindowsTaskbarVisibility()
		{
			ShowWindowsTaskbar = !ShowWindowsTaskbar;
			Monitor.ShowHideWindowsTaskbar(ShowWindowsTaskbar);
			Reposition();
		}

		//internal bool HideBar(int workspacesCount, IEnumerable<Workspace> workspaces, IBar hideBar)
		//{
		//    shownBars = shownBars.ToArray(); // because we need to hide the bar after that so we need to save the currently shown ones
		//    if (barsAtTop.Remove(hideBar) || barsAtBottom.Remove(hideBar))
		//    {
		//        FindWorkspaceBarsEquivalentClasses(workspacesCount, workspaces);
		//        ShowHideBars(null, null, appBarTopWindows[this.id - 1], appBarBottomWindows[this.id - 1]);

		//        Reposition();

		//        return true;
		//    }

		//    return false;
		//}

		//internal bool ShowBar(int workspacesCount, IEnumerable<Workspace> workspaces, IBar showBar, bool top, int position)
		//{
		//    if (!barsAtTop.Contains(showBar) && !barsAtBottom.Contains(showBar))
		//    {
		//        var bars = top ? barsAtTop : barsAtBottom;
		//        var bar = bars.First;
		//        while (bar != null && position-- > 0)
		//        {
		//            bar = bar.Next;
		//        }
		//        if (bar != null)
		//        {
		//            bars.AddBefore(bar, showBar);
		//        }
		//        else
		//        {
		//            bars.AddFirst(showBar);
		//        }

		//        FindWorkspaceBarsEquivalentClasses(workspacesCount, workspaces);
		//        ShowHideBars(null, null, appBarTopWindows[this.id - 1], appBarBottomWindows[this.id - 1]);

		//        Reposition();

		//        return true;
		//    }

		//    return false;
		//}

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
			if (IsWorkspaceVisible || window.WorkspacesCount == 1)
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
						Layout.WindowCreated(w, managedWindows, IsWorkspaceVisible);

						hasChanges |= !IsWorkspaceVisible;
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
						Layout.WindowDestroyed(w, managedWindows, IsWorkspaceVisible);

						hasChanges |= !IsWorkspaceVisible;
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
							Layout.WindowDestroyed(w, managedWindows, IsWorkspaceVisible);
						}
						else
						{
							floatingWindowsCount--;
							managedWindows.AddFirst(w);
							Layout.WindowCreated(w, managedWindows, IsWorkspaceVisible);
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

		internal void Initialize()
		{
			// I'm adding to the front of the list in WindowCreated, however EnumWindows enums
			// from the top of the Z-order to the bottom, so I need to reverse the list
			if (windows.Count > 0)
			{
				var newWindows = windows.ToArray();
				windows.Clear();
				newWindows.ForEach(w => windows.AddFirst(w));
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
}
