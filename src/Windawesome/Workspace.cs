using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public class Workspace
	{
		public readonly int ID;
		public ILayout layout { get; private set; }
		public readonly Bar[] bars;
		public readonly bool[] topBars;
		public readonly bool[] showBars;
		public readonly string name;
		public bool showWindowsTaskbar { get; private set; }
		public bool isCurrentWorkspace { get; private set; }
		public readonly bool repositionOnSwitchedTo;

		private static int count;
		private static bool isWindowsTaskbarShown;
		private static Bar[] barsShown;
		private static bool[] topBarsShown;
		private static readonly IntPtr taskbarHandle;
		private static readonly IntPtr startButtonHandle;

		private int floatingWindowsCount;
		internal Rectangle workingArea;
		private readonly Rectangle[] barPositions;

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

		private static void OnWorkspaceApplicationAdded(Workspace workspace, Window window)
		{
			if (WorkspaceApplicationAdded != null)
			{
				WorkspaceApplicationAdded(workspace, window);
			}
			Windawesome.OnLayoutUpdated();
		}

		private static void OnWorkspaceApplicationRemoved(Workspace workspace, Window window)
		{
			if (WorkspaceApplicationRemoved != null)
			{
				WorkspaceApplicationRemoved(workspace, window);
			}
			Windawesome.OnLayoutUpdated();
		}

		private static void OnWorkspaceApplicationMinimized(Workspace workspace, Window window)
		{
			if (WorkspaceApplicationMinimized != null)
			{
				WorkspaceApplicationMinimized(workspace, window);
			}
		}

		private static void OnWorkspaceApplicationRestored(Workspace workspace, Window window)
		{
			if (WorkspaceApplicationRestored != null)
			{
				WorkspaceApplicationRestored(workspace, window);
			}
		}

		private static void OnWorkspaceChangedFrom(Workspace workspace)
		{
			if (WorkspaceChangedFrom != null)
			{
				WorkspaceChangedFrom(workspace);
			}
		}

		private static void OnWorkspaceChangedTo(Workspace workspace)
		{
			if (WorkspaceChangedTo != null)
			{
				WorkspaceChangedTo(workspace);
			}
			Windawesome.OnLayoutUpdated();
		}

		private static void OnWorkspaceLayoutChanged(Workspace workspace, ILayout oldLayout)
		{
			if (WorkspaceLayoutChanged != null)
			{
				WorkspaceLayoutChanged(workspace, oldLayout);
			}
			Windawesome.OnLayoutUpdated();
		}

		private static void OnWindowActivated(IntPtr hWnd)
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
				return layout.LayoutSymbol(windowsShownInTabs.Count);
			}
		}

		static Workspace()
		{
			taskbarHandle = NativeMethods.FindWindow("Shell_TrayWnd", null);
			startButtonHandle = NativeMethods.FindWindow("Button", "Start");

			barsShown = new Bar[0];
			topBarsShown = new bool[0];

			count = 0;
		}

		public Workspace(ILayout layout, IList<Bar> bars, IList<bool> topBars = null, IList<bool> showBars = null,
			string name = "", bool showWindowsTaskbar = false, bool repositionOnSwitchedTo = false)
		{
			windows = new LinkedList<Window>();
			managedWindows = new LinkedList<Window>();
			sharedWindows = new LinkedList<Window>();
			windowsShownInTabs = new LinkedList<Window>();
			removedSharedWindows = new LinkedList<Window>();

			isCurrentWorkspace = false;
			hasChanges = false;
			floatingWindowsCount = 0;

			this.ID = ++count;
			this.layout = layout;
			this.bars = bars.ToArray();
			this.topBars = topBars == null ? bars.Select(_ => true).ToArray() : topBars.ToArray();
			this.showBars = showBars == null ? bars.Select(_ => true).ToArray() : showBars.ToArray();
			this.name = name;
			this.showWindowsTaskbar = showWindowsTaskbar;
			this.repositionOnSwitchedTo = repositionOnSwitchedTo;

			barPositions = new Rectangle[this.bars.Length];
		}

		public override int GetHashCode()
		{
			return ID;
		}

		public override bool Equals(object obj)
		{
			var workspace = obj as Workspace;
			return workspace != null && workspace.ID == ID;
		}

		private void SetWorkingAreaAndBarPositions()
		{
			SetWindowsTaskbarArea();

			SetBarPositions();

			bars.Where(bar => bar.leftmost != workingArea.Left || bar.rightmost != workingArea.Right).ForEach(bar =>
				bar.ResizeWidgets(workingArea.Left, workingArea.Right));
		}

		private void SetWindowsTaskbarArea()
		{
			NativeMethods.APPBARDATA data = NativeMethods.APPBARDATA.Default;
			data.hWnd = taskbarHandle;
			NativeMethods.SHAppBarMessage(NativeMethods.AppBarMsg.ABM_GETTASKBARPOS, ref data);
			int taskbarWidth = data.rc.right - data.rc.left;
			int taskbarHeight = data.rc.bottom - data.rc.top;

			if (showWindowsTaskbar)
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
			for (int i = 0; i < barsShown.Length; i++)
			{
				if (topBarsShown[i])
				{
					workingArea.Y -= barsShown[i].barHeight;
				}
				workingArea.Height += barsShown[i].barHeight;
			}

			for (int i = 0; i < bars.Length; i++)
			{
				if (showBars[i])
				{
					var bar = bars[i];
					int y;
					if (topBars[i])
					{
						y = workingArea.Y;
						workingArea.Y += bar.barHeight;
					}
					else
					{
						y = workingArea.Bottom - bar.barHeight;
					}
					workingArea.Height -= bar.barHeight;

					barPositions[i].Location = new Point(workingArea.X, y);
					barPositions[i].Size = new Size(workingArea.Width, bar.barHeight);
				}
			}
		}

		internal void SwitchTo(bool setForeground = true)
		{
			// hides or shows the Windows taskbar
			if (this.showWindowsTaskbar != Workspace.isWindowsTaskbarShown)
			{
				ShowHideWindowsTaskbar();
			}
			// hides or shows the Bars for this workspace
			else if (Windawesome.workspaceBarsEquivalentClasses[Windawesome.previousWorkspace - 1] != Windawesome.workspaceBarsEquivalentClasses[ID - 1])
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
				SetForeground();
			}

			isCurrentWorkspace = true;

			OnWorkspaceChangedTo(this);
		}

		internal void Unswitch()
		{
			sharedWindows.ForEach(window => window.SavePosition());

			HideWindows();

			isCurrentWorkspace = false;

			OnWorkspaceChangedFrom(this);
		}

		internal void RevertToInitialValues()
		{
			if (isCurrentWorkspace)
			{
				if (!isWindowsTaskbarShown)
				{
					ToggleWindowsTaskbarVisibility();
				}

				for (int i = 0; i < barsShown.Length; i++)
				{
					if (topBarsShown[i])
					{
						workingArea.Y -= barsShown[i].barHeight;
					}
					workingArea.Height += barsShown[i].barHeight;
				}

				SetWorkingArea();
			}

			windows.ForEach(w => w.RevertToInitialValues());

			ShowWindows();
		}

		private void SetSharedWindowChanges(Window window)
		{
			window.Initialize();
			if ((!hasChanges && !repositionOnSwitchedTo) || window.isFloating)
			{
				window.RestorePosition();
			}
		}

		private void ShowWindows()
		{
			// restores the Z order of the windows
			bool restoreZOrder = windows.Count > 1 &&
				(layout.NeedsToSaveAndRestoreZOrder() || sharedWindows.Count > 0 || floatingWindowsCount > 0);

			IntPtr prevWindowHandle = NativeMethods.HWND_TOP;
			foreach (var window in windows)
			{
				NativeMethods.ShowWindowAsync(window.hWnd, NativeMethods.SW.SW_SHOWNA);
				if (restoreZOrder)
				{
					NativeMethods.SetWindowPos(window.hWnd, prevWindowHandle, 0, 0, 0, 0,
						NativeMethods.SWP.SWP_ASYNCWINDOWPOS |
						NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE);
					prevWindowHandle = window.hWnd;
				}

				if (window.redrawOnShow)
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
			layout.Reposition(managedWindows, workingArea);
		}

		public void ChangeLayout(ILayout layout)
		{
			if (layout.LayoutName() != this.layout.LayoutName())
			{
				var oldLayout = this.layout;
				this.layout = layout;
				Reposition();
				OnWorkspaceLayoutChanged(this, oldLayout);
			}
		}

		internal void ShowHideWindowsTaskbar()
		{
			NativeMethods.SW showHide = showWindowsTaskbar ? NativeMethods.SW.SW_SHOWNA : NativeMethods.SW.SW_HIDE;

			NativeMethods.ShowWindow(taskbarHandle, showHide);
			if (Windawesome.isAtLeastVista)
			{
				NativeMethods.ShowWindow(startButtonHandle, showHide);
			}

			isWindowsTaskbarShown = showWindowsTaskbar;

			ShowBars();
		}

		private void ShowBars()
		{
			for (int i = 0; i < bars.Length; i++)
			{
				if (showBars[i])
				{
					var bar = bars[i];
					bar.form.Location = barPositions[i].Location;
					bar.form.ClientSize = barPositions[i].Size;
					bar.form.Show();
				}
			}

			var newBarsShown = bars.Where((_, i) => showBars[i]).ToArray();

			barsShown.Except(newBarsShown).ForEach(b => b.form.Hide());

			barsShown = newBarsShown;
			topBarsShown = topBars.Where((_, i) => showBars[i]).ToArray();

			if (SystemInformation.WorkingArea != workingArea)
			{
				SetWorkingArea();
			}
		}

		private void SetWorkingArea()
		{
			var workingAreaRECT = new NativeMethods.RECT();
			workingAreaRECT.left = workingArea.Left;
			workingAreaRECT.top = workingArea.Top;
			workingAreaRECT.right = workingArea.Right;
			workingAreaRECT.bottom = workingArea.Bottom;

			// this is incredibly slow for some reason. Another way?
			NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETWORKAREA, NativeMethods.RECTSize,
				ref workingAreaRECT, NativeMethods.SPIF_SENDCHANGE);
		}

		internal void ToggleWindowsTaskbarVisibility()
		{
			showWindowsTaskbar = !showWindowsTaskbar;
			SetWorkingAreaAndBarPositions();
			ShowHideWindowsTaskbar();
			Reposition();
		}

		internal void SetForeground()
		{
			if (windows.Count > 0 && !windows.First.Value.isMinimized)
			{
				var hasOwner = windows.First.Value.owner != IntPtr.Zero;
				NativeMethods.ForceForegroundWindow(hasOwner ? windows.First.Value.owner : windows.First.Value.hWnd, !hasOwner);
			}
			else
			{
				NativeMethods.ForceForegroundWindow(NativeMethods.GetDesktopWindow(), false);
			}
		}

		internal void WindowMinimized(IntPtr hWnd)
		{
			var window = MoveWindowToBottom(hWnd);
			if (window != null)
			{
				window.isMinimized = true;

				if (managedWindows.Contains(window))
				{
					managedWindows.Remove(window);
					layout.WindowMinimized(window, managedWindows, workingArea);
				}

				OnWorkspaceApplicationMinimized(this, window);
			}
		}

		internal void WindowRestored(IntPtr hWnd)
		{
			var window = MoveWindowToTop(hWnd);
			if (window != null)
			{
				window.isMinimized = false;

				if (managedWindows.Contains(window))
				{
					managedWindows.AddFirst(window);
					layout.WindowRestored(window, managedWindows, workingArea);
				}

				OnWorkspaceApplicationRestored(this, window);
			}
		}

		internal static readonly int minimizeRestoreDelay = 100;
		internal void WindowActivated(IntPtr hWnd)
		{
			Window window;
			if (hWnd == IntPtr.Zero && windows.Count > 0)
			{
				window = windows.First.Value;
				if (!window.isMinimized)
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
				if (window.isMinimized)
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
					if (!secondZOrderWindow.isMinimized)
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

			OnWindowActivated(hWnd);
		}

		internal void WindowCreated(Window window)
		{
			window.isMinimized = NativeMethods.IsIconic(window.hWnd);
			windows.AddFirst(window);
			if (window.workspacesCount > 1)
			{
				sharedWindows.AddFirst(window);
			}
			if (window.showInTabs)
			{
				windowsShownInTabs.AddFirst(window);
			}
			if (isCurrentWorkspace || window.workspacesCount == 1)
			{
				window.Initialize();
			}
			if (window.isFloating)
			{
				floatingWindowsCount++;
			}
			else if (!window.isMinimized)
			{
				managedWindows.AddFirst(window);

				layout.WindowCreated(window, managedWindows, workingArea, isCurrentWorkspace);
				hasChanges |= !isCurrentWorkspace;
			}

			OnWorkspaceApplicationAdded(this, window);
		}

		internal void WindowDestroyed(Window window, bool setForeground = true)
		{
			windows.Remove(window);
			if (window.workspacesCount > 1)
			{
				sharedWindows.Remove(window);
			}
			if (window.showInTabs)
			{
				windowsShownInTabs.Remove(window);
			}
			if (window.isFloating)
			{
				floatingWindowsCount--;
			}
			else if (!window.isMinimized)
			{
				managedWindows.Remove(window);

				layout.WindowDestroyed(window, managedWindows, workingArea, isCurrentWorkspace);
				hasChanges |= !isCurrentWorkspace;
			}

			if (isCurrentWorkspace && setForeground)
			{
				SetForeground();
			}

			OnWorkspaceApplicationRemoved(this, window);
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
			var index = Array.IndexOf(bars, bar);
			if (index != -1)
			{
				showBars[index] = !showBars[index];
				SetBarPositions();

				barsShown = bars.Where((_, i) => showBars[i]).ToArray();
				topBarsShown = topBars.Where((_, i) => showBars[i]).ToArray();

				bar.form.Visible = showBars[index];
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
				window.isFloating = !window.isFloating;
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
					layout.WindowTitlebarToggled(window, managedWindows, workingArea);
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
					layout.WindowBorderToggled(window, managedWindows, workingArea);
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
				isWindowsTaskbarShown = !showWindowsTaskbar;
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

		internal LinkedList<Window> GetWindows()
		{
			return windows;
		}

		public LinkedList<Window> GetWindowsShownInTabs()
		{
			return windowsShownInTabs;
		}
	}

	public class Window
	{
		public readonly IntPtr hWnd;
		public bool isFloating { get; internal set; }
		public bool showInTabs { get; internal set; }
		public State titlebar { get; internal set; }
		public State inTaskbar { get; internal set; }
		public State windowBorders { get; internal set; }
		public int workspacesCount { get; internal set; } // if > 1 window is shared between two or more workspaces
		public bool isMinimized { get; internal set; }
		public string caption { get; internal set; }
		public readonly string className;
		public readonly bool is64BitProcess;
		public bool redrawOnShow { get; internal set; }
		public readonly IntPtr owner;

		private readonly NativeMethods.WS titlebarStyle;

		private readonly NativeMethods.WS borderStyle;
		private readonly NativeMethods.WS_EX borderExStyle;

		private NativeMethods.WINDOWPLACEMENT windowPlacement;
		private readonly NativeMethods.WINDOWPLACEMENT originalWindowPlacement;

		internal Window(IntPtr hWnd, string className, string caption, int workspacesCount, bool is64BitProcess,
			NativeMethods.WS originalStyle, NativeMethods.WS_EX originalExStyle, IntPtr owner)
		{
			this.hWnd = hWnd;
			this.className = className;
			this.caption = caption;
			this.workspacesCount = workspacesCount;
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
			caption = window.caption;
			windowPlacement = window.windowPlacement;
			workspacesCount = window.workspacesCount;
			originalWindowPlacement = window.originalWindowPlacement;
			is64BitProcess = window.is64BitProcess;
			owner = window.owner;

			titlebarStyle = window.titlebarStyle;

			borderStyle = window.borderStyle;
			borderExStyle = window.borderExStyle;

			if (fullCopy)
			{
				isFloating = window.isFloating;
				showInTabs = window.showInTabs;
				titlebar = window.titlebar;
				inTaskbar = window.inTaskbar;
				windowBorders = window.windowBorders;
			}
		}

		internal void Initialize()
		{
			NativeMethods.WS style = NativeMethods.GetWindowStyleLongPtr(hWnd);
			NativeMethods.WS_EX exStyle = NativeMethods.GetWindowExStyleLongPtr(hWnd);
			NativeMethods.WS prevStyle = style;
			NativeMethods.WS_EX prevExStyle = exStyle;

			switch (inTaskbar)
			{
				case State.SHOWN:
					exStyle = (exStyle | NativeMethods.WS_EX.WS_EX_APPWINDOW) & ~NativeMethods.WS_EX.WS_EX_TOOLWINDOW;
					break;
				case State.HIDDEN:
					exStyle = (exStyle & ~NativeMethods.WS_EX.WS_EX_APPWINDOW) | NativeMethods.WS_EX.WS_EX_TOOLWINDOW;
					break;
			}
			switch (titlebar)
			{
				case State.SHOWN:
					style |= titlebarStyle;
					break;
				case State.HIDDEN:
					style &= ~titlebarStyle;
					break;
			}
			switch (windowBorders)
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

			bool redraw = false;
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
			inTaskbar = (State) (((int) inTaskbar + 1) % 2);
			Initialize();
		}

		internal void ToggleShowHideTitlebar()
		{
			titlebar = (State) (((int) titlebar + 1) % 2);
			Initialize();
		}

		internal void ToggleShowHideWindowBorder()
		{
			windowBorders = (State) (((int) windowBorders + 1) % 2);
			Initialize();
		}

		internal void Redraw()
		{
			// this whole thing is a hack but I've found no other way to make it work (and I've tried
			// a zillion things). Resizing seems to do the best job.
			NativeMethods.RECT rect;
			NativeMethods.GetWindowRect(hWnd, out rect);
			NativeMethods.SetWindowPos(hWnd, IntPtr.Zero, rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top + 1,
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
		}

		internal void RevertToInitialValues()
		{
			if (titlebar != State.AS_IS)
			{
				titlebar = State.SHOWN;
			}
			if (inTaskbar != State.AS_IS)
			{
				inTaskbar = State.SHOWN;
			}
			if (windowBorders != State.AS_IS)
			{
				windowBorders = State.SHOWN;
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
