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
			this.normalBackgroundColor = normalBackgroundColor ?? Color.FromArgb(0xF0, 0xF0, 0xF0);
			this.highlightedForegroundColor = highlightedForegroundColor ?? Color.FromArgb(0xFF, 0xFF, 0xFF);
			this.highlightedBackgroundColor = highlightedBackgroundColor ?? Color.FromArgb(0x33, 0x99, 0xFF);

			isShown = false;
		}

		private void ResizeApplicationPanels(int left, int right, int workspaceId)
		{
			mustResize[workspaceId] = false;

			if (applicationPanels[workspaceId].Values.Count > 0)
			{
				var eachWidth = (right - left) / (showSingleApplicationTab ? 1 : applicationPanels[workspaceId].Values.Count);

				foreach (var panel in applicationPanels[workspaceId].Values)
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
			var panel = new Panel();
			panel.SuspendLayout();
			panel.AutoSize = false;
			panel.Location = new Point(left, 0);
			panel.ForeColor = normalForegroundColor;
			panel.BackColor = normalBackgroundColor;

			var pictureBox = new PictureBox
				{
					Size = new Size(this.bar.barHeight, this.bar.barHeight),
					SizeMode = PictureBoxSizeMode.CenterImage
				};
			pictureBox.Click += this.OnApplicationTabClick;
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

			var label = bar.CreateLabel(window.DisplayName, bar.barHeight, 0);
			label.Click += this.OnApplicationTabClick;
			panel.Controls.Add(label);

			panel.ResumeLayout();
			return panel;
		}

		private void OnWindowActivated(IntPtr hWnd)
		{
			if (isShown)
			{
				Panel panel;
				var workspaceId = windawesome.CurrentWorkspace.id - 1;
				var applications = applicationPanels[workspaceId];

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

		private void OnApplicationTabClick(object sender, EventArgs e)
		{
			windawesome.SwitchToApplicationInCurrentWorkspace(
				applicationPanels[windawesome.CurrentWorkspace.id - 1].
					First(item => item.Value == (((Control) sender).Parent as Panel)).
					Key);
		}

		private void OnWindowTitleOrIconChanged(Workspace workspace, Window window, string newText)
		{
			Panel panel;
			var applications = applicationPanels[workspace.id - 1];
			if (applications.TryGetValue(window.hWnd, out panel))
			{
				panel.Controls[1].Text = newText;
			}
		}

		private void OnWorkspaceApplicationAdded(Workspace workspace, Window window)
		{
			if (window.ShowInTabs)
			{
				var workspaceId = workspace.id - 1;
				var newPanel = CreatePanel(window);

				applicationPanels[workspaceId].Add(window.hWnd, newPanel);

				if (isShown && workspace.IsCurrentWorkspace)
				{
					OnWindowActivated(window.hWnd);
					ResizeApplicationPanels(left, right, workspaceId);
				}
				else
				{
					newPanel.Hide();
					mustResize[workspaceId] = true;
				}
				bar.DoSpanWidgetControlsAdded(this, new[] { newPanel });
			}
		}

		private void OnWorkspaceApplicationRemoved(Workspace workspace, Window window)
		{
			var workspaceId = workspace.id - 1;
			Panel removedPanel;
			if (applicationPanels[workspaceId].TryGetValue(window.hWnd, out removedPanel))
			{
				applicationPanels[workspaceId].Remove(window.hWnd);
				if (isShown && workspace.IsCurrentWorkspace)
				{
					if (applicationPanels[workspaceId].Count > 0)
					{
						ActivateTopmost(workspace);
					}
					ResizeApplicationPanels(left, right, workspaceId);
				}
				else
				{
					mustResize[workspaceId] = true;
				}
				bar.DoSpanWidgetControlsRemoved(this, new[] { removedPanel });
			}
		}

		private void OnWorkspaceChangedTo(Workspace workspace)
		{
			if (isShown)
			{
				var workspaceId = workspace.id - 1;
				if (applicationPanels[workspaceId].Count > 0)
				{
					if (mustResize[workspaceId])
					{
						ResizeApplicationPanels(left, right, workspaceId);
					}
					if (!showSingleApplicationTab)
					{
						applicationPanels[workspaceId].Values.ForEach(p => p.Show());
					}
					currentlyHighlightedPanel = null;
					ActivateTopmost(workspace);
				}
			}
		}

		private void OnWorkspaceChangedFrom(Workspace workspace)
		{
			if (isShown)
			{
				var workspaceId = workspace.id - 1;
				if (applicationPanels[workspaceId].Count > 0)
				{
					if (!showSingleApplicationTab)
					{
						applicationPanels[workspaceId].Values.ForEach(p => p.Hide());
					}
					OnWindowActivated(IntPtr.Zero);
				}
			}
		}

		private void ActivateTopmost(Workspace workspace)
		{
			var topmost = workspace.GetTopmostWindow();
			if (!topmost.IsMinimized)
			{
				OnWindowActivated(topmost.hWnd);
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

			Windawesome.WindowTitleOrIconChanged += OnWindowTitleOrIconChanged;
			Workspace.WorkspaceApplicationAdded += OnWorkspaceApplicationAdded;
			Workspace.WorkspaceApplicationRemoved += OnWorkspaceApplicationRemoved;
			Workspace.WorkspaceApplicationRestored += (_, w) => OnWindowActivated(w.hWnd);
			Workspace.WindowActivatedEvent += OnWindowActivated;
			Workspace.WorkspaceChangedTo += OnWorkspaceChangedTo;
			Workspace.WorkspaceChangedFrom += OnWorkspaceChangedFrom;

			currentlyHighlightedPanel = null;

			mustResize = new bool[config.WorkspacesCount];
			applicationPanels = new Dictionary<IntPtr, Panel>[config.WorkspacesCount];
			for (var i = 0; i < config.WorkspacesCount; i++)
			{
				applicationPanels[i] = new Dictionary<IntPtr, Panel>(3);
			}
		}

		public IEnumerable<Control> GetControls(int left, int right)
		{
			this.left = left;
			this.right = right;

			return applicationPanels[windawesome.CurrentWorkspace.id - 1].Values;
		}

		public void RepositionControls(int left, int right)
		{
			this.left = left;
			this.right = right;

			for (var i = 0; i < config.WorkspacesCount; i++)
			{
				mustResize[i] = true;
			}

			ResizeApplicationPanels(left, right, windawesome.CurrentWorkspace.id - 1);
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
