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
		private static readonly uint postActionMessageNum;
		private static readonly Queue<Action> postedActions;
		private static readonly Dictionary<int, HandleMessageDelegate> messageHandlers;
		private static readonly NativeMethods.NONCLIENTMETRICS originalNonClientMetrics;
		private static readonly Dictionary<IntPtr, Action<IntPtr>> onWindowShown;
		private readonly HashSet<IntPtr> temporarilyShownWindows;
		private readonly bool changedNonClientMetrics;
		private readonly bool finishedInitializing;
		private readonly IntPtr getForegroundPrivilageAtom;
		private static Tuple<NativeMethods.MOD, Keys> uniqueHotkey;
		private static IntPtr forceForegroundWindow;
		private readonly Rectangle[] originalWorkingArea;
		private Size screenResolution;

		internal static int[] workspaceBarsEquivalentClasses;

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

			messageHandlers = new Dictionary<int, HandleMessageDelegate>(2);

			originalNonClientMetrics = NativeMethods.NONCLIENTMETRICS.GetNONCLIENTMETRICS();
			NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETNONCLIENTMETRICS, originalNonClientMetrics.cbSize,
				ref originalNonClientMetrics, 0);

			smallIconSize = SystemInformation.SmallIconSize;

			postedActions = new Queue<Action>(5);
			postActionMessageNum = NativeMethods.RegisterWindowMessage("POST_ACTION_MESSAGE");

			onWindowShown = new Dictionary<IntPtr, Action<IntPtr>>(3);
		}

		internal Windawesome()
		{
			this.CreateHandle(new CreateParams { Caption = "Windawesome", ClassName = "Message", Parent = (IntPtr) (-3) });

			HandleStatic = this.Handle;

			config = new Config();
			config.LoadPlugins(this);
			config.Workspaces = config.Workspaces.Resize(config.Workspaces.Length + 1);
			config.Workspaces[0] = config.Workspaces[config.StartingWorkspace];
			PreviousWorkspace = config.Workspaces[0].id;
			config.Bars.ForEach(b => b.InitializeBar(this, config));
			config.Plugins.ForEach(p => p.InitializePlugin(this, config));

			workspaceBarsEquivalentClasses = new int[config.WorkspacesCount];
			FindWorkspaceBarsEquivalentClasses();

			applications = new Dictionary<IntPtr, LinkedList<Tuple<Workspace, Window>>>(20);
			hiddenApplications = new HashMultiSet<IntPtr>();

			temporarilyShownWindows = new HashSet<IntPtr>();

			// add all windows to their respective workspace
			NativeMethods.EnumWindows((hWnd, _) => AddWindowToWorkspace(hWnd) || true, IntPtr.Zero);

			WindawesomeExiting += OnWindawesomeExiting;

			// add a handler for when the working area or the screen resolution changes as well as
			// a handler for the system shutting down/restarting
			SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
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
					"use by some other program! Please use a unique one, otherwise Windawesome won't be able to switch " +
					"to windows as you change workspaces!");
			}

			// register a shell hook
			NativeMethods.RegisterShellHookWindow(HandleStatic);
			shellMessageNum = NativeMethods.RegisterWindowMessage("SHELLHOOK");

			// initialize all workspaces
			foreach (var ws in config.Workspaces.Skip(1).Where(ws => ws.id != config.StartingWorkspace))
			{
				ws.GetWindows().ForEach(w => hiddenApplications.AddUnique(w.hWnd));
				ws.Initialize(false);
			}
			config.Workspaces[0].Initialize(true);

			screenResolution = SystemInformation.PrimaryMonitorSize;
			originalWorkingArea = new Rectangle[config.WorkspacesCount];
			originalWorkingArea[0] = SystemInformation.WorkingArea;
			for (var i = 1; i < config.WorkspacesCount; i++)
			{
				originalWorkingArea[i] = originalWorkingArea[0];
			}

			// switches to the default starting workspace
			config.Workspaces[0].SwitchTo();

			finishedInitializing = true;
		}

		private void OnWindawesomeExiting()
		{
			SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
			SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
			SystemEvents.SessionEnding -= OnSessionEnding;

			// unregister shell hook
			NativeMethods.DeregisterShellHookWindow(HandleStatic);

			NativeMethods.UnregisterHotKey(this.Handle, (ushort) getForegroundPrivilageAtom);
			NativeMethods.GlobalDeleteAtom((ushort) getForegroundPrivilageAtom);

			// roll back any changes to Windows
			config.Workspaces[0].RevertToInitialValues();
			foreach (var window in applications.Values.Select(l => l.First.Value.Item2))
			{
				Workspace.DoForWindowOrItsOwned(window, w => w.RevertToInitialValues());
				window.Show();
			}

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

		private void FindWorkspaceBarsEquivalentClasses()
		{
			var listOfUniqueBars = new LinkedList<Tuple<HashSet<Workspace.BarInfo>, int>>();

			int i = 0, last = 0;
			foreach (var set in
				this.config.Workspaces.Skip(1).Select(workspace => new HashSet<Workspace.BarInfo>(workspace.bars)))
			{
				Tuple<HashSet<Workspace.BarInfo>, int> matchingBar;
				if ((matchingBar = listOfUniqueBars.FirstOrDefault(uniqueBar => set.SetEquals(uniqueBar.Item1))) != null)
				{
					workspaceBarsEquivalentClasses[i++] = matchingBar.Item2;
				}
				else
				{
					workspaceBarsEquivalentClasses[i++] = ++last;
					listOfUniqueBars.AddLast(new Tuple<HashSet<Workspace.BarInfo>, int>(set, last));
				}
			}
		}

		public void Quit()
		{
			WindawesomeExiting();
			this.DestroyHandle();
		}

		#endregion

		#region Helpers

		private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
		{
			if (e.Category == UserPreferenceCategory.Desktop)
			{
				var newWorkingArea = SystemInformation.WorkingArea;
				if (newWorkingArea != config.Workspaces[0].workingArea)
				{
					if (newWorkingArea == originalWorkingArea[CurrentWorkspace.id - 1])
					{
						// because Windows resets the working area when the UAC prompt is shown,
						// as well as shows the taskbar when a full-screen application is exited... twice. :)

						// how to reproduce: start any program that triggers a UAC prompt or start
						// IrfanView 4.28 with some picture, enter full-screen with "Return" and then exit
						// with "Return" again
						PostAction(() => config.Workspaces[0].ShowHideWindowsTaskbar());
						PostAction(() => config.Workspaces[0].ShowHideWindowsTaskbar());
					}
					else
					{
						// something new has shown that has changed the working area

						config.Workspaces[0].OnWorkingAreaReset(newWorkingArea);

						originalWorkingArea[config.Workspaces[0].id - 1] = newWorkingArea;

						FindWorkspaceBarsEquivalentClasses();
					}
				}
			}
		}

		private void OnDisplaySettingsChanged(object sender, EventArgs e)
		{
			// Windows (at least 7) resets the working area to its default one if there are other docked programs
			// other than the Windows taskbar when changing resolution, otherwise the working area is left intact
			// (only scaled, of course, because of the resolution change)

			var newScreenResolution = SystemInformation.PrimaryMonitorSize;

			if (newScreenResolution != screenResolution)
			{
				var newWorkingArea = SystemInformation.WorkingArea;

				if (newWorkingArea.Y == config.Workspaces[0].workingArea.Y &&
					screenResolution.Height - config.Workspaces[0].workingArea.Bottom ==
						newScreenResolution.Height - newWorkingArea.Bottom)
				{
					for (var i = 0; i < config.WorkspacesCount; i++)
					{
						config.Workspaces[i + 1].OnScreenResolutionChanged(newWorkingArea);

						originalWorkingArea[i] = newWorkingArea; // TODO: this is wrong, should get the working area when reset
					}
				}
				else
				{
					// working area has been reset. This could be either because there is a docked program, other than
					// the Windows Taskbar, or it could be because a Remote Desktop Connection has been established

					for (var i = 0; i < config.WorkspacesCount; i++)
					{
						config.Workspaces[i + 1].OnWorkingAreaReset(newWorkingArea);

						originalWorkingArea[i] = newWorkingArea;
					}
				}

				screenResolution = newScreenResolution;
			}
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

				var programRule = config.ProgramRules.First(r => r.IsMatch(className, displayName, processName, style, exStyle));
				if (programRule.tryAgainAfter >= 0 && firstTry && finishedInitializing)
				{
					System.Threading.Thread.Sleep(programRule.tryAgainAfter);
					return AddWindowToWorkspace(hWnd, false);
				}
				if (!programRule.isManaged)
				{
					return false;
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

				var isMinimized = false;

				if (finishedInitializing)
				{
					if (hasWorkspaceZeroRule || hasCurrentWorkspaceRule)
					{
						// this means that the window must be on the current workspace anyway
						ForceForegroundWindow(hWnd);
					}
					else
					{
						switch (programRule.onWindowCreatedAction)
						{
							case OnWindowShownAction.SwitchToWindowsWorkspace:
								PostAction(() => SwitchToApplication(hWnd));
								break;
							case OnWindowShownAction.MoveWindowToCurrentWorkspace:
								PostAction(() => ChangeApplicationToWorkspace(hWnd, config.Workspaces[0].id, matchingRules.First().workspace));
								break;
							case OnWindowShownAction.TemporarilyShowWindowOnCurrentWorkspace:
								temporarilyShownWindows.Add(hWnd);
								ForceForegroundWindow(hWnd); // TODO: ICQ 7.5's windows do not show on top
								break;
							case OnWindowShownAction.HideWindow:
								config.Workspaces[0].SetTopWindowAsForeground();
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
				else
				{
					isMinimized = NativeMethods.IsIconic(hWnd);
				}

				LinkedList<Tuple<IntPtr, string, string, NativeMethods.WS, NativeMethods.WS_EX>> ownedList = null;

				if (programRule.handleOwnedWindows)
				{
					ownedList = new LinkedList<Tuple<IntPtr, string, string, NativeMethods.WS, NativeMethods.WS_EX>>();

					NativeMethods.EnumWindows((h, hOwner) =>
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
						ownedList = null; // TODO: maybe should return false?
					}
				}

				if (programRule.redrawDesktopOnWindowCreated)
				{
					// If you have a Windows Explorer window open on one workspace (and it is the only non-minimized window open) and you start
					// mintty (which defaults to another workspace) then the desktop is not redrawn right (you can see that if mintty
					// is set to be transparent
					NativeMethods.RedrawWindow(IntPtr.Zero, IntPtr.Zero, IntPtr.Zero,
						NativeMethods.RedrawWindowFlags.RDW_ALLCHILDREN |
						NativeMethods.RedrawWindowFlags.RDW_ERASE |
						NativeMethods.RedrawWindowFlags.RDW_INVALIDATE);
				}

				var is64BitProcess = Environment.Is64BitOperatingSystem && NativeMethods.Is64BitProcess(hWnd);

				var list = new LinkedList<Tuple<Workspace, Window>>();

				applications[hWnd] = list;

				foreach (var rule in matchingRules)
				{
					var window = new Window(hWnd, className, displayName, processName, workspacesCount,	is64BitProcess, style, exStyle, isMinimized,
						ownedList == null ? null : new LinkedList<Window>(
							ownedList.Select(w => new Window(w.Item1, w.Item2, w.Item3, processName,
								workspacesCount, is64BitProcess, w.Item4, w.Item5, isMinimized, null, rule, programRule))),
						rule, programRule);
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

			NativeMethods.EnumWindows((hWnd, _) =>
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
			applications.Keys.Where(app => !set.Contains(app)).ToArray().
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
			ExecuteOnWindowShown(hWnd, SendHotkey);
		}

		private static readonly NativeMethods.INPUT[] input = new NativeMethods.INPUT[18];
		// sends the uniqueHotkey combination without disrupting the currently pressed modifiers
		private static void SendHotkey(IntPtr hWnd)
		{
			uint i = 0;

			NativeMethods.BlockInput(true);

			// press needed modifiers
			var shiftShouldBePressed = uniqueHotkey.Item1.HasFlag(NativeMethods.MOD.MOD_SHIFT);
			var leftShiftPressed = (NativeMethods.GetAsyncKeyState(Keys.LShiftKey) & 0x8000) == 0x8000;
			var rightShiftPressed = (NativeMethods.GetAsyncKeyState(Keys.RShiftKey) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftShiftPressed, rightShiftPressed, shiftShouldBePressed,
				Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey, 0, NativeMethods.KEYEVENTF_KEYUP, ref i);

			var winShouldBePressed = uniqueHotkey.Item1.HasFlag(NativeMethods.MOD.MOD_WIN);
			var leftWinPressed = (NativeMethods.GetAsyncKeyState(Keys.LWin) & 0x8000) == 0x8000;
			var rightWinPressed = (NativeMethods.GetAsyncKeyState(Keys.RWin) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftWinPressed, rightWinPressed, winShouldBePressed,
				Keys.LWin, Keys.LWin, Keys.RWin, 0, NativeMethods.KEYEVENTF_KEYUP, ref i);

			var controlShouldBePressed = uniqueHotkey.Item1.HasFlag(NativeMethods.MOD.MOD_CONTROL);
			var leftControlPressed = (NativeMethods.GetAsyncKeyState(Keys.LControlKey) & 0x8000) == 0x8000;
			var rightControlPressed = (NativeMethods.GetAsyncKeyState(Keys.RControlKey) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftControlPressed, rightControlPressed, controlShouldBePressed,
				Keys.ControlKey, Keys.LControlKey, Keys.RControlKey, 0, NativeMethods.KEYEVENTF_KEYUP, ref i);

			var altShouldBePressed = uniqueHotkey.Item1.HasFlag(NativeMethods.MOD.MOD_ALT);
			var leftAltPressed = (NativeMethods.GetAsyncKeyState(Keys.LMenu) & 0x8000) == 0x8000;
			var rightAltPressed = (NativeMethods.GetAsyncKeyState(Keys.RMenu) & 0x8000) == 0x8000;

			PressReleaseModifierKey(leftAltPressed, rightAltPressed, altShouldBePressed,
				Keys.Menu, Keys.LMenu, Keys.RMenu, 0, NativeMethods.KEYEVENTF_KEYUP, ref i);

			// press and release key
			input[i++] = new NativeMethods.INPUT(uniqueHotkey.Item2, 0);
			input[i++] = new NativeMethods.INPUT(uniqueHotkey.Item2, NativeMethods.KEYEVENTF_KEYUP);

			// revert changes to modifiers
			PressReleaseModifierKey(leftAltPressed, rightAltPressed, altShouldBePressed,
				Keys.Menu, Keys.LMenu, Keys.RMenu, NativeMethods.KEYEVENTF_KEYUP, 0, ref i);

			PressReleaseModifierKey(leftControlPressed, rightControlPressed, controlShouldBePressed,
				Keys.ControlKey, Keys.LControlKey, Keys.RControlKey, NativeMethods.KEYEVENTF_KEYUP, 0, ref i);

			PressReleaseModifierKey(leftWinPressed, rightWinPressed, winShouldBePressed,
				Keys.LWin, Keys.LWin, Keys.RWin, NativeMethods.KEYEVENTF_KEYUP, 0, ref i);

			PressReleaseModifierKey(leftShiftPressed, rightShiftPressed, shiftShouldBePressed,
				Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey, NativeMethods.KEYEVENTF_KEYUP, 0, ref i);

			NativeMethods.SendInput(i, input, NativeMethods.INPUTSize);

			NativeMethods.BlockInput(false);

			forceForegroundWindow = hWnd;
		}

		private static void PressReleaseModifierKey(
			bool leftKeyPressed, bool rightKeyPressed, bool keyShouldBePressed,
			Keys key, Keys leftKey, Keys rightKey,
			uint flags, uint flags2, ref uint i)
		{
			if (keyShouldBePressed)
			{
				if (!leftKeyPressed && !rightKeyPressed)
				{
					input[i++] = new NativeMethods.INPUT(key, flags);
				}
			}
			else
			{
				if (leftKeyPressed)
				{
					input[i++] = new NativeMethods.INPUT(leftKey, flags2);
				}
				if (rightKeyPressed)
				{
					input[i++] = new NativeMethods.INPUT(rightKey, flags2);
				}
			}
		}

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

		private void OnHiddenWindowShown(IntPtr hWnd, LinkedList<Tuple<Workspace, Window>> list)
		{
			switch (list.First.Value.Item2.onHiddenWindowShownAction)
			{
				case OnWindowShownAction.SwitchToWindowsWorkspace:
					SwitchToApplication(hWnd);
					break;
				case OnWindowShownAction.MoveWindowToCurrentWorkspace:
					ChangeApplicationToWorkspace(hWnd, config.Workspaces[0].id, list.First.Value.Item1.id);
					break;
				case OnWindowShownAction.TemporarilyShowWindowOnCurrentWorkspace:
					temporarilyShownWindows.Add(hWnd);
					break;
				case OnWindowShownAction.HideWindow:
					System.Threading.Thread.Sleep(1000);
					hiddenApplications.Add(hWnd);
					list.First.Value.Item2.Hide();
					config.Workspaces[0].SetTopWindowAsForeground();
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
				config.Workspaces[0].SetTopWindowAsForeground();
			}
		}

		#endregion

		#region API

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

		public void RemoveApplicationFromCurrentWorkspace(IntPtr hWnd)
		{
			var window = GetOwnermostWindow(hWnd, config.Workspaces[0]);
			if (window != null)
			{
				if (window.WorkspacesCount == 1)
				{
					QuitApplication(window.hWnd);
				}
				else
				{
					hiddenApplications.Add(window.hWnd);
					window.Hide();

					var list = applications[window.hWnd];
					list.Remove(new Tuple<Workspace, Window>(this.CurrentWorkspace, window));
					list.Where(t => --t.Item2.WorkspacesCount == 1).ForEach(t => t.Item1.AddToRemovedSharedWindows(t.Item2));

					this.CurrentWorkspace.WindowDestroyed(window);
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
			if (workspace != this.CurrentWorkspace.id)
			{
				this.CurrentWorkspace.GetWindows().ForEach(w => hiddenApplications.Add(w.hWnd));
				this.CurrentWorkspace.Unswitch();

				foreach (var hWnd in temporarilyShownWindows)
				{
					hiddenApplications.Add(hWnd);
					applications[hWnd].First.Value.Item2.Hide();
				}

				PreviousWorkspace = this.CurrentWorkspace.id;
				config.Workspaces[0] = config.Workspaces[workspace];

				config.Workspaces[0].SwitchTo(setForeground);

				temporarilyShownWindows.Clear();

				return true;
			}

			return false;
		}

		public void ToggleShowHideBar(Bar bar)
		{
			config.Workspaces[0].ToggleShowHideBar(bar);
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

			Window window;
			if ((window = applications.Values.Select(list => list.First.Value.Item2).
				FirstOrDefault(w => classNameRegex.IsMatch(w.className) && displayNameRegex.IsMatch(w.DisplayName) && processNameRegex.IsMatch(w.processName))) != null)
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

		// only switches to applications in the current workspace
		public bool SwitchToApplicationInCurrentWorkspace(IntPtr hWnd)
		{
			var window = config.Workspaces[0].GetOwnermostWindow(hWnd);
			if (window != null)
			{
				if (window.IsMinimized)
				{
					// OpenIcon does not restore the window to its previous size (e.g. maximized)
					NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW.SW_RESTORE);
				}

				ForceForegroundWindow(window);

				return true;
			}

			return false;
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

							System.Threading.Tasks.Task.Factory.StartNew(hWnd3Object =>
								{
									var hWnd3 = (IntPtr) hWnd3Object;
									Bitmap bitmap = null;
									try
									{
										int processId;
										NativeMethods.GetWindowThreadProcessId(hWnd3, out processId);
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
									PostAction(() => action(bitmap));
								}, hWnd2);
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

		public static void ExecuteOnWindowShown(IntPtr hWnd, Action<IntPtr> action)
		{
			if (NativeMethods.IsWindowVisible(hWnd))
			{
				action(hWnd);
			}
			else
			{
				onWindowShown[hWnd] = action;
			}
		}

		public void DismissTemporarilyShownWindow(IntPtr hWnd)
		{
			if (temporarilyShownWindows.Contains(hWnd))
			{
				hiddenApplications.Add(hWnd);
				applications[hWnd].First.Value.Item2.Hide();
				config.Workspaces[0].SetTopWindowAsForeground();
				temporarilyShownWindows.Remove(hWnd);
			}
		}

		#endregion

		#region Message Loop Stuff

		private void OnShellHookMessage(IntPtr wParam, IntPtr lParam)
		{
			switch ((NativeMethods.ShellEvents) wParam)
			{
				case NativeMethods.ShellEvents.HSHELL_WINDOWCREATED: // window created or restored from tray
					{
						Action<IntPtr> onWindowShownAction;
						LinkedList<Tuple<Workspace, Window>> list;
						if (!applications.TryGetValue(lParam, out list)) // if a new window has shown
						{
							AddWindowToWorkspace(lParam);
						}
						else if (!config.Workspaces[0].ContainsWindow(lParam)) // if a hidden window has shown
						{
							// there is a problem with some windows showing up when others are created.
							// how to reproduce: start BitComet 1.26 on some workspace, switch to another one
							// and start explorer.exe (the file manager)

							// another problem is that some windows continuously keep showing when hidden.
							// how to reproduce: TortoiseSVN. About box. Click check for updates. This window
							// keeps showing up when changing workspaces
							OnHiddenWindowShown(lParam, list);
						}
						else if (onWindowShown.TryGetValue(lParam, out onWindowShownAction))
						{
							onWindowShown.Remove(lParam);
							onWindowShownAction(lParam);
						}
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
							LinkedList<Tuple<Workspace, Window>> list;
							if (!applications.TryGetValue(lParam, out list)) // if a new window has shown
							{
								RefreshApplicationsHash();
							}
							else if (!config.Workspaces[0].ContainsWindow(lParam))
							{
								OnHiddenWindowShown(lParam, list);
							}
						}

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
					{
						LinkedList<Tuple<Workspace, Window>> list;
						if (applications.TryGetValue(lParam, out list))
						{
							DoWindowFlashing(list);
						}
						break;
					}
				case NativeMethods.ShellEvents.HSHELL_REDRAW: // window's taskbar button has changed
					{
						LinkedList<Tuple<Workspace, Window>> list;
						if (applications.TryGetValue(lParam, out list))
						{
							var text = NativeMethods.GetText(lParam);
							foreach (var t in list)
							{
								t.Item2.DisplayName = text;
								DoWindowTitleOrIconChanged(t.Item1, t.Item2, text);
							}
						}
						else
						{
							AddWindowToWorkspace(lParam);
						}
						break;
					}
				case NativeMethods.ShellEvents.HSHELL_WINDOWREPLACING:
					NativeMethods.PostMessage(HandleStatic, shellMessageNum,
						(UIntPtr) (uint) NativeMethods.ShellEvents.HSHELL_WINDOWCREATED, lParam);
					break;
				case NativeMethods.ShellEvents.HSHELL_WINDOWREPLACED:
					NativeMethods.PostMessage(HandleStatic, shellMessageNum,
						(UIntPtr) (uint) NativeMethods.ShellEvents.HSHELL_WINDOWDESTROYED, lParam);
					break;
			}
		}

		private bool inShellMessage;
		protected override void WndProc(ref Message m)
		{
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

				m.Result = IntPtr.Zero;
				return ;
			}
			if (m.Msg == postActionMessageNum)
			{
				postedActions.Dequeue()();
				return ;
			}
			if (m.Msg == NativeMethods.WM_HOTKEY && m.WParam == this.getForegroundPrivilageAtom)
			{
				var count = 0;
				while (!NativeMethods.SetForegroundWindow(forceForegroundWindow) && ++count < 5)
				{
				}

				NativeMethods.SetWindowPos(forceForegroundWindow, NativeMethods.HWND_TOP, 0, 0, 0, 0,
					NativeMethods.SWP.SWP_ASYNCWINDOWPOS | NativeMethods.SWP.SWP_NOMOVE |
					NativeMethods.SWP.SWP_NOOWNERZORDER | NativeMethods.SWP.SWP_NOSIZE);
				return ;
			}
			HandleMessageDelegate messageDelegate;
			if (messageHandlers.TryGetValue(m.Msg, out messageDelegate))
			{
				var res = false;
				foreach (HandleMessageDelegate handler in messageDelegate.GetInvocationList())
				{
					res |= handler(ref m);
				}

				if (res)
				{
					return ;
				}
			}

			base.WndProc(ref m);
		}

		#endregion
	}

	public sealed class HashMultiSet<T> : IEnumerable<T>
	{
		private readonly Dictionary<T, BoxedInt> set;
		private sealed class BoxedInt
		{
			public int i = 1;
		}

		public HashMultiSet(IEqualityComparer<T> comparer = null)
		{
			set = new Dictionary<T, BoxedInt>(comparer);
		}

		public AddResult Add(T item)
		{
			BoxedInt count;
			if (set.TryGetValue(item, out count))
			{
				count.i++;
				return AddResult.Added;
			}
			else
			{
				set[item] = new BoxedInt();
				return AddResult.AddedFirst;
			}
		}

		public AddResult AddUnique(T item)
		{
			if (set.ContainsKey(item))
			{
				return AddResult.AlreadyContained;
			}
			else
			{
				set[item] = new BoxedInt();
				return AddResult.AddedFirst;
			}
		}

		public RemoveResult Remove(T item)
		{
			BoxedInt count;
			if (set.TryGetValue(item, out count))
			{
				if (count.i == 1)
				{
					set.Remove(item);
					return RemoveResult.RemovedLast;
				}
				else
				{
					count.i--;
					return RemoveResult.Removed;
				}
			}

			return RemoveResult.NotFound;
		}

		public bool Contains(T item)
		{
			return set.ContainsKey(item);
		}

		public enum AddResult : byte
		{
			AddedFirst,
			Added,
			AlreadyContained
		}

		public enum RemoveResult : byte
		{
			NotFound,
			RemovedLast,
			Removed
		}

		#region IEnumerable<T> Members

		public IEnumerator<T> GetEnumerator()
		{
			return set.Keys.GetEnumerator();
		}

		#endregion

		#region IEnumerable Members

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			return set.Keys.GetEnumerator();
		}

		#endregion
	}
}
