using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Windawesome
{
	public sealed class Windawesome : NativeWindow
	{
		public Workspace CurrentWorkspace { get; private set; }
		public Workspace PreviousWorkspace { get; private set; }

		public readonly Monitor[] monitors;
		public readonly Config config;

		public delegate bool HandleMessageDelegate(ref Message m);

		public static IntPtr HandleStatic { get; private set; }
		public static readonly bool isRunningElevated;
		public static readonly bool isAtLeastVista;
		public static readonly bool isAtLeast7;
		public static readonly Size smallIconSize;
		public static readonly IntPtr taskbarButtonsWindowHandle;

		private readonly Dictionary<IntPtr, LinkedList<Tuple<Workspace, Window>>> applications; // hWnd to a list of workspaces and windows
		private readonly HashMultiSet<IntPtr> hiddenApplications;
		private readonly IntPtr getForegroundPrivilageAtom;
		private readonly uint windawesomeThreadId = NativeMethods.GetCurrentThreadId();
		private const uint postActionMessageNum = NativeMethods.WM_USER;

		private readonly Tuple<NativeMethods.MOD, Keys> altTabHotkey = new Tuple<NativeMethods.MOD, Keys>(NativeMethods.MOD.MOD_ALT, Keys.Tab);
		private readonly Queue<Action> postedActions;
		private readonly Dictionary<int, HandleMessageDelegate> messageHandlers;

		private readonly NativeMethods.WinEventDelegate winEventDelegate;
		private readonly IntPtr windowShownOrDestroyedWinEventHook;
		private readonly IntPtr windowMinimizedOrRestoredWinEventHook;
		private readonly IntPtr windowFocusedWinEventHook;

		private IntPtr forceForegroundWindow;

		#region System Changes

#if !DEBUG
		private static readonly NativeMethods.NONCLIENTMETRICS originalNonClientMetrics;
#endif
		private static readonly NativeMethods.ANIMATIONINFO originalAnimationInfo;
		private static readonly bool originalHideMouseWhenTyping;
		private static readonly bool originalFocusFollowsMouse;
		private static readonly bool originalFocusFollowsMouseSetOnTop;

		#endregion

		#region Events

		public delegate void WindowTitleOrIconChangedEventHandler(Workspace workspace, Window window, string newText);
		public static event WindowTitleOrIconChangedEventHandler WindowTitleOrIconChanged;

		public delegate void WindowFlashingEventHandler(LinkedList<Tuple<Workspace, Window>> list);
		public static event WindowFlashingEventHandler WindowFlashing;

		public delegate void ProgramRuleMatchedEventHandler(ProgramRule programRule, IntPtr hWnd, string cName, string dName, string pName, NativeMethods.WS style, NativeMethods.WS_EX exStyle);
		public static event ProgramRuleMatchedEventHandler ProgramRuleMatched;

		public delegate void WindawesomeExitingEventHandler();
		public static event WindawesomeExitingEventHandler WindawesomeExiting;

		private static void DoWindowTitleOrIconChanged(Workspace workspace, Window window, string newText)
		{
			if (WindowTitleOrIconChanged != null)
			{
				WindowTitleOrIconChanged(workspace, window, newText);
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

		static Windawesome()
		{
			isAtLeastVista = Environment.OSVersion.Version.Major >= 6;
			isAtLeast7 = isAtLeastVista && Environment.OSVersion.Version.Minor >= 1;

			isRunningElevated = NativeMethods.IsCurrentProcessElevatedInRespectToShell();

			#region System Changes

#if !DEBUG
			originalNonClientMetrics = NativeMethods.NONCLIENTMETRICS.Default;
			NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETNONCLIENTMETRICS, originalNonClientMetrics.cbSize,
				ref originalNonClientMetrics, 0);
#endif

			originalAnimationInfo = NativeMethods.ANIMATIONINFO.Default;
			NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETANIMATION, originalAnimationInfo.cbSize,
				ref originalAnimationInfo, 0);

			NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETMOUSEVANISH, 0,
				ref originalHideMouseWhenTyping, 0);

			NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETACTIVEWINDOWTRACKING, 0,
				ref originalFocusFollowsMouse, 0);

			NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETACTIVEWNDTRKZORDER, 0,
				ref originalFocusFollowsMouseSetOnTop, 0);

			#endregion

			smallIconSize = SystemInformation.SmallIconSize;

			taskbarButtonsWindowHandle = Monitor.taskbarHandle;
			taskbarButtonsWindowHandle = NativeMethods.FindWindowEx(taskbarButtonsWindowHandle, IntPtr.Zero, "ReBarWindow32", "");
			taskbarButtonsWindowHandle = NativeMethods.FindWindowEx(taskbarButtonsWindowHandle, IntPtr.Zero, "MSTaskSwWClass", "Running Applications");
		}

		internal Windawesome()
		{
			applications = new Dictionary<IntPtr, LinkedList<Tuple<Workspace, Window>>>(20);
			hiddenApplications = new HashMultiSet<IntPtr>();
			messageHandlers = new Dictionary<int, HandleMessageDelegate>(2);
			postedActions = new Queue<Action>(5);

			monitors = Screen.AllScreens.Select((_, i) => new Monitor(i)).ToArray();

			this.CreateHandle(new CreateParams { Parent = NativeMethods.HWND_MESSAGE });
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
			PreviousWorkspace = CurrentWorkspace;

			// add workspaces to their corresponding monitors
			monitors.ForEach(m => config.Workspaces.Where(w => w.Monitor == m).ForEach(m.AddWorkspace)); // n ^ 2 but hopefully fast enough

			// set starting workspaces for each monitor
			config.StartingWorkspaces.ForEach(w => w.Monitor.SetStartingWorkspace(w));

			// initialize bars and plugins
			config.Bars.ForEach(b => b.InitializeBar(this));
			config.Plugins.ForEach(p => p.InitializePlugin(this));

			// add all windows to their respective workspaces
			NativeMethods.EnumWindows((hWnd, _) => (IsAppWindow(hWnd) && AddWindowToWorkspace(hWnd, finishedInitializing: false)) || true, IntPtr.Zero);

			// add a handler for when the working area or screen rosolution changes as well as
			// a handler for the system shutting down/restarting
			SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
			SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
			SystemEvents.SessionEnded += OnSessionEnded;

			#region System Changes

#if !DEBUG
			// set the global border and padded border widths
			var metrics = originalNonClientMetrics;
			if (config.WindowBorderWidth >= 0 && metrics.iBorderWidth != config.WindowBorderWidth)
			{
				metrics.iBorderWidth = config.WindowBorderWidth;
			}
			if (isAtLeastVista && config.WindowPaddedBorderWidth >= 0 && metrics.iPaddedBorderWidth != config.WindowPaddedBorderWidth)
			{
				metrics.iPaddedBorderWidth = config.WindowPaddedBorderWidth;
			}
			if ((config.WindowBorderWidth >= 0 && metrics.iBorderWidth != config.WindowBorderWidth) ||
				(isAtLeastVista && config.WindowPaddedBorderWidth >= 0 && metrics.iPaddedBorderWidth != config.WindowPaddedBorderWidth))
			{
				System.Threading.Tasks.Task.Factory.StartNew(() =>
					NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
						ref metrics, NativeMethods.SPIF.SPIF_SENDCHANGE));
			}
