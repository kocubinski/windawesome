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
		private LinkedList<Tuple<IntPtr, Panel>>[] applicationPanels; // hWnd -> Panel
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

			if (applicationPanels[workspaceId].Count > 0)
			{
				var eachWidth = (right - left) / (showSingleApplicationTab ? 1 : applicationPanels[workspaceId].Count);

				foreach (var panel in this.applicationPanels[workspaceId].Select(tuple => tuple.Item2))
				{
					panel.Location = new Point(left, 0);
					panel.Size = new Size(eachWidth, this.bar.GetBarHeight());
					panel.Controls[0].Size = new Size(this.bar.GetBarHeight(), this.bar.GetBarHeight());
					panel.Controls[1].Size = new Size(eachWidth - this.bar.GetBarHeight(), this.bar.GetBarHeight());
					if (!this.showSingleApplicationTab)
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

				if ((hWnd = Windawesome.DoForSelfAndOwnersWhile(hWnd, h => applications.All(t => t.Item1 != h))) != IntPtr.Zero)
				{
					panel = applications.First(t => t.Item1 == hWnd).Item2;
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

					currentlyHighlightedPanel = panel;
				}
				else
				{
					currentlyHighlightedPanel = null;
				}
			}
		}

		private void OnApplicationTabClick(object sender, EventArgs e)
		{
			windawesome.SwitchToApplication(
				applicationPanels[bar.Monitor.CurrentVisibleWorkspace.id - 1].
					First(tuple => tuple.Item2 == (((Control) sender).Parent as Panel)).Item1);
		}

		private void OnWindowTitleOrIconChanged(Workspace workspace, Window window, string newText)
		{
			Tuple<IntPtr, Panel> tuple;
			var applications = applicationPanels[workspace.id - 1];
			if (workspace.Monitor == bar.Monitor && (tuple = applications.FirstOrDefault(t => t.Item1 == window.hWnd)) != null)
			{
				tuple.Item2.Controls[1].Text = newText;
			}
		}

		private void OnWorkspaceWindowAdded(Workspace workspace, Window window)
		{
			if (workspace.Monitor == bar.Monitor && window.ShowInTabs)
			{
				var workspaceId = workspace.id - 1;
				var newPanel = CreatePanel(window);

				applicationPanels[workspaceId].AddFirst(new Tuple<IntPtr, Panel>(window.hWnd, newPanel));

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

		private void OnWorkspaceWindowRemoved(Workspace workspace, Window window)
		{
			var workspaceId = workspace.id - 1;
			var tuple = applicationPanels[workspaceId].FirstOrDefault(t => t.Item1 == window.hWnd);
			if (workspace.Monitor == bar.Monitor && tuple != null)
			{
				applicationPanels[workspaceId].Remove(tuple);
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
				bar.DoSpanWidgetControlsRemoved(this, new[] { tuple.Item2 });
			}
		}

		private void OnWorkspaceWindowOrderChanged(Workspace workspace, Window window)
		{
			if (bar.Monitor == workspace.Monitor)
			{
				var applications = applicationPanels[workspace.id - 1];
				var windows = workspace.GetManagedWindows();
				var enumerator = windows.GetEnumerator();
				var isPrev = false;
				for (var node = applications.First; node != null; node = node.Next)
				{
					enumerator.MoveNext();
					if (node.Value.Item1 == window.hWnd)
					{
						if (windows.First() == window)
						{
							applications.Remove(node);
							applications.AddFirst(node);
						}
						else if (isPrev)
						{
							var prevNode = node.Previous;
							applications.Remove(node);
							applications.AddBefore(prevNode, node);
						}
						else
						{
							var nextNode = node.Next;
							applications.Remove(node);
							applications.AddAfter(nextNode, node);
						}
						break;
					}
					if (enumerator.Current == window)
					{
						isPrev = true;
					}
				}

				if (isShown)
				{
					ResizeApplicationPanels(left, right, workspace.id - 1);
				}
				else
				{
					mustResize[workspace.id - 1] = true;
				}
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
						applicationPanels[workspaceId].ForEach(t => t.Item2.Show());
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
						applicationPanels[workspaceId].ForEach(t => t.Item2.Hide());
					}
					else if (currentlyHighlightedPanel != null)
					{
						currentlyHighlightedPanel.Hide();
					}
				}
			}
		}

		private void ActivateTopmost(Workspace workspace)
		{
			if (workspace.Monitor == bar.Monitor)
			{
				var topmost = workspace.GetTopmostZOrderWindow();
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
			Workspace.WorkspaceWindowAdded += OnWorkspaceWindowAdded;
			Workspace.WorkspaceWindowRemoved += OnWorkspaceWindowRemoved;
			Workspace.WorkspaceWindowRestored += (_, w) => OnWindowActivated(w.hWnd);
			Workspace.WindowActivatedEvent += OnWindowActivated;
			Workspace.WorkspaceHidden += OnWorkspaceHidden;
			Workspace.WorkspaceShown += OnWorkspaceShown;
			Workspace.WorkspaceDeactivated += _ => OnWindowActivated(IntPtr.Zero);
			Workspace.WorkspaceActivated += ActivateTopmost;
			Workspace.WorkspaceWindowOrderChanged += OnWorkspaceWindowOrderChanged;

			currentlyHighlightedPanel = null;

			mustResize = new bool[windawesome.config.Workspaces.Length];
			applicationPanels = new LinkedList<Tuple<IntPtr, Panel>>[windawesome.config.Workspaces.Length];
			for (var i = 0; i < windawesome.config.Workspaces.Length; i++)
			{
				applicationPanels[i] = new LinkedList<Tuple<IntPtr, Panel>>();
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
