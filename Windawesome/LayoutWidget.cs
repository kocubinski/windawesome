using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Windawesome
{
	public class LayoutWidget : IFixedWidthWidget
	{
		private Label layoutLabel;
		private readonly Color backgroundColor;
		private readonly Color foregroundColor;
		private readonly Action onClick;
		private Bar bar;
		private int left, right;
		private bool isLeft;

		public LayoutWidget(Color? backColor = null, Color? foreColor = null, Action onClick = null)
		{
			backgroundColor = backColor ?? Color.FromArgb(0x99, 0xB4, 0xD1);
			foregroundColor = foreColor ?? Color.FromArgb(0x00, 0x00, 0x00);

			this.onClick = onClick;
		}

		private void OnWorkspaceLayoutChanged(Workspace workspace)
		{
			if (workspace.Monitor == bar.Monitor && workspace.IsWorkspaceVisible)
			{
				var oldWidth = layoutLabel.Width;
				layoutLabel.Text = workspace.Layout.LayoutSymbol();
				layoutLabel.Width = TextRenderer.MeasureText(layoutLabel.Text, layoutLabel.Font).Width;
				if (layoutLabel.Width != oldWidth)
				{
					this.RepositionControls(left, right);
					bar.DoFixedWidthWidgetWidthChanged(this);
				}
			}
		}

		#region IWidget Members

		void IWidget.StaticInitializeWidget(Windawesome windawesome)
		{
		}

		void IWidget.InitializeWidget(Bar bar)
		{
			this.bar = bar;

			bar.BarShown += () => OnWorkspaceLayoutChanged(bar.Monitor.CurrentVisibleWorkspace);

			Workspace.LayoutUpdated += () => OnWorkspaceLayoutChanged(bar.Monitor.CurrentVisibleWorkspace);
			Workspace.WorkspaceShown += OnWorkspaceLayoutChanged;
			Workspace.WorkspaceLayoutChanged += (ws, _) => OnWorkspaceLayoutChanged(ws);

			layoutLabel = bar.CreateLabel("", 0);
			layoutLabel.TextAlign = ContentAlignment.MiddleCenter;
			layoutLabel.BackColor = backgroundColor;
			layoutLabel.ForeColor = foregroundColor;
			if (onClick != null)
			{
				layoutLabel.Click += (unused1, unused2) => onClick();
			}
		}

		IEnumerable<Control> IWidget.GetControls(int left, int right)
		{
			isLeft = right == -1;

			RepositionControls(left, right);

			return new Control[] { layoutLabel };
		}

		public void RepositionControls(int left, int right)
		{
			if (isLeft)
			{
				this.left = left;
				layoutLabel.Location = new Point(left, 0);
				this.right = layoutLabel.Right;
			}
			else
			{
				this.right = right;
				layoutLabel.Location = new Point(right - layoutLabel.Width, 0);
				this.left = layoutLabel.Left;
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
