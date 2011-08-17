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
		public Workspace CurrentWorkspace {	get { return workspaces[0]; } }

		public readonly Monitor[] monitors;

		public int PreviousWorkspace { get; private set; }

		public delegate bool HandleMessageDelegate(ref Message m);

		public static IntPtr HandleStatic { get; private set; }
		public static readonly bool isRunningElevated;
		public static readonly bool isAtLeastVista;
		public static readonly bool isAtLeast7;
		public static readonly Size smallIconSize;

		private readonly Config config;
		private readonly Workspace[] workspaces;
		private readonly Dictionary<IntPtr, LinkedList<Tuple<Workspace, Window>>> applications; // hWnd to a list of workspaces and windows
		private readonly HashMultiSet<IntPtr> hiddenApplications;
		private readonly uint shellMessageNum;
#if !DEBUG
		private readonly bool changedNonClientMetrics;
#endif
		private readonly IntPtr getForegroundPrivilageAtom;
		private const uint postActionMessageNum = NativeMethods.WM_USER;

		private readonly Tuple<NativeMethods.MOD, Keys> uniqueHotkey;
		private readonly Queue<Action> postedActions;
		private readonly Dictionary<int, HandleMessageDelegate> messageHandlers;
		private readonly uint windawesomeThreadId;

		private IntPtr forceForegroundWindow;

#if !DEBUG
		private static readonly NativeMethods.NONCLIENTMETRICS originalNonClientMetrics;
#endif

		#region Events

		public delegate void LayoutUpdatedEventHandler();
		public static event LayoutUpdatedEventHandler LayoutUpdated; // TODO: this should be for a specific workspace. But how to call from Widgets then?

		public delegate void WindowTitleOrIconChangedEventHandler(Workspace workspace, Window window, string newText);
		public static event WindowTitleOrIconChangedEventHandler WindowTitleOrIconChanged;

		public delegate void WindowFlashingEventHandler(LinkedList<Tuple<Workspace, Window>> list);
		public static event WindowFlashingEventHandler WindowFlashing;

		public delegate void ProgramRuleMatchedEventHandler(ProgramRule programRule, IntPtr hWnd, string cName, string dName, string pName, NativeMethods.WS style, NativeMethods.WS_EX exStyle);
		public static event ProgramRuleMatchedEventHandler ProgramRuleMatched;

		internal delegate void WindawesomeExitingEventHandler();
		internal static event WindawesomeExitingEventHandler WindawesomeExiting;

		public static void DoLayoutUpdated()
		{
			if (LayoutUpdated != null)
			{
				LayoutUpdated();
			}
		}

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

			isRunningElevated = isAtLeastVista && NativeMethods.IsUserAnAdmin();

#if !DEBUG
			originalNonClientMetrics = NativeMethods.NONCLIENTMETRICS.GetNONCLIENTMETRICS();
			NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETNONCLIENTMETRICS, originalNonClientMetrics.cbSize,
				ref originalNonClientMetrics, 0);
#endif

			smallIconSize = SystemInformation.SmallIconSize;
		}

		internal Windawesome()
		{
			applications = new Dictionary<IntPtr, LinkedList<Tuple<Workspace, Window>>>(20);
			hiddenApplications = new HashMultiSet<IntPtr>();
			windawesomeThreadId = NativeMethods.GetCurrentThreadId();
			messageHandlers = new Dictionary<int, HandleMessageDelegate>(2);
			postedActions = new Queue<Action>(5);

			monitors = Screen.AllScreens.Select((_, i) => new Monitor(i)).ToArray();

			this.CreateHandle(new CreateParams { Parent = NativeMethods.HWND_MESSAGE });
			HandleStatic = this.Handle;

			config = new Config();
			config.LoadConfiguration(this);

			//if (config.StartingWorkspaces.Length == 0 ||
			//    new HashSet<Monitor>(config.StartingWorkspaces.Select(w => w.Monitor)).Count != config.StartingWorkspaces.Length)
			//{
			//    throw new Exception("StartingWorkspaces either contains no elements or contains two or more workspaces " +
			//        "which are on the same monitor!");
			//}

			// TODO: must check if each monitor has at least one workspace (or maybe not?) and fix if not (what happens if more monitors than workspaces?)

			// TODO: ALT+TAB works accross monitors, which is annoying

			workspaces = config.Workspaces.Resize(config.Workspaces.Length + 1);
			workspaces[0] = config.StartingWorkspaces.First(w => w.Monitor.screen.Primary);
			PreviousWorkspace = CurrentWorkspace.id;

			monitors.ForEach(m => m.AddManyWorkspaces(config.Workspaces.Where(w => w.Monitor == m))); // n ^ 2 but hopefully fast enough

			// set monitor starting workspaces as this is needed in some Widgets
			config.StartingWorkspaces.ForEach(w => w.Monitor.SetStartingWorkspace(w));

			// initialize bars and plugins
			config.Bars.ForEach(b => b.InitializeBar(this, config));
			config.Plugins.ForEach(p => p.InitializePlugin(this, config));

			// add all windows to their respective workspaces
			NativeMethods.EnumDesktopWindows(IntPtr.Zero, (hWnd, _) => AddWindowToWorkspace(hWnd, finishedInitializing: false) || true, IntPtr.Zero);

			// add a handler for when the screen resolution changes as well as
			// a handler for the system shutting down/restarting
			SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
			SystemEvents.SessionEnded += OnSessionEnded;

#if !DEBUG
			// set the global border and padded border widths
			var metrics = originalNonClientMetrics;
			if (config.WindowBorderWidth >= 0 && metrics.iBorderWidth != config.WindowBorderWidth)
			{
				metrics.iBorderWidth = config.WindowBorderWidth;
				changedNonClientMetrics = true;
			}
			if (isAtLeastVista && config.WindowPaddedBorderWidth >= 0 && metrics.iPaddedBorderWidth != config.WindowPaddedBorderWidth)
			{
				metrics.iPaddedBorderWidth = config.WindowPaddedBorderWidth;
				changedNonClientMetrics = true;
			}
			if (changedNonClientMetrics)
			{
				System.Threading.Tasks.Task.Factory.StartNew(() =>
					NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
						ref metrics, NativeMethods.SPIF_SENDCHANGE));
			}
