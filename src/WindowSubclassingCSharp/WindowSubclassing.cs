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

		private static readonly uint START_WINDOW_PROC_MESSAGE;
		private static readonly uint STOP_WINDOW_PROC_MESSAGE;

		private class WindowEqualityComparer : IEqualityComparer<Window>
		{
			#region IEqualityComparer<Window> Members

			bool IEqualityComparer<Window>.Equals(Window x, Window y)
			{
				return x.hWnd == y.hWnd;
			}

			int IEqualityComparer<Window>.GetHashCode(Window obj)
			{
				return obj.hWnd.GetHashCode();
			}

			#endregion
		}

		static WindowSubclassing()
		{
			START_WINDOW_PROC_MESSAGE = NativeMethods.RegisterWindowMessage("START_WINDOW_PROC");
			STOP_WINDOW_PROC_MESSAGE = NativeMethods.RegisterWindowMessage("STOP_WINDOW_PROC");
		}

		public WindowSubclassing(IList<Tuple<string, string>> ignoredPrograms)
		{
			this.ignoredPrograms = ignoredPrograms.ToArray();
		}

		private bool IsMatch(string cName, string dName)
		{
			return ignoredPrograms.Any(t => Regex.IsMatch(cName, t.Item1) && Regex.IsMatch(dName, t.Item2));
		}

		private void Workspace_WorkspaceApplicationAdded(Workspace workspace, Window window)
		{
			if (workspace.layout.LayoutName() == "Tile" || workspace.layout.LayoutName() == "Full Screen")
			{
				if (!IsMatch(window.className, window.caption))
				{
					if (Environment.Is64BitProcess && window.is64BitProcess)
					{
						if (subclassedWindows[workspace.ID - 1].Add(window) == HashMultiSet<Window>.AddResult.ADDED_FIRST)
						{
							NativeMethods.SubclassWindow64(windawesome.Handle, window.hWnd);
						}
						if (workspace.isCurrentWorkspace)
						{
							NativeMethods.SendNotifyMessage(window.hWnd, START_WINDOW_PROC_MESSAGE, UIntPtr.Zero, IntPtr.Zero);
						}
					}
					else if (!Environment.Is64BitOperatingSystem)
					{
						if (subclassedWindows[workspace.ID - 1].Add(window) == HashMultiSet<Window>.AddResult.ADDED_FIRST)
						{
							NativeMethods.SubclassWindow32(windawesome.Handle, window.hWnd);
						}
						if (workspace.isCurrentWorkspace)
						{
							NativeMethods.SendNotifyMessage(window.hWnd, START_WINDOW_PROC_MESSAGE, UIntPtr.Zero, IntPtr.Zero);
						}
					}
				}
			}
		}

		private void Workspace_WorkspaceApplicationRemoved(Workspace workspace, Window window)
		{
			var result = subclassedWindows[workspace.ID - 1].Remove(window);
			if (result == HashMultiSet<Window>.RemoveResult.REMOVED_LAST)
			{
				NativeMethods.UnsubclassWindow(window.hWnd);
			}
			else if (result == HashMultiSet<Window>.RemoveResult.REMOVED)
			{
				NativeMethods.SendNotifyMessage(window.hWnd, STOP_WINDOW_PROC_MESSAGE, UIntPtr.Zero, IntPtr.Zero);
			}
		}

		private void Workspace_WorkspaceChangedFrom(Workspace workspace)
		{
			subclassedWindows[workspace.ID - 1].Where(w => w.workspacesCount > 1).ForEach(w =>
				NativeMethods.SendNotifyMessage(w.hWnd, STOP_WINDOW_PROC_MESSAGE, UIntPtr.Zero, IntPtr.Zero));
		}

		private void Workspace_WorkspaceChangedTo(Workspace workspace)
		{
			subclassedWindows[workspace.ID - 1].Where(w => w.workspacesCount > 1).ForEach(w =>
				NativeMethods.SendNotifyMessage(w.hWnd, START_WINDOW_PROC_MESSAGE, UIntPtr.Zero, IntPtr.Zero));
		}

		private void Workspace_WorkspaceLayoutChanged(Workspace workspace, ILayout oldLayout)
		{
			if (workspace.layout.LayoutName() != "Tile" && workspace.layout.LayoutName() != "Full Screen")
			{
				subclassedWindows[workspace.ID - 1].ForEach(w => Workspace_WorkspaceApplicationRemoved(workspace, w));
			}
		}

		//private void Listen(Window window)
		//{
		//    if (window.titlebar == State.SHOWN || window.windowBorders == State.SHOWN)
		//    {
		//        NativeMethods.SendNotifyMessage(window.hWnd, START_WINDOW_PROC_MESSAGE, UIntPtr.Zero, IntPtr.Zero);
		//    }
		//    else if (window.titlebar == State.HIDDEN && window.windowBorders == State.HIDDEN)
		//    {
		//        NativeMethods.SendNotifyMessage(window.hWnd, STOP_WINDOW_PROC_MESSAGE, UIntPtr.Zero, IntPtr.Zero);
		//    }
		//    else if (window.titlebar == State.AS_IS || window.windowBorders == State.AS_IS)
		//    {
		//        var style = NativeMethods.GetWindowStyleLongPtr(window.hWnd);

		//        if ((style & NativeMethods.WS.WS_CAPTION) != 0 || (style & NativeMethods.WS.WS_SIZEBOX) != 0)
		//        {
		//            NativeMethods.SendNotifyMessage(window.hWnd, START_WINDOW_PROC_MESSAGE, UIntPtr.Zero, IntPtr.Zero);
		//        }
		//    }
		//}

		#region IPlugin Members

		void IPlugin.InitializePlugin(Windawesome windawesome, Config config)
		{
			Workspace.WorkspaceApplicationAdded += Workspace_WorkspaceApplicationAdded;
			Workspace.WorkspaceApplicationRemoved += Workspace_WorkspaceApplicationRemoved;
			Workspace.WorkspaceChangedFrom += Workspace_WorkspaceChangedFrom;
			Workspace.WorkspaceChangedTo += Workspace_WorkspaceChangedTo;
			Workspace.WorkspaceLayoutChanged += Workspace_WorkspaceLayoutChanged;
			this.windawesome = windawesome;

			subclassedWindows = new HashMultiSet<Window>[config.workspacesCount];
			var equalityComparer = new WindowEqualityComparer();
			for (int i = 0; i < config.workspacesCount; i++)
			{
				subclassedWindows[i] = new HashMultiSet<Window>(equalityComparer);
			}
		}

		void IPlugin.Dispose()
		{
			subclassedWindows.ForEach(set => set.ForEach(w => NativeMethods.UnsubclassWindow(w.hWnd)));
		}

		#endregion
	}
}
