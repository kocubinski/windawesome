using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Windawesome
{
	public partial class Windawesome : Form
	{
		private readonly Config config;
		private readonly Dictionary<IntPtr, LinkedList<Tuple<Workspace, Window>>> applications; // hWnd to a list of workspaces and windows
		private readonly MultiHashSet<IntPtr> hiddenApplications;
		private readonly uint shellMessageNum;
		private static readonly Dictionary<int, HandleMessageDelegate> messageHandlers;
		private static readonly NativeMethods.NONCLIENTMETRICS originalNonClientMetrics;
		private readonly bool changedNonClientMetrics = false;
		private readonly bool finishedInitializing = false;

		internal static int[] workspaceBarsEquivalentClasses;

		public delegate bool HandleMessageDelegate(ref Message m);

		public static int previousWorkspace { get; private set; }
		public static readonly bool isRunningElevated;
		public static readonly bool isAtLeastVista;
		public static readonly bool isAtLeast7;
		private static TaskScheduler taskScheduler;

		public static readonly Size smallIconSize;

		#region Events

		public delegate void LayoutUpdatedEventHandler();
		public static event LayoutUpdatedEventHandler LayoutUpdated;

		public delegate void WindowTitleOrIconChangedEventHandler(Workspace workspace, Window window, string newText);
		public static event WindowTitleOrIconChangedEventHandler WindowTitleOrIconChanged;

		public delegate void WindowFlashingEventHandler(LinkedList<Tuple<Workspace, Window>> list);
		public static event WindowFlashingEventHandler WindowFlashing;

		public static void OnLayoutUpdated()
		{
			if (LayoutUpdated != null)
			{
				LayoutUpdated();
			}
		}

		private static void OnWindowTitleOrIconChanged(Workspace workspace, Window window, string newText)
		{
			if (WindowTitleOrIconChanged != null)
			{
				WindowTitleOrIconChanged(workspace, window, newText);
			}
		}

		private static void OnWindowFlashing(LinkedList<Tuple<Workspace, Window>> list)
		{
			if (WindowFlashing != null)
			{
				WindowFlashing(list);
			}
		}

		#endregion

		public Workspace CurrentWorkspace
		{
			get { return config.workspaces[0]; }
		}

		#region Windawesome Construction, Initialization and Destruction

		static Windawesome()
		{
			isAtLeastVista = Environment.OSVersion.Version.Major >= 6;
			isAtLeast7 = isAtLeastVista && Environment.OSVersion.Version.Minor >= 1;

			isRunningElevated = NativeMethods.IsUserAnAdmin();

			messageHandlers = new Dictionary<int, HandleMessageDelegate>();

			originalNonClientMetrics = NativeMethods.NONCLIENTMETRICS.GetNONCLIENTMETRICS();
			NativeMethods.SystemParametersInfo(NativeMethods.SPI_GETNONCLIENTMETRICS, originalNonClientMetrics.cbSize,
				ref originalNonClientMetrics, 0);

			smallIconSize = SystemInformation.SmallIconSize;
		}
		
		internal Windawesome()
		{
			taskScheduler = TaskScheduler.FromCurrentSynchronizationContext();

			config = new Config();
			config.LoadPlugins(this);
			config.workspaces = config.workspaces.Resize(config.workspaces.Length + 1);
			config.workspaces[0] = config.workspaces[config.startingWorkspace];
			previousWorkspace = config.workspaces[0].ID;
			config.bars.ForEach(b => b.InitializeBar(this, config));
			config.plugins.ForEach(p => p.InitializePlugin(this, config));

			workspaceBarsEquivalentClasses = new int[config.workspacesCount];
			FindWorkspaceBarsEquivalentClasses();
			
			applications = new Dictionary<IntPtr, LinkedList<Tuple<Workspace, Window>>>(30);
			hiddenApplications = new MultiHashSet<IntPtr>();

			// add all windows to their respective workspace
			NativeMethods.EnumWindows((hWnd, _) => AddWindowToWorkspace(hWnd) || true, IntPtr.Zero);

			// initialize all workspaces
			config.workspaces.Skip(1).ForEach(ws => ws.Initialize(ws.ID == config.startingWorkspace));
			
			// switches to the default starting workspace
			config.workspaces[0].SwitchTo();

			this.FormClosed += Windawesome_FormClosed;

			// add a handler for when the working area changes
			Microsoft.Win32.SystemEvents.UserPreferenceChanged += SystemEvents_UserPreferenceChanged;

			// set the global border and padded border widths
			var metrics = originalNonClientMetrics;
			if (config.borderWidth >= 0 && metrics.iBorderWidth != config.borderWidth)
			{
				metrics.iBorderWidth = config.borderWidth;
#if !DEBUG
				changedNonClientMetrics = true;
#endif
			}
			if (isAtLeastVista && config.paddedBorderWidth >= 0 && metrics.iPaddedBorderWidth != config.paddedBorderWidth)
			{
				metrics.iPaddedBorderWidth = config.paddedBorderWidth;
#if !DEBUG
				changedNonClientMetrics = true;
#endif
			}
			if (changedNonClientMetrics)
			{
				NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
					ref metrics, NativeMethods.SPIF_SENDCHANGE);
			}

			// register a shell hook
			NativeMethods.RegisterShellHookWindow(this.Handle);
			shellMessageNum = NativeMethods.RegisterWindowMessage("SHELLHOOK");

			finishedInitializing = true;
		}

		private void SystemEvents_UserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
		{
			if (e.Category == Microsoft.Win32.UserPreferenceCategory.Desktop)
			{
				if (SystemInformation.WorkingArea != config.workspaces[0].workingArea)
				{
					// because Windows resets the working area when the UAC prompt is shown,
					// as well as shows the taskbar when a full-screen application is exited... twice. :)

					// how to reproduce: start any program that triggers a UAC prompt or start
					// IrfanView 4.25 with some picture, enter full-screen with "Return" and then exit
					// with "Return" again
					this.BeginInvoke((Action) (() => config.workspaces[0].ShowHideWindowsTaskbar()));
					this.BeginInvoke((Action) (() => config.workspaces[0].ShowHideWindowsTaskbar()));
				}
			}
		}

		private void Windawesome_FormClosed(object sender, FormClosedEventArgs e)
		{
			Microsoft.Win32.SystemEvents.UserPreferenceChanged -= SystemEvents_UserPreferenceChanged;

			// unregister keyboard hook
			KeyboardHook.Dispose();

			// unregister shell hook
			NativeMethods.DeregisterShellHookWindow(this.Handle);

			// roll back any changes to Windows
			config.workspaces.Skip(1).ForEach(workspace => workspace.RevertToInitialValues());

			config.plugins.ForEach(p => p.Dispose());
			config.bars.ForEach(b => b.Dispose());

			if (changedNonClientMetrics)
			{
				var metrics = originalNonClientMetrics;
				NativeMethods.SystemParametersInfo(NativeMethods.SPI_SETNONCLIENTMETRICS, metrics.cbSize,
					ref metrics, NativeMethods.SPIF_SENDCHANGE);
			}
		}

		private void FindWorkspaceBarsEquivalentClasses()
		{
			HashSet<Bar>[] setArray = new HashSet<Bar>[config.workspacesCount];
			Dictionary<Bar, bool>[] dictArray = new Dictionary<Bar, bool>[config.workspacesCount];

			int i = 0, last = 0;
			foreach (var workspace in config.workspaces.Skip(1))
			{
				HashSet<Bar> set = new HashSet<Bar>(workspace.bars);
				Dictionary<Bar, bool> dict = new Dictionary<Bar, bool>(workspace.bars.Length);
				for (int m = 0; m < workspace.bars.Length; m++)
				{
					dict.Add(workspace.bars[m], workspace.topBars[m]);
				}

				int j;
				for (j = 0; j < i; j++)
				{
					if (set.SetEquals(setArray[j]) && workspace.bars.All(bar => dict[bar] == dictArray[j][bar]))
					{
						workspaceBarsEquivalentClasses[i] = workspaceBarsEquivalentClasses[j];
						break;
					}
				}
				if (j == i)
				{
					workspaceBarsEquivalentClasses[i] = ++last;
				}

				dictArray[i] = dict;
				setArray[i++] = set;
			}
		}

		public void Quit()
		{
			this.Close();
		}

		#endregion

		private bool AddWindowToWorkspace(IntPtr hWnd)
		{
			if (NativeMethods.IsAppWindow(hWnd))
			{
				string className = NativeMethods.GetWindowClassName(hWnd);
				string displayName = NativeMethods.GetText(hWnd);

				var programRule = config.programRules.First(r => r.IsMatch(className, displayName));
				if (!programRule.isManaged)
				{
					return false;
				}

				IEnumerable<ProgramRule.Rule> matchingRules = programRule.rules;
				var workspacesCount = programRule.rules.Length;
				// matchingRules.workspaces could be { 0, 1 } and you could be at workspace 1.
				// Then, "hWnd" would be added twice if it were not for this check
				if (workspacesCount > 1 && matchingRules.FirstOrDefault(r => r.workspace == 0) != null &&
					matchingRules.FirstOrDefault(r => r.workspace == config.workspaces[0].ID) != null)
				{
					matchingRules = matchingRules.Where(r => r.workspace == 0);
				}

				var list = new LinkedList<Tuple<Workspace, Window>>();
				applications[hWnd] = list;

				if (finishedInitializing)
				{
					if (programRule.switchToOnCreated)
					{
						this.BeginInvoke((Action) (() => SwitchToApplication(hWnd)));
					}
					else
					{
						if (matchingRules.FirstOrDefault(r => r.workspace == 0 || r.workspace == config.workspaces[0].ID) == null)
						{
							hiddenApplications.Add(hWnd);
							NativeMethods.ShowWindowAsync(hWnd, NativeMethods.SW.SW_HIDE);
						}
						config.workspaces[0].SetForeground();
					}

					System.Threading.Thread.Sleep(programRule.windowCreatedDelay);
				}

				var windowTemplate = new Window(hWnd, className, displayName, workspacesCount,
					Environment.Is64BitOperatingSystem && NativeMethods.Is64BitProcess(hWnd));

				foreach (var rule in matchingRules)
				{
					var window = new Window(windowTemplate, false)
						{
							isFloating = rule.isFloating,
							showInTabs = rule.showInTabs,
							titlebar = rule.titlebar,
							inTaskbar = rule.inTaskbar,
							windowBorders = rule.windowBorders
						};
					list.AddLast(new Tuple<Workspace, Window>(config.workspaces[rule.workspace], window));
					config.workspaces[rule.workspace].WindowCreated(window);
				}

				return true;
			}

			return false;
		}

		#region API

		public void RefreshWindawesome()
		{
			HashSet<IntPtr> set = new HashSet<IntPtr>();

			NativeMethods.EnumWindows((hWnd, _) =>
				{
					set.Add(hWnd);
					if (NativeMethods.IsAppWindow(hWnd) && !applications.ContainsKey(hWnd))
					{
						// add any application that was not added for some reason when it was created
						AddWindowToWorkspace(hWnd);
					}
					return true;
				}, IntPtr.Zero);

			// remove all non-existent applications
			applications.Where(t => !set.Contains(t.Key)).ForEach(app =>
				app.Value.ForEach(t => t.Item1.WindowDestroyed(t.Item2, false)));

			// repositions all windows in all workspaces
			config.workspaces.Skip(1).Where(ws => !ws.isCurrentWorkspace).ForEach(ws => ws.hasChanges = true);
			config.workspaces[0].Reposition();
		}

		public void ChangeApplicationToWorkspace(IntPtr hWnd, int workspace)
		{
			var currentWorkspace = config.workspaces[0];
			var newWorkspace = config.workspaces[workspace];

			var window = currentWorkspace.GetWindow(hWnd);
			if (workspace != currentWorkspace.ID && window != null && !newWorkspace.ContainsWindow(hWnd))
			{
				currentWorkspace.WindowDestroyed(window, false);
				newWorkspace.WindowCreated(window);

				var list = applications[hWnd];
				list.Remove(new Tuple<Workspace, Window>(currentWorkspace, window));
				list.AddFirst(new Tuple<Workspace, Window>(newWorkspace, window));

				SwitchToWorkspace(workspace);
			}
		}

		public void AddApplicationToWorkspace(IntPtr hWnd, int workspace)
		{
			var currentWorkspace = config.workspaces[0];
			var newWorkspace = config.workspaces[workspace];

			var window = currentWorkspace.GetWindow(hWnd);
			if (workspace != currentWorkspace.ID && window != null && !newWorkspace.ContainsWindow(hWnd))
			{
				var newWindow = new Window(window, true);

				newWorkspace.WindowCreated(newWindow);

				var list = applications[hWnd];
				list.AddFirst(new Tuple<Workspace, Window>(newWorkspace, newWindow));
				list.Where(t => ++t.Item2.workspacesCount == 2).ForEach(t => t.Item1.AddToSharedWindows(t.Item2));

				SwitchToWorkspace(workspace);
			}
		}

		public void RemoveApplicationFromCurrentWorkspace(IntPtr hWnd)
		{
			var currentWorkspace = config.workspaces[0];

			var window = currentWorkspace.GetWindow(hWnd);
			if (window != null)
			{
				if (window.workspacesCount == 1)
				{
					QuitApplication(hWnd);
				}
				else
				{
					hiddenApplications.Add(window.hWnd);
					window.Hide();

					var list = applications[hWnd];
					list.Remove(new Tuple<Workspace, Window>(currentWorkspace, window));
					list.Where(t => --t.Item2.workspacesCount == 1).ForEach(t => t.Item1.AddToRemovedSharedWindows(t.Item2));

					currentWorkspace.WindowDestroyed(window);
				}
			}
		}

		public void SwitchToWorkspace(int workspace, bool setForeground = true)
		{
			var currentWorkspace = config.workspaces[0];

			if (workspace != currentWorkspace.ID)
			{
				currentWorkspace.GetWindows().ForEach(w => hiddenApplications.Add(w.hWnd));
				currentWorkspace.Unswitch();

				previousWorkspace = currentWorkspace.ID;
				config.workspaces[0] = config.workspaces[workspace];

				config.workspaces[0].SwitchTo(setForeground);
			}
		}

		public void ToggleShowHideBar(Bar bar)
		{
			config.workspaces[0].ToggleShowHideBar(bar);
		}

		public void ToggleWindowFloating(IntPtr hWnd)
		{
			config.workspaces[0].ToggleWindowFloating(hWnd);
		}

		public void ToggleShowHideWindowInTaskbar(IntPtr hWnd)
		{
			config.workspaces[0].ToggleShowHideWindowInTaskbar(hWnd);
		}

		public void ToggleShowHideWindowTitlebar(IntPtr hWnd)
		{
			config.workspaces[0].ToggleShowHideWindowTitlebar(hWnd);
		}

		public void ToggleShowHideWindowBorder(IntPtr hWnd)
		{
			config.workspaces[0].ToggleShowHideWindowBorder(hWnd);
		}

		public void ToggleTaskbarVisibility()
		{
			config.workspaces[0].ToggleWindowsTaskbarVisibility();
		}

		public void SwitchToApplication(IntPtr hWnd)
		{
			if (!SwitchToApplicationInCurrentWorkspace(hWnd))
			{
				LinkedList<Tuple<Workspace, Window>> list;
				if (applications.TryGetValue(hWnd, out list))
				{
					SwitchToWorkspace(list.First.Value.Item1.ID, false);
					SwitchToApplicationInCurrentWorkspace(hWnd);
				}
			}
		}

		public void RunApplication(string path, string arguments = "")
		{
			if (isRunningElevated)
			{
				this.BeginInvoke((Action) (() => NativeMethods.RunApplicationNonElevated(path, arguments)));
			}
			else
			{
				System.Diagnostics.Process.Start(path, arguments);
			}
		}

		public void RunOrShowApplication(string className, string path, string displayName = null, string arguments = "")
		{
			var application = NativeMethods.FindWindow(className, displayName);
			if (application == IntPtr.Zero)
			{
				RunApplication(path, arguments);
			}
			else
			{
				SwitchToApplication(application);
			}
		}

		public void QuitApplication(IntPtr hWnd)
		{
			NativeMethods.SendNotifyMessage(hWnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_CLOSE, IntPtr.Zero);
		}

		public void MinimizeApplication(IntPtr hWnd)
		{
			// CloseWindow does not activate the next window in the Z order
			NativeMethods.PostMessage(hWnd, NativeMethods.WM_SYSCOMMAND, NativeMethods.SC_MINIMIZEU, IntPtr.Zero);
		}

		// only switches to applications in the current workspace
		public bool SwitchToApplicationInCurrentWorkspace(IntPtr hWnd)
		{
			var window = config.workspaces[0].GetWindow(hWnd);
			if (window != null)
			{
				if (window.isMinimized)
				{
					NativeMethods.OpenIcon(hWnd);
				}

				NativeMethods.ForceForegroundWindow(hWnd);

				return true;
			}

			return false;
		}

		public static void RegisterMessage(int message, HandleMessageDelegate targetHandler)
		{
			HandleMessageDelegate handlers;
			if (messageHandlers.TryGetValue(message, out handlers))
			{
				handlers += targetHandler;
			}
			else
			{
				messageHandlers[message] = targetHandler;
			}
		}

		public static Bitmap GetWindowSmallIconAsBitmap(IntPtr hWnd)
		{
			IntPtr hIcon;
			Bitmap bitmap = null;

			NativeMethods.SendMessageTimeout(
				hWnd,
				NativeMethods.WM_GETICON,
				NativeMethods.ICON_SMALL,
				IntPtr.Zero,
				NativeMethods.SMTO.SMTO_BLOCK | NativeMethods.SMTO.SMTO_ABORTIFHUNG,
				1000, out hIcon);			

			if (hIcon == IntPtr.Zero)
			{
				NativeMethods.SendMessageTimeout(
					hWnd,
					NativeMethods.WM_QUERYDRAGICON,
					UIntPtr.Zero,
					IntPtr.Zero,
					NativeMethods.SMTO.SMTO_BLOCK | NativeMethods.SMTO.SMTO_ABORTIFHUNG,
					1000, out hIcon);

				if (hIcon == IntPtr.Zero)
				{
					hIcon = NativeMethods.GetClassLongPtr(hWnd, NativeMethods.GCL_HICONSM);

					if (hIcon == IntPtr.Zero)
					{
						try
						{
							int processID;
							NativeMethods.GetWindowThreadProcessId(hWnd, out processID);
							var process = System.Diagnostics.Process.GetProcessById(processID);

							var info = new NativeMethods.SHFILEINFO();
							NativeMethods.SHGetFileInfo(process.MainModule.FileName, 0, ref info,
								Marshal.SizeOf(info), NativeMethods.SHGFI_ICON | NativeMethods.SHGFI_SMALLICON);

							if (info.hIcon != IntPtr.Zero)
							{
								bitmap = new Bitmap(Bitmap.FromHicon(info.hIcon), smallIconSize);
								NativeMethods.DestroyIcon(info.hIcon);
								return bitmap;
							}

							bitmap = Icon.ExtractAssociatedIcon(process.MainModule.FileName).ToBitmap();
							return bitmap != null ? new Bitmap(bitmap, smallIconSize) : null;
						}
						catch
						{
						}
						return null;
					}
				}
			}

			try
			{
				bitmap = new Bitmap(Bitmap.FromHicon(hIcon), smallIconSize);
			}
			catch
			{
			}
			return bitmap;
		}

		public static void RunTask(Action action)
		{
			Task.Factory.StartNew(action, System.Threading.CancellationToken.None, TaskCreationOptions.None, taskScheduler);
		}

		#endregion

		#region Message Loop Stuff

		internal void OnShellHookMessage(IntPtr wParam, IntPtr lParam)
		{
			switch ((NativeMethods.ShellEvents) wParam)
			{
				case NativeMethods.ShellEvents.HSHELL_WINDOWCREATED: // window created or restored from tray
					{
						LinkedList<Tuple<Workspace, Window>> list;
						if (applications.TryGetValue(lParam, out list))
						{
							if (!config.workspaces[0].ContainsWindow(lParam))
							{
								// because sometimes Windows shows random windows on some window's creation...

								// how to reproduce: start BitComet 1.26 on some workspace, switch to another one
								// and start explorer.exe (the file manager)
								hiddenApplications.Add(list.First.Value.Item2.hWnd);
								list.First.Value.Item2.Hide();
							}
						}
						else
						{
							AddWindowToWorkspace(lParam);
						}
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_WINDOWDESTROYED: // window destroyed or minimized to tray
					if (hiddenApplications.Remove(lParam) == MultiHashSet<IntPtr>.RemoveResult.NOT_FOUND)
					{
						// remove window from all workspaces
						LinkedList<Tuple<Workspace, Window>> list;
						if (applications.TryGetValue(lParam, out list))
						{
							list.ForEach(tuple => tuple.Item1.WindowDestroyed(tuple.Item2));
							applications.Remove(lParam);
						}
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_WINDOWACTIVATED: // window activated
				case NativeMethods.ShellEvents.HSHELL_RUDEAPPACTIVATED:
					//if (!hiddenApplications.Contains(lParam))
					{
						if (lParam != IntPtr.Zero)
						{
							if (!applications.ContainsKey(lParam))
							{
								AddWindowToWorkspace(lParam);
							}
							else if (!config.workspaces[0].ContainsWindow(lParam))
							{
								SwitchToApplication(lParam);
							}
						}

						config.workspaces[0].WindowActivated(lParam);
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_GETMINRECT: // window minimized or restored
					System.Threading.Thread.Sleep(Workspace.minimizeRestoreDelay);
					var hWnd = Marshal.ReadIntPtr(lParam);
					if (NativeMethods.IsIconic(hWnd))
					{
						config.workspaces[0].WindowMinimized(hWnd);
					}
					else
					{
						config.workspaces[0].WindowRestored(hWnd);
					}
					break;
				case NativeMethods.ShellEvents.HSHELL_FLASH: // window flashing in taskbar
					{
						LinkedList<Tuple<Workspace, Window>> list;
						if (applications.TryGetValue(lParam, out list))
						{
							OnWindowFlashing(list);
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
								t.Item2.caption = text;
								OnWindowTitleOrIconChanged(t.Item1, t.Item2, text);
							}
						}
						//else
						//{
						//    AddWindowToWorkspace(lParam);
						//}
						break;
					}
				case NativeMethods.ShellEvents.HSHELL_WINDOWREPLACING:
					NativeMethods.PostMessage(this.Handle, shellMessageNum,
						(UIntPtr) NativeMethods.ShellEvents.HSHELL_WINDOWCREATED, lParam);
					break;
				case NativeMethods.ShellEvents.HSHELL_WINDOWREPLACED:
					NativeMethods.PostMessage(this.Handle, shellMessageNum,
						(UIntPtr) NativeMethods.ShellEvents.HSHELL_WINDOWDESTROYED, lParam);
					break;
			}
		}

		private bool inShellMessage = false;
		protected override void WndProc(ref System.Windows.Forms.Message m)
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

				m.Result = IntPtr.Zero;
				return ;
			}
			else if (messageHandlers.TryGetValue(m.Msg, out messageDelegate))
			{
				bool res = false;
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

	public class MultiHashSet<T> : IEnumerable<T>
	{
		private Dictionary<T, BoxedInt> set;
		private sealed class BoxedInt
		{
			public int i;

			public BoxedInt()
			{
				this.i = 1;
			}
		}

		public MultiHashSet(IEqualityComparer<T> comparer = null)
		{
			set = new Dictionary<T, BoxedInt>(comparer);
		}

		public AddResult Add(T item)
		{
			BoxedInt count;
			if (set.TryGetValue(item, out count))
			{
				count.i++;
				return AddResult.ADDED;
			}
			else
			{
				set[item] = new BoxedInt();
				return AddResult.ADDED_FIRST;
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
					return RemoveResult.REMOVED_LAST;
				}
				else
				{
					count.i--;
					return RemoveResult.REMOVED;
				}
			}

			return RemoveResult.NOT_FOUND;
		}

		public bool Contains(T item)
		{
			return set.ContainsKey(item);
		}

		public enum AddResult : byte
		{
			ADDED_FIRST,
			ADDED
		}

		public enum RemoveResult : byte
		{
			NOT_FOUND,
			REMOVED_LAST,
			REMOVED
		}

		#region IEnumerable<T> Members

		public IEnumerator<T> GetEnumerator()
		{
			foreach (var key in set.Keys)
			{
				yield return key;
			}
		}

		#endregion

		#region IEnumerable Members

		System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
		{
			foreach (var key in set.Keys)
			{
				yield return key;
			}
		}

		#endregion
	}
}
