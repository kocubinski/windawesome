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
		public readonly BarInfo[] bars;
		public readonly string name;
		public bool ShowWindowsTaskbar { get; private set; }
		public bool IsCurrentWorkspace { get; private set; }
		public readonly bool repositionOnSwitchedTo;

		public class BarInfo
		{
			public readonly Bar bar;
			public readonly bool topBar;
			public bool ShowBar { get; internal set; }
			public Rectangle barPosition;

			public BarInfo(Bar bar, bool topBar = true, bool showBar = true)
			{
				this.bar = bar;
				this.topBar = topBar;
				this.ShowBar = showBar;
			}

			public override int GetHashCode()
			{
				return bar.GetHashCode() + barPosition.GetHashCode() + topBar.GetHashCode() + ShowBar.GetHashCode();
			}

			public override bool Equals(object obj)
			{
				var other = obj as BarInfo;
				return other != null && other.bar == this.bar && other.barPosition == this.barPosition &&
					other.ShowBar == this.ShowBar && other.topBar == this.topBar;
			}
		}

		private class ShownBarInfo
		{
			public readonly Bar bar;
			public readonly bool topBar;

			public ShownBarInfo(Bar bar, bool topBar)
			{
				this.bar = bar;
				this.topBar = topBar;
			}
		}

		private static int count;
		private static bool isWindowsTaskbarShown;
		private static readonly IntPtr taskbarHandle;
		private static readonly IntPtr startButtonHandle;
		private static IEnumerable<ShownBarInfo> shownBars;

		private int floatingWindowsCount;
		internal Rectangle workingArea;

		private LinkedList<Window> windows; // all windows, sorted in Z-order, topmost window first
		private readonly LinkedList<Window> managedWindows; // windows.Where(w => !w.isFloating && !w.isMinimized), not sorted
		private readonly LinkedList<Window> sharedWindows; // windows.Where(w => w.shared), not sorted
		private readonly LinkedList<Window> windowsShownInTabs; // windows.Where(w => w.showInTabs), not sorted
		private readonly LinkedList<Window> removedSharedWindows; // windows that need to be Initialized but then removed from shared
		internal bool hasChanges;

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
				return Layout.LayoutSymbol(windowsShownInTabs.Count);
			}
		}

		static Workspace()
		{
			taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
			if (Windawesome.isAtLeastVista)
			{
				startButtonHandle = NativeMethods.FindWindow("Button", "Start");
			}

			shownBars = new ShownBarInfo[0];

			count = 0;
		}

		public Workspace(ILayout layout, IEnumerable<BarInfo> bars, string name = "", bool showWindowsTaskbar = false,
			bool repositionOnSwitchedTo = false)
		{
			windows = new LinkedList<Window>();
			managedWindows = new LinkedList<Window>();
			sharedWindows = new LinkedList<Window>();
			windowsShownInTabs = new LinkedList<Window>();
			removedSharedWindows = new LinkedList<Window>();

			IsCurrentWorkspace = false;
			hasChanges = false;
			floatingWindowsCount = 0;

			this.id = ++count;
			this.Layout = layout;
			this.bars = bars.ToArray();
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

		private void SetWorkingAreaAndBarPositions()
		{
			SetWindowsTaskbarArea();

			SetBarPositions();

			ResizeBarWidgets();
		}

		private void ResizeBarWidgets()
		{
			this.bars.
				Where(bs => bs.bar.Leftmost != this.workingArea.Left || bs.bar.Rightmost != this.workingArea.Right).
				ForEach(bs => bs.bar.ResizeWidgets(this.workingArea.Left, this.workingArea.Right));
		}

		private void SetWindowsTaskbarArea()
		{
			var data = NativeMethods.APPBARDATA.Default;
			data.hWnd = taskbarHandle;
			NativeMethods.SHAppBarMessage(NativeMethods.AppBarMsg.ABM_GETTASKBARPOS, ref data);
			var taskbarWidth = data.rc.right - data.rc.left;
			var taskbarHeight = data.rc.bottom - data.rc.top;

			if (ShowWindowsTaskbar)
			{
				workingArea.Width -= (taskbarHeight == Screen.PrimaryScreen.Bounds.Height) ? taskbarWidth : 0;
				workingArea.Height -= (taskbarWidth == Screen.PrimaryScreen.Bounds.Width) ? taskbarHeight : 0;
				workingArea.X += (taskbarHeight == Screen.PrimaryScreen.Bounds.Height && data.rc.left == 0) ? taskbarWidth : 0;
				workingArea.Y += (taskbarWidth == Screen.PrimaryScreen.Bounds.Width && data.rc.top == 0) ? taskbarHeight : 0;
			}
			else
			{
				workingArea.Width += (taskbarHeight == Screen.PrimaryScreen.Bounds.Height) ? taskbarWidth : 0;
				workingArea.Height += (taskbarWidth == Screen.PrimaryScreen.Bounds.Width) ? taskbarHeight : 0;
				workingArea.X -= (taskbarHeight == Screen.PrimaryScreen.Bounds.Height && data.rc.left == 0) ? taskbarWidth : 0;
				workingArea.Y -= (taskbarWidth == Screen.PrimaryScreen.Bounds.Width && data.rc.top == 0) ? taskbarHeight : 0;
			}
		}

		private void SetBarPositions()
		{
			RestoreWorkingArea();

			foreach (var bs in bars.Where(bs => bs.ShowBar))
			{
				int y;
				if (bs.topBar)
				{
					y = this.workingArea.Y;
					this.workingArea.Y += bs.bar.barHeight;
				}
				else
				{
					y = this.workingArea.Bottom - bs.bar.barHeight;
				}
				this.workingArea.Height -= bs.bar.barHeight;

				bs.barPosition.Location = new Point(this.workingArea.X, y);
				bs.barPosition.Size = new Size(this.workingArea.Width, bs.bar.barHeight);
			}
		}

		internal void SwitchTo(bool setForeground = true)
		{
			// hides or shows the Windows taskbar
			if (this.ShowWindowsTaskbar != isWindowsTaskbarShown)
			{
				ShowHideWindowsTaskbar();
			}
			// hides or shows the Bars for this workspace
			else if (Windawesome.workspaceBarsEquivalentClasses[Windawesome.PreviousWorkspace - 1] != Windawesome.workspaceBarsEquivalentClasses[this.id - 1])
			{
				ShowBars();
			}

			// sets the layout- and workspace-specific changes to the windows
			sharedWindows.ForEach(SetSharedWindowChanges);
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

			ShowWindows();

			// activates the topmost non-minimized window
			if (setForeground)
			{
				SetTopWindowAsForeground();
			}

			IsCurrentWorkspace = true;

			DoWorkspaceChangedTo(this);
		}

		internal void Unswitch()
		{
			sharedWindows.ForEach(window => window.SavePosition());

			HideWindows();

			IsCurrentWorkspace = false;

			DoWorkspaceChangedFrom(this);
		}

		internal void RevertToInitialValues()
		{
			if (IsCurrentWorkspace)
			{
				if (!isWindowsTaskbarShown)
				{
					ToggleWindowsTaskbarVisibility();
				}

				RestoreWorkingArea();

				SetWorkingArea();
			}

			windows.ForEach(w => w.RevertToInitialValues());

			ShowWindows();
		}

		private void SetSharedWindowChanges(Window window)
		{
			window.Initialize();
			if ((!hasChanges && !repositionOnSwitchedTo) || window.IsFloating)
			{
				window.RestorePosition();
			}
		}

		private void ShowWindows()
		{
			// restores the Z order of the windows
			var restoreZOrder = windows.Count > 1 &&
				(Layout.NeedsToSaveAndRestoreZOrder() || sharedWindows.Count > 0 || floatingWindowsCount > 0);

			var prevWindowHandle = NativeMethods.HWND_TOP;
			foreach (var window in windows)
			{
				NativeMethods.ShowWindowAsync(window.hWnd, NativeMethods.SW.SW_SHOWNA);

				if (restoreZOrder)
				{
					NativeMethods.SetWindowPos(window.owner != IntPtr.Zero ? window.owner : window.hWnd, prevWindowHandle, 0, 0, 0, 0,
						NativeMethods.SWP.SWP_ASYNCWINDOWPOS |
						NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE);
					prevWindowHandle = window.hWnd;
				}

				if (window.HideOwnedPopups)
				{
					NativeMethods.ShowOwnedPopups(window.owner != IntPtr.Zero ? window.owner : window.hWnd, true);
				}

				if (window.RedrawOnShow)
				{
					window.Redraw();
				}
			}
		}

		private void HideWindows()
		{
			windows.ForEach(w => w.Hide());
		}

		public void Reposition()
		{
			Layout.Reposition(managedWindows, workingArea);
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

		internal void ShowHideWindowsTaskbar()
		{
			var showHide = ShowWindowsTaskbar ? NativeMethods.SW.SW_SHOWNA : NativeMethods.SW.SW_HIDE;

			NativeMethods.ShowWindow(taskbarHandle, showHide);
			if (Windawesome.isAtLeastVista)
			{
				NativeMethods.ShowWindow(startButtonHandle, showHide);
			}

			isWindowsTaskbarShown = ShowWindowsTaskbar;

			ShowBars();
		}

		private void ShowBars()
		{
			foreach (var bar in bars.Where(bs => bs.ShowBar))
			{
				bar.bar.form.Location = bar.barPosition.Location;
				bar.bar.form.ClientSize = bar.barPosition.Size;
				bar.bar.form.Show();
			}

			var newShownBars = bars.Where(bs => bs.ShowBar);
			var newBarsShown = newShownBars.Select(bs => bs.bar);

			shownBars.Select(sb => sb.bar).Except(newBarsShown).ForEach(b => b.form.Hide());
			shownBars = newBarsShown.Zip(newShownBars.Select(sb => sb.topBar), (b, tb) => new ShownBarInfo(b, tb));

			if (SystemInformation.WorkingArea != workingArea)
			{
				SetWorkingArea();
			}

			ResizeBarWidgets();
		}

		private void SetWorkingArea()
		{
			var workingAreaRECT = new NativeMethods.RECT
			    {
			        left = this.workingArea.Left,
			        top = this.workingArea.Top,
			        right = this.workingArea.Right,
			        bottom = this.workingArea.Bottom
			    };

			// this is incredibly slow for some reason. Another way?
			NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETWORKAREA, 1,
				ref workingAreaRECT, NativeMethods.SPIF_SENDCHANGE | NativeMethods.SPIF_UPDATEINIFILE);
		}

		private void RestoreWorkingArea()
		{
			foreach (var shownBar in shownBars)
			{
				if (shownBar.topBar)
				{
					this.workingArea.Y -= shownBar.bar.barHeight;
				}
				this.workingArea.Height += shownBar.bar.barHeight;
			}
		}

		internal void OnWorkingAreaReset(Rectangle newWorkingArea)
		{
			workingArea = newWorkingArea;

			shownBars = new ShownBarInfo[0];
			SetWorkingAreaAndBarPositions();
			ShowHideWindowsTaskbar();
			Reposition();
		}

		internal void OnScreenResolutionChanged(Rectangle newWorkingArea)
		{
			workingArea = newWorkingArea;

			this.bars.ForEach(bs =>
				bs.bar.form.ClientSize = bs.barPosition.Size = new Size(workingArea.Width, bs.bar.barHeight));
			ResizeBarWidgets();

			Reposition();
		}

		internal void ToggleWindowsTaskbarVisibility()
		{
			ShowWindowsTaskbar = !ShowWindowsTaskbar;
			SetWorkingAreaAndBarPositions();
			ShowHideWindowsTaskbar();
			Reposition();
		}

		internal void SetTopWindowAsForeground()
		{
			if (windows.Count > 0 && !windows.First.Value.IsMinimized)
			{
				Windawesome.ForceForegroundWindow(windows.First.Value);
			}
			else
			{
				Windawesome.ForceForegroundWindow(NativeMethods.GetDesktopWindow());
			}
		}

		internal void WindowMinimized(IntPtr hWnd)
		{
			var window = MoveWindowToBottom(hWnd);
			if (window != null)
			{
				window.IsMinimized = true;

				if (managedWindows.Remove(window))
				{
					Layout.WindowMinimized(window, managedWindows, workingArea);
				}

				DoWorkspaceApplicationMinimized(this, window);
			}
		}

		internal void WindowRestored(IntPtr hWnd)
		{
			var window = MoveWindowToTop(hWnd);
			if (window != null)
			{
				window.IsMinimized = false;

				if (!window.IsFloating)
				{
					managedWindows.AddFirst(window);
					Layout.WindowRestored(window, managedWindows, workingArea);
				}

				DoWorkspaceApplicationRestored(this, window);
			}
		}

		internal const int minimizeRestoreDelay = 100;
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
			window.IsMinimized = NativeMethods.IsIconic(window.hWnd);
			windows.AddFirst(window);
			if (window.WorkspacesCount > 1)
			{
				sharedWindows.AddFirst(window);
			}
			if (window.ShowInTabs)
			{
				windowsShownInTabs.AddFirst(window);
			}
			if (IsCurrentWorkspace || window.WorkspacesCount == 1)
			{
				window.Initialize();
			}
			if (window.IsFloating)
			{
				floatingWindowsCount++;
			}
			else if (!window.IsMinimized)
			{
				managedWindows.AddFirst(window);

				Layout.WindowCreated(window, managedWindows, workingArea, IsCurrentWorkspace);
				hasChanges |= !IsCurrentWorkspace;
			}

			DoWorkspaceApplicationAdded(this, window);
		}

		internal void WindowDestroyed(Window window, bool setForeground = true)
		{
			windows.Remove(window);
			if (window.WorkspacesCount > 1)
			{
				sharedWindows.Remove(window);
			}
			if (window.ShowInTabs)
			{
				windowsShownInTabs.Remove(window);
			}
			if (window.IsFloating)
			{
				floatingWindowsCount--;
			}
			else if (!window.IsMinimized)
			{
				managedWindows.Remove(window);

				Layout.WindowDestroyed(window, managedWindows, workingArea, IsCurrentWorkspace);
				hasChanges |= !IsCurrentWorkspace;
			}

			if (IsCurrentWorkspace && setForeground)
			{
				SetTopWindowAsForeground();
			}

			DoWorkspaceApplicationRemoved(this, window);
		}

		public bool ContainsWindow(IntPtr hWnd)
		{
			return windows.Any(w => w.hWnd == hWnd);
		}

		public Window GetWindow(IntPtr hWnd)
		{
			return windows.FirstOrDefault(w => w.hWnd == hWnd);
		}

		public int GetWindowsCount()
		{
			return windows.Count;
		}

		internal void ToggleShowHideBar(Bar bar)
		{
			var barStruct = bars.FirstOrDefault(bs => bs.bar == bar);
			if (barStruct != null)
			{
				barStruct.ShowBar = !barStruct.ShowBar;
				SetBarPositions();

				var newShownBars = bars.Where(bs => bs.ShowBar);
				shownBars = newShownBars.Select(sb => sb.bar).
					Zip(newShownBars.Select(sb => sb.topBar), (b, tb) => new ShownBarInfo(b, tb));

				bar.form.Visible = barStruct.ShowBar;
				SetWorkingArea();
				Reposition();
			}
		}

		internal void ToggleWindowFloating(IntPtr hWnd)
		{
			var window = GetWindow(hWnd);
			if (window != null)
			{
				WindowDestroyed(window, false);
				window.IsFloating = !window.IsFloating;
				WindowCreated(window);
			}
		}

		internal void ToggleShowHideWindowInTaskbar(IntPtr hWnd)
		{
			var window = GetWindow(hWnd);
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
				if (managedWindows.Contains(window))
				{
					Layout.WindowTitlebarToggled(window, managedWindows, workingArea);
				}
			}
		}

		internal void ToggleShowHideWindowBorder(IntPtr hWnd)
		{
			var window = GetWindow(hWnd);
			if (window != null)
			{
				window.ToggleShowHideWindowBorder();
				if (managedWindows.Contains(window))
				{
					Layout.WindowBorderToggled(window, managedWindows, workingArea);
				}
			}
		}

		internal void Initialize(bool startingWorkspace)
		{
			// I'm adding to the front of the list in WindowCreated, however EnumWindows enums
			// from the top of the Z-order to the bottom, so I need to reverse the list
			windows = new LinkedList<Window>(windows.Reverse());

			workingArea = SystemInformation.WorkingArea;
			SetWorkingAreaAndBarPositions();

			if (startingWorkspace)
			{
				isWindowsTaskbarShown = !ShowWindowsTaskbar;
			}
			else
			{
				HideWindows();
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
				if (windows.Count > 1)
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
				if (windows.Count > 1)
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
			return windows.Count > 0 ? windows.First.Value : null;
		}

		internal void AddToSharedWindows(Window window)
		{
			sharedWindows.AddFirst(window);
		}

		internal void AddToRemovedSharedWindows(Window window)
		{
			removedSharedWindows.AddFirst(window);
		}

		internal IEnumerable<Window> GetWindows()
		{
			return windows;
		}
	}

	public class Window
	{
		public readonly IntPtr hWnd;
		public bool IsFloating { get; internal set; }
		public bool ShowInTabs { get; internal set; }
		public State Titlebar { get; internal set; }
		public State InTaskbar { get; internal set; }
		public State WindowBorders { get; internal set; }
		public int WorkspacesCount { get; internal set; } // if > 1 window is shared between two or more workspaces
		public bool IsMinimized { get; internal set; }
		public string DisplayName { get; internal set; }
		public readonly string className;
        public readonly string processName;
		public readonly bool is64BitProcess;
		public bool RedrawOnShow { get; internal set; }
		public bool ActivateLastActivePopup { get; internal set; }
		public bool HideOwnedPopups { get; internal set; }
		public readonly IntPtr owner;

		private readonly NativeMethods.WS titlebarStyle;

		private readonly NativeMethods.WS borderStyle;
		private readonly NativeMethods.WS_EX borderExStyle;

		private NativeMethods.WINDOWPLACEMENT windowPlacement;
		private readonly NativeMethods.WINDOWPLACEMENT originalWindowPlacement;

		internal Window(IntPtr hWnd, string className, string displayName, string processName, int workspacesCount, bool is64BitProcess,
			NativeMethods.WS originalStyle, NativeMethods.WS_EX originalExStyle, IntPtr owner)
		{
			this.hWnd = hWnd;
			this.className = className;
			this.DisplayName = displayName;
            this.processName = processName;
			this.WorkspacesCount = workspacesCount;
			this.is64BitProcess = is64BitProcess;
			this.owner = owner;

			windowPlacement = NativeMethods.WINDOWPLACEMENT.Default;
			SavePosition();

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

			originalWindowPlacement = windowPlacement;
		}

		internal Window(Window window, bool fullCopy)
		{
			hWnd = window.hWnd;
			className = window.className;
			this.DisplayName = window.DisplayName;
            processName = window.processName;
			windowPlacement = window.windowPlacement;
			this.WorkspacesCount = window.WorkspacesCount;
			originalWindowPlacement = window.originalWindowPlacement;
			is64BitProcess = window.is64BitProcess;
			owner = window.owner;
			ActivateLastActivePopup = window.ActivateLastActivePopup;
			HideOwnedPopups = window.HideOwnedPopups;

			titlebarStyle = window.titlebarStyle;

			borderStyle = window.borderStyle;
			borderExStyle = window.borderExStyle;

			if (fullCopy)
			{
				this.IsFloating = window.IsFloating;
				this.ShowInTabs = window.ShowInTabs;
				this.Titlebar = window.Titlebar;
				this.InTaskbar = window.InTaskbar;
				this.WindowBorders = window.WindowBorders;
			}
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

			var redraw = false;
			if (style != prevStyle)
			{
				NativeMethods.SetWindowStyleLongPtr(hWnd, style);
				redraw = true;
			}
			if (exStyle != prevExStyle)
			{
				NativeMethods.SetWindowExStyleLongPtr(hWnd, exStyle);
				redraw = true;
			}

			if (redraw)
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

		internal void Hide()
		{
			NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW.SW_HIDE);
			if (HideOwnedPopups)
			{
				NativeMethods.ShowOwnedPopups(owner != IntPtr.Zero ? owner : hWnd, false);
			}
		}

		internal void RevertToInitialValues()
        {
            // TODO: better to do something like this:
            //NativeMethods.SetWindowStyleLongPtr(hWnd, originalStyle);
            //NativeMethods.SetWindowExStyleLongPtr(hWnd, originalExStyle);
            //Redraw();
            // but it doesn't work - some windows lose their Taskbar buttons, although they are still visible in
            // the ALT-TAB menu. Some other windows gain a Taskbar button, while not visible
            // in the ALT-TAB menu. In both cases the ALT-TAB menu is correct

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

			windowPlacement = originalWindowPlacement;
			RestorePosition();
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
	}
}