#endif

			// register hotkey for forcing a foreground window
			uniqueHotkey = config.UniqueHotkey;
			getForegroundPrivilageAtom = (IntPtr) NativeMethods.GlobalAddAtom("WindawesomeShortcutGetForegroundPrivilage");
			if (!NativeMethods.RegisterHotKey(this.Handle, (ushort) getForegroundPrivilageAtom, config.UniqueHotkey.Item1, config.UniqueHotkey.Item2))
			{
				OutputWarning("There was a problem registering the unique hotkey! Probably this key-combination is in " +
					"use by some other program! Please use a unique one, otherwise Windawesome will sometimes have a problem " +
					" switching to windows as you change workspaces!");
			}

			// register a shell hook
			NativeMethods.RegisterShellHookWindow(this.Handle);
			shellMessageNum = NativeMethods.RegisterWindowMessage("SHELLHOOK");

			// initialize all workspaces and hide windows not on StartingWorkspaces
			var windowsToHide = new HashSet<Window>();
			foreach (var workspace in config.Workspaces)
			{
				workspace.GetOwnerWindows().ForEach(w => windowsToHide.Add(w));
				workspace.Initialize();
			}

			windowsToHide.ExceptWith(config.StartingWorkspaces.SelectMany(ws => ws.GetOwnerWindows()));
			windowsToHide.ForEach(HideWindow);

			// initialize monitors and switches to the default starting workspaces
			monitors.ForEach(m => m.Initialize());
			Monitor.ShowHideWindowsTaskbar(CurrentWorkspace.ShowWindowsTaskbar);
			SetWorkspaceTopManagedWindowAsForeground(CurrentWorkspace);
			CurrentWorkspace.IsCurrentWorkspace = true;
		}

		public void Quit()
		{
			SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
			SystemEvents.SessionEnded -= OnSessionEnded;

			// unregister shell hook
			NativeMethods.DeregisterShellHookWindow(this.Handle);

			NativeMethods.UnregisterHotKey(this.Handle, (ushort) getForegroundPrivilageAtom);
			NativeMethods.GlobalDeleteAtom((ushort) getForegroundPrivilageAtom);

			// roll back any changes to Windows
			monitors.ForEach(m => m.Dispose());
			Monitor.StaticDispose();

			var winPosInfo = NativeMethods.BeginDeferWindowPos(applications.Values.Count);
			foreach (var window in applications.Values.Select(l => l.First.Value.Item2))
			{
				window.DoForSelfOrOwned(w => w.RevertToInitialValues());
				winPosInfo = NativeMethods.DeferWindowPos(winPosInfo, window.hWnd, IntPtr.Zero, 0, 0, 0, 0,
					NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE |
					NativeMethods.SWP.SWP_NOZORDER | NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_SHOWWINDOW);
				window.ShowPopupsAndRedraw();
			}
			NativeMethods.EndDeferWindowPos(winPosInfo);

			// dispose of plugins and bars
			config.Plugins.ForEach(p => p.Dispose());
			config.Bars.ForEach(b => b.Dispose());

#if !DEBUG
			// revert the size of non-client area of windows
			if (changedNonClientMetrics)
			{
				var metrics = originalNonClientMetrics;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
					ref metrics, NativeMethods.SPIF_SENDCHANGE);
			}
