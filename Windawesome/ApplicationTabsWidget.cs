using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace Windawesome
{
	public class ApplicationTabsWidget : ISpanWidget
	{
		private static Windawesome windawesome;

		// TODO: may use a single panel for each workspace which contains all the panels for the applications
		// when changing a workspace, only the parent panel must be shown/hidden
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

			windawesome.GetWindowSmallIconAsBitmap(window.hWnd, bitmap => pictureBox.Image = bitmap);

			var label = bar.CreateLabel(window.DisplayName, bar.GetBarHeight(), 0);
			label.Click += this.OnApplicationTabClick;
			panel.Controls.Add(label);

			panel.ResumeLayout();
			return panel;
		}

		private void OnWindowActivated(IntPtr hWnd)
		{
			if (isShown && (!showSingleApplicationTab || bar.Monitor.CurrentVisibleWorkspace.IsCurrentWorkspace))
			{
				Panel panel = null;
				var workspaceId = bar.Monitor.CurrentVisibleWorkspace.id - 1;
				var applications = applicationPanels[workspaceId];

				if (Windawesome.DoForSelfAndOwnersWhile(hWnd, h => !applications.TryGetValue(h, out panel)))
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
			if (workspace.Monitor == bar.Monitor && applications.TryGetValue(window.hWnd, out panel))
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
					if (workspace.IsCurrentWorkspace)
					{
						ActivateTopmost(workspace);
					}
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
					if (workspace.IsCurrentWorkspace)
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
					else if (currentlyHighlightedPanel != null)
					{
						currentlyHighlightedPanel.Hide();
					}
					OnWindowActivated(IntPtr.Zero);
				}
			}
		}

		private void ActivateTopmost(Workspace workspace)
		{
			if (workspace.Monitor == bar.Monitor)
			{
				var topmost = workspace.GetTopmostWindow();
				if (topmost != null)
				{
					OnWindowActivated(topmost.hWnd);
				}
			}
		}

		private void OnBarShown()
		{
			isShown = true;
			windawesome.monitors.ForEach(m => OnWorkspaceShown(m.CurrentVisibleWorkspace));
		}

		private void OnBarHidden()
		{
			windawesome.monitors.ForEach(m => OnWorkspaceHidden(m.CurrentVisibleWorkspace));
			isShown = false;
		}

		#region IWidget Members

		void IWidget.StaticInitializeWidget(Windawesome windawesome)
		{
			ApplicationTabsWidget.windawesome = windawesome;
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
			Workspace.WorkspaceActivated += ActivateTopmost;

			currentlyHighlightedPanel = null;

			mustResize = new bool[windawesome.config.Workspaces.Length];
			applicationPanels = new Dictionary<IntPtr, Panel>[windawesome.config.Workspaces.Length];
			for (var i = 0; i < windawesome.config.Workspaces.Length; i++)
			{
				applicationPanels[i] = new Dictionary<IntPtr, Panel>(3);
			}
		}

		IEnumerable<Control> ISpanWidget.GetInitialControls()
		{
			return Enumerable.Empty<Control>();
		}

		void IWidget.RepositionControls(int left, int right)
		{
			this.left = left;
			this.right = right;

			for (var i = 0; i < windawesome.config.Workspaces.Length; i++)
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
