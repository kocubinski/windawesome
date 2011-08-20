using System;
using System.Collections.Generic;
using System.Linq;

namespace Windawesome
{
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
					style |= borderStyle;
					exStyle |= borderExStyle;
					break;
				case State.HIDDEN:
					style &= ~borderStyle;
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
			windowPlacement.Flags |= NativeMethods.WPF.WPF_ASYNCWINDOWPLACEMENT;
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
				NativeMethods.SetMenu(this.hWnd, this.ShowMenu ? this.menu : IntPtr.Zero);
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
