using System.IO;

namespace Windawesome
{
	public class LoggerPlugin : IPlugin
	{
		private readonly bool logCreation;
		private readonly bool logDeletion;
		private readonly bool logWorkspaceSwitching;
		private readonly bool logWindowMinimization;
		private readonly bool logWindowRestoration;
		private readonly bool logActivation;
		private readonly StreamWriter writer;
		private Windawesome windawesome;

		public LoggerPlugin(string filename = "logplugin.txt", bool logCreation = true, bool logDeletion = true,
			bool logWorkspaceSwitching = false, bool logWindowMinimization = false, bool logWindowRestoration = false,
			bool logActivation = false)
		{
			this.logCreation = logCreation;
			this.logDeletion = logDeletion;
			this.logWorkspaceSwitching = logWorkspaceSwitching;
			this.logWindowMinimization = logWindowMinimization;
			this.logWindowRestoration = logWindowRestoration;
			this.logActivation = logActivation;
			writer = new StreamWriter(filename, true);
		}

		private void OnWorkspaceApplicationAdded(Workspace workspace, Window window)
		{
			writer.WriteLine("ADDED - class '{0}'; caption '{1}'; workspace '{2}'",
				window.className, window.Caption, workspace.id);
		}

		private void OnWorkspaceApplicationRemoved(Workspace workspace, Window window)
		{
			writer.WriteLine("REMOVED - class '{0}'; caption '{1}'; workspace '{2}'",
				window.className, window.Caption, workspace.id);
		}

		private void OnWorkspaceApplicationMinimized(Workspace workspace, Window window)
		{
			writer.WriteLine("MINIMIZED - class '{0}'; caption '{1}'; workspace '{2}'",
				window.className, window.Caption, workspace.id);
		}

		private void OnWorkspaceApplicationRestored(Workspace workspace, Window window)
		{
			writer.WriteLine("RESTORED - class '{0}'; caption '{1}'; workspace '{2}'",
				window.className, window.Caption, workspace.id);
		}

		private void OnWorkspaceChangedFrom(Workspace workspace)
		{
			writer.WriteLine("Changed from workspace '{0}'", workspace.id);
		}

		private void OnWorkspaceChangedTo(Workspace workspace)
		{
			writer.WriteLine("Changed to workspace '{0}'", workspace.id);
		}

		private void OnWindowActivatedEvent(System.IntPtr hWnd)
		{
			var window = windawesome.CurrentWorkspace.GetWindow(hWnd);
			if (window != null)
			{
				writer.WriteLine("ACTIVATED - class '{0}'; caption '{1}'; workspace '{2}'",
					window.className, window.Caption, windawesome.CurrentWorkspace.id);
			}
			else
			{
				writer.WriteLine("ACTIVATED - HWND '{0}'; caption '{1}'; workspace '{2}'",
					hWnd, NativeMethods.GetText(hWnd), windawesome.CurrentWorkspace.id);
			}
		}

		#region IPlugin Members

		void IPlugin.InitializePlugin(Windawesome windawesome, Config config)
		{
			this.windawesome = windawesome;

			if (logCreation)
			{
				Workspace.WorkspaceApplicationAdded += OnWorkspaceApplicationAdded;
			}
			if (logDeletion)
			{
				Workspace.WorkspaceApplicationRemoved += OnWorkspaceApplicationRemoved;
			}
			if (logWorkspaceSwitching)
			{
				Workspace.WorkspaceChangedFrom += OnWorkspaceChangedFrom;
				Workspace.WorkspaceChangedTo += OnWorkspaceChangedTo;
			}
			if (logWindowMinimization)
			{
				Workspace.WorkspaceApplicationMinimized += OnWorkspaceApplicationMinimized;
			}
			if (logWindowRestoration)
			{
				Workspace.WorkspaceApplicationRestored += OnWorkspaceApplicationRestored;
			}
			if (logActivation)
			{
				Workspace.WindowActivatedEvent += OnWindowActivatedEvent;
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
