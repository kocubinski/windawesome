using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public sealed class Workspace
	{
		public readonly int id;
		public Monitor Monitor { get; internal set; }
		public ILayout Layout { get; private set; }
		public readonly LinkedList<IBar>[] barsAtTop;
		public readonly LinkedList<IBar>[] barsAtBottom;
		public readonly string name;
		public readonly bool repositionOnSwitchedTo;
		public bool ShowWindowsTaskbar { get; private set; }
		public bool IsWorkspaceVisible { get; private set; }

		private bool isCurrentWorkspace;
		public bool IsCurrentWorkspace
		{
			get { return isCurrentWorkspace; }

			internal set
			{
				isCurrentWorkspace = value;
				if (isCurrentWorkspace)
				{
					DoWorkspaceActivated(this);
				}
				else
				{
					DoWorkspaceDeactivated(this);
				}
			}
		}

		internal bool hasChanges;
		internal int hideFromAltTabWhenOnInactiveWorkspaceCount;

		private int sharedWindowsCount;

		private readonly LinkedList<Window> windows; // all windows, owner window, sorted in Z-order, topmost window first

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

		public delegate void WorkspaceHiddenEventHandler(Workspace workspace);
		public static event WorkspaceHiddenEventHandler WorkspaceHidden;

		public delegate void WorkspaceShownEventHandler(Workspace workspace);
		public static event WorkspaceShownEventHandler WorkspaceShown;

		public delegate void WorkspaceActivatedEventHandler(Workspace workspace);
		public static event WorkspaceActivatedEventHandler WorkspaceActivated;

		public delegate void WorkspaceDeactivatedEventHandler(Workspace workspace);
		public static event WorkspaceDeactivatedEventHandler WorkspaceDeactivated;

		public delegate void WorkspaceMonitorChangedEventHandler(Workspace workspace, Monitor oldMonitor, Monitor newMonitor);
		public static event WorkspaceMonitorChangedEventHandler WorkspaceMonitorChanged;

		public delegate void WorkspaceLayoutChangedEventHandler(Workspace workspace, ILayout oldLayout);
		public static event WorkspaceLayoutChangedEventHandler WorkspaceLayoutChanged;

		public delegate void WindowActivatedEventHandler(IntPtr hWnd);
		public static event WindowActivatedEventHandler WindowActivatedEvent;

		public delegate void WindowTitlebarToggledEventHandler(Window window);
		public event WindowTitlebarToggledEventHandler WindowTitlebarToggled;

		public delegate void WindowBorderToggledEventHandler(Window window);
		public event WindowBorderToggledEventHandler WindowBorderToggled;

		public delegate void LayoutUpdatedEventHandler();
		public static event LayoutUpdatedEventHandler LayoutUpdated; // TODO: this should be for a specific workspace. But how to call from Widgets then?

		private static void DoWorkspaceApplicationAdded(Workspace workspace, Window window)
		{
			if (WorkspaceApplicationAdded != null)
			{
				WorkspaceApplicationAdded(workspace, window);
			}
		}

		private static void DoWorkspaceApplicationRemoved(Workspace workspace, Window window)
		{
			if (WorkspaceApplicationRemoved != null)
			{
				WorkspaceApplicationRemoved(workspace, window);
			}
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

		private static void DoWorkspaceHidden(Workspace workspace)
		{
			if (WorkspaceHidden != null)
			{
				WorkspaceHidden(workspace);
			}
		}

		private static void DoWorkspaceShown(Workspace workspace)
		{
			if (WorkspaceShown != null)
			{
				WorkspaceShown(workspace);
			}
		}

		private static void DoWorkspaceActivated(Workspace workspace)
		{
			if (WorkspaceActivated != null)
			{
				WorkspaceActivated(workspace);
			}
		}

		private static void DoWorkspaceDeactivated(Workspace workspace)
		{
			if (WorkspaceDeactivated != null)
			{
				WorkspaceDeactivated(workspace);
			}
		}

		private static void DoWorkspaceMonitorChanged(Workspace workspace, Monitor oldMonitor, Monitor newMonitor)
		{
			if (WorkspaceMonitorChanged != null)
			{
				WorkspaceMonitorChanged(workspace, oldMonitor, newMonitor);
			}
		}

		private static void DoWorkspaceLayoutChanged(Workspace workspace, ILayout oldLayout)
		{
			if (WorkspaceLayoutChanged != null)
			{
				WorkspaceLayoutChanged(workspace, oldLayout);
			}
		}

		private static void DoWindowActivated(IntPtr hWnd)
		{
			if (WindowActivatedEvent != null)
			{
				WindowActivatedEvent(hWnd);
			}
		}

		private void DoWindowTitlebarToggled(Window window)
		{
			if (WindowTitlebarToggled != null)
			{
				WindowTitlebarToggled(window);
			}
		}

		private void DoWindowBorderToggled(Window window)
		{
			if (WindowBorderToggled != null)
			{
				WindowBorderToggled(window);
			}
		}

		public static void DoLayoutUpdated()
		{
			if (LayoutUpdated != null)
			{
				LayoutUpdated();
			}
		}

		#endregion

		public Workspace(Monitor monitor, ILayout layout, IEnumerable<IBar> barsAtTop = null, IEnumerable<IBar> barsAtBottom = null,
			string name = "", bool showWindowsTaskbar = false, bool repositionOnSwitchedTo = false)
		{
			windows = new LinkedList<Window>();

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
			layout.Initialize(this);
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
			if (sharedWindowsCount > 0)
			{
				// sets the layout- and workspace-specific changes to the windows
				windows.Where(w => w.WorkspacesCount > 1).ForEach(w => w.DoForSelfOrOwned(win => RestoreSharedWindowState(win, false)));
			}

			if (NeedsToReposition())
			{
				// Repositions if there is/are new/deleted windows
				Reposition();
			}

			IsWorkspaceVisible = true;
			DoWorkspaceShown(this);
		}

		internal void Unswitch()
		{
			if (sharedWindowsCount > 0)
			{
				windows.Where(w => w.WorkspacesCount > 1 && ShouldSaveAndRestoreSharedWindowsPosition(w)).
					ForEach(w => w.DoForSelfOrOwned(win => win.SavePosition()));
			}

			IsWorkspaceVisible = false;
			DoWorkspaceHidden(this);
		}

		private void RestoreSharedWindowState(Window window, bool doNotShow)
		{
			window.Initialize();
			if (ShouldSaveAndRestoreSharedWindowsPosition(window))
			{
				window.RestorePosition(doNotShow);
			}
		}

		private bool ShouldSaveAndRestoreSharedWindowsPosition(Window window)
		{
			return !NeedsToReposition() || window.IsFloating || Layout.ShouldSaveAndRestoreSharedWindowsPosition();
		}

		public bool NeedsToReposition()
		{
			return hasChanges || repositionOnSwitchedTo;
		}

		public void Reposition()
		{
			Layout.Reposition();
			hasChanges = false;
		}

		public void ChangeLayout(ILayout layout)
		{
			if (layout.LayoutName() != this.Layout.LayoutName())
			{
				layout.Initialize(this);
				var oldLayout = this.Layout;
				this.Layout = layout;
				Reposition();
				DoWorkspaceLayoutChanged(this, oldLayout);
			}
		}

		internal void ToggleWindowsTaskbarVisibility()
		{
			if (Monitor.screen.Primary)
			{
				ShowWindowsTaskbar = !ShowWindowsTaskbar;
				Monitor.ShowHideWindowsTaskbar(ShowWindowsTaskbar);
				Reposition();
			}
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
							if (!w.IsFloating)
							{
								Layout.WindowMinimized(w);
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
								Layout.WindowRestored(w);
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
			if (windows.Count > 0)
			{
				Window window;
				if (hWnd == NativeMethods.shellWindow)
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
					else if (windows.First.Next != null)
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
			}

			DoWindowActivated(hWnd);
		}

		internal void WindowCreated(Window window)
		{
			windows.AddFirst(window);
			if (window.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace)
			{
				hideFromAltTabWhenOnInactiveWorkspaceCount++;
			}
			if (IsWorkspaceVisible || window.WorkspacesCount == 1)
			{
				window.DoForSelfOrOwned(w => w.Initialize());
			}

			window.DoForSelfOrOwned(w =>
				{
					if (w.WorkspacesCount > 1)
					{
						sharedWindowsCount++;
					}
					if (!w.IsMinimized && !w.IsFloating)
					{
						Layout.WindowCreated(w);

						hasChanges |= !IsWorkspaceVisible;
					}
				});

			DoWorkspaceApplicationAdded(this, window);
		}

		internal void WindowDestroyed(Window window)
		{
			windows.Remove(window);
			if (window.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace)
			{
				hideFromAltTabWhenOnInactiveWorkspaceCount--;
			}

			window.DoForSelfOrOwned(w =>
				{
					if (w.WorkspacesCount > 1)
					{
						sharedWindowsCount--;
					}
					if (!w.IsMinimized && !w.IsFloating)
					{
						Layout.WindowDestroyed(w);

						hasChanges |= !IsWorkspaceVisible;
					}
				});

			DoWorkspaceApplicationRemoved(this, window);
		}

		public bool ContainsWindow(IntPtr hWnd)
		{
			return windows.Any(w => w.hWnd == hWnd);
		}

		public Window GetManagedWindow(IntPtr hWnd)
		{
			return GetManagedWindows().FirstOrDefault(w => w.hWnd == hWnd);
		}

		public int GetWindowsCount()
		{
			return windows.Count;
		}

		internal Window GetWindow(IntPtr hWnd)
		{
			return windows.FirstOrDefault(w => w.hWnd == hWnd);
		}

		internal void ToggleWindowFloating(Window window)
		{
			if (window != null)
			{
				var windowIsFloating = window.IsFloating;
				window.DoForSelfOrOwned(w =>
					{
						w.IsFloating = !w.IsFloating;
						if (!w.IsMinimized)
						{
							if (w.IsFloating)
							{
								Layout.WindowDestroyed(w);
							}
							else
							{
								Layout.WindowCreated(w);
							}
						}
					});
				window.IsFloating = !windowIsFloating;
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
			var window = GetManagedWindow(hWnd);
			if (window != null)
			{
				window.ToggleShowHideTitlebar();
				DoWindowTitlebarToggled(window);
			}
		}

		internal void ToggleShowHideWindowBorder(IntPtr hWnd)
		{
			var window = GetManagedWindow(hWnd);
			if (window != null)
			{
				window.ToggleShowHideWindowBorder();
				DoWindowBorderToggled(window);
			}
		}

		internal void ToggleShowHideWindowMenu(IntPtr hWnd)
		{
			var window = GetManagedWindow(hWnd);
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
			window.DoForSelfOrOwned(_ => sharedWindowsCount++);
		}

		internal void RemoveFromSharedWindows(Window window)
		{
			window.DoForSelfOrOwned(w =>
				{
					RestoreSharedWindowState(w, !IsWorkspaceVisible);
					sharedWindowsCount--;
				});
		}

		internal LinkedList<Window> GetWindows()
		{
			return windows;
		}

		public IEnumerable<Window> GetManagedWindows()
		{
			foreach (var window in windows.Unless(w => w.IsMinimized || w.IsFloating))
			{
				if (window.ownedWindows != null)
				{
					foreach (var ownedWindow in window.ownedWindows)
					{
						yield return ownedWindow;
					}
				}
				else
				{
					yield return window;
				}
			}
		}
	}
}
