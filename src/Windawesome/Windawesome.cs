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
		private readonly Config config;
		private readonly Dictionary<IntPtr, LinkedList<Tuple<Workspace, Window>>> applications; // hWnd to a list of workspaces and windows
		private readonly HashMultiSet<IntPtr> hiddenApplications;
		private readonly uint shellMessageNum;
		private readonly HashSet<IntPtr> temporarilyShownWindows;
		private readonly bool changedNonClientMetrics;
		private readonly bool finishedInitializing;
		private readonly IntPtr getForegroundPrivilageAtom;
		private static Tuple<NativeMethods.MOD, Keys> uniqueHotkey;
		private static IntPtr forceForegroundWindow;
		private const uint postActionMessageNum = NativeMethods.WM_USER;
		private static readonly Queue<Action> postedActions;
		private static readonly Dictionary<int, HandleMessageDelegate> messageHandlers;
		private static readonly NativeMethods.NONCLIENTMETRICS originalNonClientMetrics;
		private static readonly uint windawesomeThreadId;

		public delegate bool HandleMessageDelegate(ref Message m);

		public static IntPtr HandleStatic { get; private set; }
		public static int PreviousWorkspace { get; private set; }
		public static readonly bool isRunningElevated;
		public static readonly bool isAtLeastVista;
		public static readonly bool isAtLeast7;
		public static readonly Size smallIconSize;

		#region Events

		public delegate void LayoutUpdatedEventHandler();
		public static event LayoutUpdatedEventHandler LayoutUpdated;

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

		public Workspace CurrentWorkspace
		{
			get { return config.Workspaces[0]; }
		}

		#region Windawesome Construction, Initialization and Destruction

		static Windawesome()
		{
			isAtLeastVista = Environment.OSVersion.Version.Major >= 6;
			isAtLeast7 = isAtLeastVista && Environment.OSVersion.Version.Minor >= 1;

			isRunningElevated = isAtLeastVista && NativeMethods.IsUserAnAdmin();

			windawesomeThreadId = NativeMethods.GetCurrentThreadId();

			messageHandlers = new Dictionary<int, HandleMessageDelegate>(2);

			originalNonClientMetrics = NativeMethods.NONCLIENTMETRICS.GetNONCLIENTMETRICS();
			NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETNONCLIENTMETRICS, originalNonClientMetrics.cbSize,
				ref originalNonClientMetrics, 0);

			smallIconSize = SystemInformation.SmallIconSize;

			postedActions = new Queue<Action>(5);
		}

		internal Windawesome()
		{
			this.CreateHandle(new CreateParams { Parent = NativeMethods.HWND_MESSAGE });

			HandleStatic = this.Handle;

			config = new Config();
			config.LoadPlugins(this);

			config.Workspaces = config.Workspaces.Resize(config.Workspaces.Length + 1);
			config.Workspaces[0] = config.Workspaces[config.StartingWorkspace];
			PreviousWorkspace = config.Workspaces[0].id;

			config.Bars.ForEach(b => b.InitializeBar(this, config));
			config.Plugins.ForEach(p => p.InitializePlugin(this, config));

			Workspace.FindWorkspaceBarsEquivalentClasses(config.WorkspacesCount, config.Workspaces.Skip(1));

			applications = new Dictionary<IntPtr, LinkedList<Tuple<Workspace, Window>>>(20);
			hiddenApplications = new HashMultiSet<IntPtr>();

			temporarilyShownWindows = new HashSet<IntPtr>();

			// add all windows to their respective workspace
			NativeMethods.EnumDesktopWindows(IntPtr.Zero, (hWnd, _) => (NativeMethods.IsWindowVisible(hWnd) && AddWindowToWorkspace(hWnd)) || true, IntPtr.Zero);

			WindawesomeExiting += OnWindawesomeExiting;

			// add a handler for when the screen resolution changes as well as
			// a handler for the system shutting down/restarting
			SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
			SystemEvents.SessionEnding += OnSessionEnding;

			// set the global border and padded border widths
			var metrics = originalNonClientMetrics;
			if (config.BorderWidth >= 0 && metrics.iBorderWidth != config.BorderWidth)
			{
				metrics.iBorderWidth = config.BorderWidth;
#if !DEBUG
				changedNonClientMetrics = true;
#endif
			}
			if (isAtLeastVista && config.PaddedBorderWidth >= 0 && metrics.iPaddedBorderWidth != config.PaddedBorderWidth)
			{
				metrics.iPaddedBorderWidth = config.PaddedBorderWidth;
#if !DEBUG
				changedNonClientMetrics = true;
#endif
			}
			if (changedNonClientMetrics)
			{
				System.Threading.Tasks.Task.Factory.StartNew(() =>
					NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
						ref metrics, NativeMethods.SPIF_SENDCHANGE));
			}

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

			// initialize all workspaces
			foreach (var ws in config.Workspaces.Skip(1).Where(ws => ws.id != config.StartingWorkspace))
			{
				ws.GetWindows().ForEach(w => hiddenApplications.AddUnique(w.hWnd));
				ws.Initialize(false);
			}
			config.Workspaces[0].Initialize(true);

			// switches to the default starting workspace
			config.Workspaces[0].SwitchTo();
			config.Workspaces[0].SetTopManagedWindowAsForeground();

			finishedInitializing = true;
		}

		private void OnWindawesomeExiting()
		{
			SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
			SystemEvents.SessionEnding -= OnSessionEnding;

			// unregister shell hook
			NativeMethods.DeregisterShellHookWindow(this.Handle);

			NativeMethods.UnregisterHotKey(this.Handle, (ushort) getForegroundPrivilageAtom);
			NativeMethods.GlobalDeleteAtom((ushort) getForegroundPrivilageAtom);

			// roll back any changes to Windows
			Workspace.Dispose();

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

			// revert the size of non-client area of windows
			if (changedNonClientMetrics)
			{
				var metrics = originalNonClientMetrics;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
					ref metrics, NativeMethods.SPIF_SENDCHANGE);
			}
		}

		public void Quit()
		{
			WindawesomeExiting();
			this.DestroyHandle();
		}

		#endregion

		#region Helpers

		private void OnDisplaySettingsChanged(object sender, EventArgs e)
		{
			config.Workspaces[0].Reposition();
			config.Workspaces.Where(ws => !ws.IsCurrentWorkspace).ForEach(ws => ws.hasChanges = true);
		}

		private void OnSessionEnding(object sender, SessionEndingEventArgs e)
		{
			Quit();
		}

		private bool AddWindowToWorkspace(IntPtr hWnd, bool firstTry = true)
		{
			if (NativeMethods.IsAppWindow(hWnd))
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
				var hasCurrentWorkspaceRule = matchingRules.Any(r => r.workspace == config.Workspaces[0].id);
				// matchingRules.workspaces could be { 0, 1 } and you could be at workspace 1.
				// Then, "hWnd" would be added twice if it were not for this check
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
						ActivateWindow(hWnd, hWnd, NativeMethods.IsIconic(hWnd)); // TODO: there should be an option for this
					}
					else
					{
						switch (programRule.onWindowCreatedAction)
						{
							case OnWindowShownAction.SwitchToWindowsWorkspace:
								PostAction(() => SwitchToApplication(hWnd));
								break;
							case OnWindowShownAction.MoveWindowToCurrentWorkspace:
								var workspaceId = config.Workspaces[0].id;
								var matchingRuleWorkspace = matchingRules.First().workspace;
								PostAction(() => ChangeApplicationToWorkspace(hWnd, workspaceId, matchingRuleWorkspace));
								break;
							case OnWindowShownAction.TemporarilyShowWindowOnCurrentWorkspace:
								temporarilyShownWindows.Add(hWnd);
								ActivateWindow(hWnd, hWnd, NativeMethods.IsIconic(hWnd)); // TODO: there should be an option for this
								break;
							case OnWindowShownAction.HideWindow:
								System.Threading.Thread.Sleep(500); // TODO: is this enough? Is it too much?
								config.Workspaces[0].SetTopManagedWindowAsForeground(); // TODO: perhaps switch to the last window that was foreground?
								hiddenApplications.Add(hWnd);
								NativeMethods.ShowWindow(hWnd, NativeMethods.SW.SW_HIDE);
								break;
						}
					}

					if (programRule.windowCreatedDelay > 0)
					{
						System.Threading.Thread.Sleep(programRule.windowCreatedDelay);
					}
				}

				LinkedList<Tuple<IntPtr, string, string, NativeMethods.WS, NativeMethods.WS_EX>> ownedList = null;

				if (programRule.handleOwnedWindows)
				{
					ownedList = new LinkedList<Tuple<IntPtr, string, string, NativeMethods.WS, NativeMethods.WS_EX>>();

					NativeMethods.EnumDesktopWindows(IntPtr.Zero, (h, hOwner) =>
						{
							if (NativeMethods.IsWindowVisible(h))
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
									ownedList.AddLast(new Tuple<IntPtr, string, string, NativeMethods.WS, NativeMethods.WS_EX>
										(h, classNameOwned, displayNameOwned, styleOwned, exStyleOwned));
								}
							}
							return true;
						}, hWnd);

					if (ownedList.Count == 0)
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

				var is64BitProcess = NativeMethods.Is64BitProcess(hWnd);

				var list = new LinkedList<Tuple<Workspace, Window>>();

				applications[hWnd] = list;

				foreach (var rule in matchingRules)
				{
					var newOwnedList = ownedList == null ? null :
						new LinkedList<Window>(ownedList.Select(w => new Window(w.Item1, w.Item2, w.Item3, processName,
							workspacesCount, is64BitProcess, w.Item4, w.Item5, null, rule, programRule)));
					var window = new Window(hWnd, className, displayName, processName, workspacesCount,
						is64BitProcess, style, exStyle, newOwnedList, rule, programRule);

					list.AddLast(new Tuple<Workspace, Window>(config.Workspaces[rule.workspace], window));

					config.Workspaces[rule.workspace].WindowCreated(window);
				}

				return true;
			}

			return false;
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
			applications.Keys.Unless(set.Contains).ToArray().
				ForEach(RemoveApplicationFromAllWorkspaces);
		}

		internal static void ForceForegroundWindow(Window window)
		{
			var hWnd = window.hWnd;
			if (window.activateLastActivePopup)
			{
				hWnd = NativeMethods.GetLastActivePopup(hWnd);
			}
			ForceForegroundWindow(hWnd);
		}

		internal static void ForceForegroundWindow(IntPtr hWnd)
		{
			if (WindowIsNotHung(hWnd))
			{
				var foregroundWindow = NativeMethods.GetForegroundWindow();
				if (foregroundWindow != hWnd)
				{
					bool successfullyChanged = false;
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
						SendHotkey(uniqueHotkey);
						forceForegroundWindow = hWnd;
					}
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

		private static readonly NativeMethods.INPUT[] input = new NativeMethods.INPUT[18];

		private static readonly NativeMethods.INPUT shiftKeyDown = new NativeMethods.INPUT(Keys.ShiftKey, 0);
		private static readonly NativeMethods.INPUT shiftKeyUp = new NativeMethods.INPUT(Keys.ShiftKey, NativeMethods.KEYEVENTF_KEYUP);
		private static readonly NativeMethods.INPUT leftShiftKeyDown = new NativeMethods.INPUT(Keys.LShiftKey, 0);
		private static readonly NativeMethods.INPUT leftShiftKeyUp = new NativeMethods.INPUT(Keys.LShiftKey, NativeMethods.KEYEVENTF_KEYUP);
		private static readonly NativeMethods.INPUT rightShiftKeyDown = new NativeMethods.INPUT(Keys.RShiftKey, 0);
		private static readonly NativeMethods.INPUT rightShiftKeyUp = new NativeMethods.INPUT(Keys.RShiftKey, NativeMethods.KEYEVENTF_KEYUP);

		private static readonly NativeMethods.INPUT winKeyDown = new NativeMethods.INPUT(Keys.LWin, 0);
		private static readonly NativeMethods.INPUT winKeyUp = new NativeMethods.INPUT(Keys.LWin, NativeMethods.KEYEVENTF_KEYUP);
		private static readonly NativeMethods.INPUT leftWinKeyDown = new NativeMethods.INPUT(Keys.LWin, 0);
		private static readonly NativeMethods.INPUT leftWinKeyUp = new NativeMethods.INPUT(Keys.LWin, NativeMethods.KEYEVENTF_KEYUP);
		private static readonly NativeMethods.INPUT rightWinKeyDown = new NativeMethods.INPUT(Keys.RWin, 0);
		private static readonly NativeMethods.INPUT rightWinKeyUp = new NativeMethods.INPUT(Keys.RWin, NativeMethods.KEYEVENTF_KEYUP);

		private static readonly NativeMethods.INPUT controlKeyDown = new NativeMethods.INPUT(Keys.ControlKey, 0);
		private static readonly NativeMethods.INPUT controlKeyUp = new NativeMethods.INPUT(Keys.ControlKey, NativeMethods.KEYEVENTF_KEYUP);
		private static readonly NativeMethods.INPUT leftControlKeyDown = new NativeMethods.INPUT(Keys.LControlKey, 0);
		private static readonly NativeMethods.INPUT leftControlKeyUp = new NativeMethods.INPUT(Keys.LControlKey, NativeMethods.KEYEVENTF_KEYUP);
		private static readonly NativeMethods.INPUT rightControlKeyDown = new NativeMethods.INPUT(Keys.RControlKey, 0);
		private static readonly NativeMethods.INPUT rightControlKeyUp = new NativeMethods.INPUT(Keys.RControlKey, NativeMethods.KEYEVENTF_KEYUP);

		private static readonly NativeMethods.INPUT altKeyDown = new NativeMethods.INPUT(Keys.Menu, 0);
		private static readonly NativeMethods.INPUT altKeyUp = new NativeMethods.INPUT(Keys.Menu, NativeMethods.KEYEVENTF_KEYUP);
		private static readonly NativeMethods.INPUT leftAltKeyDown = new NativeMethods.INPUT(Keys.LMenu, 0);
		private static readonly NativeMethods.INPUT leftAltKeyUp = new NativeMethods.INPUT(Keys.LMenu, NativeMethods.KEYEVENTF_KEYUP);
		private static readonly NativeMethods.INPUT rightAltKeyDown = new NativeMethods.INPUT(Keys.RMenu, 0);
		private static readonly NativeMethods.INPUT rightAltKeyUp = new NativeMethods.INPUT(Keys.RMenu, NativeMethods.KEYEVENTF_KEYUP);
		// sends the hotkey combination without disrupting the currently pressed modifiers
		private static void SendHotkey(Tuple<NativeMethods.MOD, Keys> hotkey)
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

		private static void PressReleaseModifierKey(
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

		private static Window GetOwnermostWindow(IntPtr hWnd, Workspace workspace)
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
					ChangeApplicationToWorkspace(hWnd, config.Workspaces[0].id, tuple.Item1.id);
					break;
				case OnWindowShownAction.TemporarilyShowWindowOnCurrentWorkspace:
					temporarilyShownWindows.Add(hWnd);
					break;
				case OnWindowShownAction.HideWindow:
					System.Threading.Thread.Sleep(1000); // TODO: is this enough? Is it too much?
					HideWindow(tuple.Item2);
					config.Workspaces[0].SetTopManagedWindowAsForeground(); // TODO: perhaps switch to the last window that was foreground?
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
				config.Workspaces[0].SetTopManagedWindowAsForeground(); // TODO: perhaps switch to the last window that was foreground?
			}
		}

		private void HideWindow(Window window)
		{
			hiddenApplications.Add(window.hWnd);
			window.Hide();
		}

		private void ShowHideWindows(Workspace oldWorkspace, Workspace newWorkspace, bool setForeground)
		{
			var showWindows = newWorkspace.GetWindows();
			var hideWindows = oldWorkspace.GetWindows().Except(showWindows);

			var winPosInfo = NativeMethods.BeginDeferWindowPos(showWindows.Count + oldWorkspace.GetWindows().Count);

			foreach (var window in newWorkspace.GetWindows().Where(WindowIsNotHung))
			{
				winPosInfo = NativeMethods.DeferWindowPos(winPosInfo, window.hWnd, IntPtr.Zero, 0, 0, 0, 0,
					NativeMethods.SWP.SWP_NOACTIVATE | NativeMethods.SWP.SWP_NOMOVE | NativeMethods.SWP.SWP_NOSIZE |
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
				newWorkspace.SetTopManagedWindowAsForeground();
			}
		}

		// only switches to applications in the current workspace
		private bool SwitchToApplicationInCurrentWorkspace(IntPtr hWnd)
		{
			var window = GetOwnermostWindow(hWnd, config.Workspaces[0]);
			if (window != null)
			{
				ActivateWindow(hWnd, window, window.IsMinimized);

				return true;
			}

			return false;
		}

		// there is a dynamic here because this could be either a Window or an IntPtr
		private static void ActivateWindow(IntPtr hWnd, dynamic window, bool isMinimized)
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
			config.Workspaces.Skip(1).Where(ws => !ws.IsCurrentWorkspace).ForEach(ws => ws.hasChanges = true);
			config.Workspaces[0].Reposition();

			// redraw all windows in current workspace
			config.Workspaces[0].GetWindows().ForEach(w => w.Redraw());
		}

		public void ChangeApplicationToWorkspace(IntPtr hWnd, int toWorkspace = 0, int fromWorkspace = 0, bool follow = true)
		{
			var oldWorkspace = config.Workspaces[fromWorkspace];
			var newWorkspace = config.Workspaces[toWorkspace];

			if (newWorkspace.id != oldWorkspace.id)
			{
				var window = GetOwnermostWindow(hWnd, oldWorkspace);

				if (window != null && !newWorkspace.ContainsWindow(hWnd))
				{
					oldWorkspace.WindowDestroyed(window, false);
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
			var oldWorkspace = config.Workspaces[fromWorkspace];
			var newWorkspace = config.Workspaces[toWorkspace];

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
			if (!config.Workspaces[0].ContainsWindow(window.hWnd))
			{
				temporarilyShownWindows.Add(window.hWnd);
				window.Show();
			}
		}

		public void RemoveApplicationFromWorkspace(IntPtr hWnd, int workspace = 0, bool setForeground = true)
		{
			var window = GetOwnermostWindow(hWnd, config.Workspaces[workspace]);
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
					list.Remove(new Tuple<Workspace, Window>(config.Workspaces[workspace], window));
					list.Where(t => --t.Item2.WorkspacesCount == 1).ForEach(t => t.Item1.AddToRemovedSharedWindows(t.Item2));

					config.Workspaces[workspace].WindowDestroyed(window, setForeground);
				}
			}
		}

		public void RemoveApplicationFromAllWorkspaces(IntPtr hWnd) // sort of UnmanageWindow
		{
			LinkedList<Tuple<Workspace, Window>> list;
			if (applications.TryGetValue(hWnd, out list))
			{
				list.ForEach(tuple => tuple.Item1.WindowDestroyed(tuple.Item2));
				applications.Remove(hWnd);
				temporarilyShownWindows.Remove(hWnd);
			}
		}

		public bool SwitchToWorkspace(int workspace, bool setForeground = true)
		{
			var oldWorkspace = config.Workspaces[0];
			var newWorkspace = config.Workspaces[workspace];
			if (workspace != oldWorkspace.id)
			{
				var willReposition = newWorkspace.hasChanges || newWorkspace.repositionOnSwitchedTo;
				
				if (!willReposition)
				{
					// first show and hide if there are no changes
					ShowHideWindows(oldWorkspace, newWorkspace, setForeground);
				}

				if (temporarilyShownWindows.Count > 0)
				{
					temporarilyShownWindows.ForEach(hWnd => HideWindow(applications[hWnd].First.Value.Item2));
					temporarilyShownWindows.Clear();
				}

				oldWorkspace.Unswitch();

				PreviousWorkspace = oldWorkspace.id;
				config.Workspaces[0] = newWorkspace;

				newWorkspace.SwitchTo();

				if (willReposition)
				{
					// show and hide only after Reposition has been called if there are changes
					ShowHideWindows(oldWorkspace, newWorkspace, setForeground);
				}

				return true;
			}

			return false;
		}

		public bool HideBar(IBar bar)
		{
			return config.Workspaces[0].HideBar(config.WorkspacesCount, config.Workspaces.Skip(1), bar);
		}

		public bool ShowBar(IBar bar, bool top = true, int position = 0)
		{
			return config.Workspaces[0].ShowBar(config.WorkspacesCount, config.Workspaces.Skip(1), bar, top, position);
		}

		public void ToggleWindowFloating(IntPtr hWnd)
		{
			config.Workspaces[0].ToggleWindowFloating(GetOwnermostWindow(hWnd, config.Workspaces[0]));
		}

		public void ToggleShowHideWindowInTaskbar(IntPtr hWnd)
		{
			Workspace.ToggleShowHideWindowInTaskbar(GetOwnermostWindow(hWnd, config.Workspaces[0]));
		}

		public void ToggleShowHideWindowTitlebar(IntPtr hWnd)
		{
			config.Workspaces[0].ToggleShowHideWindowTitlebar(hWnd);
		}

		public void ToggleShowHideWindowBorder(IntPtr hWnd)
		{
			config.Workspaces[0].ToggleShowHideWindowBorder(hWnd);
		}

		public void ToggleTaskbarVisibility()
		{
			config.Workspaces[0].ToggleWindowsTaskbarVisibility();
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

		public static void RegisterMessage(int message, HandleMessageDelegate targetHandler)
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

		private static readonly HashSet<NativeMethods.SendMessageCallbackDelegate> delegatesSet = new HashSet<NativeMethods.SendMessageCallbackDelegate>();
		public static void GetWindowSmallIconAsBitmap(IntPtr hWnd, Action<Bitmap> action)
		{
			NativeMethods.SendMessageCallbackDelegate firstCallback = null;
			firstCallback = (hWnd1, unused1, unused2, lResult) =>
				{
					delegatesSet.Remove(firstCallback);

					if (lResult != IntPtr.Zero)
					{
						try
						{
							action(new Bitmap(Bitmap.FromHicon(lResult), smallIconSize));
							return ;
						}
						catch
						{
						}
					}

					NativeMethods.SendMessageCallbackDelegate secondCallback = null;
					secondCallback = (hWnd2, unused3, unused4, hIcon) =>
						{
							delegatesSet.Remove(secondCallback);

							if (hIcon != IntPtr.Zero)
							{
								try
								{
									action(new Bitmap(Bitmap.FromHicon(hIcon), smallIconSize));
									return ;
								}
								catch
								{
								}
							}

							hIcon = NativeMethods.GetClassLongPtr(hWnd2, NativeMethods.GCL_HICONSM);

							if (hIcon != IntPtr.Zero)
							{
								try
								{
									action(new Bitmap(Bitmap.FromHicon(hIcon), smallIconSize));
									return ;
								}
								catch
								{
								}
							}

							if (!NativeMethods.IsWindow(hWnd2))
							{
								return ;
							}

							var uiThread = System.Threading.Tasks.TaskScheduler.FromCurrentSynchronizationContext();

							System.Threading.Tasks.Task.Factory.StartNew(() =>
								{
									Bitmap bitmap = null;
									try
									{
										int processId;
										NativeMethods.GetWindowThreadProcessId(hWnd2, out processId);
										var process = System.Diagnostics.Process.GetProcessById(processId);

										var info = new NativeMethods.SHFILEINFO();

										NativeMethods.SHGetFileInfo(process.MainModule.FileName, 0, ref info,
											Marshal.SizeOf(info), NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

										if (info.hIcon != IntPtr.Zero)
										{
											bitmap = new Bitmap(Bitmap.FromHicon(info.hIcon), smallIconSize);
											NativeMethods.DestroyIcon(info.hIcon);
										}
										else
										{
											bitmap = Icon.ExtractAssociatedIcon(process.MainModule.FileName).ToBitmap();
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
								}).ContinueWith(t => action(t.Result), System.Threading.CancellationToken.None,
									System.Threading.Tasks.TaskContinuationOptions.None, uiThread);
						};

					if (NativeMethods.SendMessageCallback(hWnd1, NativeMethods.WM_QUERYDRAGICON, UIntPtr.Zero,
						IntPtr.Zero, secondCallback, UIntPtr.Zero))
					{
						delegatesSet.Add(secondCallback);
					}
				};

			if (NativeMethods.SendMessageCallback(hWnd, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL,
				IntPtr.Zero, firstCallback, UIntPtr.Zero))
			{
				delegatesSet.Add(firstCallback);
			}
		}

		public static void PostAction(Action action)
		{
			postedActions.Enqueue(action);
			NativeMethods.PostMessage(HandleStatic, postActionMessageNum, UIntPtr.Zero, IntPtr.Zero);
		}

		public void DismissTemporarilyShownWindow(IntPtr hWnd)
		{
			if (temporarilyShownWindows.Contains(hWnd))
			{
				HideWindow(applications[hWnd].First.Value.Item2);
				config.Workspaces[0].SetTopManagedWindowAsForeground();
				temporarilyShownWindows.Remove(hWnd);
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
					else if (!hiddenApplications.Contains(lParam) && !config.Workspaces[0].ContainsWindow(lParam) && !temporarilyShownWindows.Contains(lParam)) // if a hidden window has shown
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
						if (lParam != IntPtr.Zero && !temporarilyShownWindows.Contains(lParam))
						{
							if (!applications.TryGetValue(lParam, out list)) // if a new window has shown
							{
								RefreshApplicationsHash();
							}
							else if (!config.Workspaces[0].ContainsWindow(lParam))
							{
								OnHiddenWindowShown(lParam, list.First.Value);
							}
						}

						// TODO: when switching from a workspace to another, both containing a shared window,
						// and the shared window is the active window, Windows sends a HSHELL_WINDOWACTIVATED
						// for the shared window after the switch (even if it is not the top window in the
						// workspace being switched to), which causes a wrong reordering in Z order
						config.Workspaces[0].WindowActivated(lParam);
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_GETMINRECT: // window minimized or restored
					System.Threading.Thread.Sleep(Workspace.minimizeRestoreDelay);
					var hWnd = Marshal.ReadIntPtr(lParam);
					if (NativeMethods.IsIconic(hWnd))
					{
						config.Workspaces[0].WindowMinimized(hWnd);
					}
					else
					{
						config.Workspaces[0].WindowRestored(hWnd);
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

		private bool inShellMessage;
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
