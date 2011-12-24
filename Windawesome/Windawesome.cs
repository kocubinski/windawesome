using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Windawesome
{
	public sealed class Windawesome : NativeWindow
	{
		public Workspace CurrentWorkspace { get; private set; }

		public readonly Monitor[] monitors;
		public readonly Config config;

		public delegate bool HandleMessageDelegate(ref Message m);

		public static IntPtr HandleStatic { get; private set; }

		private readonly Dictionary<IntPtr, LinkedList<Tuple<Workspace, Window>>> applications; // hWnd to a list of workspaces and windows
		private readonly WindowBase[] topmostWindows;
		private readonly HashMultiSet<IntPtr> hiddenApplications;
		private readonly uint windawesomeThreadId = NativeMethods.GetCurrentThreadId();

		private readonly Dictionary<int, HandleMessageDelegate> messageHandlers;

		private readonly NativeMethods.WinEventDelegate winEventDelegate;
		private readonly IntPtr windowDestroyedShownOrHiddenWinEventHook;
		private readonly IntPtr windowMinimizedOrRestoredWinEventHook;
		private readonly IntPtr windowFocusedWinEventHook;

		#region Events

		public delegate void WindowTitleOrIconChangedEventHandler(Workspace workspace, Window window, string newText, Bitmap newIcon);
		public static event WindowTitleOrIconChangedEventHandler WindowTitleOrIconChanged;

		public delegate void WindowFlashingEventHandler(LinkedList<Tuple<Workspace, Window>> list);
		public static event WindowFlashingEventHandler WindowFlashing;

		public delegate void ProgramRuleMatchedEventHandler(ProgramRule programRule, IntPtr hWnd, string cName, string dName, string pName, NativeMethods.WS style, NativeMethods.WS_EX exStyle);
		public static event ProgramRuleMatchedEventHandler ProgramRuleMatched;

		public delegate void WindawesomeExitingEventHandler();
		public static event WindawesomeExitingEventHandler WindawesomeExiting;

		private static void DoWindowTitleOrIconChanged(Workspace workspace, Window window, string newText, Bitmap newIcon)
		{
			if (WindowTitleOrIconChanged != null)
			{
				WindowTitleOrIconChanged(workspace, window, newText, newIcon);
			}
		}

		private static void DoWindowFlashing(LinkedList<Tuple<Workspace, Window>> list)
		{
			if (WindowFlashing != null)
			{
				WindowFlashing(list);
			}
		}

		private static void DoProgramRuleMatched(ProgramRule programRule, IntPtr hWnd, string className, string displayName, string processName, NativeMethods.WS style, NativeMethods.WS_EX exStyle)
		{
			if (ProgramRuleMatched != null)
			{
				ProgramRuleMatched(programRule, hWnd, className, displayName, processName, style, exStyle);
			}
		}

		#endregion

		#region Windawesome Construction, Initialization and Destruction

		internal Windawesome()
		{
			applications = new Dictionary<IntPtr, LinkedList<Tuple<Workspace, Window>>>(20);
			hiddenApplications = new HashMultiSet<IntPtr>();
			messageHandlers = new Dictionary<int, HandleMessageDelegate>(2);

			monitors = Screen.AllScreens.Select((_, i) => new Monitor(i)).ToArray();

			this.CreateHandle(new CreateParams { Parent = NativeMethods.HWND_MESSAGE, ClassName = "Message" });
			HandleStatic = this.Handle;

			config = new Config();
			config.LoadConfiguration(this);

			var startingWorkspacesCount = config.StartingWorkspaces.Count();
			var distinctStartingWorkspaceMonitorsCount = config.StartingWorkspaces.Select(w => w.Monitor).Distinct().Count();
			if (distinctStartingWorkspaceMonitorsCount != monitors.Length ||
				distinctStartingWorkspaceMonitorsCount != startingWorkspacesCount)
			{
				throw new Exception("Each Monitor should have exactly one corresponding Workspace in StartingWorkspaces, i.e. " +
					"you should have as many Workspaces in StartingWorkspaces as you have Monitors!");
			}

			CurrentWorkspace = config.StartingWorkspaces.First(w => w.Monitor.screen.Primary);

			topmostWindows = new WindowBase[config.Workspaces.Length];
			Workspace.WindowActivatedEvent += h => topmostWindows[CurrentWorkspace.id - 1] = CurrentWorkspace.GetWindow(h) ?? new WindowBase(h);

			// add workspaces to their corresponding monitors
			monitors.ForEach(m => config.Workspaces.Where(w => w.Monitor == m).ForEach(m.AddWorkspace)); // n ^ 2 but hopefully fast enough

			// set starting workspaces for each monitor
			config.StartingWorkspaces.ForEach(w => w.Monitor.SetStartingWorkspace(w));

			// initialize bars and plugins
			config.Bars.ForEach(b => b.InitializeBar(this));
			config.Plugins.ForEach(p => p.InitializePlugin(this));

			// add all windows to their respective workspaces
			NativeMethods.EnumWindows((hWnd, _) => (Utilities.IsAppWindow(hWnd) && AddWindowToWorkspace(hWnd, finishedInitializing: false)) || true, IntPtr.Zero);

			// add a handler for when the working area or screen rosolution changes as well as
			// a handler for the system shutting down/restarting
			SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
			SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
			SystemEvents.SessionEnding += OnSessionEnding;

			SystemSettingsChanger.ApplyChanges(config);

			// initialize all workspaces and hide windows not on StartingWorkspaces
			var windowsToHide = new HashSet<Window>();
			foreach (var workspace in config.Workspaces)
			{
				workspace.GetWindows().ForEach(w => windowsToHide.Add(w));
				workspace.Initialize();
				topmostWindows[workspace.id - 1] = workspace.GetWindows().FirstOrDefault(w => !NativeMethods.IsIconic(w.hWnd)) ?? new WindowBase(NativeMethods.shellWindow);
			}
			windowsToHide.ExceptWith(config.StartingWorkspaces.SelectMany(ws => ws.GetWindows()));
			var winPosInfo = NativeMethods.BeginDeferWindowPos(windowsToHide.Count);
			winPosInfo = windowsToHide.Where(Utilities.WindowIsNotHung).Aggregate(winPosInfo, (current, w) =>
				NativeMethods.DeferWindowPos(current, w.hWnd, IntPtr.Zero, 0, 0, 0, 0,
					NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE |
					NativeMethods.SWP.SWP_NOSIZE | NativeMethods.SWP.SWP_NOZORDER |
					NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_HIDEWINDOW));
			NativeMethods.EndDeferWindowPos(winPosInfo);

			// remove windows from ALT-TAB menu and Taskbar
			config.StartingWorkspaces.Where(ws => ws != CurrentWorkspace).SelectMany(ws => ws.GetWindows()).
				Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(false));

			// initialize monitors and switch to the default starting workspaces
			monitors.ForEach(m => m.Initialize());
			Monitor.ShowHideWindowsTaskbar(CurrentWorkspace.ShowWindowsTaskbar);
			DoForTopmostWindowForWorkspace(CurrentWorkspace, ActivateWindow);
			CurrentWorkspace.IsCurrentWorkspace = true;

			// register a shell hook
			NativeMethods.RegisterShellHookWindow(this.Handle);

			// register some shell events
			winEventDelegate = WinEventDelegate;
			windowDestroyedShownOrHiddenWinEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT.EVENT_OBJECT_DESTROY, NativeMethods.EVENT.EVENT_OBJECT_HIDE,
				IntPtr.Zero, winEventDelegate, 0, 0,
				NativeMethods.WINEVENT.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT.WINEVENT_SKIPOWNTHREAD);
			windowMinimizedOrRestoredWinEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT.EVENT_SYSTEM_MINIMIZEEND,
				IntPtr.Zero, winEventDelegate, 0, 0,
				NativeMethods.WINEVENT.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT.WINEVENT_SKIPOWNTHREAD);
			windowFocusedWinEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT.EVENT_SYSTEM_FOREGROUND,
				IntPtr.Zero, winEventDelegate, 0, 0,
				NativeMethods.WINEVENT.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT.WINEVENT_SKIPOWNTHREAD);

			if (config.CheckForUpdates)
			{
				UpdateChecker.CheckForUpdate();
			}
		}

		public void Quit()
		{
			SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
			SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
			SystemEvents.SessionEnding -= OnSessionEnding;

			// unregister the shell events
			NativeMethods.UnhookWinEvent(windowDestroyedShownOrHiddenWinEventHook);
			NativeMethods.UnhookWinEvent(windowMinimizedOrRestoredWinEventHook);
			NativeMethods.UnhookWinEvent(windowFocusedWinEventHook);

			// unregister shell hook
			NativeMethods.DeregisterShellHookWindow(this.Handle);

			// dispose of Layouts
			config.Workspaces.ForEach(ws => ws.Layout.Dispose());

			// roll back any changes to Windows
			monitors.ForEach(m => m.Dispose());
			Monitor.StaticDispose();

			applications.Values.ForEach(l => l.First.Value.Item2.RevertToInitialValues());

			// dispose of plugins and bars
			config.Plugins.ForEach(p => p.Dispose());
			config.Bars.ForEach(b => b.Dispose());

			SystemSettingsChanger.RevertChanges(config);

			WindawesomeExiting();
			this.DestroyHandle();
		}

		#endregion

		#region Helpers

		private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
		{
			if (e.Category == UserPreferenceCategory.Desktop)
			{
				OnDisplaySettingsChanged(sender, e);
			}
		}

		private void OnDisplaySettingsChanged(object sender, EventArgs e)
		{
			monitors.ForEach(m => m.SetBoundsAndWorkingArea());
			config.Workspaces.ForEach(ws => ws.Reposition());
		}

		private void OnSessionEnding(object sender, SessionEndingEventArgs e)
		{
			Quit();
		}

		private bool AddWindowToWorkspace(IntPtr hWnd, bool firstTry = true, bool finishedInitializing = true)
		{
			LinkedList<Tuple<Workspace, Window>> workspacesWindowsList;
			if (ApplicationsTryGetValue(hWnd, out workspacesWindowsList))
			{
				return workspacesWindowsList.First.Value.Item2.hWnd != hWnd && workspacesWindowsList.First.Value.Item2.AddToOwnedWindows(hWnd);
			}

			var className = NativeMethods.GetWindowClassName(hWnd);
			var displayName = NativeMethods.GetText(hWnd);
			var style = NativeMethods.GetWindowStyleLongPtr(hWnd);
			var exStyle = NativeMethods.GetWindowExStyleLongPtr(hWnd);
			int processId;
			NativeMethods.GetWindowThreadProcessId(hWnd, out processId);

			using (var process = System.Diagnostics.Process.GetProcessById(processId))
			{
				var processName = process.ProcessName;

				var programRule = config.ProgramRules.FirstOrDefault(r => r.IsMatch(hWnd, className, displayName, processName, style, exStyle));
				DoProgramRuleMatched(programRule, hWnd, className, displayName, processName, style, exStyle);
				if (programRule == null || !programRule.isManaged)
				{
					return false;
				}
				if (programRule.tryAgainAfter >= 0 && firstTry && finishedInitializing)
				{
					System.Threading.Thread.Sleep(programRule.tryAgainAfter);
					return Utilities.IsAppWindow(hWnd) && AddWindowToWorkspace(hWnd, false);
				}

				IEnumerable<ProgramRule.Rule> matchingRules = programRule.rules;
				var workspacesCount = programRule.rules.Length;
				var hasWorkspaceZeroRule = matchingRules.Any(r => r.workspace == 0);
				var hasCurrentWorkspaceRule = matchingRules.Any(r => r.workspace == CurrentWorkspace.id);
				// matchingRules.workspaces could be { 0, 1 } and you could be at workspace 1.
				// Then, "hWnd" would be added twice if it were not for this check
				// TODO: it could be added twice on two different workspaces which are shown at the same time
				if (hasWorkspaceZeroRule && hasCurrentWorkspaceRule)
				{
					matchingRules = matchingRules.Where(r => r.workspace != 0);
					workspacesCount--;
				}

				if (finishedInitializing)
				{
					if (!hasWorkspaceZeroRule && !hasCurrentWorkspaceRule)
					{
						var hasVisibleWorkspaceRule = matchingRules.Any(r => config.Workspaces[r.workspace - 1].IsWorkspaceVisible);
						switch (programRule.onWindowCreatedAction)
						{
							case OnWindowCreatedOrShownAction.TemporarilyShowWindowOnCurrentWorkspace:
								if (!hasVisibleWorkspaceRule)
								{
									CurrentWorkspace.Monitor.temporarilyShownWindows.Add(hWnd);
									OnWindowCreatedOnCurrentWorkspace(hWnd, programRule);
								}
								break;
							case OnWindowCreatedOrShownAction.HideWindow:
								if (!hasVisibleWorkspaceRule)
								{
									hiddenApplications.Add(hWnd);
									NativeMethods.ShowWindow(hWnd, NativeMethods.SW.SW_HIDE);
								}
								if (NativeMethods.GetForegroundWindow() == hWnd)
								{
									DoForTopmostWindowForWorkspace(CurrentWorkspace, ActivateWindow);
								}
								break;
						}
					}

					if (programRule.windowCreatedDelay == -1)
					{
						try
						{
							process.WaitForInputIdle(10000);
						}
						catch (InvalidOperationException)
						{
						}
						catch (System.ComponentModel.Win32Exception)
						{
						}
					}
					else if (programRule.windowCreatedDelay > 0)
					{
						System.Threading.Thread.Sleep(programRule.windowCreatedDelay);
					}
				}

				if (programRule.redrawDesktopOnWindowCreated)
				{
					// If you have a Windows Explorer window open on one workspace (and it is the only non-minimized window open) and you start
					// mintty (which defaults to another workspace) then the desktop is not redrawn right (you can see that if mintty
					// is set to be transparent
					// On Windows XP SP3
					NativeMethods.RedrawWindow(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
						NativeMethods.RDW.RDW_ALLCHILDREN | NativeMethods.RDW.RDW_ERASE | NativeMethods.RDW.RDW_INVALIDATE);
				}

				var list = new LinkedList<Tuple<Workspace, Window>>();
				applications[hWnd] = list;

				var menu = NativeMethods.GetMenu(hWnd);
				var is64BitProcess = NativeMethods.Is64BitProcess(hWnd);

				var isMinimized = NativeMethods.IsIconic(hWnd);

				foreach (var rule in matchingRules)
				{
					var window = new Window(hWnd, className, displayName, processName, workspacesCount,
						is64BitProcess, style, exStyle, rule, programRule, menu);

					var workspace = rule.workspace == 0 ? CurrentWorkspace : config.Workspaces[rule.workspace - 1];
					list.AddLast(Tuple.Create(workspace, window));

					workspace.WindowCreated(window);

					if (!workspace.IsCurrentWorkspace && !isMinimized)
					{
						if (hasWorkspaceZeroRule || hasCurrentWorkspaceRule ||
							list.Count > 1 ||
							programRule.onWindowCreatedAction == OnWindowCreatedOrShownAction.HideWindow ||
							programRule.onWindowCreatedAction == OnWindowCreatedOrShownAction.TemporarilyShowWindowOnCurrentWorkspace)
						{
							// this workspace is not going to be switched to because of this window addition
							switch (programRule.onWindowCreatedOnInactiveWorkspaceAction)
							{
								case OnWindowCreatedOnWorkspaceAction.MoveToTop:
									topmostWindows[workspace.id - 1] = window;
									break;
							}
						}
					}
				}

				if (!programRule.showMenu)
				{
					list.First.Value.Item2.ShowHideWindowMenu();
				}

				if (finishedInitializing)
				{
					if (!hasWorkspaceZeroRule && !hasCurrentWorkspaceRule)
					{
						switch (programRule.onWindowCreatedAction)
						{
							case OnWindowCreatedOrShownAction.SwitchToWindowsWorkspace:
								SwitchToWorkspace(list.First.Value.Item1.id, false);
								OnWindowCreatedOnCurrentWorkspace(hWnd, programRule);
								break;
							case OnWindowCreatedOrShownAction.MoveWindowToCurrentWorkspace:
								ChangeApplicationToWorkspace(hWnd, CurrentWorkspace.id, matchingRules.First().workspace);
								OnWindowCreatedOnCurrentWorkspace(hWnd, programRule);
								break;
						}
					}
					else
					{
						OnWindowCreatedOnCurrentWorkspace(hWnd, programRule);
					}
				}
			}

			return true;
		}

		private void OnWindowCreatedOnCurrentWorkspace(IntPtr newWindow, ProgramRule programRule)
		{
			switch (programRule.onWindowCreatedOnCurrentWorkspaceAction)
			{
				case OnWindowCreatedOnWorkspaceAction.MoveToTop:
					ActivateWindow(new WindowBase(newWindow));
					break;
				case OnWindowCreatedOnWorkspaceAction.PreserveTopmostWindow:
					if (DoForTopmostWindowForWorkspace(CurrentWorkspace, ActivateWindow) == NativeMethods.shellWindow)
					{
						ActivateWindow(new WindowBase(newWindow));
					}
					break;
			}
		}

		private void RefreshApplicationsHash()
		{
			// remove all non-existent applications
			applications.Values.Where(l => !NativeMethods.IsWindow(l.First.Value.Item2.hWnd)).
				ToArray().ForEach(UnmanageWindow);

			// add any application that was not added for some reason when it was created
			NativeMethods.EnumWindows((hWnd, _) =>
				(Utilities.IsAppWindow(hWnd) && !applications.ContainsKey(hWnd) && AddWindowToWorkspace(hWnd)) || true, IntPtr.Zero);
		}

		private void ForceForegroundWindow(WindowBase window)
		{
			var hWnd = NativeMethods.GetLastActivePopup(window.rootOwner);

			if (Utilities.WindowIsNotHung(hWnd))
			{
				var foregroundWindow = NativeMethods.GetForegroundWindow();
				if (foregroundWindow != hWnd)
				{
					if (foregroundWindow == IntPtr.Zero)
					{
						TrySetForegroundWindow(hWnd);
					}
					else if (Utilities.WindowIsNotHung(foregroundWindow))
					{
						var foregroundWindowThreadId = NativeMethods.GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
						if (NativeMethods.AttachThreadInput(this.windawesomeThreadId, foregroundWindowThreadId, true))
						{
							var targetWindowThreadId = NativeMethods.GetWindowThreadProcessId(hWnd, IntPtr.Zero);
							var successfullyAttached = NativeMethods.AttachThreadInput(foregroundWindowThreadId, targetWindowThreadId, true);

							TrySetForegroundWindow(hWnd);

							if (successfullyAttached)
							{
								NativeMethods.AttachThreadInput(foregroundWindowThreadId, targetWindowThreadId, false);
							}
							NativeMethods.AttachThreadInput(this.windawesomeThreadId, foregroundWindowThreadId, false);
						}
					}
				}
				else
				{
					NativeMethods.BringWindowToTop(hWnd);
				}
			}
		}

		private static void TrySetForegroundWindow(IntPtr hWnd)
		{
			const int setForegroundTryCount = 5;
			var count = 0;
			while (!NativeMethods.SetForegroundWindow(hWnd) && ++count < setForegroundTryCount)
			{
				System.Threading.Thread.Sleep(10);
			}

			if (count != setForegroundTryCount)
			{
				const int getForegroundTryCount = 20;
				count = 0;
				while (NativeMethods.GetForegroundWindow() != hWnd && ++count < getForegroundTryCount)
				{
					System.Threading.Thread.Sleep(30);
				}

				NativeMethods.BringWindowToTop(hWnd);
			}
		}

		private void FollowWindow(Workspace fromWorkspace, Workspace toWorkspace, bool follow, WindowBase window)
		{
			if (follow)
			{
				SwitchToWorkspace(toWorkspace.id, false);
				ActivateWindow(window);
			}
			else if (fromWorkspace.IsCurrentWorkspace)
			{
				DoForTopmostWindowForWorkspace(CurrentWorkspace, ActivateWindow);
			}
		}

		private void HideWindow(Window window)
		{
			if (Utilities.IsVisibleAndNotHung(window))
			{
				hiddenApplications.Add(window.hWnd);
				window.GetOwnedWindows().ForEach(hWnd => NativeMethods.ShowWindow(hWnd, NativeMethods.SW.SW_HIDE));
			}
		}

		private void ShowHideWindows(Workspace oldWorkspace, Workspace newWorkspace, bool setForeground)
		{
			var winPosInfo = NativeMethods.BeginDeferWindowPos(newWorkspace.GetWindowsCount() + oldWorkspace.GetWindowsCount());

			var showWindows = newWorkspace.GetWindows();
			foreach (var window in showWindows.Where(Utilities.WindowIsNotHung))
			{
				winPosInfo = window.GetOwnedWindows().Aggregate(winPosInfo, (current, hWnd) =>
					NativeMethods.DeferWindowPos(current, hWnd, IntPtr.Zero, 0, 0, 0, 0,
						NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE |
						NativeMethods.SWP.SWP_NOSIZE | NativeMethods.SWP.SWP_NOZORDER |
						NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_SHOWWINDOW));
				if (window.redrawOnShow)
				{
					window.Redraw();
				}
			}

			var hideWindows = oldWorkspace.sharedWindowsCount > 0 && newWorkspace.sharedWindowsCount > 0 ?
				oldWorkspace.GetWindows().Except(showWindows) : oldWorkspace.GetWindows();
			// if the window is not visible we shouldn't add it to hiddenApplications as EVENT_OBJECT_HIDE won't be sent
			foreach (var window in hideWindows.Where(Utilities.IsVisibleAndNotHung))
			{
				hiddenApplications.Add(window.hWnd);
				winPosInfo = window.GetOwnedWindows().Aggregate(winPosInfo, (current, hWnd) =>
					NativeMethods.DeferWindowPos(current, hWnd, IntPtr.Zero, 0, 0, 0, 0,
						NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE |
						NativeMethods.SWP.SWP_NOSIZE | NativeMethods.SWP.SWP_NOZORDER |
						NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_HIDEWINDOW));
			}

			NativeMethods.EndDeferWindowPos(winPosInfo);

			// activates the topmost non-minimized window
			if (setForeground)
			{
				DoForTopmostWindowForWorkspace(newWorkspace, ForceForegroundWindow);
			}
		}

		private void ActivateWindow(WindowBase window)
		{
			if (NativeMethods.IsIconic(window.hWnd))
			{
				// OpenIcon does not restore the window to its previous size (e.g. maximized)
				// ShowWindow(SW_RESTORE) doesn't redraw some windows correctly (like TortoiseHG commit window)
				Utilities.RestoreApplication(window.hWnd);
				System.Threading.Thread.Sleep(NativeMethods.minimizeRestoreDelay);
			}
			else
			{
				ForceForegroundWindow(window);
			}
			CurrentWorkspace.WindowActivated(window.hWnd);
		}

		private IntPtr DoForTopmostWindowForWorkspace(Workspace workspace, Action<WindowBase> action)
		{
			var window = topmostWindows[workspace.id - 1];
			if (window == null || !NativeMethods.IsWindowVisible(window.hWnd) || NativeMethods.IsIconic(window.hWnd))
			{
				NativeMethods.EnumWindows((hWnd, _) =>
					{
						if (!Utilities.IsAppWindow(hWnd) || NativeMethods.IsIconic(hWnd))
						{
							return true;
						}
						if ((topmostWindows[workspace.id - 1] = workspace.GetWindow(hWnd)) != null)
						{
							// the workspace contains this window so make it the topmost one
							return false;
						}
						if ((Utilities.IsAltTabWindow(hWnd) && !applications.ContainsKey(hWnd)) ||
							workspace.Monitor.temporarilyShownWindows.Contains(hWnd))
						{
							// the window is not a managed-by-another-visible-workspace one so make it the topmost one
							topmostWindows[workspace.id - 1] = new WindowBase(hWnd);
							return false;
						}
						return true;
					}, IntPtr.Zero);

				if (topmostWindows[workspace.id - 1] == null)
				{
					topmostWindows[workspace.id - 1] = new WindowBase(NativeMethods.shellWindow);
				}

				window = topmostWindows[workspace.id - 1];
			}

			action(window);

			return window.hWnd;
		}

		private bool ApplicationsTryGetValue(IntPtr hWnd, out LinkedList<Tuple<Workspace, Window>> list)
		{
			// return DoForSelfAndOwnersWhile(hWnd, h => !applications.TryGetValue(h, out list));
			list = null;
			while (hWnd != IntPtr.Zero && !applications.TryGetValue(hWnd, out list))
			{
				hWnd = NativeMethods.GetWindow(hWnd, NativeMethods.GW.GW_OWNER);
			}
			return list != null;
		}

		private void UnmanageWindow(LinkedList<Tuple<Workspace, Window>> list)
		{
			list.ForEach(t => t.Item1.WindowDestroyed(t.Item2));
			var window = list.First.Value.Item2;
			if (!window.ShowMenu && window.menu != IntPtr.Zero && !window.ToggleShowHideWindowMenu())
			{
				NativeMethods.DestroyMenu(window.menu);
			}
			applications.Remove(window.hWnd);
			monitors.ForEach(m => m.temporarilyShownWindows.Remove(window.hWnd));

			WaitAndActivateNextTopmost(window.hWnd);
		}

		private void WaitAndActivateNextTopmost(IntPtr hWnd)
		{
			if (topmostWindows[CurrentWorkspace.id - 1].hWnd == hWnd)
			{
				while (NativeMethods.GetForegroundWindow() == hWnd)
				{
					System.Threading.Thread.Sleep(20);
				}
				DoForTopmostWindowForWorkspace(CurrentWorkspace, ActivateWindow);
			}
		}

		#endregion

		#region API

		public bool TryGetManagedWindowForWorkspace(IntPtr hWnd, Workspace workspace,
			out Window window, out LinkedList<Tuple<Workspace, Window>> list)
		{
			if (ApplicationsTryGetValue(hWnd, out list))
			{
				var tuple = list.FirstOrDefault(t => t.Item1 == workspace);
				if (tuple != null)
				{
					window = tuple.Item2;
					return true;
				}
			}
			window = null;
			return false;
		}

		public void RefreshWindawesome()
		{
			hiddenApplications.Clear();

			RefreshApplicationsHash();

			// set monitor bounds, repositions all windows in all workspaces and redraw all windows in visible workspaces
			monitors.ForEach(m => m.SetBoundsAndWorkingArea());
			config.Workspaces.ForEach(ws => ws.Reposition());
			monitors.SelectMany(m => m.CurrentVisibleWorkspace.GetWindows()).ForEach(w => w.Redraw());

			// refresh bars
			config.Bars.ForEach(b => b.Refresh());
		}

		public void ChangeApplicationToWorkspace(IntPtr hWnd, int toWorkspaceId = 0, int fromWorkspaceId = 0, bool follow = true)
		{
			var oldWorkspace = fromWorkspaceId == 0 ? CurrentWorkspace : config.Workspaces[fromWorkspaceId - 1];
			var newWorkspace = toWorkspaceId == 0 ? CurrentWorkspace : config.Workspaces[toWorkspaceId - 1];

			if (newWorkspace.id != oldWorkspace.id)
			{
				Window window;
				LinkedList<Tuple<Workspace, Window>> list;
				if (TryGetManagedWindowForWorkspace(hWnd, oldWorkspace, out window, out list) &&
					list.All(t => t.Item1 != newWorkspace))
				{
					oldWorkspace.WindowDestroyed(window);
					newWorkspace.WindowCreated(window);

					if (!follow)
					{
						if (oldWorkspace.IsWorkspaceVisible && !newWorkspace.IsWorkspaceVisible)
						{
							HideWindow(window);
						}
						else if (!oldWorkspace.IsWorkspaceVisible && newWorkspace.IsWorkspaceVisible)
						{
							window.ShowAsync();
						}
					}

					list.Remove(Tuple.Create(oldWorkspace, window));
					list.AddFirst(Tuple.Create(newWorkspace, window));

					FollowWindow(oldWorkspace, newWorkspace, follow, window);
				}
			}
		}

		public void AddApplicationToWorkspace(IntPtr hWnd, int toWorkspaceId = 0, int fromWorkspaceId = 0, bool follow = true)
		{
			var oldWorkspace = fromWorkspaceId == 0 ? CurrentWorkspace : config.Workspaces[fromWorkspaceId - 1];
			var newWorkspace = toWorkspaceId == 0 ? CurrentWorkspace : config.Workspaces[toWorkspaceId - 1];

			if (newWorkspace.id != oldWorkspace.id)
			{
				Window window;
				LinkedList<Tuple<Workspace, Window>> list;
				if (TryGetManagedWindowForWorkspace(hWnd, oldWorkspace, out window, out list) &&
					list.All(t => t.Item1 != newWorkspace))
				{
					var newWindow = new Window(window);

					newWorkspace.WindowCreated(newWindow);
					if (!follow && !oldWorkspace.IsWorkspaceVisible && newWorkspace.IsWorkspaceVisible)
					{
						newWindow.ShowAsync();
					}

					list.AddFirst(Tuple.Create(newWorkspace, newWindow));
					list.Where(t => ++t.Item2.WorkspacesCount == 2).ForEach(t => t.Item1.sharedWindowsCount++);

					FollowWindow(oldWorkspace, newWorkspace, follow, newWindow);
				}
			}
		}

		public void RemoveApplicationFromWorkspace(IntPtr hWnd, int workspaceId = 0, bool setForeground = true)
		{
			var workspace = workspaceId == 0 ? CurrentWorkspace : config.Workspaces[workspaceId - 1];
			Window window;
			LinkedList<Tuple<Workspace, Window>> list;
			if (TryGetManagedWindowForWorkspace(hWnd, workspace, out window, out list))
			{
				if (window.WorkspacesCount == 1)
				{
					Utilities.QuitApplication(window.hWnd);
				}
				else
				{
					if (workspace.IsWorkspaceVisible)
					{
						HideWindow(window);
					}
					workspace.WindowDestroyed(window);

					list.Remove(Tuple.Create(workspace, window));
					list.Where(t => --t.Item2.WorkspacesCount == 1).ForEach(t => t.Item1.RemoveFromSharedWindows(t.Item2));

					if (workspace.IsCurrentWorkspace && setForeground)
					{
						DoForTopmostWindowForWorkspace(workspace, ActivateWindow);
					}
				}
			}
		}

		public void SwitchToWorkspace(int workspaceId, bool setForeground = true)
		{
			var newWorkspace = workspaceId == 0 ? CurrentWorkspace : config.Workspaces[workspaceId - 1];
			if (newWorkspace.id != CurrentWorkspace.id)
			{
				if (newWorkspace.IsWorkspaceVisible)
				{
					// workspace is already visible on another monitor

					if (setForeground)
					{
						DoForTopmostWindowForWorkspace(newWorkspace, ForceForegroundWindow);
					}

					CurrentWorkspace.IsCurrentWorkspace = false;
					newWorkspace.IsCurrentWorkspace = true;
				}
				else
				{
					// TODO: must check if there are shared windows on two different monitors

					if (CurrentWorkspace.Monitor.temporarilyShownWindows.Count > 0)
					{
						CurrentWorkspace.Monitor.temporarilyShownWindows.ForEach(hWnd => HideWindow(applications[hWnd].First.Value.Item2));
						CurrentWorkspace.Monitor.temporarilyShownWindows.Clear();
					}

					var currentVisibleWorkspace = newWorkspace.Monitor.CurrentVisibleWorkspace;

					var needsToReposition = newWorkspace.NeedsToReposition();

					if (!needsToReposition)
					{
						// first show and hide if there are no changes
						ShowHideWindows(currentVisibleWorkspace, newWorkspace, setForeground);
					}

					CurrentWorkspace.IsCurrentWorkspace = false;
					newWorkspace.Monitor.SwitchToWorkspace(newWorkspace);
					newWorkspace.IsCurrentWorkspace = true;

					if (needsToReposition)
					{
						// show and hide only after Reposition has been called if there are changes
						ShowHideWindows(currentVisibleWorkspace, newWorkspace, setForeground);
					}
				}

				if (monitors.Length > 1)
				{
					if (CurrentWorkspace.Monitor != newWorkspace.Monitor)
					{
						if (config.MoveMouseOverMonitorsOnSwitch)
						{
							Utilities.MoveMouseToMiddleOf(newWorkspace.Monitor.Bounds);
						}

						// remove windows from ALT-TAB menu and Taskbar
						if (CurrentWorkspace.hideFromAltTabWhenOnInactiveWorkspaceCount > 0)
						{
							CurrentWorkspace.GetWindows().Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(false));
						}
					}

					// add windows to ALT-TAB menu and Taskbar
					if (newWorkspace.hideFromAltTabWhenOnInactiveWorkspaceCount > 0)
					{
						newWorkspace.GetWindows().Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(true));
					}
				}

				CurrentWorkspace = newWorkspace;

				DoForTopmostWindowForWorkspace(CurrentWorkspace, w => CurrentWorkspace.WindowActivated(w.hWnd));
			}
		}

		public void MoveWorkspaceToMonitor(Workspace workspace, Monitor newMonitor, bool showOnNewMonitor = true, bool switchTo = true)
		{
			var oldMonitor = workspace.Monitor;
			if (oldMonitor != newMonitor && oldMonitor.Workspaces.Count() > 1)
			{
				// unswitch the current workspace
				if (CurrentWorkspace != workspace && switchTo)
				{
					CurrentWorkspace.IsCurrentWorkspace = false;

					// remove windows from ALT-TAB menu and Taskbar
					if (CurrentWorkspace.hideFromAltTabWhenOnInactiveWorkspaceCount > 0)
					{
						CurrentWorkspace.GetWindows().Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(false));
					}
				}

				// if the workspace to be moved is visible on the old monitor, switch to another one
				if (oldMonitor.CurrentVisibleWorkspace == workspace)
				{
					var oldMonitorNewWorkspace = oldMonitor.Workspaces.First(ws => !ws.IsWorkspaceVisible);
					ShowHideWindows(workspace, oldMonitorNewWorkspace, false);
					oldMonitor.SwitchToWorkspace(oldMonitorNewWorkspace);
				}

				// remove from old/add to new monitor
				oldMonitor.RemoveWorkspace(workspace);
				workspace.Monitor = newMonitor;
				newMonitor.AddWorkspace(workspace);

				if (switchTo || showOnNewMonitor) // if the workspace must be switched to, it must be shown too
				{
					// switch to the workspace now on the new monitor
					ShowHideWindows(newMonitor.CurrentVisibleWorkspace, workspace, true);
					newMonitor.SwitchToWorkspace(workspace);
				}

				// switch to the moved workspace
				if (CurrentWorkspace != workspace && switchTo)
				{
					workspace.IsCurrentWorkspace = true;

					// add windows to ALT-TAB menu and Taskbar
					if (workspace.hideFromAltTabWhenOnInactiveWorkspaceCount > 0)
					{
						workspace.GetWindows().Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(true));
					}

					CurrentWorkspace = workspace;
				}

				if (CurrentWorkspace == workspace && config.MoveMouseOverMonitorsOnSwitch)
				{
					Utilities.MoveMouseToMiddleOf(workspace.Monitor.Bounds);
				}

				// reposition the windows on the workspace
				workspace.Reposition();

				Workspace.DoWorkspaceMonitorChanged(workspace, oldMonitor, newMonitor);
			}
		}

		// TODO: AddBarToWorkspace(IBar bar, Workspace workspace) ?

		public void ToggleWindowFloating(IntPtr hWnd)
		{
			Window window;
			LinkedList<Tuple<Workspace, Window>> _;
			if (TryGetManagedWindowForWorkspace(hWnd, CurrentWorkspace, out window, out _))
			{
				CurrentWorkspace.ToggleWindowFloating(window);
			}
		}

		public void ToggleShowHideWindowTitlebar(IntPtr hWnd)
		{
			Window window;
			LinkedList<Tuple<Workspace, Window>> _;
			if (TryGetManagedWindowForWorkspace(hWnd, CurrentWorkspace, out window, out _))
			{
				CurrentWorkspace.ToggleShowHideWindowTitlebar(window);
			}
		}

		public void ToggleShowHideWindowBorder(IntPtr hWnd)
		{
			Window window;
			LinkedList<Tuple<Workspace, Window>> _;
			if (TryGetManagedWindowForWorkspace(hWnd, CurrentWorkspace, out window, out _))
			{
				CurrentWorkspace.ToggleShowHideWindowBorder(window);
			}
		}

		public void ToggleTaskbarVisibility()
		{
			CurrentWorkspace.ToggleWindowsTaskbarVisibility();
		}

		public void ToggleShowHideWindowInTaskbar(IntPtr hWnd)
		{
			Window window;
			LinkedList<Tuple<Workspace, Window>> _;
			if (TryGetManagedWindowForWorkspace(hWnd, CurrentWorkspace, out window, out _))
			{
				window.ToggleShowHideInTaskbar();
			}
		}

		public void ToggleShowHideWindowMenu(IntPtr hWnd)
		{
			Window window;
			LinkedList<Tuple<Workspace, Window>> _;
			if (TryGetManagedWindowForWorkspace(hWnd, CurrentWorkspace, out window, out _))
			{
				window.ToggleShowHideWindowMenu();
			}
		}

		public void SwitchToApplication(IntPtr hWnd)
		{
			LinkedList<Tuple<Workspace, Window>> list;
			if (ApplicationsTryGetValue(hWnd, out list))
			{
				if (list.All(t => !t.Item1.IsCurrentWorkspace))
				{
					var visibleWorkspace = list.Select(t => t.Item1).FirstOrDefault(ws => ws.IsWorkspaceVisible);
					SwitchToWorkspace(visibleWorkspace != null ? visibleWorkspace.id : list.First.Value.Item1.id, false);
				}
				ActivateWindow(list.First.Value.Item2);
			}
		}

		public void RunOrShowApplication(string className, string path, string displayName = ".*", string processName = ".*", string arguments = "")
		{
			var classNameRegex = new System.Text.RegularExpressions.Regex(className, System.Text.RegularExpressions.RegexOptions.Compiled);
			var displayNameRegex = new System.Text.RegularExpressions.Regex(displayName, System.Text.RegularExpressions.RegexOptions.Compiled);
			var processNameRegex = new System.Text.RegularExpressions.Regex(processName, System.Text.RegularExpressions.RegexOptions.Compiled);

			var window = applications.Values.Select(list => list.First.Value.Item2).
				FirstOrDefault(w => classNameRegex.IsMatch(w.className) && displayNameRegex.IsMatch(w.DisplayName) && processNameRegex.IsMatch(w.processName));
			if (window != null)
			{
				SwitchToApplication(window.hWnd);
			}
			else
			{
				Utilities.RunApplication(path, arguments);
			}
		}

		public void RegisterMessage(int message, HandleMessageDelegate targetHandler)
		{
			HandleMessageDelegate handlers;
			if (messageHandlers.TryGetValue(message, out handlers))
			{
				messageHandlers[message] = handlers + targetHandler;
			}
			else
			{
				messageHandlers[message] = targetHandler;
			}
		}

		public void TemporarilyShowWindowOnCurrentWorkspace(Window window)
		{
			if (!NativeMethods.IsWindowVisible(window.hWnd))
			{
				CurrentWorkspace.Monitor.temporarilyShownWindows.Add(window.hWnd);
				window.ShowAsync();
			}
		}

		public void DismissTemporarilyShownWindow(IntPtr hWnd)
		{
			LinkedList<Tuple<Workspace, Window>> list;
			if (ApplicationsTryGetValue(hWnd, out list))
			{
				var window = list.First.Value.Item2;
				var monitor = monitors.FirstOrDefault(m => m.temporarilyShownWindows.Remove(window.hWnd));
				if (monitor != null)
				{
					HideWindow(window);
					if (monitor.CurrentVisibleWorkspace.IsCurrentWorkspace)
					{
						DoForTopmostWindowForWorkspace(CurrentWorkspace, ActivateWindow);
					}
				}
			}
		}

		#endregion

		#region Message Loop Stuff

		private void WinEventDelegate(IntPtr hWinEventHook, NativeMethods.EVENT eventType,
			IntPtr hWnd, NativeMethods.OBJID idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (idChild == NativeMethods.CHILDID_SELF && idObject == NativeMethods.OBJID.OBJID_WINDOW && hWnd != IntPtr.Zero)
			{
				LinkedList<Tuple<Workspace, Window>> list;
				switch (eventType)
				{
					case NativeMethods.EVENT.EVENT_OBJECT_SHOW:
						if (Utilities.IsAppWindow(hWnd))
						{
							if (!applications.TryGetValue(hWnd, out list)) // if a new window has shown
							{
								AddWindowToWorkspace(hWnd);
							}
							else // a hidden window has shown
							{
								// there is a problem with some windows showing up when others are created.
								// how to reproduce: start BitComet 1.26 on some workspace, switch to another one
								// and start explorer.exe (the file manager)
								// on Windows 7 Ultimate x64 SP1

								// another problem is that some windows continuously keep showing when hidden.
								// how to reproduce: TortoiseSVN. About box. Click check for updates. This window
								// keeps showing up when changing workspaces
								WindowShownOrActivated(list);
							}
						}
						break;
					// this is needed as some windows could be destroyed without being visible
					case NativeMethods.EVENT.EVENT_OBJECT_DESTROY:
						if (applications.TryGetValue(hWnd, out list))
						{
							UnmanageWindow(list);
							hiddenApplications.RemoveAll(hWnd);
						}
						break;
					// this is needed as some windows like Outlook 2010 splash screen
					// do not send an HSHELL_WINDOWDESTROYED
					case NativeMethods.EVENT.EVENT_OBJECT_HIDE:
						if (applications.TryGetValue(hWnd, out list) &&
							hiddenApplications.Remove(hWnd) == HashMultiSet<IntPtr>.RemoveResult.NotFound)
						{
							UnmanageWindow(list);
						}
						break;
					// these actually work (in contrast with HSHELL_GETMINRECT)
					case NativeMethods.EVENT.EVENT_SYSTEM_MINIMIZESTART:
						CurrentWorkspace.WindowMinimized(hWnd);
						WaitAndActivateNextTopmost(hWnd);
						break;
					case NativeMethods.EVENT.EVENT_SYSTEM_MINIMIZEEND:
						CurrentWorkspace.WindowRestored(hWnd);
						break;
					// HSHELL_WINDOWACTIVATED/HSHELL_RUDEAPPACTIVATED doesn't work for some windows like Digsby Buddy List
					// EVENT_OBJECT_FOCUS doesn't work with Firefox on the other hand
					case NativeMethods.EVENT.EVENT_SYSTEM_FOREGROUND:
						// the check for visibility is necessary because some windows are activated, i.e.
						// EVENT_SYSTEM_FOREGROUND is sent for them, BEFORE they are shown. Then AddWindowToWorkspace
						// could try to activate them or something, which is bad if they are still invisible

						// this, however, is not good for unmanaged windows, which won't be activated because of
						// AddWindowToWorkspace and their activation won't be noted when created
						if (NativeMethods.IsWindowVisible(hWnd) && !hiddenApplications.Contains(hWnd))
						{
							if (hWnd != NativeMethods.shellWindow)
							{
								if (ApplicationsTryGetValue(hWnd, out list))
								{
									if (!CurrentWorkspace.Monitor.temporarilyShownWindows.Contains(list.First.Value.Item2.hWnd))
									{
										hWnd = WindowShownOrActivated(list);
									}
								}
								else if (AddWindowToWorkspace(hWnd))
								{
									return ;
								}
							}

							CurrentWorkspace.WindowActivated(hWnd);
						}
						break;
				}
			}
		}

		private IntPtr WindowShownOrActivated(LinkedList<Tuple<Workspace, Window>> list)
		{
			var window = list.First.Value.Item2;
			var activatedWindow = window.hWnd;

			if (list.All(t => !t.Item1.IsCurrentWorkspace))
			{
				Workspace workspace;
				if (monitors.Length > 1 &&
					(workspace = list.Select(t => t.Item1).FirstOrDefault(ws => ws.IsWorkspaceVisible)) != null)
				{
					// the window is actually visible on another monitor
					// (e.g. when the user has ALT-TABbed to the window across monitors)

					SwitchToWorkspace(workspace.id, false);
				}
				else
				{
					switch (window.onHiddenWindowShownAction)
					{
						case OnWindowCreatedOrShownAction.SwitchToWindowsWorkspace:
							SwitchToApplication(window.hWnd);
							break;
						case OnWindowCreatedOrShownAction.MoveWindowToCurrentWorkspace:
							ChangeApplicationToWorkspace(window.hWnd, CurrentWorkspace.id, list.First.Value.Item1.id);
							break;
						case OnWindowCreatedOrShownAction.TemporarilyShowWindowOnCurrentWorkspace:
							CurrentWorkspace.Monitor.temporarilyShownWindows.Add(window.hWnd);
							break;
						case OnWindowCreatedOrShownAction.HideWindow:
							HideWindow(window);
							activatedWindow = DoForTopmostWindowForWorkspace(CurrentWorkspace, ActivateWindow);
							break;
					}
				}
			}

			return activatedWindow;
		}

		protected override void WndProc(ref Message m)
		{
			if (m.Msg == NativeMethods.WM_SHELLHOOKMESSAGE)
			{
				LinkedList<Tuple<Workspace, Window>> list;
				switch ((NativeMethods.ShellEvents) m.WParam)
				{
					case NativeMethods.ShellEvents.HSHELL_REDRAW:
						if (applications.TryGetValue(m.LParam, out list))
						{
							var text = NativeMethods.GetText(m.LParam);
							if (text != list.First.Value.Item2.DisplayName)
							{
								foreach (var t in list)
								{
									t.Item2.DisplayName = text;
									DoWindowTitleOrIconChanged(t.Item1, t.Item2, text, null);
								}
							}
							else if (list.First.Value.Item2.updateIcon)
							{
								Utilities.GetWindowSmallIconAsBitmap(list.First.Value.Item2.hWnd, bitmap =>
									list.ForEach(t => DoWindowTitleOrIconChanged(t.Item1, t.Item2, null, bitmap)));
							}
						}
						break;
					// this is the only thing that cannot be done with WinEvents (as far as I can tell)
					// it would be nice to remove the shell hook and use only WinEvents
					case NativeMethods.ShellEvents.HSHELL_FLASH:
						if (ApplicationsTryGetValue(m.LParam, out list))
						{
							DoWindowFlashing(list);
						}
						break;
				}
			}
			else
			{
				var res = false;

				HandleMessageDelegate messageDelegate;
				if (messageHandlers.TryGetValue(m.Msg, out messageDelegate))
				{
					foreach (HandleMessageDelegate handler in messageDelegate.GetInvocationList())
					{
						res |= handler(ref m);
					}
				}

				if (!res)
				{
					base.WndProc(ref m);
				}
			}
		}

		#endregion
	}
}