#endif

			// set the minimize/maximize/restore animations
			if ((originalAnimationInfo.iMinAnimate == 1 && !config.ShowMinimizeMaximizeRestoreAnimations) ||
				(originalAnimationInfo.iMinAnimate == 0 &&  config.ShowMinimizeMaximizeRestoreAnimations))
			{
				var animationInfo = originalAnimationInfo;
				animationInfo.iMinAnimate = config.ShowMinimizeMaximizeRestoreAnimations ? 1 : 0;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETANIMATION, animationInfo.cbSize,
					ref animationInfo, NativeMethods.SPIF.SPIF_SENDCHANGE);
			}

			// set the "hide mouse when typing"
			if (config.HideMouseWhenTyping != originalHideMouseWhenTyping)
			{
				var hideMouseWhenTyping = config.HideMouseWhenTyping;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETMOUSEVANISH, 0,
					ref hideMouseWhenTyping, NativeMethods.SPIF.SPIF_SENDCHANGE);
			}

			// set the "focus follows mouse"
			if (config.FocusFollowsMouse != originalFocusFollowsMouse)
			{
				var focusFollowsMouse = config.FocusFollowsMouse;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWINDOWTRACKING, 0,
					ref focusFollowsMouse, NativeMethods.SPIF.SPIF_SENDCHANGE);
			}

			// set the "set window on top on focus follows mouse"
			if (config.FocusFollowsMouseSetOnTop != originalFocusFollowsMouseSetOnTop)
			{
				var focusFollowsMouseSetOnTop = config.FocusFollowsMouseSetOnTop;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWNDTRKZORDER, 0,
					ref focusFollowsMouseSetOnTop, NativeMethods.SPIF.SPIF_SENDCHANGE);
			}

			#endregion

			// register hotkey for forcing a foreground window
			getForegroundPrivilageAtom = (IntPtr) NativeMethods.GlobalAddAtom("WindawesomeShortcutGetForegroundPrivilage");
			if (!NativeMethods.RegisterHotKey(this.Handle, (ushort) getForegroundPrivilageAtom, config.UniqueHotkey.Item1, config.UniqueHotkey.Item2))
			{
				OutputWarning("There was a problem registering the unique hotkey! Probably this key-combination is in " +
					"use by some other program! Please use a unique one, otherwise Windawesome will sometimes have a problem " +
					" switching to windows as you change workspaces!");
			}

			// initialize all workspaces and hide windows not on StartingWorkspaces
			var windowsToHide = new HashSet<Window>();
			foreach (var workspace in config.Workspaces)
			{
				workspace.windowsZOrder.ForEach(w => windowsToHide.Add(w));
				workspace.Initialize();
			}
			windowsToHide.ExceptWith(config.StartingWorkspaces.SelectMany(ws => ws.windowsZOrder));
			var winPosInfo = NativeMethods.BeginDeferWindowPos(windowsToHide.Count);
			winPosInfo = windowsToHide.Where(WindowIsNotHung).Aggregate(winPosInfo, (current, w) =>
				NativeMethods.DeferWindowPos(current, w.hWnd, IntPtr.Zero, 0, 0, 0, 0,
					NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE |
					NativeMethods.SWP.SWP_NOSIZE | NativeMethods.SWP.SWP_NOZORDER |
					NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_HIDEWINDOW));
			NativeMethods.EndDeferWindowPos(winPosInfo);

			// remove windows from ALT-TAB menu and Taskbar
			config.StartingWorkspaces.Where(ws => ws != CurrentWorkspace).SelectMany(ws => ws.windowsZOrder).
				Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(false));

			// initialize monitors and switch to the default starting workspaces
			monitors.ForEach(m => m.Initialize());
			Monitor.ShowHideWindowsTaskbar(CurrentWorkspace.ShowWindowsTaskbar);
			SetWorkspaceTopManagedWindowAsForeground(CurrentWorkspace);
			CurrentWorkspace.IsCurrentWorkspace = true;

			// register a shell hook
			NativeMethods.RegisterShellHookWindow(this.Handle);

			// register some shell events
			winEventDelegate = WinEventDelegate;
			windowShownOrDestroyedWinEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT.EVENT_OBJECT_DESTROY, NativeMethods.EVENT.EVENT_OBJECT_HIDE,
				IntPtr.Zero, winEventDelegate, 0, 0,
				NativeMethods.WINEVENT.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT.WINEVENT_SKIPOWNTHREAD);
			windowMinimizedOrRestoredWinEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT.EVENT_SYSTEM_MINIMIZEEND,
				IntPtr.Zero, winEventDelegate, 0, 0,
				NativeMethods.WINEVENT.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT.WINEVENT_SKIPOWNTHREAD);
			windowFocusedWinEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT.EVENT_OBJECT_FOCUS, NativeMethods.EVENT.EVENT_OBJECT_FOCUS,
				IntPtr.Zero, winEventDelegate, 0, 0,
				NativeMethods.WINEVENT.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT.WINEVENT_SKIPOWNTHREAD);
		}

		public void Quit()
		{
			SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
			SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
			SystemEvents.SessionEnded -= OnSessionEnded;

			// unregister the shell events
			NativeMethods.UnhookWinEvent(windowShownOrDestroyedWinEventHook);
			NativeMethods.UnhookWinEvent(windowMinimizedOrRestoredWinEventHook);
			NativeMethods.UnhookWinEvent(windowFocusedWinEventHook);

			// unregister shell hook
			NativeMethods.DeregisterShellHookWindow(this.Handle);

			NativeMethods.UnregisterHotKey(this.Handle, (ushort) getForegroundPrivilageAtom);
			NativeMethods.GlobalDeleteAtom((ushort) getForegroundPrivilageAtom);

			// dispose of Layouts
			config.Workspaces.ForEach(ws => ws.Layout.Dispose());

			// roll back any changes to Windows
			monitors.ForEach(m => m.Dispose());
			Monitor.StaticDispose();

			applications.Values.ForEach(l => l.First.Value.Item2.RevertToInitialValues());

			// dispose of plugins and bars
			config.Plugins.ForEach(p => p.Dispose());
			config.Bars.ForEach(b => b.Dispose());

			#region System Changes

#if !DEBUG
			// revert the size of non-client area of windows
			var metrics = originalNonClientMetrics;
			if ((config.WindowBorderWidth >= 0 && metrics.iBorderWidth != config.WindowBorderWidth) ||
				(isAtLeastVista && config.WindowPaddedBorderWidth >= 0 && metrics.iPaddedBorderWidth != config.WindowPaddedBorderWidth))
			{
				NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
					ref metrics, NativeMethods.SPIF.SPIF_SENDCHANGE);
			}
