using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public class WorkspacesWidget : IFixedWidthWidget
	{
		private static Windawesome windawesome;
		private static Config config;
		private Label[] workspaceLabels;
		private readonly Color[] normalForegroundColor;
		private readonly Color[] normalBackgroundColor;
		private readonly Color highlightedForegroundColor;
		private readonly Color highlightedBackgroundColor;
		private readonly Color flashingForegroundColor;
		private readonly Color flashingBackgroundColor;
		private int left, right;
		private bool isLeft;
		private bool isShown;
		private readonly bool flashWorkspaces;
		private readonly Dictionary<IntPtr, Workspace> flashingWindows;
		private static Timer flashTimer;
		private static HashSet<Workspace> flashingWorkspaces;

		public WorkspacesWidget(Color[] normalForegroundColor = null, Color[] normalBackgroundColor = null,
			Color? highlightedForegroundColor = null, Color? highlightedBackgroundColor = null,
			Color? flashingForegroundColor = null, Color? flashingBackgroundColor = null, bool flashWorkspaces = true)
		{
			this.normalForegroundColor = normalForegroundColor ?? new[]
				{
					Color.FromArgb(0x00, 0x00, 0x00),
					Color.FromArgb(0x00, 0x00, 0x00),
					Color.FromArgb(0x00, 0x00, 0x00),
					Color.FromArgb(0x00, 0x00, 0x00),
					Color.FromArgb(0x00, 0x00, 0x00),
					Color.FromArgb(0xFF, 0xFF, 0xFF),
					Color.FromArgb(0xFF, 0xFF, 0xFF),
					Color.FromArgb(0xFF, 0xFF, 0xFF),
					Color.FromArgb(0xFF, 0xFF, 0xFF),
					Color.FromArgb(0xFF, 0xFF, 0xFF),
				};
			this.normalBackgroundColor = normalBackgroundColor ?? new[]
				{
					Color.FromArgb(0xF0, 0xF0, 0xF0),
					Color.FromArgb(0xD8, 0xD8, 0xD8),
					Color.FromArgb(0xC0, 0xC0, 0xC0),
					Color.FromArgb(0xA8, 0xA8, 0xA8),
					Color.FromArgb(0x90, 0x90, 0x90),
					Color.FromArgb(0x78, 0x78, 0x78),
					Color.FromArgb(0x60, 0x60, 0x60),
					Color.FromArgb(0x48, 0x48, 0x48),
					Color.FromArgb(0x30, 0x30, 0x30),
					Color.FromArgb(0x18, 0x18, 0x18),
				};
			this.highlightedForegroundColor = highlightedForegroundColor ?? Color.FromArgb(0xFF, 0xFF, 0xFF);
			this.highlightedBackgroundColor = highlightedBackgroundColor ?? Color.FromArgb(0x33, 0x99, 0xFF);
			this.flashingForegroundColor = flashingForegroundColor ?? Color.White;
			this.flashingBackgroundColor = flashingBackgroundColor ?? Color.Red;
			this.flashWorkspaces = flashWorkspaces;
			if (flashWorkspaces)
			{
				flashingWindows = new Dictionary<IntPtr, Workspace>(3);

				if (flashTimer == null)
				{
					flashTimer = new Timer { Interval = 500 };
					flashingWorkspaces = new HashSet<Workspace>();
				}
			}
		}

		private void OnWorkspaceLabelClick(object sender, EventArgs e)
		{
			windawesome.SwitchToWorkspace(Array.IndexOf(workspaceLabels, sender as Label) + 1);
		}

		private void SetWorkspaceLabelColor(Workspace workspace, Window window)
		{
			var workspaceLabel = workspaceLabels[workspace.id - 1];
			if (workspace.IsCurrentWorkspace && isShown)
			{
				workspaceLabel.BackColor = highlightedBackgroundColor;
				workspaceLabel.ForeColor = highlightedForegroundColor;
			}
			else
			{
				var count = workspace.GetWindowsCount();
				if (count > 9)
				{
					count = 9;
				}
				workspaceLabel.BackColor = normalBackgroundColor[count];
				workspaceLabel.ForeColor = normalForegroundColor[count];
			}
		}

		private void OnWorkspaceChangedFromTo(Workspace workspace)
		{
			if (isShown)
			{
				SetWorkspaceLabelColor(workspace, null);
			}
		}

		private void OnWindowFlashing(LinkedList<Tuple<Workspace, Window>> list)
		{
			if (list.First.Value.Item2.hWnd != NativeMethods.GetForegroundWindow() && NativeMethods.IsWindow(list.First.Value.Item2.hWnd))
			{
				flashingWindows[list.First.Value.Item2.hWnd] = list.First.Value.Item1;
				flashingWorkspaces.Add(list.First.Value.Item1);
				if (flashingWorkspaces.Count == 1)
				{
					flashTimer.Start();
				}
			}
		}

		private void OnTimerTick(object sender, EventArgs e)
		{
			if (isShown)
			{
				foreach (var flashingWorkspace in flashingWorkspaces)
				{
					if (workspaceLabels[flashingWorkspace.id - 1].BackColor == flashingBackgroundColor)
					{
						SetWorkspaceLabelColor(flashingWorkspace, null);
					}
					else
					{
						workspaceLabels[flashingWorkspace.id - 1].BackColor = flashingBackgroundColor;
						workspaceLabels[flashingWorkspace.id - 1].ForeColor = flashingForegroundColor;
					}
				}
			}
		}

		private void OnWindowActivated(IntPtr hWnd)
		{
			Workspace workspace;
			if (flashingWindows.TryGetValue(hWnd, out workspace))
			{
				flashingWindows.Remove(hWnd);
				if (flashingWindows.Values.All(w => w != workspace))
				{
					SetWorkspaceLabelColor(workspace, null);
					flashingWorkspaces.Remove(workspace);
					if (flashingWorkspaces.Count == 0)
					{
						flashTimer.Stop();
					}
				}
			}
		}

		#region IWidget Members

		void IWidget.StaticInitializeWidget(Windawesome windawesome, Config config)
		{
			WorkspacesWidget.windawesome = windawesome;
			WorkspacesWidget.config = config;
		}

		void IWidget.InitializeWidget(Bar bar)
		{
			if (flashWorkspaces)
			{
				flashTimer.Tick += OnTimerTick;
				Workspace.WindowActivatedEvent += OnWindowActivated;
				Workspace.WorkspaceApplicationRestored += (ws, w) => OnWindowActivated(w.hWnd);
				Windawesome.WindowFlashing += OnWindowFlashing;
			}

			isShown = false;

			workspaceLabels = new Label[config.WorkspacesCount];

			Workspace.WorkspaceApplicationAdded += SetWorkspaceLabelColor;
			Workspace.WorkspaceApplicationRemoved += SetWorkspaceLabelColor;

			Workspace.WorkspaceChangedFrom += OnWorkspaceChangedFromTo;
			Workspace.WorkspaceChangedTo += OnWorkspaceChangedFromTo;

			for (var i = 1; i < config.Workspaces.Length; i++)
			{
				var workspace = config.Workspaces[i];
				var name = i + (workspace.name == "" ? "" : ":" + workspace.name);

				var label = bar.CreateLabel(" " + name + " ", 0);
				label.TextAlign = ContentAlignment.MiddleCenter;
				label.Click += OnWorkspaceLabelClick;
				workspaceLabels[i - 1] = label;
				SetWorkspaceLabelColor(workspace, null);
			}
		}

		IEnumerable<Control> IWidget.GetControls(int left, int right)
		{
			isLeft = right == -1;

			this.RepositionControls(left, right);

			return workspaceLabels;
		}

		public void RepositionControls(int left, int right)
		{
			this.left = left;
			this.right = right;

			if (isLeft)
			{
				for (var i = 1; i <= config.WorkspacesCount; i++)
				{
					var label = workspaceLabels[i - 1];
					label.Location = new Point(left, 0);
					left += label.Width;
				}
				this.right = left;
			}
			else
			{
				for (var i = config.WorkspacesCount; i > 0; i--)
				{
					var label = workspaceLabels[i - 1];
					right -= label.Width;
					label.Location = new Point(right, 0);
				}
				this.left = right;
			}
		}

		int IWidget.GetLeft()
		{
			return left;
		}

		int IWidget.GetRight()
		{
			return right;
		}

		void IWidget.WidgetShown()
		{
			isShown = true;
		}

		void IWidget.WidgetHidden()
		{
			isShown = false;

			if (flashWorkspaces)
			{
				flashingWindows.Values.ForEach(w => SetWorkspaceLabelColor(w, null));
			}
		}

		void IWidget.StaticDispose()
		{
		}

		void IWidget.Dispose()
		{
		}

		#endregion
	}
}