#endif

			WindawesomeExiting();
			this.DestroyHandle();
		}

		#endregion

		#region Helpers

		private void OnDisplaySettingsChanged(object sender, EventArgs e)
		{
			config.Workspaces.ForEach(ws => ws.hasChanges = true);
			monitors.ForEach(m => m.CurrentVisibleWorkspace.Reposition());
		}

		private void OnSessionEnded(object sender, SessionEndedEventArgs e)
		{
			Quit();
		}

		private bool IsAppWindow(IntPtr hWnd)
		{
			return NativeMethods.IsWindowVisible(hWnd) && NativeMethods.GetParent(hWnd) == IntPtr.Zero &&
				!NativeMethods.GetWindowStyleLongPtr(hWnd).HasFlag(NativeMethods.WS.WS_CHILD);
		}

		private bool AddWindowToWorkspace(IntPtr hWnd, bool firstTry = true, bool finishedInitializing = true)
		{
			if (IsAppWindow(hWnd))
			{
				var className = NativeMethods.GetWindowClassName(hWnd);
				var displayName = NativeMethods.GetText(hWnd);
				var style = NativeMethods.GetWindowStyleLongPtr(hWnd);
				var exStyle = NativeMethods.GetWindowExStyleLongPtr(hWnd);
				int processId;
				NativeMethods.GetWindowThreadProcessId(hWnd, out processId);
				var processName = System.Diagnostics.Process.GetProcessById(processId).ProcessName;

				var programRule = config.ProgramRules.FirstOrDefault(r => r.IsMatch(hWnd, className, displayName, processName, style, exStyle));
				DoProgramRuleMatched(programRule, hWnd, className, displayName, processName, style, exStyle);
				if (programRule == null || !programRule.isManaged)
				{
					// add to hiddenApplications in order to not try again to add the window
					hiddenApplications.AddUnique(hWnd);
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
						switch (programRule.onWindowCreatedAction)
						{
							case OnWindowShownAction.SwitchToWindowsWorkspace:
								PostAction(() => SwitchToApplication(hWnd));
								break;
							case OnWindowShownAction.MoveWindowToCurrentWorkspace:
								var workspaceId = CurrentWorkspace.id;
								var matchingRuleWorkspace = matchingRules.First().workspace;
								PostAction(() => ChangeApplicationToWorkspace(hWnd, workspaceId, matchingRuleWorkspace));
								break;
							case OnWindowShownAction.TemporarilyShowWindowOnCurrentWorkspace:
								CurrentWorkspace.Monitor.temporarilyShownWindows.Add(hWnd);
								OnWindowCreatedOnCurrentWorkspace(hWnd, programRule);
								break;
							case OnWindowShownAction.HideWindow:
								System.Threading.Thread.Sleep(500); // TODO: is this enough? Is it too much?
								SetWorkspaceTopManagedWindowAsForeground(CurrentWorkspace);
								if (matchingRules.All(r => !workspaces[r.workspace].IsWorkspaceVisible))
								{
									hiddenApplications.Add(hWnd);
									NativeMethods.ShowWindow(hWnd, NativeMethods.SW.SW_HIDE);
								}
								break;
						}
					}

					if (programRule.windowCreatedDelay > 0)
					{
						System.Threading.Thread.Sleep(programRule.windowCreatedDelay);
					}
				}

				var is64BitProcess = NativeMethods.Is64BitProcess(hWnd);
				LinkedList<Window>[] ownedLists = null;

				int i;
				if (programRule.handleOwnedWindows)
				{
					ownedLists = matchingRules.Select(_ => new LinkedList<Window>()).ToArray();

					NativeMethods.EnumDesktopWindows(IntPtr.Zero, (h, hOwner) =>
						{
							if (IsAppWindow(h))
							{
								var owner = h;
								do
								{
									owner = NativeMethods.GetWindow(owner, NativeMethods.GW.GW_OWNER);
								}
								while (owner != IntPtr.Zero && owner != hOwner);

								if (owner == hOwner)
								{
									var classNameOwned = NativeMethods.GetWindowClassName(h);
									var displayNameOwned = NativeMethods.GetText(h);
									var styleOwned = NativeMethods.GetWindowStyleLongPtr(h);
									var exStyleOwned = NativeMethods.GetWindowExStyleLongPtr(h);
									var ownedMenu = NativeMethods.GetMenu(h);
									i = 0;
									foreach (var rule in matchingRules)
									{
										ownedLists[i++].AddLast(new Window(h, classNameOwned, displayNameOwned, processName,
											workspacesCount, is64BitProcess, styleOwned, exStyleOwned, null, rule, programRule, ownedMenu));
									}
								}
							}
							return true;
						}, hWnd);

					if (ownedLists[0].Count == 0)
					{
						if (firstTry && finishedInitializing)
						{
							System.Threading.Thread.Sleep(500);
							return AddWindowToWorkspace(hWnd, false);
						}
						else
						{
							// add to hiddenApplications in order to not try again to add the window
							hiddenApplications.AddUnique(hWnd);
							return false;
						}
					}
				}

				if (programRule.redrawDesktopOnWindowCreated)
				{
					// If you have a Windows Explorer window open on one workspace (and it is the only non-minimized window open) and you start
					// mintty (which defaults to another workspace) then the desktop is not redrawn right (you can see that if mintty
					// is set to be transparent
					// On Windows XP SP3
					NativeMethods.RedrawWindow(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
						NativeMethods.RedrawWindowFlags.RDW_ALLCHILDREN |
						NativeMethods.RedrawWindowFlags.RDW_ERASE |
						NativeMethods.RedrawWindowFlags.RDW_INVALIDATE);
				}

				var list = new LinkedList<Tuple<Workspace, Window>>();
				applications[hWnd] = list;

				var menu = NativeMethods.GetMenu(hWnd);
				i = 0;
				foreach (var rule in matchingRules)
				{
					var window = new Window(hWnd, className, displayName, processName, workspacesCount,
						is64BitProcess, style, exStyle, ownedLists == null ? null : ownedLists[i++], rule, programRule, menu);

					list.AddLast(new Tuple<Workspace, Window>(workspaces[rule.workspace], window));

					workspaces[rule.workspace].WindowCreated(window);
				}

				if (!programRule.showMenu)
				{
					list.First.Value.Item2.DoForSelfOrOwned(w => w.ShowWindowMenu());
				}

				return true;
			}

			return false;
		}

		private void OnWindowCreatedOnCurrentWorkspace(IntPtr hWnd, ProgramRule programRule)
		{
			switch (programRule.onWindowCreatedOnCurrentWorkspaceAction)
			{
				case OnWindowCreatedOnCurrentWorkspaceAction.ActivateWindow:
					ActivateWindow(hWnd, hWnd, NativeMethods.IsIconic(hWnd));
					break;
				case OnWindowCreatedOnCurrentWorkspaceAction.ActivatePreviousActiveWindow:
					// TODO: is there a better way?
					System.Threading.Thread.Sleep(500);
					SetWorkspaceTopManagedWindowAsForeground(CurrentWorkspace);
					break;
			}
		}

		private void OutputWarning(string warning)
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
			var set = new HashSet<IntPtr>();

			NativeMethods.EnumDesktopWindows(IntPtr.Zero, (hWnd, _) =>
				{
					set.Add(hWnd);
					if (NativeMethods.IsWindowVisible(hWnd) && !applications.ContainsKey(hWnd))
					{
						// add any application that was not added for some reason when it was created
						AddWindowToWorkspace(hWnd);
					}
					return true;
				}, IntPtr.Zero);

			// remove all non-existent applications
			applications.Keys.Unless(set.Contains).ToArray().ForEach(RemoveApplicationFromAllWorkspaces);
		}

		private void SetWorkspaceTopManagedWindowAsForeground(Workspace workspace)
		{
			// TODO: perhaps switch to the last window that was foreground?
			var topmost = workspace.GetTopmostWindow();
			if (topmost != null)
			{
				ForceForegroundWindow(topmost);
			}
			else
			{
				ForceForegroundWindow(NativeMethods.GetShellWindow());
			}
		}

		private void ForceForegroundWindow(Window window)
		{
			var hWnd = window.hWnd;
			if (window.activateLastActivePopup)
			{
				hWnd = NativeMethods.GetLastActivePopup(hWnd);
			}
			ForceForegroundWindow(hWnd);
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
						var foregroundWindowThread = NativeMethods.GetWindowThreadProcessId(foregroundWindow, IntPtr.Zero);
						if (NativeMethods.AttachThreadInput(windawesomeThreadId, foregroundWindowThread, true))
						{
							successfullyChanged = TrySetForegroundWindow(hWnd);
							NativeMethods.AttachThreadInput(windawesomeThreadId, foregroundWindowThread, false);
						}
					}

					if (!successfullyChanged)
					{
						forceForegroundWindow = hWnd;
						SendHotkey(uniqueHotkey);
					}
				}
			}
		}

		private bool TrySetForegroundWindow(IntPtr hWnd)
		{
			const int tryCount = 5;
			var count = 0;
			while (!NativeMethods.SetForegroundWindow(hWnd) && ++count < tryCount)
			{
			}

			if (count == tryCount)
			{
				return false;
			}
			else
			{
				NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOP, 0, 0, 0, 0,
					NativeMethods.SWP.SWP_ASYNCWINDOWPOS | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE);

				return true;
			}
		}

		#region SendHotkey

		private readonly NativeMethods.INPUT[] input = new NativeMethods.INPUT[18];

		private readonly NativeMethods.INPUT shiftKeyDown = new NativeMethods.INPUT(Keys.ShiftKey, 0);
		private readonly NativeMethods.INPUT shiftKeyUp = new NativeMethods.INPUT(Keys.ShiftKey, NativeMethods.KEYEVENTF_KEYUP);
		private readonly NativeMethods.INPUT leftShiftKeyDown = new NativeMethods.INPUT(Keys.LShiftKey, 0);
		private readonly NativeMethods.INPUT leftShiftKeyUp = new NativeMethods.INPUT(Keys.LShiftKey, NativeMethods.KEYEVENTF_KEYUP);
		private readonly NativeMethods.INPUT rightShiftKeyDown = new NativeMethods.INPUT(Keys.RShiftKey, 0);
		private readonly NativeMethods.INPUT rightShiftKeyUp = new NativeMethods.INPUT(Keys.RShiftKey, NativeMethods.KEYEVENTF_KEYUP);

		private readonly NativeMethods.INPUT winKeyDown = new NativeMethods.INPUT(Keys.LWin, 0);
		private readonly NativeMethods.INPUT winKeyUp = new NativeMethods.INPUT(Keys.LWin, NativeMethods.KEYEVENTF_KEYUP);
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

			PressReleaseModifierKey(leftWinPressed, rightWinPressed, winShouldBePressed, winKeyDown, leftWinKeyUp, rightWinKeyUp, ref i);

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

			PressReleaseModifierKey(leftWinPressed, rightWinPressed, winShouldBePressed, winKeyUp, leftWinKeyDown, rightWinKeyDown, ref i);

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

		private Window GetOwnermostWindow(IntPtr hWnd, Workspace workspace)
		{
			var window = workspace.GetOwnermostWindow(hWnd);
			while (window == null)
			{
				hWnd = NativeMethods.GetWindow(hWnd, NativeMethods.GW.GW_OWNER);
				if (hWnd == IntPtr.Zero)
				{
					return null;
				}
				window = workspace.GetOwnermostWindow(hWnd);
			}
			return window;
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
					System.Threading.Thread.Sleep(1000); // TODO: is this enough? Is it too much?
					HideWindow(tuple.Item2);
					SetWorkspaceTopManagedWindowAsForeground(CurrentWorkspace);
					break;
			}
		}

		private void FollowWindow(int toWorkspace, bool follow, Window window)
		{
			if (follow)
			{
				if (!SwitchToWorkspace(toWorkspace))
				{
					ForceForegroundWindow(window);
				}
			}
			else
			{
				SetWorkspaceTopManagedWindowAsForeground(CurrentWorkspace);
			}
		}

		private void HideWindow(Window window)
		{
			hiddenApplications.Add(window.hWnd);
			window.Hide();
		}

		private void ShowHideWindows(Workspace oldWorkspace, Workspace newWorkspace, bool setForeground)
		{
			var showWindows = newWorkspace.GetOwnerWindows();
			var hideWindows = oldWorkspace.GetOwnerWindows().Except(showWindows);

			var winPosInfo = NativeMethods.BeginDeferWindowPos(showWindows.Count + oldWorkspace.GetOwnerWindows().Count);

			var newTopmostWindow = newWorkspace.GetTopmostWindow();
			foreach (var window in showWindows.Where(WindowIsNotHung))
			{
				winPosInfo = NativeMethods.DeferWindowPos(winPosInfo, window.hWnd, IntPtr.Zero, 0, 0, 0, 0,
					(window == newTopmostWindow ? 0 : NativeMethods.SWP.SWP_NOACTIVATE) | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE |
					NativeMethods.SWP.SWP_NOZORDER | NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_SHOWWINDOW);
				window.ShowPopupsAndRedraw();
			}

			foreach (var window in hideWindows)
			{
				if (WindowIsNotHung(window))
				{
					hiddenApplications.Add(window.hWnd);
					window.HidePopups();
					winPosInfo = NativeMethods.DeferWindowPos(winPosInfo, window.hWnd, IntPtr.Zero, 0, 0, 0, 0,
						NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE |
						NativeMethods.SWP.SWP_NOZORDER | NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_HIDEWINDOW);
				}
				else if (!hiddenApplications.Contains(window.hWnd))
				{
					HideWindow(window);
				}
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
			var window = GetOwnermostWindow(hWnd, CurrentWorkspace);
			if (window != null)
			{
				ActivateWindow(hWnd, window, window.IsMinimized);

				return true;
			}

			return false;
		}

		// there is a dynamic here because this could be either a Window or an IntPtr
		private void ActivateWindow(IntPtr hWnd, dynamic window, bool isMinimized)
		{
			if (isMinimized)
			{
				// OpenIcon does not restore the window to its previous size (e.g. maximized)
				NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW.SW_RESTORE);
				System.Threading.Thread.Sleep(Workspace.minimizeRestoreDelay);
				PostAction(() => ForceForegroundWindow(window));
			}
			else
			{
				ForceForegroundWindow(window);
			}
		}

		#endregion

		#region API

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
					NativeMethods.SMTO.SMTO_ABORTIFHUNG | NativeMethods.SMTO.SMTO_BLOCK, 1000, IntPtr.Zero) != IntPtr.Zero ||
				Marshal.GetLastWin32Error() != NativeMethods.ERROR_TIMEOUT;
		}

		public void RefreshWindawesome()
		{
			RefreshApplicationsHash();

			// repositions all windows in all workspaces
			config.Workspaces.ForEach(ws => ws.hasChanges = true);
			monitors.ForEach(m => m.CurrentVisibleWorkspace.Reposition());

			// redraw all windows in current workspace
			monitors.ForEach(m => m.CurrentVisibleWorkspace.GetOwnerWindows().ForEach(w => w.Redraw()));

			// refresh bars
			config.Bars.ForEach(b => b.Refresh());
		}

		public void ChangeApplicationToWorkspace(IntPtr hWnd, int toWorkspace = 0, int fromWorkspace = 0, bool follow = true)
		{
			var oldWorkspace = workspaces[fromWorkspace];
			var newWorkspace = workspaces[toWorkspace];

			if (newWorkspace.id != oldWorkspace.id)
			{
				var window = GetOwnermostWindow(hWnd, oldWorkspace);

				if (window != null && !newWorkspace.ContainsWindow(hWnd))
				{
					oldWorkspace.WindowDestroyed(window);
					newWorkspace.WindowCreated(window);

					var list = applications[window.hWnd];
					list.Remove(new Tuple<Workspace, Window>(oldWorkspace, window));
					list.AddFirst(new Tuple<Workspace, Window>(newWorkspace, window));

					FollowWindow(toWorkspace, follow, window);
				}
			}
		}

		public void AddApplicationToWorkspace(IntPtr hWnd, int toWorkspace = 0, int fromWorkspace = 0, bool follow = true)
		{
			var oldWorkspace = workspaces[fromWorkspace];
			var newWorkspace = workspaces[toWorkspace];

			if (newWorkspace.id != oldWorkspace.id)
			{
				var window = GetOwnermostWindow(hWnd, oldWorkspace);

				if (window != null && !newWorkspace.ContainsWindow(hWnd))
				{
					var newWindow = new Window(window);

					newWorkspace.WindowCreated(newWindow);

					var list = applications[window.hWnd];
					list.AddFirst(new Tuple<Workspace, Window>(newWorkspace, newWindow));
					list.Where(t => ++t.Item2.WorkspacesCount == 2).ForEach(t => t.Item1.AddToSharedWindows(t.Item2));

					FollowWindow(toWorkspace, follow, window);
				}
			}
		}

		public void TemporarilyShowWindowOnCurrentWorkspace(Window window)
		{
			if (!CurrentWorkspace.ContainsWindow(window.hWnd))
			{
				CurrentWorkspace.Monitor.temporarilyShownWindows.Add(window.hWnd);
				window.Show();
			}
		}

		public void RemoveApplicationFromWorkspace(IntPtr hWnd, int workspace = 0, bool setForeground = true)
		{
			var window = GetOwnermostWindow(hWnd, workspaces[workspace]);
			if (window != null)
			{
				if (window.WorkspacesCount == 1)
				{
					QuitApplication(window.hWnd);
				}
				else
				{
					HideWindow(window);

					var list = applications[window.hWnd];
					list.Remove(new Tuple<Workspace, Window>(workspaces[workspace], window));
					list.Where(t => --t.Item2.WorkspacesCount == 1).ForEach(t => t.Item1.AddToRemovedSharedWindows(t.Item2));

					workspaces[workspace].WindowDestroyed(window);
					if (workspaces[workspace].IsCurrentWorkspace && setForeground)
					{
						SetWorkspaceTopManagedWindowAsForeground(workspaces[workspace]);
					}
				}
			}
		}

		// TODO: when the last application on a monitor is removed, an application from another monitor is activated
		public void RemoveApplicationFromAllWorkspaces(IntPtr hWnd) // sort of UnmanageWindow
		{
			LinkedList<Tuple<Workspace, Window>> list;
			if (applications.TryGetValue(hWnd, out list))
			{
				list.First.Value.Item2.DoForSelfOrOwned(w =>
					{
						if (!w.ShowMenu && w.menu != IntPtr.Zero)
						{
							NativeMethods.DestroyMenu(w.menu);
						}
					});
				list.ForEach(t => t.Item1.WindowDestroyed(t.Item2));
				Workspace workspace;
				if ((workspace = list.Select(t => t.Item1).FirstOrDefault(ws => ws.IsCurrentWorkspace)) != null)
				{
					SetWorkspaceTopManagedWindowAsForeground(workspace);
				}
				applications.Remove(hWnd);
				monitors.ForEach(m => m.temporarilyShownWindows.Remove(hWnd));
			}
		}

		public bool SwitchToWorkspace(int workspace, bool setForeground = true)
		{
			var newWorkspace = workspaces[workspace];
			if (workspace != CurrentWorkspace.id)
			{
				// TODO: perhaps move mouse over monitors? an option?

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
					
					var currentVisibleWorkspace = newWorkspace.Monitor.CurrentVisibleWorkspace;

					var needsToReposition = newWorkspace.NeedsToReposition();

					if (!needsToReposition)
					{
						// first show and hide if there are no changes
						ShowHideWindows(currentVisibleWorkspace, newWorkspace, setForeground);
					}

					if (newWorkspace.Monitor.temporarilyShownWindows.Count > 0)
					{
						newWorkspace.Monitor.temporarilyShownWindows.ForEach(hWnd => HideWindow(applications[hWnd].First.Value.Item2));
						newWorkspace.Monitor.temporarilyShownWindows.Clear();
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

				PreviousWorkspace = CurrentWorkspace.id;
				workspaces[0] = newWorkspace;

				return true;
			}

			return false;
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
			CurrentWorkspace.ToggleWindowFloating(GetOwnermostWindow(hWnd, CurrentWorkspace));
		}

		public void ToggleShowHideWindowInTaskbar(IntPtr hWnd)
		{
			Workspace.ToggleShowHideWindowInTaskbar(GetOwnermostWindow(hWnd, CurrentWorkspace));
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
			if (isRunningElevated)
			{
				PostAction(() => NativeMethods.RunApplicationNonElevated(path, arguments));
			}
			else
			{
				System.Diagnostics.Process.Start(path, arguments);
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
				IntPtr.Zero, NativeMethods.SMTO.SMTO_ABORTIFHUNG | NativeMethods.SMTO.SMTO_BLOCK, 500, out result);

			if (result == IntPtr.Zero)
			{
				NativeMethods.SendMessageTimeout(hWnd, NativeMethods.WM_QUERYDRAGICON, UIntPtr.Zero,
					IntPtr.Zero, NativeMethods.SMTO.SMTO_ABORTIFHUNG | NativeMethods.SMTO.SMTO_BLOCK, 500, out result);
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
								bitmap = Icon.ExtractAssociatedIcon(processFileName).ToBitmap();
								if (bitmap != null)
								{
									bitmap = new Bitmap(bitmap, smallIconSize);
								}
							}
						}
						catch
						{
						}

						return bitmap;
					}, hWnd).ContinueWith(t => action(t.Result), System.Threading.CancellationToken.None,
						System.Threading.Tasks.TaskContinuationOptions.None, System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext());
			}
			else
			{
				action(new Bitmap(Bitmap.FromHicon(result), smallIconSize));
			}
		}

		public void PostAction(Action action)
		{
			postedActions.Enqueue(action);
			NativeMethods.PostMessage(HandleStatic, postActionMessageNum, UIntPtr.Zero, IntPtr.Zero);
		}

		public void DismissTemporarilyShownWindow(IntPtr hWnd)
		{
			Monitor monitor;
			if ((monitor = monitors.FirstOrDefault(m => m.temporarilyShownWindows.Remove(hWnd))) != null)
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

		private void OnShellHookMessage(IntPtr wParam, IntPtr lParam)
		{
			LinkedList<Tuple<Workspace, Window>> list;

			switch ((NativeMethods.ShellEvents) wParam)
			{
				case NativeMethods.ShellEvents.HSHELL_WINDOWCREATED: // window created or restored from tray
					if (!applications.ContainsKey(lParam)) // if a new window has shown
					{
						AddWindowToWorkspace(lParam);
					}
					else if (!hiddenApplications.Contains(lParam) && !CurrentWorkspace.ContainsWindow(lParam) && !CurrentWorkspace.Monitor.temporarilyShownWindows.Contains(lParam)) // if a hidden window has shown
					{
						// there is a problem with some windows showing up when others are created.
						// how to reproduce: start BitComet 1.26 on some workspace, switch to another one
						// and start explorer.exe (the file manager)
						// on Windows 7 Ultimate x64 SP1

						// another problem is that some windows continuously keep showing when hidden.
						// how to reproduce: TortoiseSVN. About box. Click check for updates. This window
						// keeps showing up when changing workspaces
						NativeMethods.PostMessage(this.Handle, shellMessageNum,
							(UIntPtr) (uint) NativeMethods.ShellEvents.HSHELL_WINDOWACTIVATED, lParam);
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_WINDOWDESTROYED: // window destroyed or minimized to tray
					if (hiddenApplications.Remove(lParam) == HashMultiSet<IntPtr>.RemoveResult.NotFound)
					{
						RemoveApplicationFromAllWorkspaces(lParam);
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_WINDOWACTIVATED: // window activated
				case NativeMethods.ShellEvents.HSHELL_RUDEAPPACTIVATED:
					if (!hiddenApplications.Contains(lParam))
					{
						if (lParam != IntPtr.Zero && !CurrentWorkspace.Monitor.temporarilyShownWindows.Contains(lParam))
						{
							if (!applications.TryGetValue(lParam, out list)) // if a new window has shown
							{
								RefreshApplicationsHash();
							}
							else if (!CurrentWorkspace.ContainsWindow(lParam))
							{
								OnHiddenWindowShown(lParam, list.First.Value);
							}
						}

						// TODO: when switching from a workspace to another, both containing a shared window,
						// and the shared window is the active window, Windows sends a HSHELL_WINDOWACTIVATED
						// for the shared window after the switch (even if it is not the top window in the
						// workspace being switched to), which causes a wrong reordering in Z order
						CurrentWorkspace.WindowActivated(lParam);
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_GETMINRECT: // window minimized or restored
					System.Threading.Thread.Sleep(Workspace.minimizeRestoreDelay);
					var hWnd = Marshal.ReadIntPtr(lParam);
					if (NativeMethods.IsIconic(hWnd))
					{
						CurrentWorkspace.WindowMinimized(hWnd);
					}
					else
					{
						CurrentWorkspace.WindowRestored(hWnd);
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_FLASH: // window flashing in taskbar
					if (applications.TryGetValue(lParam, out list))
					{
						DoWindowFlashing(list);
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_REDRAW: // window's taskbar button has changed
					if (applications.TryGetValue(lParam, out list))
					{
						var text = NativeMethods.GetText(lParam);
						foreach (var t in list)
						{
							t.Item2.DisplayName = text;
							DoWindowTitleOrIconChanged(t.Item1, t.Item2, text);
						}
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_WINDOWREPLACING:
					NativeMethods.PostMessage(this.Handle, shellMessageNum,
						(UIntPtr) (uint) NativeMethods.ShellEvents.HSHELL_WINDOWCREATED, lParam);
					break;
				case NativeMethods.ShellEvents.HSHELL_WINDOWREPLACED:
					NativeMethods.PostMessage(this.Handle, shellMessageNum,
						(UIntPtr) (uint) NativeMethods.ShellEvents.HSHELL_WINDOWDESTROYED, lParam);
					break;
			}
		}

		public bool inShellMessage;
		protected override void WndProc(ref Message m)
		{
			HandleMessageDelegate messageDelegate;
			if (m.Msg == shellMessageNum)
			{
				if (inShellMessage)
				{
					NativeMethods.PostMessage(m.HWnd, m.Msg, m.WParam, m.LParam);
				}
				else
				{
					inShellMessage = true;
					OnShellHookMessage(m.WParam, m.LParam);
					inShellMessage = false;
				}
			}
			else if (m.Msg == postActionMessageNum)
			{
				postedActions.Dequeue()();
			}
			else if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam == this.getForegroundPrivilageAtom)
			{
				TrySetForegroundWindow(forceForegroundWindow);
				forceForegroundWindow = IntPtr.Zero;
			}
			else if (messageHandlers.TryGetValue(m.Msg, out messageDelegate))
			{
				var res = false;
				foreach (HandleMessageDelegate handler in messageDelegate.GetInvocationList())
				{
					res |= handler(ref m);
				}

				if (!res)
				{
					base.WndProc(ref m);
				}
			}
			else
			{
				base.WndProc(ref m);
			}
		}

		#endregion
	}
}