#endif

			// revert the minimize/maximize/restore animations
			if ((originalAnimationInfo.iMinAnimate == 1 && !config.ShowMinimizeMaximizeRestoreAnimations) ||
				(originalAnimationInfo.iMinAnimate == 0 &&  config.ShowMinimizeMaximizeRestoreAnimations))
			{
				var animationInfo = originalAnimationInfo;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETANIMATION, animationInfo.cbSize,
					ref animationInfo, NativeMethods.SPIF.SPIF_UPDATEINIFILE | NativeMethods.SPIF.SPIF_SENDCHANGE);
			}

			// revert the hiding of the mouse when typing
			if (config.HideMouseWhenTyping != originalHideMouseWhenTyping)
			{
				var hideMouseWhenTyping = originalHideMouseWhenTyping;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETMOUSEVANISH, 0,
					ref hideMouseWhenTyping, NativeMethods.SPIF.SPIF_UPDATEINIFILE | NativeMethods.SPIF.SPIF_SENDCHANGE);
			}

			// revert the "focus follows mouse"
			if (config.FocusFollowsMouse != originalFocusFollowsMouse)
			{
				var focusFollowsMouse = originalFocusFollowsMouse;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWINDOWTRACKING, 0,
					ref focusFollowsMouse, NativeMethods.SPIF.SPIF_UPDATEINIFILE | NativeMethods.SPIF.SPIF_SENDCHANGE);
			}

			// revert the "set window on top on focus follows mouse"
			if (config.FocusFollowsMouseSetOnTop != originalFocusFollowsMouseSetOnTop)
			{
				var focusFollowsMouseSetOnTop = originalFocusFollowsMouseSetOnTop;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWNDTRKZORDER, 0,
					ref focusFollowsMouseSetOnTop, NativeMethods.SPIF.SPIF_UPDATEINIFILE | NativeMethods.SPIF.SPIF_SENDCHANGE);
			}

			#endregion

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

		private void OnSessionEnded(object sender, SessionEndedEventArgs e)
		{
			Quit();
		}

		private static bool IsAppWindow(IntPtr hWnd)
		{
			return NativeMethods.IsWindowVisible(hWnd) &&
				!NativeMethods.GetWindowStyleLongPtr(hWnd).HasFlag(NativeMethods.WS.WS_CHILD);
		}

		private bool AddWindowToWorkspace(IntPtr hWnd, bool firstTry = true, bool finishedInitializing = true)
		{
			LinkedList<Tuple<Workspace, Window>> workspacesWindowsList;
			if (ApplicationsTryGetValue(hWnd, out workspacesWindowsList))
			{
				if (workspacesWindowsList.First.Value.Item2.IsMatchOwnedWindow(hWnd) &&
					workspacesWindowsList.First.Value.Item2.ownedWindows.FindLast(hWnd) == null)
				{
					workspacesWindowsList.First.Value.Item2.ownedWindows.AddLast(hWnd);
					workspacesWindowsList.ForEach(t => t.Item1.ownedWindowsCount++);
					return true;
				}
				return false;
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
					return AddWindowToWorkspace(hWnd, false);
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
					if (hasWorkspaceZeroRule || hasCurrentWorkspaceRule)
					{
						// this means that the window must be on the current workspace anyway
						OnWindowCreatedOnCurrentWorkspace(hWnd, programRule);
					}
					else
					{
						var hasVisibleWorkspaceRule = matchingRules.Any(r => config.Workspaces[r.workspace - 1].IsWorkspaceVisible);
						switch (programRule.onWindowCreatedAction)
						{
							case OnWindowShownAction.SwitchToWindowsWorkspace:
								PostAction(() => SwitchToApplication(hWnd));
								break;
							case OnWindowShownAction.MoveWindowToCurrentWorkspace:
								var workspaceId = CurrentWorkspace.id;
								var matchingRuleWorkspace = matchingRules.First().workspace;
								PostAction(() =>
									{
										ChangeApplicationToWorkspace(hWnd, workspaceId, matchingRuleWorkspace);
										OnWindowCreatedOnCurrentWorkspace(hWnd, programRule);
									});
								break;
							case OnWindowShownAction.TemporarilyShowWindowOnCurrentWorkspace:
								if (!hasVisibleWorkspaceRule)
								{
									CurrentWorkspace.Monitor.temporarilyShownWindows.Add(hWnd);
									OnWindowCreatedOnCurrentWorkspace(hWnd, programRule);
								}
								break;
							case OnWindowShownAction.HideWindow:
								if (!hasVisibleWorkspaceRule)
								{
									hiddenApplications.Add(hWnd);
									NativeMethods.ShowWindow(hWnd, NativeMethods.SW.SW_HIDE);
								}
								SetWorkspaceTopManagedWindowAsForeground(CurrentWorkspace);
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
						NativeMethods.RDW.RDW_ALLCHILDREN |
						NativeMethods.RDW.RDW_ERASE |
						NativeMethods.RDW.RDW_INVALIDATE);
				}

				var list = new LinkedList<Tuple<Workspace, Window>>();
				applications[hWnd] = list;

				var menu = NativeMethods.GetMenu(hWnd);
				var is64BitProcess = NativeMethods.Is64BitProcess(hWnd);

				foreach (var rule in matchingRules)
				{
					var window = new Window(hWnd, className, displayName, processName, workspacesCount,
						is64BitProcess, style, exStyle, rule, programRule, menu);

					var workspace = rule.workspace == 0 ? CurrentWorkspace : config.Workspaces[rule.workspace - 1];
					list.AddLast(new Tuple<Workspace, Window>(workspace, window));

					workspace.WindowCreated(window);
				}

				if (!programRule.showMenu)
				{
					list.First.Value.Item2.ShowWindowMenu();
				}
			}

			return true;
		}

		private void OnWindowCreatedOnCurrentWorkspace(IntPtr hWnd, ProgramRule programRule)
		{
			switch (programRule.onWindowCreatedOnCurrentWorkspaceAction)
			{
				case OnWindowCreatedOnCurrentWorkspaceAction.ActivateWindow:
					ActivateWindow(hWnd);
					break;
				case OnWindowCreatedOnCurrentWorkspaceAction.MoveToBottom:
					var topmost = CurrentWorkspace.GetTopmostZOrderWindow();
					if (topmost != null)
					{
						NativeMethods.SetWindowPos(hWnd, CurrentWorkspace.windowsZOrder.Last(w => !w.IsMinimized).hWnd, 0, 0, 0, 0,
							NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE);
						ForceForegroundWindow(topmost);
						PostAction(() => CurrentWorkspace.WindowActivated(topmost.hWnd));
					}
					else
					{
						ActivateWindow(hWnd);
					}
					break;
			}
		}

		private static void OutputWarning(string warning)
		{
			System.IO.File.AppendAllLines("warnings.txt", new[]
				{
					"------------------------------------",
					DateTime.Now.ToString(),
					warning
				});
		}

		private void RefreshApplicationsHash()
		{
			// remove all non-existent applications
			applications.Keys.Unless(NativeMethods.IsWindow).ToArray().ForEach(w => RemoveApplicationFromAllWorkspaces(w, true));

			// add any application that was not added for some reason when it was created
			NativeMethods.EnumWindows((hWnd, _) =>
				(IsAppWindow(hWnd) && !applications.ContainsKey(hWnd) && AddWindowToWorkspace(hWnd)) || true, IntPtr.Zero);
		}

		private void SetWorkspaceTopManagedWindowAsForeground(Workspace workspace)
		{
			// TODO: perhaps switch to the last window that was foreground?
			var topmost = workspace.GetTopmostZOrderWindow();
			if (topmost != null)
			{
				ForceForegroundWindow(topmost);
			}
			else
			{
				ForceForegroundWindow(NativeMethods.shellWindow);
			}
		}

		private void ForceForegroundWindow(Window window)
		{
			ForceForegroundWindow(window.ownedWindows.Last.Value);
		}

		private void ForceForegroundWindow(IntPtr hWnd)
		{
			if (WindowIsNotHung(hWnd))
			{
				var foregroundWindow = NativeMethods.GetForegroundWindow();
				if (foregroundWindow != hWnd)
				{
					var successfullyChanged = false;
					if (foregroundWindow == IntPtr.Zero)
					{
						successfullyChanged = TrySetForegroundWindow(hWnd);
					}
					else if (WindowIsNotHung(foregroundWindow))
					{
						var foregroundWindowThreadId = NativeMethods.GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
						if (NativeMethods.AttachThreadInput(windawesomeThreadId, foregroundWindowThreadId, true))
						{
							successfullyChanged = TrySetForegroundWindow(hWnd);
							NativeMethods.AttachThreadInput(windawesomeThreadId, foregroundWindowThreadId, false);
						}
					}

					if (!successfullyChanged)
					{
						forceForegroundWindow = hWnd;
						SendHotkey(config.UniqueHotkey);
					}
				}
				else
				{
					NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
						NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE);
				}
			}
		}

		private static bool TrySetForegroundWindow(IntPtr hWnd)
		{
			const int tryCount = 5;
			var count = 0;
			while (!NativeMethods.SetForegroundWindow(hWnd) && ++count < tryCount)
			{
			}

			if (count == 5)
			{
				System.Threading.Thread.Sleep(10);
				if (NativeMethods.GetForegroundWindow() != hWnd)
				{
					return false;
				}
			}

			NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
				NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE);

			return true;
		}

		#region SendHotkey

		private readonly NativeMethods.INPUT[] input = new NativeMethods.INPUT[18];

		private readonly NativeMethods.INPUT shiftKeyDown = new NativeMethods.INPUT(Keys.ShiftKey, 0);
		private readonly NativeMethods.INPUT shiftKeyUp = new NativeMethods.INPUT(Keys.ShiftKey, NativeMethods.KEYEVENTF_KEYUP);
		private readonly NativeMethods.INPUT leftShiftKeyDown = new NativeMethods.INPUT(Keys.LShiftKey, 0);
		private readonly NativeMethods.INPUT leftShiftKeyUp = new NativeMethods.INPUT(Keys.LShiftKey, NativeMethods.KEYEVENTF_KEYUP);
		private readonly NativeMethods.INPUT rightShiftKeyDown = new NativeMethods.INPUT(Keys.RShiftKey, 0);
		private readonly NativeMethods.INPUT rightShiftKeyUp = new NativeMethods.INPUT(Keys.RShiftKey, NativeMethods.KEYEVENTF_KEYUP);

		private readonly NativeMethods.INPUT leftWinKeyDown = new NativeMethods.INPUT(Keys.LWin, 0);
		private readonly NativeMethods.INPUT leftWinKeyUp = new NativeMethods.INPUT(Keys.LWin, NativeMethods.KEYEVENTF_KEYUP);
		private readonly NativeMethods.INPUT rightWinKeyDown = new NativeMethods.INPUT(Keys.RWin, 0);
		private readonly NativeMethods.INPUT rightWinKeyUp = new NativeMethods.INPUT(Keys.RWin, NativeMethods.KEYEVENTF_KEYUP);

		private readonly NativeMethods.INPUT controlKeyDown = new NativeMethods.INPUT(Keys.ControlKey, 0);
		private readonly NativeMethods.INPUT controlKeyUp = new NativeMethods.INPUT(Keys.ControlKey, NativeMethods.KEYEVENTF_KEYUP);
		private readonly NativeMethods.INPUT leftControlKeyDown = new NativeMethods.INPUT(Keys.LControlKey, 0);
		private readonly NativeMethods.INPUT leftControlKeyUp = new NativeMethods.INPUT(Keys.LControlKey, NativeMethods.KEYEVENTF_KEYUP);
		private readonly NativeMethods.INPUT rightControlKeyDown = new NativeMethods.INPUT(Keys.RControlKey, 0);
		private readonly NativeMethods.INPUT rightControlKeyUp = new NativeMethods.INPUT(Keys.RControlKey, NativeMethods.KEYEVENTF_KEYUP);

		private readonly NativeMethods.INPUT altKeyDown = new NativeMethods.INPUT(Keys.Menu, 0);
		private readonly NativeMethods.INPUT altKeyUp = new NativeMethods.INPUT(Keys.Menu, NativeMethods.KEYEVENTF_KEYUP);
		private readonly NativeMethods.INPUT leftAltKeyDown = new NativeMethods.INPUT(Keys.LMenu, 0);
		private readonly NativeMethods.INPUT leftAltKeyUp = new NativeMethods.INPUT(Keys.LMenu, NativeMethods.KEYEVENTF_KEYUP);
		private readonly NativeMethods.INPUT rightAltKeyDown = new NativeMethods.INPUT(Keys.RMenu, 0);
		private readonly NativeMethods.INPUT rightAltKeyUp = new NativeMethods.INPUT(Keys.RMenu, NativeMethods.KEYEVENTF_KEYUP);
		// sends the hotkey combination without disrupting the currently pressed modifiers
		private void SendHotkey(Tuple<NativeMethods.MOD, Keys> hotkey)
		{
			uint i = 0;

			// press needed modifiers
			var shiftShouldBePressed = hotkey.Item1.HasFlag(NativeMethods.MOD.MOD_SHIFT);
			var leftShiftPressed = (NativeMethods.GetAsyncKeyState(Keys.LShiftKey) & 0x8000) == 0x8000;
			var rightShiftPressed = (NativeMethods.GetAsyncKeyState(Keys.RShiftKey) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftShiftPressed, rightShiftPressed, shiftShouldBePressed, shiftKeyDown, leftShiftKeyUp, rightShiftKeyUp, ref i);

			var winShouldBePressed = hotkey.Item1.HasFlag(NativeMethods.MOD.MOD_WIN);
			var leftWinPressed = (NativeMethods.GetAsyncKeyState(Keys.LWin) & 0x8000) == 0x8000;
			var rightWinPressed = (NativeMethods.GetAsyncKeyState(Keys.RWin) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftWinPressed, rightWinPressed, winShouldBePressed, leftWinKeyDown, leftWinKeyUp, rightWinKeyUp, ref i);

			var controlShouldBePressed = hotkey.Item1.HasFlag(NativeMethods.MOD.MOD_CONTROL);
			var leftControlPressed = (NativeMethods.GetAsyncKeyState(Keys.LControlKey) & 0x8000) == 0x8000;
			var rightControlPressed = (NativeMethods.GetAsyncKeyState(Keys.RControlKey) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftControlPressed, rightControlPressed, controlShouldBePressed, controlKeyDown, leftControlKeyUp, rightControlKeyUp, ref i);

			var altShouldBePressed = hotkey.Item1.HasFlag(NativeMethods.MOD.MOD_ALT);
			var leftAltPressed = (NativeMethods.GetAsyncKeyState(Keys.LMenu) & 0x8000) == 0x8000;
			var rightAltPressed = (NativeMethods.GetAsyncKeyState(Keys.RMenu) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftAltPressed, rightAltPressed, altShouldBePressed, altKeyDown, leftAltKeyUp, rightAltKeyUp, ref i);

			// press and release key
			input[i++] = new NativeMethods.INPUT(hotkey.Item2, 0);
			input[i++] = new NativeMethods.INPUT(hotkey.Item2, NativeMethods.KEYEVENTF_KEYUP);

			// revert changes to modifiers
			PressReleaseModifierKey(leftAltPressed, rightAltPressed, altShouldBePressed, altKeyUp, leftAltKeyDown, rightAltKeyDown, ref i);

			PressReleaseModifierKey(leftControlPressed, rightControlPressed, controlShouldBePressed, controlKeyUp, leftControlKeyDown, rightControlKeyDown, ref i);

			PressReleaseModifierKey(leftWinPressed, rightWinPressed, winShouldBePressed, leftWinKeyUp, leftWinKeyDown, rightWinKeyDown, ref i);

			PressReleaseModifierKey(leftShiftPressed, rightShiftPressed, shiftShouldBePressed, shiftKeyUp, leftShiftKeyDown, rightShiftKeyDown, ref i);

			NativeMethods.SendInput(i, input, NativeMethods.INPUTSize);
		}

		private void PressReleaseModifierKey(
			bool leftKeyPressed, bool rightKeyPressed, bool keyShouldBePressed,
			NativeMethods.INPUT action, NativeMethods.INPUT leftAction, NativeMethods.INPUT rightAction, ref uint i)
		{
			if (keyShouldBePressed)
			{
				if (!leftKeyPressed && !rightKeyPressed)
				{
					input[i++] = action;
				}
			}
			else
			{
				if (leftKeyPressed)
				{
					input[i++] = leftAction;
				}
				if (rightKeyPressed)
				{
					input[i++] = rightAction;
				}
			}
		}

		#endregion

		private void FollowWindow(Workspace fromWorkspace, Workspace toWorkspace, bool follow, Window window)
		{
			if (follow)
			{
				if (!SwitchToWorkspace(toWorkspace.id))
				{
					ForceForegroundWindow(window);
				}
			}
			else if (fromWorkspace.IsCurrentWorkspace)
			{
				SetWorkspaceTopManagedWindowAsForeground(fromWorkspace);
			}
		}

		private void HideWindow(Window window, bool async = true)
		{
			if (NativeMethods.IsWindowVisible(window.hWnd))
			{
				hiddenApplications.Add(window.hWnd);
				if (async)
				{
					window.HideAsync();
				}
				else if (WindowIsNotHung(window))
				{
					window.Hide();
				}
			}
		}

		private void ShowHideWindows(Workspace oldWorkspace, Workspace newWorkspace, bool setForeground)
		{
			var winPosInfo = NativeMethods.BeginDeferWindowPos(newWorkspace.GetWindowsCount() + newWorkspace.ownedWindowsCount + oldWorkspace.GetWindowsCount() + oldWorkspace.ownedWindowsCount);

			var showWindows = newWorkspace.windowsZOrder;
			foreach (var window in showWindows)
			{
				winPosInfo = window.ownedWindows.Where(WindowIsNotHung).Aggregate(winPosInfo, (current, hWnd) =>
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
				oldWorkspace.windowsZOrder.Except(showWindows) : oldWorkspace.windowsZOrder;
			// if the window is not visible we shouldn't add it to hiddenApplications as EVENT_OBJECT_HIDE won't be sent
			foreach (var hWnd in hideWindows.SelectMany(w => w.ownedWindows).Where(h => NativeMethods.IsWindowVisible(h) && WindowIsNotHung(h)))
			{
				this.hiddenApplications.Add(hWnd);
				winPosInfo = NativeMethods.DeferWindowPos(winPosInfo, hWnd, IntPtr.Zero, 0, 0, 0, 0,
					NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE |
					NativeMethods.SWP.SWP_NOSIZE | NativeMethods.SWP.SWP_NOZORDER |
					NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_HIDEWINDOW);
			}

			NativeMethods.EndDeferWindowPos(winPosInfo);

			// activates the topmost non-minimized window
			if (setForeground)
			{
				SetWorkspaceTopManagedWindowAsForeground(newWorkspace);
			}
		}

		// only switches to applications in the current workspace
		private bool SwitchToApplicationInCurrentWorkspace(IntPtr hWnd)
		{
			var window = CurrentWorkspace.GetWindow(hWnd);
			if (window != null)
			{
				ActivateWindow(window);

				return true;
			}

			return false;
		}

		private static void RestoreIfMinimized(IntPtr hWnd, bool isMinimized)
		{
			if (isMinimized)
			{
				// OpenIcon does not restore the window to its previous size (e.g. maximized)
				NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW.SW_RESTORE);
				System.Threading.Thread.Sleep(NativeMethods.minimizeRestoreDelay);
			}
		}

		private void ActivateWindow(IntPtr hWnd)
		{
			RestoreIfMinimized(hWnd, NativeMethods.IsIconic(hWnd));
			ForceForegroundWindow(hWnd);
		}

		private void ActivateWindow(Window window)
		{
			RestoreIfMinimized(window.hWnd, window.IsMinimized);
			ForceForegroundWindow(window);
		}

		private static void MoveMouseToMiddleOf(Rectangle bounds)
		{
			NativeMethods.SetCursorPos((bounds.Left + bounds.Right) / 2, (bounds.Top + bounds.Bottom) / 2);
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

		#endregion

		#region API

		public static IntPtr DoForSelfAndOwnersWhile(IntPtr hWnd, Predicate<IntPtr> action)
		{
			while (hWnd != IntPtr.Zero && action(hWnd))
			{
				hWnd = NativeMethods.GetWindow(hWnd, NativeMethods.GW.GW_OWNER);
			}
			return hWnd;
		}

		public static IntPtr GetTopOwnerWindow(IntPtr hWnd)
		{
			var resultHWnd = hWnd;
			while (hWnd != IntPtr.Zero)
			{
				resultHWnd = hWnd;
				hWnd = NativeMethods.GetWindow(hWnd, NativeMethods.GW.GW_OWNER);
			}
			return resultHWnd;
		}

		public static bool WindowIsNotHung(Window window)
		{
			return WindowIsNotHung(window.hWnd);
		}

		public static bool WindowIsNotHung(IntPtr hWnd)
		{
			// IsHungAppWindow is not going to work, as it starts returning true 5 seconds after the window
			// has hung - so if a SetWindowPos, e.g., is called on such a window, it may block forever, even
			// though IsHungAppWindow returned false

			// SendMessageTimeout with a small timeout will timeout for some programs which are heavy on
			// computation and do not respond in time - like Visual Studio. A big timeout works, but if an
			// application is really hung, then Windawesome is blocked for this number of milliseconds.
			// That can be felt and is annoying. Perhaps the best scenario is:
			// return !IsHungAppWindow && (SendMessageTimeout(with_big_timeout) || GetLastWin32Error)
			// As this will not block forever at any point and it will only block the main thread for "timeout"
			// milliseconds the first 5 seconds when a program is hung - after that IsHungAppWindow catches it
			// immediately and returns. However, I decided that in most cases apps are not hung, so the overhead
			// of calling IsHungAppWindow AND SendMessageTimeout is not worth.

			return NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_NULL, UIntPtr.Zero, IntPtr.Zero,
				NativeMethods.SMTO.SMTO_ABORTIFHUNG | NativeMethods.SMTO.SMTO_BLOCK, 3000, IntPtr.Zero) != IntPtr.Zero;
		}

		public void RefreshWindawesome()
		{
			hiddenApplications.Clear();

			RefreshApplicationsHash();

			// set monitor bounds, repositions all windows in all workspaces and redraw all windows in visible workspaces
			monitors.ForEach(m => m.SetBoundsAndWorkingArea());
			config.Workspaces.ForEach(ws => ws.Reposition());
			monitors.SelectMany(m => m.CurrentVisibleWorkspace.windowsZOrder).ForEach(w => w.Redraw());

			// refresh bars
			config.Bars.ForEach(b => b.Refresh());
		}

		public void ChangeApplicationToWorkspace(IntPtr hWnd, int toWorkspaceId = 0, int fromWorkspaceId = 0, bool follow = true)
		{
			var oldWorkspace = fromWorkspaceId == 0 ? CurrentWorkspace : config.Workspaces[fromWorkspaceId - 1];
			var newWorkspace = toWorkspaceId == 0 ? CurrentWorkspace : config.Workspaces[toWorkspaceId - 1];

			if (newWorkspace.id != oldWorkspace.id)
			{
				var window = oldWorkspace.GetWindow(hWnd);

				if (window != null && !newWorkspace.ContainsWindow(hWnd))
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

					var list = applications[window.hWnd];
					list.Remove(new Tuple<Workspace, Window>(oldWorkspace, window));
					list.AddFirst(new Tuple<Workspace, Window>(newWorkspace, window));

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
				var window = oldWorkspace.GetWindow(hWnd);

				if (window != null && !newWorkspace.ContainsWindow(hWnd))
				{
					var newWindow = new Window(window);

					newWorkspace.WindowCreated(newWindow);
					if (!follow && !oldWorkspace.IsWorkspaceVisible && newWorkspace.IsWorkspaceVisible)
					{
						window.ShowAsync();
					}

					var list = applications[window.hWnd];
					list.AddFirst(new Tuple<Workspace, Window>(newWorkspace, newWindow));
					list.Where(t => ++t.Item2.WorkspacesCount == 2).ForEach(t => t.Item1.sharedWindowsCount++);

					FollowWindow(oldWorkspace, newWorkspace, follow, window);
				}
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

		public void RemoveApplicationFromWorkspace(IntPtr hWnd, int workspaceId = 0, bool setForeground = true)
		{
			var workspace = workspaceId == 0 ? CurrentWorkspace : config.Workspaces[workspaceId - 1];
			var window = workspace.GetWindow(hWnd);
			if (window != null)
			{
				if (window.WorkspacesCount == 1)
				{
					QuitApplication(window.hWnd);
				}
				else
				{
					if (workspace.IsWorkspaceVisible)
					{
						HideWindow(window);
					}
					workspace.WindowDestroyed(window);

					var list = applications[window.hWnd];
					list.Remove(new Tuple<Workspace, Window>(workspace, window));
					list.Where(t => --t.Item2.WorkspacesCount == 1).ForEach(t => t.Item1.RemoveFromSharedWindows(t.Item2));

					if (workspace.IsCurrentWorkspace && setForeground)
					{
						SetWorkspaceTopManagedWindowAsForeground(workspace);
					}
				}
			}
		}

		public void RemoveApplicationFromAllWorkspaces(IntPtr hWnd, bool windowHidden) // sort of UnmanageWindow
		{
			LinkedList<Tuple<Workspace, Window>> list;
			if (ApplicationsTryGetValue(hWnd, out list))
			{
				if (list.First.Value.Item2.hWnd == hWnd)
				{
					var oldWorkspaceWindowCount = CurrentWorkspace.GetWindowsCount();
					list.ForEach(t => t.Item1.WindowDestroyed(t.Item2));
					if (CurrentWorkspace.GetWindowsCount() != oldWorkspaceWindowCount)
					{
						// the window was on the current workspace, so activate another one
						SetWorkspaceTopManagedWindowAsForeground(CurrentWorkspace);
						// TODO: this doesn't always work when closing the last window of a workspace
						// and another one is visible on another monitor

						if (monitors.Length > 1 && CurrentWorkspace.GetWindowsCount() == 0)
						{
							// Windows sometimes activates for a second the topmost window on another monitor so it
							// disrupts the Z order of other windows

							NativeMethods.EnumWindows((h, _) =>
								{
									LinkedList<Tuple<Workspace, Window>> windowList;
									if (IsAppWindow(h) && applications.TryGetValue(h, out windowList))
									{
										var tuple = windowList.First(t => t.Item1.IsWorkspaceVisible);
										if (tuple.Item1.windowsZOrder.First.Next != null)
										{
											var secondZOrderWindow = tuple.Item1.windowsZOrder.First.Next.Value.hWnd;
											NativeMethods.SetWindowPos(tuple.Item2.hWnd, secondZOrderWindow, 0, 0, 0, 0,
												NativeMethods.SWP.SWP_ASYNCWINDOWPOS | NativeMethods.SWP.SWP_NOACTIVATE |
												NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE);
											NativeMethods.SetWindowPos(secondZOrderWindow, tuple.Item2.hWnd, 0, 0, 0, 0,
												NativeMethods.SWP.SWP_ASYNCWINDOWPOS | NativeMethods.SWP.SWP_NOACTIVATE |
												NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE);
										}
										else
										{
											NativeMethods.SetWindowPos(tuple.Item2.hWnd, NativeMethods.HWND_BOTTOM, 0, 0, 0, 0,
												NativeMethods.SWP.SWP_ASYNCWINDOWPOS | NativeMethods.SWP.SWP_NOACTIVATE |
												NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE);
										}
										return false;
									}
									return true;
								}, IntPtr.Zero);
						}
					}
					var window = list.First.Value.Item2;
					if (!window.ShowMenu && window.menu != IntPtr.Zero)
					{
						if (windowHidden)
						{
							NativeMethods.DestroyMenu(window.menu);
						}
						else
						{
							window.ToggleShowHideWindowMenu();
						}
					}
					applications.Remove(hWnd);
					monitors.ForEach(m => m.temporarilyShownWindows.Remove(hWnd));
				}
				else
				{
					var node = list.First.Value.Item2.ownedWindows.FindLast(hWnd);
					if (node != null)
					{
						list.First.Value.Item2.ownedWindows.Remove(node);
						list.ForEach(t => t.Item1.ownedWindowsCount--);
					}
				}
			}
		}

		public bool SwitchToWorkspace(int workspaceId, bool setForeground = true)
		{
			var newWorkspace = workspaceId == 0 ? CurrentWorkspace : config.Workspaces[workspaceId - 1];
			if (newWorkspace.id != CurrentWorkspace.id)
			{
				if (newWorkspace.IsWorkspaceVisible)
				{
					// workspace is already visible on another monitor

					if (setForeground)
					{
						SetWorkspaceTopManagedWindowAsForeground(newWorkspace);
					}

					CurrentWorkspace.IsCurrentWorkspace = false;
					newWorkspace.IsCurrentWorkspace = true;
				}
				else
				{
					// TODO: must check if there are shared windows on two different monitors

					if (CurrentWorkspace.Monitor.temporarilyShownWindows.Count > 0)
					{
						CurrentWorkspace.Monitor.temporarilyShownWindows.ForEach(hWnd => HideWindow(applications[hWnd].First.Value.Item2, false));
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
						if (CurrentWorkspace.GetWindowsCount() > 0 && CurrentWorkspace.hideFromAltTabWhenOnInactiveWorkspaceCount != CurrentWorkspace.GetWindowsCount())
						{
							// TODO: moving the windows from CurrentWorkspace to the bottom of the Z-order is not correct
							// if there are more than 2 monitors - rather, they should be above the rest of the monitors' windows

							var winPosInfo = NativeMethods.BeginDeferWindowPos(CurrentWorkspace.GetWindowsCount());

							var previousHWnd = NativeMethods.HWND_BOTTOM;
							foreach (var window in CurrentWorkspace.windowsZOrder.Where(WindowIsNotHung))
							{
								winPosInfo = NativeMethods.DeferWindowPos(winPosInfo, window.hWnd, previousHWnd, 0, 0, 0, 0,
									NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE);
								previousHWnd = window.hWnd;
							}

							NativeMethods.EndDeferWindowPos(winPosInfo);
						}

						if (config.MoveMouseOverMonitorsOnSwitch)
						{
							MoveMouseToMiddleOf(newWorkspace.Monitor.Bounds);
						}

						// remove windows from ALT-TAB menu and Taskbar
						if (CurrentWorkspace.hideFromAltTabWhenOnInactiveWorkspaceCount > 0)
						{
							CurrentWorkspace.windowsZOrder.Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(false));
						}
					}

					// add windows to ALT-TAB menu and Taskbar
					if (newWorkspace.hideFromAltTabWhenOnInactiveWorkspaceCount > 0)
					{
						newWorkspace.windowsZOrder.Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(true));
					}
				}

				// set previous and current workspaces
				PreviousWorkspace = CurrentWorkspace;
				CurrentWorkspace = newWorkspace;

				return true;
			}

			return false;
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
						CurrentWorkspace.windowsZOrder.Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(false));
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
						workspace.windowsZOrder.Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(true));
					}

					PreviousWorkspace = CurrentWorkspace;
					CurrentWorkspace = workspace;
				}

				if (CurrentWorkspace == workspace && config.MoveMouseOverMonitorsOnSwitch)
				{
					MoveMouseToMiddleOf(workspace.Monitor.Bounds);
				}

				// reposition the windows on the workspace
				workspace.Reposition();

				Workspace.DoWorkspaceMonitorChanged(workspace, oldMonitor, newMonitor);
			}
		}

		public bool HideBar(IBar bar)
		{
			return true;
			//return CurrentWorkspace.HideBar(config.Workspaces.Length, config.Workspaces, bar);
		}

		public bool ShowBar(IBar bar, bool top = true, int position = 0)
		{
			return true;
			//return CurrentWorkspace.ShowBar(config.Workspaces.Length, config.Workspaces, bar, top, position);
		}

		public void ToggleWindowFloating(IntPtr hWnd)
		{
			CurrentWorkspace.ToggleWindowFloating(hWnd);
		}

		public void ToggleShowHideWindowInTaskbar(IntPtr hWnd)
		{
			CurrentWorkspace.ToggleShowHideWindowInTaskbar(hWnd);
		}

		public void ToggleShowHideWindowTitlebar(IntPtr hWnd)
		{
			CurrentWorkspace.ToggleShowHideWindowTitlebar(hWnd);
		}

		public void ToggleShowHideWindowBorder(IntPtr hWnd)
		{
			CurrentWorkspace.ToggleShowHideWindowBorder(hWnd);
		}

		public void ToggleTaskbarVisibility()
		{
			CurrentWorkspace.ToggleWindowsTaskbarVisibility();
		}

		public void ToggleShowHideWindowMenu(IntPtr hWnd)
		{
			CurrentWorkspace.ToggleShowHideWindowMenu(hWnd);
		}

		public void SwitchToApplication(IntPtr hWnd)
		{
			if (!SwitchToApplicationInCurrentWorkspace(hWnd))
			{
				LinkedList<Tuple<Workspace, Window>> list;
				if (applications.TryGetValue(hWnd, out list))
				{
					SwitchToWorkspace(list.First.Value.Item1.id, false);
					SwitchToApplicationInCurrentWorkspace(hWnd);
				}
			}
		}

		public void RunApplication(string path, string arguments = "")
		{
			System.Threading.Tasks.Task.Factory.StartNew(() =>
				{
					if (isAtLeastVista && isRunningElevated)
					{
						NativeMethods.RunApplicationNonElevated(path, arguments); // TODO: this is not working on XP
					}
					else
					{
						System.Diagnostics.Process.Start(path, arguments);
					}
				});
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
				RunApplication(path, arguments);
			}
		}

		public void QuitApplication(IntPtr hWnd)
		{
			NativeMethods.SendNotifyMessage(hWnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_CLOSE, IntPtr.Zero);
		}

		public void MinimizeApplication(IntPtr hWnd)
		{
			NativeMethods.SendNotifyMessage(hWnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MINIMIZE, IntPtr.Zero);
		}

		public void MaximizeApplication(IntPtr hWnd)
		{
			NativeMethods.SendNotifyMessage(hWnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MAXIMIZE, IntPtr.Zero);
		}

		public void RestoreApplication(IntPtr hWnd)
		{
			NativeMethods.SendNotifyMessage(hWnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_RESTORE, IntPtr.Zero);
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

		public void GetWindowSmallIconAsBitmap(IntPtr hWnd, Action<Bitmap> action)
		{
			IntPtr result;
			NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL,
				IntPtr.Zero, NativeMethods.SMTO.SMTO_BLOCK, 500, out result);

			if (result == IntPtr.Zero)
			{
				NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_QUERYDRAGICON, UIntPtr.Zero,
					IntPtr.Zero, NativeMethods.SMTO.SMTO_BLOCK, 500, out result);
			}

			if (result == IntPtr.Zero)
			{
				result = NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCL_HICONSM);
			}

			if (result == IntPtr.Zero)
			{
				System.Threading.Tasks.Task.Factory.StartNew(hWnd2 =>
					{
						Bitmap bitmap = null;
						try
						{
							int processId;
							NativeMethods.GetWindowThreadProcessId((IntPtr) hWnd2, out processId);
							var processFileName = System.Diagnostics.Process.GetProcessById(processId).MainModule.FileName;

							var info = new NativeMethods.SHFILEINFO();

							NativeMethods.SHGetFileInfo(processFileName, 0, ref info,
								Marshal.SizeOf(info), NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

							if (info.hIcon != IntPtr.Zero)
							{
								bitmap = new Bitmap(Bitmap.FromHicon(info.hIcon), smallIconSize);
								NativeMethods.DestroyIcon(info.hIcon);
							}
							else
							{
								var icon = Icon.ExtractAssociatedIcon(processFileName);
								if (icon != null)
								{
									bitmap = new Bitmap(icon.ToBitmap(), smallIconSize);
								}
							}
						}
						catch
						{
						}

						return bitmap;
					}, hWnd).ContinueWith(t => action(t.Result), System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
			}
			else
			{
				Bitmap bitmap = null;
				try
				{
					bitmap = new Bitmap(Bitmap.FromHicon(result), smallIconSize);
				}
				catch
				{
				}
				action(bitmap);
			}
		}

		public void PostAction(Action action)
		{
			postedActions.Enqueue(action);
			NativeMethods.PostMessage(HandleStatic, postActionMessageNum, UIntPtr.Zero, IntPtr.Zero);
		}

		public void DismissTemporarilyShownWindow(IntPtr hWnd)
		{
			var monitor = monitors.FirstOrDefault(m => m.temporarilyShownWindows.Remove(hWnd));
			if (monitor != null)
			{
				HideWindow(applications[hWnd].First.Value.Item2);
				if (monitor.CurrentVisibleWorkspace.IsCurrentWorkspace)
				{
					SetWorkspaceTopManagedWindowAsForeground(CurrentWorkspace);
				}
			}
		}

		#endregion

		#region Message Loop Stuff

		private void WinEventDelegate(IntPtr hWinEventHook, NativeMethods.EVENT eventType,
			IntPtr hWnd, NativeMethods.OBJID idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
		{
			if (eventType == NativeMethods.EVENT.EVENT_OBJECT_FOCUS)
			{
				// HSHELL_WINDOWACTIVATED/HSHELL_RUDEAPPACTIVATED doesn't work for some windows like Digsby Buddy List
				OnWindowActivated(NativeMethods.GetForegroundWindow());
			}
			else if (idChild == NativeMethods.CHILDID_SELF && idObject == NativeMethods.OBJID.OBJID_WINDOW && hWnd != IntPtr.Zero)
			{
				switch (eventType)
				{
					case NativeMethods.EVENT.EVENT_OBJECT_SHOW:
						if (IsAppWindow(hWnd))
						{
							if (!applications.ContainsKey(hWnd)) // if a new window has shown
							{
								AddWindowToWorkspace(hWnd);
							}
							else if (!CurrentWorkspace.ContainsWindow(hWnd) && !CurrentWorkspace.Monitor.temporarilyShownWindows.Contains(hWnd)) // if a hidden window has shown
							{
								// there is a problem with some windows showing up when others are created.
								// how to reproduce: start BitComet 1.26 on some workspace, switch to another one
								// and start explorer.exe (the file manager)
								// on Windows 7 Ultimate x64 SP1

								// another problem is that some windows continuously keep showing when hidden.
								// how to reproduce: TortoiseSVN. About box. Click check for updates. This window
								// keeps showing up when changing workspaces
								OnWindowActivated(hWnd);
							}
						}
						break;
					// differentiating between hiding and destroying a window is nice - therefore HSHELL_WINDOWDESTROYED is not enough
					case NativeMethods.EVENT.EVENT_OBJECT_DESTROY:
						RemoveApplicationFromAllWorkspaces(hWnd, true);
						break;
					case NativeMethods.EVENT.EVENT_OBJECT_HIDE:
						if (hiddenApplications.Remove(hWnd) == HashMultiSet<IntPtr>.RemoveResult.NotFound)
						{
							// a window has been closed but it was hidden by the application rather than destroyed (e.g. ICQ 7.6 main window)
							RemoveApplicationFromAllWorkspaces(hWnd, false);
						}
						break;
					// these actually work (in contrast with HSHELL_GETMINRECT)
					case NativeMethods.EVENT.EVENT_SYSTEM_MINIMIZESTART:
						CurrentWorkspace.WindowMinimized(hWnd);
						break;
					case NativeMethods.EVENT.EVENT_SYSTEM_MINIMIZEEND:
						CurrentWorkspace.WindowRestored(hWnd);
						break;
				}
			}
		}

		private void OnWindowActivated(IntPtr hWnd)
		{
			// TODO: when switching from a workspace to another, both containing a shared window,
			// and the shared window is the active window, Windows sends a HSHELL_WINDOWACTIVATED
			// for the shared window after the switch (even if it is not the top window in the
			// workspace being switched to), which causes a wrong reordering in Z order

			if (!hiddenApplications.Contains(hWnd))
			{
				if (hWnd != NativeMethods.shellWindow && !CurrentWorkspace.Monitor.temporarilyShownWindows.Contains(hWnd))
				{
					LinkedList<Tuple<Workspace, Window>> list;
					if (!ApplicationsTryGetValue(hWnd, out list)) // if a new window has shown
					{
						RefreshApplicationsHash();
					}
					else
					{
						hWnd = list.First.Value.Item2.hWnd;
						if (!CurrentWorkspace.ContainsWindow(hWnd))
						{
							Workspace workspace;
							if (monitors.Length > 1 && (workspace = list.Select(t => t.Item1).FirstOrDefault(ws => ws.IsWorkspaceVisible)) != null)
							{
								// the window is actually visible on another monitor
								// (e.g. when the user has ALT-TABbed to the window across monitors)

								SwitchToWorkspace(workspace.id, false);
							}
							else
							{
								OnHiddenWindowShown(hWnd, list.First.Value);
							}
						}
					}
				}

				CurrentWorkspace.WindowActivated(hWnd);
			}
		}

		private void OnHiddenWindowShown(IntPtr hWnd, Tuple<Workspace, Window> tuple)
		{
			switch (tuple.Item2.onHiddenWindowShownAction)
			{
				case OnWindowShownAction.SwitchToWindowsWorkspace:
					SwitchToApplication(hWnd);
					break;
				case OnWindowShownAction.MoveWindowToCurrentWorkspace:
					ChangeApplicationToWorkspace(hWnd, CurrentWorkspace.id, tuple.Item1.id);
					break;
				case OnWindowShownAction.TemporarilyShowWindowOnCurrentWorkspace:
					CurrentWorkspace.Monitor.temporarilyShownWindows.Add(hWnd);
					break;
				case OnWindowShownAction.HideWindow:
					HideWindow(tuple.Item2);
					SetWorkspaceTopManagedWindowAsForeground(CurrentWorkspace);
					break;
			}
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
							foreach (var t in list)
							{
								t.Item2.DisplayName = text;
								DoWindowTitleOrIconChanged(t.Item1, t.Item2, text);
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
			else if (m.Msg == postActionMessageNum)
			{
				postedActions.Dequeue()();
			}
			else if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam == this.getForegroundPrivilageAtom)
			{
				if (!TrySetForegroundWindow(forceForegroundWindow) && forceForegroundWindow != NativeMethods.shellWindow)
				{
					SendHotkey(altTabHotkey);
					// TODO: sometimes class "#32771" (WinSwitch) is still visible after this - fix!
				}
				forceForegroundWindow = IntPtr.Zero;
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
