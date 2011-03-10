using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public class WorkspacesWidget : IWidget
	{
		private static Windawesome windawesome;
		private static Config config;
		private Bar bar;
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
		private bool flashWorkspaces;
		private static readonly Timer flashTimer;
		private static readonly HashSet<Workspace> flashingWorkspaces;
		private static bool anyFlashWorkspaces;

		static WorkspacesWidget()
		{
			flashTimer = new Timer();
			flashTimer.Interval = 500;

			flashingWorkspaces = new HashSet<Workspace>();
		}

		public WorkspacesWidget(Color[] normalForegroundColor = null, Color[] normalBackgroundColor = null,
			Color? highlightedForegroundColor = null, Color? highlightedBackgroundColor = null,
			Color? flashingForegroundColor = null, Color? flashingBackgroundColor = null, bool flashWorkspaces = true)
		{
			this.normalForegroundColor = normalForegroundColor ?? new Color[10]
				{
					Color.FromArgb(0x00, 0x00, 0x00),
					Color.FromArgb(0x00, 0x00, 0x00),
					Color.FromArgb(0x00, 0x00, 0x00),
					Color.FromArgb(0x00, 0x00, 0x00),
					Color.FromArgb(0x00, 0x00, 0x00),
					Color.FromArgb(0xff, 0xff, 0xff),
					Color.FromArgb(0xff, 0xff, 0xff),
					Color.FromArgb(0xff, 0xff, 0xff),
					Color.FromArgb(0xff, 0xff, 0xff),
					Color.FromArgb(0xff, 0xff, 0xff),
				};
			this.normalBackgroundColor = normalBackgroundColor ?? new Color[10]
				{
					Color.FromArgb(0xf0, 0xf0, 0xf0),
					Color.FromArgb(0xd8, 0xd8, 0xd8),
					Color.FromArgb(0xc0, 0xc0, 0xc0),
					Color.FromArgb(0xa8, 0xa8, 0xa8),
					Color.FromArgb(0x90, 0x90, 0x90),
					Color.FromArgb(0x78, 0x78, 0x78),
					Color.FromArgb(0x60, 0x60, 0x60),
					Color.FromArgb(0x48, 0x48, 0x48),
					Color.FromArgb(0x30, 0x30, 0x30),
					Color.FromArgb(0x18, 0x18, 0x18),
				};
			this.highlightedForegroundColor = highlightedForegroundColor ?? Color.FromArgb(0xff, 0xff, 0xff);
			this.highlightedBackgroundColor = highlightedBackgroundColor ?? Color.FromArgb(0x33, 0x99, 0xff);
			this.flashingForegroundColor = flashingForegroundColor ?? Color.White;
			this.flashingBackgroundColor = flashingBackgroundColor ?? Color.Red;
			this.flashWorkspaces = flashWorkspaces;
			anyFlashWorkspaces |= flashWorkspaces;
		}

		private void workspaceLabel_Click(object sender, EventArgs e)
		{
			windawesome.SwitchToWorkspace(Array.IndexOf(workspaceLabels, sender as Label) + 1);
		}

		private void SetWorkspaceLabelColor(Workspace workspace, Window window)
		{
			var workspaceLabel = workspaceLabels[workspace.ID - 1];
			if (workspace.isCurrentWorkspace && isShown)
			{
				workspaceLabel.BackColor = highlightedBackgroundColor;
				workspaceLabel.ForeColor = highlightedForegroundColor;
			}
			else
			{
				int count = workspace.GetWindowsCount();
				if (count > 9)
				{
					count = 9;
				}
				workspaceLabel.BackColor = normalBackgroundColor[count];
				workspaceLabel.ForeColor = normalForegroundColor[count];
			}
		}

		private void WorkspacesWidget_WorkspaceChangedFromTo(Workspace workspace)
		{
			if (isShown)
			{
				if (flashWorkspaces && flashingWorkspaces.Count > 0 && workspace.isCurrentWorkspace && flashingWorkspaces.Contains(workspace))
				{
					if (flashingWorkspaces.Count == 1)
					{
						flashTimer.Stop();
					}
					flashingWorkspaces.Remove(workspace);
				}

				SetWorkspaceLabelColor(workspace, null);
			}
		}

		private void WorkspacesWidget_WindowFlashing(LinkedList<Tuple<Workspace, Window>> list)
		{
			if (list.All(t => !t.Item1.isCurrentWorkspace))
			{
				flashingWorkspaces.Add(list.First.Value.Item1);
				if (flashingWorkspaces.Count == 1)
				{
					flashTimer.Start();
				}
			}
		}

		private void flashTimer_Tick(object sender, EventArgs e)
		{
			if (isShown)
			{
				foreach (var flashingWorkspace in flashingWorkspaces)
				{
					if (workspaceLabels[flashingWorkspace.ID - 1].BackColor == flashingBackgroundColor)
					{
						SetWorkspaceLabelColor(flashingWorkspace, null);
					}
					else
					{
						workspaceLabels[flashingWorkspace.ID - 1].BackColor = flashingBackgroundColor;
						workspaceLabels[flashingWorkspace.ID - 1].ForeColor = flashingForegroundColor;
					}
				}
			}
		}

		#region IWidget Members

		public WidgetType GetWidgetType()
		{
			return WidgetType.FixedWidth;
		}

		public void StaticInitializeWidget(Windawesome windawesome, Config config)
		{
			WorkspacesWidget.windawesome = windawesome;
			WorkspacesWidget.config = config;

			if (anyFlashWorkspaces)
			{
				Windawesome.WindowFlashing += WorkspacesWidget_WindowFlashing;
			}
		}

		public void InitializeWidget(Bar bar)
		{
			this.bar = bar;

			if (flashWorkspaces)
			{
				flashTimer.Tick += flashTimer_Tick;
			}

			isShown = false;

			workspaceLabels = new Label[config.workspacesCount];

			Workspace.WorkspaceApplicationAdded += SetWorkspaceLabelColor;
			Workspace.WorkspaceApplicationRemoved += SetWorkspaceLabelColor;
			Workspace.WorkspaceChangedFrom += WorkspacesWidget_WorkspaceChangedFromTo;
			Workspace.WorkspaceChangedTo += WorkspacesWidget_WorkspaceChangedFromTo;

			for (int i = 1; i < config.workspaces.Length; i++)
			{
				var workspace = config.workspaces[i];
				string name = i.ToString() + (workspace.name == "" ? "" : ":" + workspace.name);

				Label label = bar.CreateLabel(" " + name + " ", 0);
				label.TextAlign = ContentAlignment.MiddleCenter;
				label.Click += workspaceLabel_Click;
				workspaceLabels[i - 1] = label;
				SetWorkspaceLabelColor(workspace, null);
			}
		}

		public IEnumerable<Control> GetControls(int left, int right)
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
				for (int i = 1; i <= config.workspacesCount; i++)
				{
					Label label = workspaceLabels[i - 1];
					label.Location = new Point(left, 0);
					left += label.Width;
				}
				this.right = left;
			}
			else
			{
				for (int i = config.workspacesCount; i > 0; i--)
				{
					string name = i.ToString() + (config.workspaces[i].name == "" ? "" : ":" + config.workspaces[i].name);

					Label label = workspaceLabels[i - 1];
					right -= label.Width;
					label.Location = new Point(right, 0);
				}
				this.left = right;
			}
		}

		public int GetLeft()
		{
			return left;
		}

		public int GetRight()
		{
			return right;
		}

		public void WidgetShown()
		{
			isShown = true;
		}

		public void WidgetHidden()
		{
			isShown = false;

			if (flashWorkspaces)
			{
				flashingWorkspaces.ForEach(w => SetWorkspaceLabelColor(w, null));
			}
		}

		public void StaticDispose()
		{
		}

		public void Dispose()
		{
		}

		#endregion
	}
}
