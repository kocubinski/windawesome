using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Windawesome
{
	public class WindowSubclassing : IPlugin
	{
		private readonly Tuple<string, string>[] ignoredPrograms; // (className, displayName)
		private Windawesome windawesome;
		private HashMultiSet<Window>[] subclassedWindows;

		private static readonly uint startWindowProcMessage;
		private static readonly uint stopWindowProcMessage;

		static WindowSubclassing()
		{
			// TODO: can use messages between WM_USER and 0x7FFF
			startWindowProcMessage = NativeMethods.RegisterWindowMessage("START_WINDOW_PROC");
			stopWindowProcMessage = NativeMethods.RegisterWindowMessage("STOP_WINDOW_PROC");
		}

		public WindowSubclassing(IEnumerable<Tuple<string, string>> ignoredPrograms)
		{
			this.ignoredPrograms = ignoredPrograms.ToArray();
		}

		private bool IsMatch(string cName, string dName)
		{
			return ignoredPrograms.Any(t => Regex.IsMatch(cName, t.Item1) && Regex.IsMatch(dName, t.Item2));
		}

		private void OnWorkspaceApplicationAdded(Workspace workspace, Window window)
		{
			if (workspace.Layout.LayoutName() == "Tile" || workspace.Layout.LayoutName() == "Full Screen")
			{
				if (!IsMatch(window.className, window.DisplayName))
				{
					if (Environment.Is64BitProcess && window.is64BitProcess)
					{
						if (subclassedWindows[workspace.id - 1].Add(window) == HashMultiSet<Window>.AddResult.AddedFirst)
						{
							NativeMethods.SubclassWindow64(windawesome.Handle, window.hWnd);
						}
						if (workspace.IsCurrentWorkspace)
						{
							NativeMethods.SendNotifyMessage(window.hWnd, startWindowProcMessage, UIntPtr.Zero, IntPtr.Zero);
						}
					}
					else if (!Environment.Is64BitOperatingSystem)
					{
						if (subclassedWindows[workspace.id - 1].Add(window) == HashMultiSet<Window>.AddResult.AddedFirst)
						{
							NativeMethods.SubclassWindow32(windawesome.Handle, window.hWnd);
						}
						if (workspace.IsCurrentWorkspace)
						{
							NativeMethods.SendNotifyMessage(window.hWnd, startWindowProcMessage, UIntPtr.Zero, IntPtr.Zero);
						}
					}
				}
			}
		}

		private void OnWorkspaceApplicationRemoved(Workspace workspace, Window window)
		{
			switch (subclassedWindows[workspace.id - 1].Remove(window))
			{
				case HashMultiSet<Window>.RemoveResult.RemovedLast:
					NativeMethods.UnsubclassWindow(window.hWnd);
					break;
				case HashMultiSet<Window>.RemoveResult.Removed:
					NativeMethods.SendNotifyMessage(window.hWnd, stopWindowProcMessage, UIntPtr.Zero, IntPtr.Zero);
					break;
			}
		}

		private void OnWorkspaceChangedFrom(Workspace workspace)
		{
			subclassedWindows[workspace.id - 1].Where(w => w.WorkspacesCount > 1).ForEach(w =>
				NativeMethods.SendNotifyMessage(w.hWnd, stopWindowProcMessage, UIntPtr.Zero, IntPtr.Zero));
		}

		private void OnWorkspaceChangedTo(Workspace workspace)
		{
			subclassedWindows[workspace.id - 1].Where(w => w.WorkspacesCount > 1).ForEach(w =>
				NativeMethods.SendNotifyMessage(w.hWnd, startWindowProcMessage, UIntPtr.Zero, IntPtr.Zero));
		}

		private void OnWorkspaceLayoutChanged(Workspace workspace, ILayout oldLayout)
		{
			if (workspace.Layout.LayoutName() != "Tile" && workspace.Layout.LayoutName() != "Full Screen")
			{
				subclassedWindows[workspace.id - 1].ForEach(w => OnWorkspaceApplicationRemoved(workspace, w));
			}
		}

		//private void Listen(Window window)
		//{
		//		if (window.titlebar == State.SHOWN || window.windowBorders == State.SHOWN)
		//		{
		//				NativeMethods.SendNotifyMessage(window.hWnd, START_WINDOW_PROC_MESSAGE, UIntPtr.Zero, IntPtr.Zero);
		//		}
		//		else if (window.titlebar == State.HIDDEN && window.windowBorders == State.HIDDEN)
		//		{
		//				NativeMethods.SendNotifyMessage(window.hWnd, STOP_WINDOW_PROC_MESSAGE, UIntPtr.Zero, IntPtr.Zero);
		//		}
		//		else if (window.titlebar == State.AS_IS || window.windowBorders == State.AS_IS)
		//		{
		//				var style = NativeMethods.GetWindowStyleLongPtr(window.hWnd);

		//				if ((style & NativeMethods.WS.WS_CAPTION) != 0 || (style & NativeMethods.WS.WS_SIZEBOX) != 0)
		//				{
		//						NativeMethods.SendNotifyMessage(window.hWnd, START_WINDOW_PROC_MESSAGE, UIntPtr.Zero, IntPtr.Zero);
		//				}
		//		}
		//}

		#region IPlugin Members

		void IPlugin.InitializePlugin(Windawesome windawesome, Config config)
		{
			Workspace.WorkspaceApplicationAdded += OnWorkspaceApplicationAdded;
			Workspace.WorkspaceApplicationRemoved += OnWorkspaceApplicationRemoved;
			//Workspace.WorkspaceChangedFrom += OnWorkspaceChangedFrom;
			//Workspace.WorkspaceChangedTo += OnWorkspaceChangedTo;
			Workspace.WorkspaceLayoutChanged += OnWorkspaceLayoutChanged;
			this.windawesome = windawesome;

			subclassedWindows = new HashMultiSet<Window>[config.Workspaces.Length];
			for (var i = 0; i < config.Workspaces.Length; i++)
			{
				subclassedWindows[i] = new HashMultiSet<Window>();
			}
		}

		void IPlugin.Dispose()
		{
			subclassedWindows.ForEach(set => set.ForEach(w => NativeMethods.UnsubclassWindow(w.hWnd)));
		}

		#endregion
	}
}
