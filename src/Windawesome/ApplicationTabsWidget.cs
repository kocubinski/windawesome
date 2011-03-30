using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public class ApplicationTabsWidget : IWidget
	{
		private static Windawesome windawesome;
		private static Config config;

		private Dictionary<IntPtr, Panel>[] applicationPanels; // hWnd -> Panel
		private Panel currentlyHighlightedPanel;
		private bool[] mustResize;
		private int left, right;
		private readonly bool showSingleApplicationTab;
		private readonly Color normalForegroundColor;
		private readonly Color normalBackgroundColor;
		private readonly Color highlightedForegroundColor;
		private readonly Color highlightedBackgroundColor;
		private bool isShown;

		private Bar bar;

		public ApplicationTabsWidget(bool showSingleApplicationTab = false,
			Color? normalForegroundColor = null, Color? normalBackgroundColor = null,
			Color? highlightedForegroundColor = null, Color? highlightedBackgroundColor = null)
		{
			this.showSingleApplicationTab = showSingleApplicationTab;
			this.normalForegroundColor = normalForegroundColor ?? Color.FromArgb(0x00, 0x00, 0x00);
			this.normalBackgroundColor = normalBackgroundColor ?? Color.FromArgb(0xf0, 0xf0, 0xf0);
			this.highlightedForegroundColor = highlightedForegroundColor ?? Color.FromArgb(0xff, 0xff, 0xff);
			this.highlightedBackgroundColor = highlightedBackgroundColor ?? Color.FromArgb(0x33, 0x99, 0xff);

			isShown = false;
		}

		private void ResizeApplicationPanels(int left, int right, int workspaceID)
		{
			mustResize[workspaceID] = false;

			if (applicationPanels[workspaceID].Values.Count > 0)
			{
				var eachWidth = (right - left) / (showSingleApplicationTab ? 1 : applicationPanels[workspaceID].Values.Count);

				foreach (var panel in applicationPanels[workspaceID].Values)
				{
					panel.Location = new Point(left, 0);
					panel.Size = new Size(eachWidth, bar.barHeight);
					panel.Controls[0].Size = new Size(bar.barHeight, bar.barHeight);
					panel.Controls[1].Size = new Size(eachWidth - bar.barHeight, bar.barHeight);
					if (!showSingleApplicationTab)
					{
						left += eachWidth;
					}
				}
			}
		}

		private Panel CreatePanel(Window window)
		{
			Panel panel = new Panel();
			panel.SuspendLayout();
			panel.AutoSize = false;
			panel.Location = new Point(left, 0);
			panel.ForeColor = normalForegroundColor;
			panel.BackColor = normalBackgroundColor;

			PictureBox pictureBox = new PictureBox();
			pictureBox.Size = new Size(bar.barHeight, bar.barHeight);
			pictureBox.SizeMode = PictureBoxSizeMode.CenterImage;
			pictureBox.Click += applicationTab_Click;
			panel.Controls.Add(pictureBox);

			Windawesome.GetWindowSmallIconAsBitmap(window.hWnd, bitmap =>
				{
					try
					{
						pictureBox.Image = bitmap;
					}
					catch
					{
					}
				});

			Label label = bar.CreateLabel(window.caption, bar.barHeight, 0);
			label.Click += applicationTab_Click;
			panel.Controls.Add(label);

			panel.ResumeLayout();
			return panel;
		}

		private void ApplicationTabsWidget_WindowActivated(IntPtr hWnd)
		{
			if (isShown)
			{
				Panel panel;
				int workspaceID = windawesome.CurrentWorkspace.ID - 1;
				var applications = applicationPanels[workspaceID];

				if (applications.TryGetValue(hWnd, out panel))
				{
					if (panel == currentlyHighlightedPanel)
					{
						if (!showSingleApplicationTab && applications.Count == 1 && currentlyHighlightedPanel != null)
						{
							currentlyHighlightedPanel.ForeColor = normalForegroundColor;
							currentlyHighlightedPanel.BackColor = normalBackgroundColor;
						}
						return ;
					}
				}

				// removes the current highlight
				if (currentlyHighlightedPanel != null)
				{
					if (showSingleApplicationTab)
					{
						currentlyHighlightedPanel.Hide();
					}
					else
					{
						currentlyHighlightedPanel.ForeColor = normalForegroundColor;
						currentlyHighlightedPanel.BackColor = normalBackgroundColor;
					}
				}

				// highlights the new app
				if (panel != null)
				{
					if (showSingleApplicationTab)
					{
						panel.Show();
					}
					else if (applications.Count > 1)
					{
						panel.ForeColor = highlightedForegroundColor;
						panel.BackColor = highlightedBackgroundColor;
					}
				}

				currentlyHighlightedPanel = panel;
			}
		}

		private void applicationTab_Click(object sender, EventArgs e)
		{
			windawesome.SwitchToApplicationInCurrentWorkspace(
				applicationPanels[windawesome.CurrentWorkspace.ID - 1].
					First(item => item.Value == ((sender as Control).Parent as Panel)).
					Key);
		}

		private void ApplicationTabsWidget_WindowTitleOrIconChanged(Workspace workspace, Window window, string newText)
		{
			Panel panel;
			var applications = applicationPanels[workspace.ID - 1];
			if (applications.TryGetValue(window.hWnd, out panel))
			{
				panel.Controls[1].Text = newText;
			}
		}

		private void ApplicationTabsWidget_WorkspaceApplicationAdded(Workspace workspace, Window window)
		{
			if (window.showInTabs)
			{
				var workspaceID = workspace.ID - 1;
				var newPanel = CreatePanel(window);

				applicationPanels[workspaceID].Add(window.hWnd, newPanel);

				if (isShown && workspace.isCurrentWorkspace)
				{
					ApplicationTabsWidget_WindowActivated(window.hWnd);
					ResizeApplicationPanels(left, right, workspaceID);
				}
				else
				{
					newPanel.Hide();
					mustResize[workspaceID] = true;
				}
				bar.OnSpanWidgetControlsAdded(this, new Panel[] { newPanel });
			}
		}

		private void ApplicationTabsWidget_WorkspaceApplicationRemoved(Workspace workspace, Window window)
		{
			var workspaceID = workspace.ID - 1;
			Panel removedPanel;
			if (applicationPanels[workspaceID].TryGetValue(window.hWnd, out removedPanel))
			{
				applicationPanels[workspaceID].Remove(window.hWnd);
				if (isShown && workspace.isCurrentWorkspace)
				{
					if (applicationPanels[workspaceID].Count > 0)
					{
						ActivateTopmost(workspace);
					}
					ResizeApplicationPanels(left, right, workspaceID);
				}
				else
				{
					mustResize[workspaceID] = true;
				}
				bar.OnSpanWidgetControlsRemoved(this, new Panel[] { removedPanel });
			}
		}

		private void ApplicationTabsWidget_WorkspaceChangedTo(Workspace workspace)
		{
			if (isShown)
			{
				var workspaceID = workspace.ID - 1;
				if (applicationPanels[workspaceID].Count > 0)
				{
					if (mustResize[workspaceID])
					{
						ResizeApplicationPanels(left, right, workspaceID);
					}
					if (!showSingleApplicationTab)
					{
						applicationPanels[workspaceID].Values.ForEach(p => p.Show());
					}
					currentlyHighlightedPanel = null;
					ActivateTopmost(workspace);
				}
			}
		}

		private void ApplicationTabsWidget_WorkspaceChangedFrom(Workspace workspace)
		{
			if (isShown)
			{
				var workspaceID = workspace.ID - 1;
				if (applicationPanels[workspaceID].Count > 0)
				{
					if (!showSingleApplicationTab)
					{
						applicationPanels[workspaceID].Values.ForEach(p => p.Hide());
					}
					ApplicationTabsWidget_WindowActivated(IntPtr.Zero);
				}
			}
		}

		private void ActivateTopmost(Workspace workspace)
		{
			var topmost = workspace.GetTopmostWindow();
			if (!topmost.isMinimized)
			{
				ApplicationTabsWidget_WindowActivated(topmost.hWnd);
			}
		}

		#region IWidget Members

		public WidgetType GetWidgetType()
		{
			return WidgetType.Span;
		}

		public void StaticInitializeWidget(Windawesome windawesome, Config config)
		{
			ApplicationTabsWidget.windawesome = windawesome;
			ApplicationTabsWidget.config = config;
		}

		public void InitializeWidget(Bar bar)
		{
			this.bar = bar;

			Windawesome.WindowTitleOrIconChanged += ApplicationTabsWidget_WindowTitleOrIconChanged;
			Workspace.WorkspaceApplicationAdded += ApplicationTabsWidget_WorkspaceApplicationAdded;
			Workspace.WorkspaceApplicationRemoved += ApplicationTabsWidget_WorkspaceApplicationRemoved;
			Workspace.WorkspaceApplicationRestored += (_, w) => ApplicationTabsWidget_WindowActivated(w.hWnd);
			Workspace.WindowActivatedEvent += ApplicationTabsWidget_WindowActivated;
			Workspace.WorkspaceChangedTo += ApplicationTabsWidget_WorkspaceChangedTo;
			Workspace.WorkspaceChangedFrom += ApplicationTabsWidget_WorkspaceChangedFrom;

			currentlyHighlightedPanel = null;

			mustResize = new bool[config.workspacesCount];
			applicationPanels = new Dictionary<IntPtr, Panel>[config.workspacesCount];
			for (int i = 0; i < config.workspacesCount; i++)
			{
				mustResize[i] = false;
				applicationPanels[i] = new Dictionary<IntPtr, Panel>(3);
			}
		}

		public IEnumerable<Control> GetControls(int left, int right)
		{
			this.left = left;
			this.right = right;

			return applicationPanels[windawesome.CurrentWorkspace.ID - 1].Values;
		}

		public void RepositionControls(int left, int right)
		{
			this.left = left;
			this.right = right;

			for (int i = 0; i < config.workspacesCount; i++)
			{
				mustResize[i] = true;
			}

			ResizeApplicationPanels(left, right, windawesome.CurrentWorkspace.ID - 1);
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
