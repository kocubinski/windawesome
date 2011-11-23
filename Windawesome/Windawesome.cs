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
		private readonly WindowBase[] topmostWindows;
		private readonly HashMultiSet<IntPtr> hiddenApplications;
		private readonly IntPtr getForegroundPrivilageAtom;
		private readonly uint windawesomeThreadId = NativeMethods.GetCurrentThreadId();

		private readonly Tuple<NativeMethods.MOD, Keys> altEscHotkey = new Tuple<NativeMethods.MOD, Keys>(NativeMethods.MOD.MOD_ALT, Keys.Escape);
		private readonly Dictionary<int, HandleMessageDelegate> messageHandlers;

		private readonly NativeMethods.WinEventDelegate winEventDelegate;
		private readonly IntPtr windowShownOrDestroyedWinEventHook;
		private readonly IntPtr windowMinimizedOrRestoredWinEventHook;
		private readonly IntPtr windowFocusedWinEventHook;

		private IntPtr forceForegroundWindow;

		#region System Changes

		private static readonly NativeMethods.NONCLIENTMETRICS originalNonClientMetrics;
		private static readonly NativeMethods.ANIMATIONINFO originalAnimationInfo;
		private static readonly bool originalHideMouseWhenTyping;
		private static readonly bool originalFocusFollowsMouse;
		private static readonly bool originalFocusFollowsMouseSetOnTop;

		#endregion

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

		static Windawesome()
		{
			isAtLeastVista = Environment.OSVersion.Version.Major >= 6;
			isAtLeast7 = isAtLeastVista && Environment.OSVersion.Version.Minor >= 1;

			isRunningElevated = NativeMethods.IsCurrentProcessElevatedInRespectToShell();

			#region System Changes

			originalNonClientMetrics = NativeMethods.NONCLIENTMETRICS.Default;
			NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETNONCLIENTMETRICS, originalNonClientMetrics.cbSize,
				ref originalNonClientMetrics, 0);

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
			NativeMethods.EnumWindows((hWnd, _) => (IsAppWindow(hWnd) && AddWindowToWorkspace(hWnd, finishedInitializing: false)) || true, IntPtr.Zero);

			// add a handler for when the working area or screen rosolution changes as well as
			// a handler for the system shutting down/restarting
			SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
			SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
			SystemEvents.SessionEnded += OnSessionEnded;

			#region System Changes

			System.Threading.Tasks.Task.Factory.StartNew(() =>
				{
					// set the "hide mouse when typing"
					if (config.HideMouseWhenTyping != originalHideMouseWhenTyping)
					{
						var hideMouseWhenTyping = config.HideMouseWhenTyping;
						NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETMOUSEVANISH, 0,
							ref hideMouseWhenTyping, 0);

						NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
							(UIntPtr) (uint) NativeMethods.SPI.SPI_SETMOUSEVANISH, IntPtr.Zero);
					}

					// set the "focus follows mouse"
					if (config.FocusFollowsMouse != originalFocusFollowsMouse)
					{
						var focusFollowsMouse = config.FocusFollowsMouse;
						NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWINDOWTRACKING, 0,
							ref focusFollowsMouse, 0);

						NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
							(UIntPtr) (uint) NativeMethods.SPI.SPI_SETACTIVEWINDOWTRACKING, IntPtr.Zero);
					}

					// set the "set window on top on focus follows mouse"
					if (config.FocusFollowsMouseSetOnTop != originalFocusFollowsMouseSetOnTop)
					{
						var focusFollowsMouseSetOnTop = config.FocusFollowsMouseSetOnTop;
						NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWNDTRKZORDER, 0,
							ref focusFollowsMouseSetOnTop, 0);

						NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
							(UIntPtr) (uint) NativeMethods.SPI.SPI_SETACTIVEWNDTRKZORDER, IntPtr.Zero);
					}

					// set the minimize/maximize/restore animations
					if ((originalAnimationInfo.iMinAnimate == 1 && !config.ShowMinimizeMaximizeRestoreAnimations) ||
						(originalAnimationInfo.iMinAnimate == 0 && config.ShowMinimizeMaximizeRestoreAnimations))
					{
						var animationInfo = originalAnimationInfo;
						animationInfo.iMinAnimate = config.ShowMinimizeMaximizeRestoreAnimations ? 1 : 0;
						NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETANIMATION, animationInfo.cbSize,
							ref animationInfo, 0);

						NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
							(UIntPtr) (uint) NativeMethods.SPI.SPI_SETANIMATION, IntPtr.Zero);
					}

					// set the global border and padded border widths
					if ((config.WindowBorderWidth >= 0 && originalNonClientMetrics.iBorderWidth != config.WindowBorderWidth) ||
						(isAtLeastVista && config.WindowPaddedBorderWidth >= 0 && originalNonClientMetrics.iPaddedBorderWidth != config.WindowPaddedBorderWidth))
					{
						var metrics = originalNonClientMetrics;
						metrics.iBorderWidth = config.WindowBorderWidth;
						metrics.iPaddedBorderWidth = config.WindowPaddedBorderWidth;
						NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
							ref metrics, 0);

						NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
							(UIntPtr) (uint) NativeMethods.SPI.SPI_SETNONCLIENTMETRICS, IntPtr.Zero);
					}
				});

			#endregion

			// register hotkey for forcing a foreground window
			getForegroundPrivilageAtom = (IntPtr) NativeMethods.GlobalAddAtom("WindawesomeShortcutGetForegroundPrivilage");
			if (!NativeMethods.RegisterHotKey(this.Handle, (ushort) getForegroundPrivilageAtom, config.UniqueHotkey.Item1, config.UniqueHotkey.Item2))
			{
				OutputWarning("There was a problem registering the unique hotkey! Probably this key-combination is in " +
					"use by some other program! Please use a unique one, otherwise Windawesome will sometimes have a problem " +
					"switching to windows as you change workspaces!");
			}

			// initialize all workspaces and hide windows not on StartingWorkspaces
			var windowsToHide = new HashSet<Window>();
			foreach (var workspace in config.Workspaces)
			{
				workspace.windows.ForEach(w => windowsToHide.Add(w));
				workspace.Initialize();
				topmostWindows[workspace.id - 1] = workspace.GetWindows().FirstOrDefault(w => !NativeMethods.IsIconic(w.hWnd)) ?? new WindowBase(NativeMethods.shellWindow);
			}
			windowsToHide.ExceptWith(config.StartingWorkspaces.SelectMany(ws => ws.windows));
			var winPosInfo = NativeMethods.BeginDeferWindowPos(windowsToHide.Count);
			winPosInfo = windowsToHide.Where(WindowIsNotHung).Aggregate(winPosInfo, (current, w) =>
				NativeMethods.DeferWindowPos(current, w.hWnd, IntPtr.Zero, 0, 0, 0, 0,
					NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE |
					NativeMethods.SWP.SWP_NOSIZE | NativeMethods.SWP.SWP_NOZORDER |
					NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_HIDEWINDOW));
			NativeMethods.EndDeferWindowPos(winPosInfo);

			// remove windows from ALT-TAB menu and Taskbar
			config.StartingWorkspaces.Where(ws => ws != CurrentWorkspace).SelectMany(ws => ws.windows).
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
			windowShownOrDestroyedWinEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT.EVENT_OBJECT_DESTROY, NativeMethods.EVENT.EVENT_OBJECT_HIDE,
				IntPtr.Zero, winEventDelegate, 0, 0,
				NativeMethods.WINEVENT.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT.WINEVENT_SKIPOWNTHREAD);
			windowMinimizedOrRestoredWinEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT.EVENT_SYSTEM_MINIMIZESTART, NativeMethods.EVENT.EVENT_SYSTEM_MINIMIZEEND,
				IntPtr.Zero, winEventDelegate, 0, 0,
				NativeMethods.WINEVENT.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT.WINEVENT_SKIPOWNTHREAD);
			windowFocusedWinEventHook = NativeMethods.SetWinEventHook(NativeMethods.EVENT.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT.EVENT_SYSTEM_FOREGROUND,
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

			var thread = new System.Threading.Thread(() => // this has to be a foreground thread
				{
					// revert the hiding of the mouse when typing
					if (config.HideMouseWhenTyping != originalHideMouseWhenTyping)
					{
						var hideMouseWhenTyping = originalHideMouseWhenTyping;
						NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETMOUSEVANISH, 0,
							ref hideMouseWhenTyping, NativeMethods.SPIF.SPIF_UPDATEINIFILE);

						NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
							(UIntPtr) (uint) NativeMethods.SPI.SPI_SETMOUSEVANISH, IntPtr.Zero);
					}

					// revert the "focus follows mouse"
					if (config.FocusFollowsMouse != originalFocusFollowsMouse)
					{
						var focusFollowsMouse = originalFocusFollowsMouse;
						NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWINDOWTRACKING, 0,
							ref focusFollowsMouse, NativeMethods.SPIF.SPIF_UPDATEINIFILE);

						NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
							(UIntPtr) (uint) NativeMethods.SPI.SPI_SETACTIVEWINDOWTRACKING, IntPtr.Zero);
					}

					// revert the "set window on top on focus follows mouse"
					if (config.FocusFollowsMouseSetOnTop != originalFocusFollowsMouseSetOnTop)
					{
						var focusFollowsMouseSetOnTop = originalFocusFollowsMouseSetOnTop;
						NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETACTIVEWNDTRKZORDER, 0,
							ref focusFollowsMouseSetOnTop, NativeMethods.SPIF.SPIF_UPDATEINIFILE);

						NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
							(UIntPtr) (uint) NativeMethods.SPI.SPI_SETACTIVEWNDTRKZORDER, IntPtr.Zero);
					}

					// revert the minimize/maximize/restore animations
					if ((originalAnimationInfo.iMinAnimate == 1 && !config.ShowMinimizeMaximizeRestoreAnimations) ||
						(originalAnimationInfo.iMinAnimate == 0 && config.ShowMinimizeMaximizeRestoreAnimations))
					{
						var animationInfo = originalAnimationInfo;
						NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETANIMATION, animationInfo.cbSize,
							ref animationInfo, NativeMethods.SPIF.SPIF_UPDATEINIFILE);

						NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
							(UIntPtr) (uint) NativeMethods.SPI.SPI_SETANIMATION, IntPtr.Zero);
					}

					// revert the size of non-client area of windows
					if ((config.WindowBorderWidth >= 0 && originalNonClientMetrics.iBorderWidth != config.WindowBorderWidth) ||
						(isAtLeastVista && config.WindowPaddedBorderWidth >= 0 && originalNonClientMetrics.iPaddedBorderWidth != config.WindowPaddedBorderWidth))
					{
						var metrics = originalNonClientMetrics;
						NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
							ref metrics, NativeMethods.SPIF.SPIF_UPDATEINIFILE);

						NativeMethods.SendNotifyMessage(NativeMethods.HWND_BROADCAST, NativeMethods.WM_SETTINGCHANGE,
							(UIntPtr) (uint) NativeMethods.SPI.SPI_SETNONCLIENTMETRICS, IntPtr.Zero);
					}
				});
			thread.Start();
			new System.Threading.Timer(_ =>
				{
					// SystemParametersInfo sometimes hangs because of SPI_SETNONCLIENTMETRICS,
					// even though SPIF_SENDCHANGE is not added to the flags
					if (thread.IsAlive)
					{
						thread.Abort();
						Environment.Exit(0);
					}
				}, null, 5000, 0);

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
			if (ApplicationsTryGetValue(hWnd, out workspacesWindowsList) &&
				workspacesWindowsList.First.Value.Item2.hWnd != hWnd)
			{
				if (workspacesWindowsList.First.Value.Item2.IsMatchOwnedWindow(hWnd))
				{
					var ownedWindows = workspacesWindowsList.First.Value.Item2.OwnedWindows;
					if (ownedWindows.FindLast(hWnd) == null)
					{
						ownedWindows.AddLast(hWnd);
					}
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
					return IsAppWindow(hWnd) && AddWindowToWorkspace(hWnd, false);
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
								DoForTopmostWindowForWorkspace(CurrentWorkspace, ActivateWindow);
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
					list.AddLast(new Tuple<Workspace, Window>(workspace, window));

					workspace.WindowCreated(window);

					if (!workspace.IsCurrentWorkspace && !isMinimized)
					{
						if (hasWorkspaceZeroRule || hasCurrentWorkspaceRule ||
							list.First.Value.Item1 != workspace ||
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
					list.First.Value.Item2.ShowWindowMenu();
				}

				if (finishedInitializing)
				{
					if (!hasWorkspaceZeroRule && !hasCurrentWorkspaceRule)
					{
						switch (programRule.onWindowCreatedAction)
						{
							case OnWindowCreatedOrShownAction.SwitchToWindowsWorkspace:
								SwitchToWorkspace(list.First.Value.Item1.id, false);
								break;
							case OnWindowCreatedOrShownAction.MoveWindowToCurrentWorkspace:
								ChangeApplicationToWorkspace(hWnd, CurrentWorkspace.id, matchingRules.First().workspace);
								break;
						}
					}
					OnWindowCreatedOnCurrentWorkspace(hWnd, programRule);
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
			applications.Keys.Unless(NativeMethods.IsWindow).ToArray().ForEach(h => RemoveApplicationFromAllWorkspaces(h, true));

			// add any application that was not added for some reason when it was created
			NativeMethods.EnumWindows((hWnd, _) =>
				(IsAppWindow(hWnd) && !applications.ContainsKey(hWnd) && AddWindowToWorkspace(hWnd)) || true, IntPtr.Zero);
		}

		private void ForceForegroundWindow(WindowBase window)
		{
			var hWnd = window.GetLastActiveWindow();

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
						if (NativeMethods.AttachThreadInput(this.windawesomeThreadId, foregroundWindowThreadId, true))
						{
							var targetWindowThreadId = NativeMethods.GetWindowThreadProcessId(hWnd, IntPtr.Zero);
							var successfullyAttached = NativeMethods.AttachThreadInput(foregroundWindowThreadId, targetWindowThreadId, true);

							uint foregroundLockTimeout = 0;
							NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_GETFOREGROUNDLOCKTIMEOUT,
								0, ref foregroundLockTimeout, 0);
							if (foregroundLockTimeout != 0)
							{
								uint zeroForegroundLockTimeout = 0;
								NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETFOREGROUNDLOCKTIMEOUT,
									0, ref zeroForegroundLockTimeout, 0);
							}
							successfullyChanged = TrySetForegroundWindow(hWnd);
							if (foregroundLockTimeout != 0)
							{
								NativeMethods.SystemParametersInfo(NativeMethods.SPI.SPI_SETFOREGROUNDLOCKTIMEOUT,
									0, ref foregroundLockTimeout, 0);
							}

							if (successfullyAttached)
							{
								NativeMethods.AttachThreadInput(foregroundWindowThreadId, targetWindowThreadId, false);
							}
							NativeMethods.AttachThreadInput(this.windawesomeThreadId, foregroundWindowThreadId, false);
						}
					}

					if (!successfullyChanged)
					{
						this.forceForegroundWindow = hWnd;
						this.SendHotkey(this.config.UniqueHotkey);
					}
				}
				else
				{
					NativeMethods.BringWindowToTop(hWnd);
				}
			}
		}

		private static bool TrySetForegroundWindow(IntPtr hWnd)
		{
			const int setForegroundTryCount = 5;
			var count = 0;
			while (!NativeMethods.SetForegroundWindow(hWnd) && ++count < setForegroundTryCount)
			{
				System.Threading.Thread.Sleep(10);
			}

			if (count == setForegroundTryCount)
			{
				System.Threading.Thread.Sleep(50);
				if (NativeMethods.GetForegroundWindow() != hWnd)
				{
					return false;
				}
			}
			else
			{
				const int getForegroundTryCount = 20;
				count = 0;
				while (NativeMethods.GetForegroundWindow() != hWnd && ++count < getForegroundTryCount)
				{
					System.Threading.Thread.Sleep(30);
				}

				if (count == getForegroundTryCount)
				{
					return false;
				}
			}

			NativeMethods.BringWindowToTop(hWnd);

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

		private static bool IsVisibleAndNotHung(Window window)
		{
			return NativeMethods.IsWindowVisible(window.hWnd) && WindowIsNotHung(window.hWnd);
		}

		private void HideWindow(Window window)
		{
			if (IsVisibleAndNotHung(window))
			{
				hiddenApplications.Add(window.hWnd);
				window.OwnedWindows.ForEach(hWnd => NativeMethods.ShowWindow(hWnd, NativeMethods.SW.SW_HIDE));
			}
		}

		private void ShowHideWindows(Workspace oldWorkspace, Workspace newWorkspace, bool setForeground)
		{
			var winPosInfo = NativeMethods.BeginDeferWindowPos(newWorkspace.GetWindowsCount() + oldWorkspace.GetWindowsCount());

			var showWindows = newWorkspace.windows;
			foreach (var window in showWindows.Where(WindowIsNotHung))
			{
				winPosInfo = window.OwnedWindows.Aggregate(winPosInfo, (current, hWnd) =>
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
				oldWorkspace.windows.Except(showWindows) : oldWorkspace.windows;
			// if the window is not visible we shouldn't add it to hiddenApplications as EVENT_OBJECT_HIDE won't be sent
			foreach (var window in hideWindows.Where(IsVisibleAndNotHung))
			{
				hiddenApplications.Add(window.hWnd);
				winPosInfo = window.OwnedWindows.Aggregate(winPosInfo, (current, hWnd) =>
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
			if (NativeMethods.IsIconic(window.hWnd) && WindowIsNotHung(window.hWnd))
			{
				// OpenIcon does not restore the window to its previous size (e.g. maximized)
				NativeMethods.ShowWindow(window.hWnd, NativeMethods.SW.SW_RESTORE);
				System.Threading.Thread.Sleep(NativeMethods.minimizeRestoreDelay);
			}
			ForceForegroundWindow(window);
			CurrentWorkspace.WindowActivated(window.hWnd);
		}

		private IntPtr DoForTopmostWindowForWorkspace(Workspace workspace, Action<WindowBase> action)
		{
			var window = topmostWindows[workspace.id - 1];
			if (window == null || !NativeMethods.IsWindowVisible(window.hWnd) || NativeMethods.IsIconic(window.hWnd))
			{
				NativeMethods.EnumWindows((hWnd, _) =>
					{
						if (!IsAppWindow(hWnd) || NativeMethods.IsIconic(hWnd))
						{
							return true;
						}
						if ((topmostWindows[workspace.id - 1] = workspace.GetWindow(hWnd)) != null)
						{
							// the workspace contains this window so make it the topmost one
							return false;
						}
						if (IsAltTabWindow(hWnd) && !applications.ContainsKey(hWnd))
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
			}

			window = topmostWindows[workspace.id - 1];
			action(window);

			return window.hWnd;
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

		// http://blogs.msdn.com/b/oldnewthing/archive/2007/10/08/5351207.aspx
		// http://stackoverflow.com/questions/210504/enumerate-windows-like-alt-tab-does
		public static bool IsAltTabWindow(IntPtr hWnd)
		{
			var exStyle = NativeMethods.GetWindowExStyleLongPtr(hWnd);
			if (exStyle.HasFlag(NativeMethods.WS_EX.WS_EX_TOOLWINDOW) ||
				NativeMethods.GetWindow(hWnd, NativeMethods.GW.GW_OWNER) != IntPtr.Zero)
			{
				return false;
			}
			if (exStyle.HasFlag(NativeMethods.WS_EX.WS_EX_APPWINDOW))
			{
				return true;
			}

			// Start at the root owner
			var hWndTry = NativeMethods.GetAncestor(hWnd, NativeMethods.GA.GA_ROOTOWNER);
			IntPtr oldHWnd;

			// See if we are the last active visible popup
			do
			{
				oldHWnd = hWndTry;
				hWndTry = NativeMethods.GetLastActivePopup(hWndTry);
			}
			while (oldHWnd != hWndTry && !NativeMethods.IsWindowVisible(hWndTry));

			return hWndTry == hWnd;
		}

		public static IntPtr DoForSelfAndOwnersWhile(IntPtr hWnd, Predicate<IntPtr> action)
		{
			while (hWnd != IntPtr.Zero && action(hWnd))
			{
				hWnd = NativeMethods.GetWindow(hWnd, NativeMethods.GW.GW_OWNER);
			}
			return hWnd;
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
			// As this will not block forever at any point and it will only block the main thread for "with_big_timeout"
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
			monitors.SelectMany(m => m.CurrentVisibleWorkspace.windows).ForEach(w => w.Redraw());

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

					list.AddFirst(new Tuple<Workspace, Window>(newWorkspace, newWindow));
					list.Where(t => ++t.Item2.WorkspacesCount == 2).ForEach(t => t.Item1.sharedWindowsCount++);

					FollowWindow(oldWorkspace, newWorkspace, follow, newWindow);
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
			Window window;
			LinkedList<Tuple<Workspace, Window>> list;
			if (TryGetManagedWindowForWorkspace(hWnd, workspace, out window, out list))
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

					list.Remove(new Tuple<Workspace, Window>(workspace, window));
					list.Where(t => --t.Item2.WorkspacesCount == 1).ForEach(t => t.Item1.RemoveFromSharedWindows(t.Item2));

					if (workspace.IsCurrentWorkspace && setForeground)
					{
						DoForTopmostWindowForWorkspace(workspace, ActivateWindow);
					}
				}
			}
		}

		public void RemoveApplicationFromAllWorkspaces(IntPtr hWnd, bool windowDestroyed) // sort of UnmanageWindow
		{
			LinkedList<Tuple<Workspace, Window>> list;
			if (applications.TryGetValue(hWnd, out list))
			{
				list.ForEach(t => t.Item1.WindowDestroyed(t.Item2));
				var window = list.First.Value.Item2;
				if (!window.ShowMenu && window.menu != IntPtr.Zero)
				{
					if (windowDestroyed)
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
							MoveMouseToMiddleOf(newWorkspace.Monitor.Bounds);
						}

						// remove windows from ALT-TAB menu and Taskbar
						if (CurrentWorkspace.hideFromAltTabWhenOnInactiveWorkspaceCount > 0)
						{
							CurrentWorkspace.windows.Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(false));
						}
					}

					// add windows to ALT-TAB menu and Taskbar
					if (newWorkspace.hideFromAltTabWhenOnInactiveWorkspaceCount > 0)
					{
						newWorkspace.windows.Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(true));
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
						CurrentWorkspace.windows.Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(false));
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
						workspace.windows.Where(w => w.hideFromAltTabAndTaskbarWhenOnInactiveWorkspace).ForEach(w => w.ShowInAltTabAndTaskbar(true));
					}

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

		public void DismissTemporarilyShownWindow(IntPtr hWnd)
		{
			LinkedList<Tuple<Workspace, Window>> list;
			if (ApplicationsTryGetValue(hWnd, out list))
			{
				hWnd = list.First.Value.Item2.hWnd;
				var monitor = monitors.FirstOrDefault(m => m.temporarilyShownWindows.Remove(hWnd));
				if (monitor != null)
				{
					HideWindow(list.First.Value.Item2);
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
						if (IsAppWindow(hWnd))
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
					// differentiating between hiding and destroying a window is nice - therefore HSHELL_WINDOWDESTROYED is not enough
					case NativeMethods.EVENT.EVENT_OBJECT_DESTROY:
						RemoveApplicationFromAllWorkspaces(hWnd, true);
						hiddenApplications.RemoveAll(hWnd);
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
					// HSHELL_WINDOWACTIVATED/HSHELL_RUDEAPPACTIVATED doesn't work for some windows like Digsby Buddy List
					// EVENT_OBJECT_FOCUS doesn't work with Firefox on the other hand
					case NativeMethods.EVENT.EVENT_SYSTEM_FOREGROUND:
						if (!hiddenApplications.Contains(hWnd))
						{
							if (hWnd != NativeMethods.shellWindow &&
								!CurrentWorkspace.Monitor.temporarilyShownWindows.Contains(hWnd))
							{
								if (!ApplicationsTryGetValue(hWnd, out list)) // if a new window has shown
								{
									if (AddWindowToWorkspace(hWnd))
									{
										return ;
									}
								}
								else
								{
									hWnd = WindowShownOrActivated(list);
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
								GetWindowSmallIconAsBitmap(list.First.Value.Item2.hWnd, bitmap =>
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
			else if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam == this.getForegroundPrivilageAtom)
			{
				if (!TrySetForegroundWindow(forceForegroundWindow) && forceForegroundWindow != NativeMethods.shellWindow)
				{
					SendHotkey(altEscHotkey);
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
