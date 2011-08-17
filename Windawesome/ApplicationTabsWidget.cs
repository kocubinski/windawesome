using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	// TODO: something doesn't work with multiple bars and when clicking on apps to change them
	public class ApplicationTabsWidget : ISpanWidget
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
					panel.Size = new Size(eachWidth, bar.GetBarHeight());
					panel.Controls[0].Size = new Size(bar.GetBarHeight(), bar.GetBarHeight());
					panel.Controls[1].Size = new Size(eachWidth - bar.GetBarHeight(), bar.GetBarHeight());
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
					Size = new Size(this.bar.GetBarHeight(), this.bar.GetBarHeight()),
					SizeMode = PictureBoxSizeMode.CenterImage
				};
			pictureBox.Click += this.OnApplicationTabClick;
			panel.Controls.Add(pictureBox);

			Windawesome.GetWindowSmallIconAsBitmap(window.hWnd, bitmap => pictureBox.Image = bitmap);

			var label = bar.CreateLabel(window.DisplayName, bar.GetBarHeight(), 0);
			label.Click += this.OnApplicationTabClick;
			panel.Controls.Add(label);

			panel.ResumeLayout();
			return panel;
		}

		// TODO: when changing an application to a workspace which needs to be repositioned, the panels are not highlighted correctly
		private void OnWindowActivated(IntPtr hWnd)
		{
			if (isShown)
			{
				Panel panel;
				var workspaceId = bar.Monitor.CurrentVisibleWorkspace.id - 1;
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
			windawesome.SwitchToApplication(
				applicationPanels[bar.Monitor.CurrentVisibleWorkspace.id - 1].
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
			if (workspace.Monitor == bar.Monitor && window.ShowInTabs)
			{
				var workspaceId = workspace.id - 1;
				var newPanel = CreatePanel(window);

				applicationPanels[workspaceId].Add(window.hWnd, newPanel);

				if (isShown && workspace.IsWorkspaceVisible)
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
			if (workspace.Monitor == bar.Monitor && applicationPanels[workspaceId].TryGetValue(window.hWnd, out removedPanel))
			{
				applicationPanels[workspaceId].Remove(window.hWnd);
				if (isShown && workspace.IsWorkspaceVisible)
				{
					ActivateTopmost(workspace);
					ResizeApplicationPanels(left, right, workspaceId);
				}
				else
				{
					mustResize[workspaceId] = true;
				}
				bar.DoSpanWidgetControlsRemoved(this, new[] { removedPanel });
			}
		}

		private void OnWorkspaceShown(Workspace workspace)
		{
			if (isShown && workspace.Monitor == bar.Monitor)
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

		private void OnWorkspaceHidden(Workspace workspace)
		{
			if (isShown && workspace.Monitor == bar.Monitor)
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
			if (topmost != null)
			{
				OnWindowActivated(topmost.hWnd);
			}
		}

		private void OnBarShown()
		{
			isShown = true;
			OnWorkspaceShown(bar.Monitor.CurrentVisibleWorkspace);
		}

		private void OnBarHidden()
		{
			OnWorkspaceHidden(bar.Monitor.CurrentVisibleWorkspace);
			isShown = false;
		}

		#region IWidget Members

		void IWidget.StaticInitializeWidget(Windawesome windawesome, Config config)
		{
			ApplicationTabsWidget.windawesome = windawesome;
			ApplicationTabsWidget.config = config;
		}

		void IWidget.InitializeWidget(Bar bar)
		{
			this.bar = bar;

			bar.BarShown += OnBarShown;
			bar.BarHidden += OnBarHidden;

			Windawesome.WindowTitleOrIconChanged += OnWindowTitleOrIconChanged;
			Workspace.WorkspaceApplicationAdded += OnWorkspaceApplicationAdded;
			Workspace.WorkspaceApplicationRemoved += OnWorkspaceApplicationRemoved;
			Workspace.WorkspaceApplicationRestored += (_, w) => OnWindowActivated(w.hWnd);
			Workspace.WindowActivatedEvent += OnWindowActivated;
			Workspace.WorkspaceHidden += OnWorkspaceHidden;
			Workspace.WorkspaceShown += OnWorkspaceShown;

			currentlyHighlightedPanel = null;

			mustResize = new bool[config.Workspaces.Length];
			applicationPanels = new Dictionary<IntPtr, Panel>[config.Workspaces.Length];
			for (var i = 0; i < config.Workspaces.Length; i++)
			{
				applicationPanels[i] = new Dictionary<IntPtr, Panel>(3);
			}
		}

		IEnumerable<Control> IWidget.GetControls(int left, int right)
		{
			this.left = left;
			this.right = right;

			return applicationPanels[bar.Monitor.CurrentVisibleWorkspace.id - 1].Values;
		}

		void IWidget.RepositionControls(int left, int right)
		{
			this.left = left;
			this.right = right;

			for (var i = 0; i < config.Workspaces.Length; i++)
			{
				mustResize[i] = true;
			}

			ResizeApplicationPanels(left, right, bar.Monitor.CurrentVisibleWorkspace.id - 1);
		}

		int IWidget.GetLeft()
		{
			return left;
		}

		int IWidget.GetRight()
		{
			return right;
		}

		void IWidget.StaticDispose()
		{
		}

		void IWidget.Dispose()
		{
		}

		void IWidget.Refresh()
		{
		}

		#endregion
	}
}
