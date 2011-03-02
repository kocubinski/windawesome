using System.IO;

namespace Windawesome
{
	public class LoggerPlugin : IPlugin
	{
		private bool logCreation;
		private bool logDeletion;
		private bool logWorkspaceSwitching;
		private bool logWindowMinimization;
		private bool logWindowRestoration;
		private bool logActivation;
		private string filename;
		private StreamWriter writer;
		private Windawesome windawesome;

		public LoggerPlugin(string filename = "logplugin.txt", bool logCreation = true, bool logDeletion = true,
			bool logWorkspaceSwitching = false, bool logWindowMinimization = false, bool logWindowRestoration = false,
			bool logActivation = false)
		{
			this.filename = filename;
			this.logCreation = logCreation;
			this.logDeletion = logDeletion;
			this.logWorkspaceSwitching = logWorkspaceSwitching;
			this.logWindowMinimization = logWindowMinimization;
			this.logWindowRestoration = logWindowRestoration;
			this.logActivation = logActivation;
			writer = new StreamWriter(filename, true);
		}

		private void Workspace_WorkspaceApplicationAdded(Workspace workspace, Window window)
		{
			writer.WriteLine("ADDED - class '{0}'; caption '{1}'; workspace '{2}'",
				window.className, window.caption, workspace.ID);
		}

		private void Workspace_WorkspaceApplicationRemoved(Workspace workspace, Window window)
		{
			writer.WriteLine("REMOVED - class '{0}'; caption '{1}'; workspace '{2}'",
				window.className, window.caption, workspace.ID);
		}

		private void Workspace_WorkspaceApplicationMinimized(Workspace workspace, Window window)
		{
			writer.WriteLine("MINIMIZED - class '{0}'; caption '{1}'; workspace '{2}'",
				window.className, window.caption, workspace.ID);
		}

		private void Workspace_WorkspaceApplicationRestored(Workspace workspace, Window window)
		{
			writer.WriteLine("RESTORED - class '{0}'; caption '{1}'; workspace '{2}'",
				window.className, window.caption, workspace.ID);
		}

		private void Workspace_WorkspaceChangedFrom(Workspace workspace)
		{
			writer.WriteLine("Changed from workspace '{0}'", workspace.ID);
		}

		private void Workspace_WorkspaceChangedTo(Workspace workspace)
		{
			writer.WriteLine("Changed to workspace '{0}'", workspace.ID);
		}

		private void Workspace_WindowActivatedEvent(System.IntPtr hWnd)
		{
			var window = windawesome.CurrentWorkspace.GetWindow(hWnd);
			if (window != null)
			{
				writer.WriteLine("ACTIVATED - class '{0}'; caption '{1}'; workspace '{2}'",
					window.className, window.caption, windawesome.CurrentWorkspace.ID);
			}
			else
			{
				writer.WriteLine("ACTIVATED - HWND '{0}'; caption '{1}'; workspace '{2}'",
					hWnd, NativeMethods.GetText(hWnd), windawesome.CurrentWorkspace.ID);
			}
		}

		#region IPlugin Members

		void IPlugin.InitializePlugin(Windawesome windawesome, Config config)
		{
			this.windawesome = windawesome;

			if (logCreation)
			{
				Workspace.WorkspaceApplicationAdded += Workspace_WorkspaceApplicationAdded;
			}
			if (logDeletion)
			{
				Workspace.WorkspaceApplicationRemoved += Workspace_WorkspaceApplicationRemoved;
			}
			if (logWorkspaceSwitching)
			{
				Workspace.WorkspaceChangedFrom += Workspace_WorkspaceChangedFrom;
				Workspace.WorkspaceChangedTo += Workspace_WorkspaceChangedTo;
			}
			if (logWindowMinimization)
			{
				Workspace.WorkspaceApplicationMinimized += Workspace_WorkspaceApplicationMinimized;
			}
			if (logWindowRestoration)
			{
				Workspace.WorkspaceApplicationRestored += Workspace_WorkspaceApplicationRestored;
			}
			if (logActivation)
			{
				Workspace.WindowActivatedEvent += Workspace_WindowActivatedEvent;
			}
		}

		void IPlugin.Dispose()
		{
			writer.WriteLine("==========================================");
			writer.Close();
		}

		#endregion
	}
}
