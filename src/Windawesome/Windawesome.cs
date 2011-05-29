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
		private readonly Dictionary<IntPtr, IntPtr> ownerWindows; // owner -> owned
		private readonly uint shellMessageNum;
		private static readonly uint postActionMessageNum;
		private static readonly Queue<Action> postedActions;
		private static readonly Dictionary<int, HandleMessageDelegate> messageHandlers;
		private static readonly NativeMethods.NONCLIENTMETRICS originalNonClientMetrics;
		private static IntPtr onWindowShownHandle;
		private static Action<IntPtr> onWindowShownHandler;
		private readonly bool changedNonClientMetrics;
		private readonly bool finishedInitializing;
		private readonly IntPtr getForegroundPrivilageAtom;
		private static Tuple<NativeMethods.MOD, Keys> uniqueHotkey;
		private static IntPtr forceForegroundWindow;
		private readonly Rectangle[] originalWorkingArea;

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

		private bool ApplicationsTryGetValue(IntPtr hWnd, out LinkedList<Tuple<Workspace, Window>> list)
		{
			IntPtr owned;
			if (ownerWindows.TryGetValue(hWnd, out owned))
			{
				hWnd = owned;
			}
			return applications.TryGetValue(hWnd, out list);
		}

		public Workspace CurrentWorkspace
		{
			get { return config.Workspaces[0]; }
		}

		#region Windawesome Construction, Initialization and Destruction

		static Windawesome()
		{
			isAtLeastVista = Environment.OSVersion.Version.Major >= 6;
			isAtLeast7 = isAtLeastVista && Environment.OSVersion.Version.Minor >= 1;

			isRunningElevated = NativeMethods.IsUserAnAdmin();

			messageHandlers = new Dictionary<int, HandleMessageDelegate>(2);

			originalNonClientMetrics = NativeMethods.NONCLIENTMETRICS.GetNONCLIENTMETRICS();
			NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETNONCLIENTMETRICS, originalNonClientMetrics.cbSize,
				ref originalNonClientMetrics, 0);

			smallIconSize = SystemInformation.SmallIconSize;

			postedActions = new Queue<Action>(5);
			postActionMessageNum = NativeMethods.RegisterWindowMessage("POST_ACTION_MESSAGE");
		}

		internal Windawesome()
		{
			this.CreateHandle(new CreateParams { Caption = "", ClassName = "Message", Parent = (IntPtr) (-3) });

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
			ownerWindows = new Dictionary<IntPtr, IntPtr>(2);

			// add all windows to their respective workspace
			NativeMethods.EnumWindows((hWnd, _) => AddWindowToWorkspace(hWnd) || true, IntPtr.Zero);

			WindawesomeExiting += OnWindawesomeExiting;

			// add a handler for when the working area changes
			SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
			SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

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
			config.Workspaces.Skip(1).Where(ws => ws.id != config.StartingWorkspace).
				ForEach(ws => ws.GetWindows().ForEach(w => hiddenApplications.Add(w.hWnd)));
			config.Workspaces.Skip(1).ForEach(ws => ws.Initialize(ws.id == config.StartingWorkspace));

			// switches to the default starting workspace
			PostAction(() => config.Workspaces[0].SwitchTo());

			originalWorkingArea = new Rectangle[config.WorkspacesCount];
			PostAction(() =>
				{
					originalWorkingArea[0] = SystemInformation.WorkingArea;
					for (var i = 1; i < config.WorkspacesCount; i++)
					{
						originalWorkingArea[i] = originalWorkingArea[0];
					}
				});

			finishedInitializing = true;
		}

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

						config.Workspaces[0].OnWorkingAreaReset();

						originalWorkingArea[CurrentWorkspace.id - 1] = newWorkingArea;

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

			var newWorkingArea = SystemInformation.WorkingArea;

			for (var i = 0; i < config.WorkspacesCount; i++)
			{
				config.Workspaces[i + 1].OnScreenResolutionChanged(newWorkingArea);

				originalWorkingArea[i] = newWorkingArea;
			}
		}

		private void OnWindawesomeExiting()
		{
			SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
			SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;

			NativeMethods.UnregisterHotKey(this.Handle, (ushort) getForegroundPrivilageAtom);
			NativeMethods.GlobalDeleteAtom((ushort) getForegroundPrivilageAtom);

			// unregister shell hook
			NativeMethods.DeregisterShellHookWindow(HandleStatic);

			// roll back any changes to Windows
			config.Workspaces.Skip(1).ForEach(workspace => workspace.RevertToInitialValues());

			config.Plugins.ForEach(p => p.Dispose());
			config.Bars.ForEach(b => b.Dispose());

			if (changedNonClientMetrics)
			{
				var metrics = originalNonClientMetrics;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
					ref metrics, NativeMethods.SPIF_SENDCHANGE);
			}
		}

		private void FindWorkspaceBarsEquivalentClasses()
		{
			var setArray = new HashSet<Workspace.BarInfo>[config.WorkspacesCount];

			int i = 0, last = 0;
			foreach (var set in
				this.config.Workspaces.Skip(1).Select(workspace => new HashSet<Workspace.BarInfo>(workspace.bars)))
			{
				int j;
				for (j = 0; j < i; j++)
				{
					if (set.SetEquals(setArray[j]))
					{
						workspaceBarsEquivalentClasses[i] = workspaceBarsEquivalentClasses[j];
						break;
					}
				}
				if (j == i)
				{
					workspaceBarsEquivalentClasses[i] = ++last;
				}

				setArray[i++] = set;
			}
		}

		public void Quit()
		{
			WindawesomeExiting();
			this.DestroyHandle();
		}

		#endregion

		#region Helpers

		private bool AddWindowToWorkspace(IntPtr hWnd)
		{
			if (NativeMethods.IsAppWindow(hWnd))
			{
				var className = NativeMethods.GetWindowClassName(hWnd);
				var displayName = NativeMethods.GetText(hWnd);
				var style = NativeMethods.GetWindowStyleLongPtr(hWnd);
				var exStyle = NativeMethods.GetWindowExStyleLongPtr(hWnd);

				var programRule = config.ProgramRules.First(r => r.IsMatch(className, displayName, style, exStyle));
				if (!programRule.isManaged)
				{
					return false;
				}

				IEnumerable<ProgramRule.Rule> matchingRules = programRule.rules;

				if (finishedInitializing)
				{
					if (!programRule.switchToOnCreated && matchingRules.FirstOrDefault(r => r.workspace == 0 || r.workspace == config.Workspaces[0].id) == null)
					{
						hiddenApplications.Add(hWnd);
						NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW.SW_HIDE);
						config.Workspaces[0].SetForeground();
					}
					else
					{
						PostAction(() => SwitchToApplication(hWnd));
					}

					System.Threading.Thread.Sleep(programRule.windowCreatedDelay);
				}

				var workspacesCount = programRule.rules.Length;
				// matchingRules.workspaces could be { 0, 1 } and you could be at workspace 1.
				// Then, "hWnd" would be added twice if it were not for this check
				if (workspacesCount > 1 && matchingRules.FirstOrDefault(r => r.workspace == 0) != null &&
					matchingRules.FirstOrDefault(r => r.workspace == config.Workspaces[0].id) != null)
				{
					matchingRules = matchingRules.Where(r => r.workspace == 0);
				}

				var list = new LinkedList<Tuple<Workspace, Window>>();

				Window windowTemplate;
				if (programRule.handleOwnedWindows)
				{
					NativeMethods.EnumWindows((h, hOwner) =>
						{
							if (NativeMethods.IsWindowVisible(h) &&
								NativeMethods.GetWindow(h, NativeMethods.GW.GW_OWNER) == hOwner)
							{
								ownerWindows[hOwner] = h;
								return false;
							}
							return true;
						}, hWnd);

					IntPtr hOwned;
					if (!ownerWindows.TryGetValue(hWnd, out hOwned))
					{
						return false;
					}
					var classNameOwned = NativeMethods.GetWindowClassName(hOwned);
					var displayNameOwned = NativeMethods.GetText(hOwned);
					var styleOwned = NativeMethods.GetWindowStyleLongPtr(hOwned);
					var exStyleOwned = NativeMethods.GetWindowExStyleLongPtr(hOwned);
					windowTemplate = new Window(hOwned, classNameOwned, displayNameOwned, workspacesCount,
						Environment.Is64BitOperatingSystem && NativeMethods.Is64BitProcess(hWnd), styleOwned, exStyleOwned, hWnd);
					applications[hOwned] = list;
				}
				else
				{
					windowTemplate = new Window(hWnd, className, displayName, workspacesCount,
						Environment.Is64BitOperatingSystem && NativeMethods.Is64BitProcess(hWnd), style, exStyle, IntPtr.Zero);
					applications[hWnd] = list;
				}

				foreach (var rule in matchingRules)
				{
					var window = new Window(windowTemplate, false)
						{
							IsFloating = rule.isFloating,
							ShowInTabs = rule.showInTabs,
							Titlebar = rule.titlebar,
							InTaskbar = rule.inTaskbar,
							WindowBorders = rule.windowBorders,
							RedrawOnShow = rule.redrawOnShow
						};
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

		internal static void ForceForegroundWindow(IntPtr hWnd)
		{
			ExecuteOnWindowShown(hWnd, SendHotkey);
		}

		private static readonly NativeMethods.INPUT[] input = new NativeMethods.INPUT[18];
		// sends the uniqueHotkey combination without disrupting the currently pressed modifiers
		private static void SendHotkey(IntPtr hWnd)
		{
			uint i = 0;

			// press needed modifiers
			var shiftShouldBePressed = uniqueHotkey.Item1.HasFlag(NativeMethods.MOD.MOD_SHIFT);
			var leftShiftPressed = (NativeMethods.GetKeyState(Keys.LShiftKey) & 0x80) == 0x80;
			var rightShiftPressed = (NativeMethods.GetKeyState(Keys.RShiftKey) & 0x80) == 0x80;

			PressReleaseModifierKey(leftShiftPressed, rightShiftPressed, shiftShouldBePressed,
				Keys.ShiftKey, Keys.LShiftKey, Keys.RShiftKey, 0, NativeMethods.KEYEVENTF_KEYUP, ref i);

			var winShouldBePressed = uniqueHotkey.Item1.HasFlag(NativeMethods.MOD.MOD_WIN);
			var leftWinPressed = (NativeMethods.GetKeyState(Keys.LWin) & 0x80) == 0x80;
			var rightWinPressed = (NativeMethods.GetKeyState(Keys.RWin) & 0x80) == 0x80;

			PressReleaseModifierKey(leftWinPressed, rightWinPressed, winShouldBePressed,
				Keys.LWin, Keys.LWin, Keys.RWin, 0, NativeMethods.KEYEVENTF_KEYUP, ref i);

			var controlShouldBePressed = uniqueHotkey.Item1.HasFlag(NativeMethods.MOD.MOD_CONTROL);
			var leftControlPressed = (NativeMethods.GetKeyState(Keys.LControlKey) & 0x80) == 0x80;
			var rightControlPressed = (NativeMethods.GetKeyState(Keys.RControlKey) & 0x80) == 0x80;

			PressReleaseModifierKey(leftControlPressed, rightControlPressed, controlShouldBePressed,
				Keys.ControlKey, Keys.LControlKey, Keys.RControlKey, 0, NativeMethods.KEYEVENTF_KEYUP, ref i);

			var altShouldBePressed = uniqueHotkey.Item1.HasFlag(NativeMethods.MOD.MOD_ALT);
			var leftAltPressed = (NativeMethods.GetKeyState(Keys.LMenu) & 0x80) == 0x80;
			var rightAltPressed = (NativeMethods.GetKeyState(Keys.RMenu) & 0x80) == 0x80;

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

		public void ChangeApplicationToWorkspace(IntPtr hWnd, int workspace)
		{
			var newWorkspace = config.Workspaces[workspace];

			var window = this.CurrentWorkspace.GetWindow(hWnd);
			if (workspace != this.CurrentWorkspace.id && window != null && !newWorkspace.ContainsWindow(hWnd))
			{
				this.CurrentWorkspace.WindowDestroyed(window, false);
				newWorkspace.WindowCreated(window);

				var list = applications[window.hWnd];
				list.Remove(new Tuple<Workspace, Window>(this.CurrentWorkspace, window));
				list.AddFirst(new Tuple<Workspace, Window>(newWorkspace, window));

				SwitchToWorkspace(workspace);
			}
		}

		public void AddApplicationToWorkspace(IntPtr hWnd, int workspace)
		{
			var newWorkspace = config.Workspaces[workspace];

			var window = this.CurrentWorkspace.GetWindow(hWnd);
			if (workspace != this.CurrentWorkspace.id && window != null && !newWorkspace.ContainsWindow(hWnd))
			{
				var newWindow = new Window(window, true);

				newWorkspace.WindowCreated(newWindow);

				var list = applications[window.hWnd];
				list.AddFirst(new Tuple<Workspace, Window>(newWorkspace, newWindow));
				list.Where(t => ++t.Item2.WorkspacesCount == 2).ForEach(t => t.Item1.AddToSharedWindows(t.Item2));

				SwitchToWorkspace(workspace);
			}
		}

		public void RemoveApplicationFromCurrentWorkspace(IntPtr hWnd)
		{
			var window = this.CurrentWorkspace.GetWindow(hWnd);
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
			}
		}

		public void SwitchToWorkspace(int workspace, bool setForeground = true)
		{
			if (workspace != this.CurrentWorkspace.id)
			{
				this.CurrentWorkspace.GetWindows().ForEach(w => hiddenApplications.Add(w.hWnd));
				this.CurrentWorkspace.Unswitch();

				PreviousWorkspace = this.CurrentWorkspace.id;
				config.Workspaces[0] = config.Workspaces[workspace];

				config.Workspaces[0].SwitchTo(setForeground);
			}
		}

		public void ToggleShowHideBar(Bar bar)
		{
			config.Workspaces[0].ToggleShowHideBar(bar);
		}

		public void ToggleWindowFloating(IntPtr hWnd)
		{
			config.Workspaces[0].ToggleWindowFloating(hWnd);
		}

		public void ToggleShowHideWindowInTaskbar(IntPtr hWnd)
		{
			config.Workspaces[0].ToggleShowHideWindowInTaskbar(hWnd);
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
			IntPtr owned;
			if (ownerWindows.TryGetValue(hWnd, out owned))
			{
				hWnd = owned;
			}
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

		public void RunOrShowApplication(string className, string path, string displayName = ".*", string arguments = "")
		{
			var classNameRegex = new System.Text.RegularExpressions.Regex(className, System.Text.RegularExpressions.RegexOptions.Compiled);
			var displayNameRegex = new System.Text.RegularExpressions.Regex(displayName, System.Text.RegularExpressions.RegexOptions.Compiled);

			Window window;
			if ((window = applications.Values.Select(list => list.First.Value.Item2).
				FirstOrDefault(w => classNameRegex.IsMatch(w.className) && displayNameRegex.IsMatch(w.Caption))) != null)
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

		// only switches to applications in the current workspace
		public bool SwitchToApplicationInCurrentWorkspace(IntPtr hWnd)
		{
			var window = config.Workspaces[0].GetWindow(hWnd);
			if (window != null)
			{
				if (window.IsMinimized)
				{
					// OpenIcon does not restore the window to its previous size (e.g. maximized)
					NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW.SW_RESTORE);
				}

				ForceForegroundWindow(hWnd);

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
				onWindowShownHandle = hWnd;
				onWindowShownHandler = action;
			}
		}

		#endregion

		#region Message Loop Stuff

		private void OnShellHookMessage(IntPtr wParam, IntPtr lParam)
		{
			switch ((NativeMethods.ShellEvents) wParam)
			{
				case NativeMethods.ShellEvents.HSHELL_WINDOWCREATED: // window created or restored from tray
					if (!applications.ContainsKey(lParam))
					{
						AddWindowToWorkspace(lParam);
					}
					else if (!config.Workspaces[0].ContainsWindow(lParam))
					{
						// there is a problem with some windows showing up when others are created
						// how to reproduce: start BitComet 1.26 on some workspace, switch to another one
						// and start explorer.exe (the file manager)

						// another problem is that some windows continuously keep showing when hidden
						// how to reproduce: TortoiseSVN. About box. Click check for updates. This window
						// keeps showing up when changing workspaces
						NativeMethods.SendNotifyMessage(HandleStatic, shellMessageNum,
							(UIntPtr) (uint) NativeMethods.ShellEvents.HSHELL_WINDOWACTIVATED, lParam);
					}
					else if (lParam == onWindowShownHandle)
					{
						onWindowShownHandle = IntPtr.Zero;
						onWindowShownHandler(lParam);
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_WINDOWDESTROYED: // window destroyed or minimized to tray
					if (hiddenApplications.Remove(lParam) == HashMultiSet<IntPtr>.RemoveResult.NotFound)
					{
						IntPtr owned;
						if (ownerWindows.TryGetValue(lParam, out owned))
						{
							RemoveApplicationFromAllWorkspaces(owned);
							hiddenApplications.Remove(owned);
							ownerWindows.Remove(lParam);
						}
						else
						{
							RemoveApplicationFromAllWorkspaces(lParam);
						}
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_WINDOWACTIVATED: // window activated
				case NativeMethods.ShellEvents.HSHELL_RUDEAPPACTIVATED:
					if (!hiddenApplications.Contains(lParam))
					{
						if (lParam != IntPtr.Zero)
						{
							if (!applications.ContainsKey(lParam))
							{
								IntPtr owned;
								if (ownerWindows.TryGetValue(lParam, out owned))
								{
									lParam = owned;
								}
								else
								{
									RefreshApplicationsHash();
								}
							}
							else if (!config.Workspaces[0].ContainsWindow(lParam))
							{
								SwitchToApplication(lParam);
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
						if (ApplicationsTryGetValue(lParam, out list))
						{
							DoWindowFlashing(list);
						}
						break;
					}
				case NativeMethods.ShellEvents.HSHELL_REDRAW: // window's taskbar button has changed
					{
						LinkedList<Tuple<Workspace, Window>> list;
						if (ApplicationsTryGetValue(lParam, out list))
						{
							var text = NativeMethods.GetText(lParam);
							foreach (var t in list)
							{
								t.Item2.Caption = text;
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
				NativeMethods.SetForegroundWindow(forceForegroundWindow);
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
			Added
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
